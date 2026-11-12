using System;
using IronGrind.Networking;

namespace IronGrind.Client.Session
{
    /// <summary>Outcome of <see cref="SnapshotFragmentCollector.Accept"/>.</summary>
    public enum SnapshotFragmentResult
    {
        /// <summary>The fragment was stored; more are missing.</summary>
        Accepted,

        /// <summary>The fragment was dropped (duplicate, stale, mismatched, before index 0, or after completion).</summary>
        Ignored,

        /// <summary>The last missing fragment arrived; the snapshot is complete.</summary>
        Completed,

        /// <summary><c>totalFragments</c> exceeds the configured maximum; the entry must fail.</summary>
        TooLarge
    }

    /// <summary>
    /// Client-side reassembly of a zone state snapshot (networking-wire-protocol.md reassembly steps 1-5). Fragment index
    /// 0 starts a snapshot and allocates <c>totalFragments * MaxPayloadBytes</c>; each fragment is copied to
    /// <c>index * MaxPayloadBytes</c>. Parsing the entity states is out of scope.
    /// </summary>
    public sealed class SnapshotFragmentCollector
    {
        private readonly int _maxFragments;
        private byte[] _buffer;
        private bool[] _received;
        private uint _version;
        private int _total;
        private int _receivedCount;
        private bool _active;
        private bool _completed;
        private int _lastFragmentLength;

        /// <summary>Creates a collector.</summary>
        /// <param name="maxFragments">Largest accepted <c>totalFragments</c> (at least 1).</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFragments"/> is below 1.</exception>
        public SnapshotFragmentCollector(int maxFragments)
        {
            if (maxFragments < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFragments));
            }

            _maxFragments = maxFragments;
        }

        /// <summary>True once every fragment of the current snapshot has arrived.</summary>
        public bool IsComplete => _completed;

        /// <summary>The version of the completed snapshot; meaningful only when <see cref="IsComplete"/> is true.</summary>
        public uint CompletedVersion { get; private set; }

        /// <summary>
        /// The reassembled byte count of the completed snapshot:
        /// <c>(total - 1) * MaxPayloadBytes + lastFragmentPayloadLength</c>. Meaningful only when <see cref="IsComplete"/> is true.
        /// </summary>
        public int CompletedLength { get; private set; }

        /// <summary>Number of distinct fragments stored for the current snapshot.</summary>
        public int ReceivedCount => _receivedCount;

        /// <summary>The reassembly buffer of the current snapshot (null before index 0).</summary>
        public byte[] Buffer => _buffer;

        /// <summary>Feeds one decoded fragment into the collector.</summary>
        /// <param name="header">The fragment header.</param>
        /// <param name="payload">The fragment payload (at most <see cref="ZoneStateSnapshotFragment.MaxPayloadBytes"/>).</param>
        /// <returns>What happened to the fragment.</returns>
        public SnapshotFragmentResult Accept(in ZoneStateSnapshotFragment header, ReadOnlySpan<byte> payload)
        {
            if (_completed)
            {
                return SnapshotFragmentResult.Ignored;
            }

            if (!_active)
            {
                if (header.FragmentIndex != 0)
                {
                    return SnapshotFragmentResult.Ignored;
                }

                if (header.TotalFragments > _maxFragments)
                {
                    return SnapshotFragmentResult.TooLarge;
                }

                Start(header);
            }
            else if (header.FragmentIndex == 0 && StaleDiscardComparer.IsNewerVersion(_version, header.SnapshotVersion))
            {
                if (header.TotalFragments > _maxFragments)
                {
                    Reset();
                    return SnapshotFragmentResult.TooLarge;
                }

                Start(header);
            }
            else if (header.SnapshotVersion != _version || header.TotalFragments != _total)
            {
                return SnapshotFragmentResult.Ignored;
            }

            if (header.FragmentIndex >= _total
                || payload.Length > ZoneStateSnapshotFragment.MaxPayloadBytes
                || _received[header.FragmentIndex])
            {
                return SnapshotFragmentResult.Ignored;
            }

            payload.CopyTo(new Span<byte>(_buffer, header.FragmentIndex * ZoneStateSnapshotFragment.MaxPayloadBytes, payload.Length));
            _received[header.FragmentIndex] = true;
            _receivedCount++;
            if (header.FragmentIndex == _total - 1)
            {
                _lastFragmentLength = payload.Length;
            }

            if (_receivedCount < _total)
            {
                return SnapshotFragmentResult.Accepted;
            }

            _completed = true;
            CompletedVersion = _version;
            CompletedLength = (_total - 1) * ZoneStateSnapshotFragment.MaxPayloadBytes + _lastFragmentLength;
            return SnapshotFragmentResult.Completed;
        }

        /// <summary>Discards the partial or completed snapshot and its buffer.</summary>
        public void Reset()
        {
            _buffer = null;
            _received = null;
            _version = 0;
            _total = 0;
            _receivedCount = 0;
            _active = false;
            _completed = false;
            CompletedVersion = 0;
            CompletedLength = 0;
            _lastFragmentLength = 0;
        }

        private void Start(in ZoneStateSnapshotFragment header)
        {
            _active = true;
            _version = header.SnapshotVersion;
            _total = header.TotalFragments;
            _receivedCount = 0;
            _lastFragmentLength = 0;
            _buffer = new byte[_total * ZoneStateSnapshotFragment.MaxPayloadBytes];
            _received = new bool[_total];
        }
    }
}

using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The bounded, thread-safe inbox of relayed messages (ADR-015 Decision 3). One writer thread (the listener, or
    /// the in-memory bus in tests) calls <see cref="TryAdd"/>; the tick thread calls <see cref="Drain"/> in tick step 1a.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Storage.</b> A ring of <c>capacity</c> slots, each a byte array of <c>maxBodyBytes</c> (the largest allowlist
    /// row, 52), allocated at construction. <see cref="TryAdd"/>, <see cref="Drain"/> and an empty drain allocate
    /// nothing. A full inbox drops the new entry (it never overwrites) and counts it; <see cref="Drain"/> logs the
    /// drops once, as one line, when the count is non-zero. <see cref="TryAdd"/> never logs, so it is safe on any thread.
    /// </para>
    /// <para>
    /// <b>Locking.</b> One <c>lock</c> guards the indices and the drop counter. <see cref="Drain"/> takes the count at
    /// its start and delivers only those entries, so an entry added during the drain waits for the next tick. The
    /// handler runs outside the lock; a slot is not reused until the handler returns. Exactly one reader is
    /// supported. A throwing handler is caught per entry (one error each) and the remaining entries are still delivered.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var inbox = new CrossZoneRelayInbox(allowlist.LargestMaxBodyBytes);   // capacity 64
    /// inbox.TryAdd(0xE103, body);                         // listener thread
    /// int delivered = inbox.Drain(currentTick, handler);  // tick thread
    /// </code>
    /// </example>
    public sealed class CrossZoneRelayInbox : ICrossZoneRelayInbox
    {
        /// <summary>Default capacity (tuning knob RELAY_INBOX_CAPACITY).</summary>
        public const int RELAY_INBOX_CAPACITY = 64;

        private const string LOG_PREFIX = "[CrossZoneRelayInbox]";

        private readonly object _lock = new object();
        private readonly byte[][] _slots;
        private readonly ushort[] _typeIds;
        private readonly int[] _lengths;
        private readonly int _capacity;
        private readonly int _maxBodyBytes;
        private int _head;
        private int _count;
        private int _dropped;

        /// <summary>Creates the inbox and allocates its slots.</summary>
        /// <param name="maxBodyBytes">Slot size, no default: the composition root passes <c>allowlist.LargestMaxBodyBytes</c>.</param>
        /// <param name="capacity">Entries held at once; the RELAY_INBOX_CAPACITY knob, default 64.</param>
        /// <exception cref="ArgumentOutOfRangeException">A size is not positive (body bytes may be 0).</exception>
        public CrossZoneRelayInbox(int maxBodyBytes, int capacity = RELAY_INBOX_CAPACITY)
        {
            if (maxBodyBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBodyBytes));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
            _maxBodyBytes = maxBodyBytes;
            _slots = new byte[capacity][];
            _typeIds = new ushort[capacity];
            _lengths = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _slots[i] = new byte[maxBodyBytes];
            }
        }

        /// <summary>Entries currently waiting.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        /// <inheritdoc />
        public bool TryAdd(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            lock (_lock)
            {
                if (_count >= _capacity || body.Length > _maxBodyBytes)
                {
                    _dropped++;
                    return false;
                }

                int slot = (_head + _count) % _capacity;
                body.CopyTo(new Span<byte>(_slots[slot], 0, body.Length));
                _typeIds[slot] = messageTypeId;
                _lengths[slot] = body.Length;
                _count++;
                return true;
            }
        }

        /// <inheritdoc />
        public int Drain(uint currentTick, ICrossZoneRelayHandler handler)
        {
            int toDeliver;
            int dropped;
            lock (_lock)
            {
                toDeliver = _count;
                dropped = _dropped;
                _dropped = 0;
            }

            if (dropped > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} EntriesDropped: count={dropped} tick={currentTick} - the inbox was full or an entry was too long; the entries were lost.");
            }

            if (toDeliver == 0)
            {
                return 0;
            }

            for (int i = 0; i < toDeliver; i++)
            {
                // Only this thread moves _head, and a writer only fills slots beyond head + count: the slot is stable.
                int slot = _head;
                ushort typeId = _typeIds[slot];
                try
                {
                    handler.OnRelayed(typeId, new ReadOnlySpan<byte>(_slots[slot], 0, _lengths[slot]));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} HandlerThrew: type=0x{typeId:X4} tick={currentTick} - {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    lock (_lock)
                    {
                        _head = (_head + 1) % _capacity;
                        _count--;
                    }
                }
            }

            return toDeliver;
        }
    }
}

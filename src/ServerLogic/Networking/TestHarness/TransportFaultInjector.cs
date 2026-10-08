#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// In-memory implementation of <see cref="ITransportFaultInjector"/>. Stores pending fault
    /// injections keyed by <c>MessageTypeID</c>; consumed by the outbound send path via the
    /// internal seam methods below.
    /// </summary>
    /// <remarks>
    /// The real transport/serialization send path this seam plugs into does not exist yet (out of
    /// scope for this story — see the Networking Wire Protocol epic). The internal methods below
    /// are the integration point a future story's send path will call once it exists; today they
    /// exist so this story's own tests can prove the fault-injection behavior deterministically
    /// without a real transport.
    /// </remarks>
    public sealed class TransportFaultInjector : ITransportFaultInjector
    {
        /// <summary>
        /// The project's "any message type" sentinel for <see cref="DropNextOutbound"/> (the GDD
        /// mentions <c>MessageTypeID.Any</c> without pinning a concrete value). CR-NET-7's
        /// production <c>MessageTypeID</c> allocation table never assigns <c>0xFFFF</c> to a real
        /// message type, so it is safe to use as the "any type" marker here.
        /// </summary>
        public const ushort AnyMessageTypeId = ushort.MaxValue;

        private readonly Dictionary<ushort, int> _pendingDrops = new Dictionary<ushort, int>();
        private readonly Dictionary<ushort, Queue<int>> _pendingDelays = new Dictionary<ushort, Queue<int>>();
        private readonly HashSet<ushort> _pendingReorders = new HashSet<ushort>();
        private readonly HashSet<ushort> _pendingFragmentDrops = new HashSet<ushort>();
        private bool _dropLastFragmentPending;

        private uint _sequenceNumber;
        private uint? _pendingSequenceNumberOverride;
        private bool _hasEmittedAnyMessage;

        /// <inheritdoc/>
        public void DropNextOutbound(ushort messageTypeId, int count)
        {
            _pendingDrops[messageTypeId] = _pendingDrops.TryGetValue(messageTypeId, out int existing)
                ? existing + count
                : count;
        }

        /// <inheritdoc/>
        public void DelayNextOutbound(ushort messageTypeId, int count, int delayMs)
        {
            if (!_pendingDelays.TryGetValue(messageTypeId, out Queue<int> queue))
            {
                queue = new Queue<int>();
                _pendingDelays[messageTypeId] = queue;
            }

            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(delayMs);
            }
        }

        /// <inheritdoc/>
        public void ReorderNext(ushort messageTypeId)
        {
            _pendingReorders.Add(messageTypeId);
        }

        /// <inheritdoc/>
        public void DropSnapshotFragment(ushort fragmentIndex)
        {
            if (fragmentIndex == ushort.MaxValue)
            {
                _dropLastFragmentPending = true;
            }
            else
            {
                _pendingFragmentDrops.Add(fragmentIndex);
            }
        }

        /// <inheritdoc/>
        public void SetSequenceNumber(uint value)
        {
            if (!_hasEmittedAnyMessage)
            {
                // Not mid-stream yet — applies immediately, no tick boundary needed.
                _sequenceNumber = value;
                _pendingSequenceNumberOverride = null;
            }
            else
            {
                // Mid-stream — defer to the next AdvanceTickBoundary() call.
                _pendingSequenceNumberOverride = value;
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _pendingDrops.Clear();
            _pendingDelays.Clear();
            _pendingReorders.Clear();
            _pendingFragmentDrops.Clear();
            _dropLastFragmentPending = false;
            _pendingSequenceNumberOverride = null;
            _hasEmittedAnyMessage = false;
        }

        // ---------------------------------------------------------------------
        // Internal seam: consulted by the outbound send path once it exists
        // (future Networking Core story). Visible to IronGrind.Foundation.EditModeTests
        // via InternalsVisibleTo (src/Foundation/AssemblyInfo.cs) so this story's own
        // tests can verify fault-injection behavior without a real transport.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Reports whether the next outbound message of <paramref name="messageTypeId"/> should be
        /// dropped, consuming one unit of the pending drop count (checking an exact
        /// <paramref name="messageTypeId"/> match before falling back to
        /// <see cref="AnyMessageTypeId"/>).
        /// </summary>
        internal bool TryConsumeDropDecision(ushort messageTypeId)
        {
            if (TryConsumeFrom(_pendingDrops, messageTypeId))
                return true;

            return TryConsumeFrom(_pendingDrops, AnyMessageTypeId);
        }

        private static bool TryConsumeFrom(Dictionary<ushort, int> counts, ushort key)
        {
            if (counts.TryGetValue(key, out int remaining) && remaining > 0)
            {
                remaining--;
                if (remaining <= 0)
                    counts.Remove(key);
                else
                    counts[key] = remaining;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reports whether the next outbound message of <paramref name="messageTypeId"/> has a
        /// pending delay, consuming one unit of it and returning the delay in milliseconds.
        /// </summary>
        internal bool TryConsumeDelay(ushort messageTypeId, out int delayMs)
        {
            if (_pendingDelays.TryGetValue(messageTypeId, out Queue<int> queue) && queue.Count > 0)
            {
                delayMs = queue.Dequeue();
                if (queue.Count == 0)
                    _pendingDelays.Remove(messageTypeId);
                return true;
            }

            delayMs = 0;
            return false;
        }

        /// <summary>
        /// Reports whether the next two outbound messages of <paramref name="messageTypeId"/>
        /// should be reordered, consuming the pending reorder request.
        /// </summary>
        internal bool TryConsumeReorder(ushort messageTypeId)
        {
            return _pendingReorders.Remove(messageTypeId);
        }

        /// <summary>
        /// Reports whether the given fragment of a <c>ZoneStateSnapshot</c> transmission should be
        /// dropped. <paramref name="isLastFragment"/> lets the caller signal the dynamically
        /// determined last fragment, matching the <c>ushort.MaxValue</c> "drop the last fragment"
        /// sentinel regardless of the fragment's actual index.
        /// </summary>
        internal bool TryConsumeSnapshotFragmentDrop(ushort fragmentIndex, bool isLastFragment)
        {
            if (isLastFragment && _dropLastFragmentPending)
            {
                _dropLastFragmentPending = false;
                return true;
            }

            return _pendingFragmentDrops.Remove(fragmentIndex);
        }

        /// <summary>
        /// Applies any pending <see cref="SetSequenceNumber"/> override atomically. Must be called
        /// exactly once per server tick boundary, never mid-tick.
        /// </summary>
        internal void AdvanceTickBoundary()
        {
            if (_pendingSequenceNumberOverride.HasValue)
            {
                _sequenceNumber = _pendingSequenceNumberOverride.Value;
                _pendingSequenceNumberOverride = null;
            }
        }

        /// <summary>
        /// Returns the current outbound sequence number and increments the counter, wrapping from
        /// <see cref="uint.MaxValue"/> to <c>1</c> — never to <c>0</c> — per CR-NET-7.5:
        /// <c>SequenceNumber</c> starts at 1 per connection and <c>0</c> is reserved as
        /// "uninitialized," so it must never appear in a valid message even across wraparound
        /// (Story 005, <see cref="IronGrind.Networking.StaleDiscardComparer"/> zero-skip fix).
        /// </summary>
        internal uint ConsumeNextSequenceNumber()
        {
            _hasEmittedAnyMessage = true;
            uint current = _sequenceNumber;
            unchecked { _sequenceNumber++; }
            if (_sequenceNumber == 0)
            {
                _sequenceNumber = 1;
            }
            return current;
        }
    }
}
#endif

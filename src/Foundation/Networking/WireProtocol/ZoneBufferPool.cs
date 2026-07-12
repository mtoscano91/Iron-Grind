using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Manages up to <see cref="MAX_PLAYERS_PER_ZONE"/> pre-allocated <see cref="ClientBufferSet"/>
    /// instances for one zone instance (CR-NET-7.7 Buffer Allocation). All <c>ClientBufferSet</c>
    /// instances — and therefore all <c>MAX_PLAYERS_PER_ZONE × 3</c> backing <see cref="byte"/><c>[]</c>
    /// arrays — are allocated once, in this class's constructor, modeling "allocated when the zone
    /// instance is created" (CR-NET-7.7). Nothing in this class allocates on the hot per-tick path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pure data-structure plumbing:</b> this class has no NGO/session/connection integration —
    /// it does not know what a real network connection is. A future story wires <see cref="TryAllocate"/>/
    /// <see cref="Release"/> to real connection accept/close events; this story only proves the
    /// pool's capacity and exhaustion-signaling contract.
    /// </para>
    /// <para>
    /// <b>Buffer exhaustion policy (AC-BUF-1):</b> if <see cref="TryAllocate"/> is called when the
    /// pool is already at capacity, allocation fails (<see langword="false"/> return — the
    /// <c>TryAllocate</c> naming matches this codebase's existing <c>TryRead</c>/
    /// <c>TryValidateStatID</c> convention for "fails without throwing, caller checks the bool")
    /// and a <c>BufferPoolExhausted</c> critical anomaly is logged via <see cref="Debug.LogError"/>
    /// — the GDD explicitly calls this condition "critical" (a configuration error: the zone
    /// accepted more connections than it pre-allocated capacity for), unlike the
    /// <see cref="Debug.LogWarning"/>-level anomalies elsewhere in this folder.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pool = new ZoneBufferPool(ZoneBufferPool.MAX_PLAYERS_PER_ZONE);
    /// if (pool.TryAllocate(out int slotIndex, out ClientBufferSet buffers))
    /// {
    ///     // use buffers.RUBatchBuffer / CycleBroadcastBuffer / PositionBuffer for this connection
    /// }
    /// // ... later, on disconnect ...
    /// pool.Release(slotIndex);
    /// </code>
    /// </example>
    public sealed class ZoneBufferPool
    {
        /// <summary>
        /// Maximum concurrent players in a single zone instance (<c>design/registry/entities.yaml</c>,
        /// safe range [10, 100], project default 50). A compile-time structural constant, not
        /// read from external config, matching this folder's other wire-protocol constants (e.g.
        /// <see cref="PriorityPathQueue{T}.PRIORITY_PATH_CAP"/>). Changing this value requires a
        /// recompile and, above 50, a tick-budget re-verification (F-NET-6).
        /// </summary>
        public const int MAX_PLAYERS_PER_ZONE = 50;

        private readonly ClientBufferSet[] _pool;
        private readonly bool[] _allocated;
        private readonly int _capacity;

        /// <summary>The total number of pre-allocated <see cref="ClientBufferSet"/> slots in this pool.</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Pre-allocates <paramref name="capacity"/> <see cref="ClientBufferSet"/> instances
        /// (<paramref name="capacity"/> × 3 backing byte arrays total). Models "allocated when the
        /// zone instance is created" (CR-NET-7.7) — call once per zone instance.
        /// </summary>
        /// <param name="capacity">
        /// The number of client slots to pre-allocate. Defaults to <see cref="MAX_PLAYERS_PER_ZONE"/>.
        /// Must be positive.
        /// </param>
        /// <example>
        /// <code>
        /// var pool = new ZoneBufferPool(); // capacity == MAX_PLAYERS_PER_ZONE (50)
        /// var smallPool = new ZoneBufferPool(capacity: 10); // for a test fixture
        /// </code>
        /// </example>
        public ZoneBufferPool(int capacity = MAX_PLAYERS_PER_ZONE)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "ZoneBufferPool capacity must be positive.");
            }

            _capacity = capacity;
            _pool = new ClientBufferSet[capacity];
            _allocated = new bool[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _pool[i] = new ClientBufferSet();
            }
        }

        /// <summary>
        /// Attempts to allocate the next free <see cref="ClientBufferSet"/> slot for a new client
        /// connection. Returns <see langword="false"/> — and logs a <c>BufferPoolExhausted</c>
        /// critical anomaly — if the pool is already at capacity (AC-BUF-1). This models rejecting
        /// the connection at the transport layer before session establishment; the actual
        /// transport-level rejection is out of scope for this story.
        /// </summary>
        /// <param name="slotIndex">
        /// On success, the index to pass to <see cref="Release"/> when this connection closes.
        /// <c>-1</c> on failure.
        /// </param>
        /// <param name="bufferSet">On success, the allocated <see cref="ClientBufferSet"/>. <see langword="null"/> on failure.</param>
        /// <returns><see langword="true"/> if a slot was allocated; otherwise <see langword="false"/>.</returns>
        /// <example>
        /// <code>
        /// if (!pool.TryAllocate(out int slotIndex, out ClientBufferSet buffers))
        /// {
        ///     // reject the connection before session establishment
        /// }
        /// </code>
        /// </example>
        public bool TryAllocate(out int slotIndex, out ClientBufferSet bufferSet)
        {
            for (int i = 0; i < _capacity; i++)
            {
                if (!_allocated[i])
                {
                    _allocated[i] = true;
                    slotIndex = i;
                    bufferSet = _pool[i];
                    return true;
                }
            }

            Debug.LogError($"[ZoneBufferPool] BufferPoolExhausted (CRITICAL): attempted to allocate a ClientBufferSet " +
                $"beyond pre-allocated capacity ({_capacity} = MAX_PLAYERS_PER_ZONE × 1 ClientBufferSet each holding 3 buffers). " +
                "The connection must be rejected before session establishment (CR-NET-7.7).");
            slotIndex = -1;
            bufferSet = null;
            return false;
        }

        /// <summary>
        /// Releases the slot at <paramref name="slotIndex"/> (previously returned by
        /// <see cref="TryAllocate"/>) back to the pool, making it available for a future
        /// connection. Models "released when the zone closes" for the owning connection
        /// (CR-NET-7.7) — a future story calls this from the real connection-close event.
        /// </summary>
        /// <param name="slotIndex">The slot index previously returned by <see cref="TryAllocate"/>.</param>
        /// <example>
        /// <code>
        /// pool.Release(slotIndex);
        /// </code>
        /// </example>
        public void Release(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, $"slotIndex must be in [0, {_capacity}).");
            }

            _allocated[slotIndex] = false;
        }
    }
}

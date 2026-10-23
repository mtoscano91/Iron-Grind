using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The players that take part in the zone tick (CR-ZI-8 step 10, user decision 2026-10-23). An admitted player is
    /// added to a pending list with the tick it was admitted in; <see cref="ActivatePending"/>, called once per tick by
    /// <c>ZoneTickPipeline</c> step 3 (wired in Story 014), moves it into the active set only in a later tick. Admission
    /// completes in step 1 (the completion drain) of a tick, so the step-3 call of that same tick leaves it pending and
    /// the next tick's call activates it. A player whose slot was released before activation is dropped. All
    /// collections are sized to the pool's capacity in the constructor, so a tick allocates nothing. Tick thread only.
    /// </summary>
    public sealed class ZoneTickRoster
    {
        private readonly ZoneSlotPool _pool;
        private readonly IServerTickSource _ticks;
        private readonly List<EntityID> _pending;
        private readonly List<uint> _pendingTicks;
        private readonly HashSet<EntityID> _active;

        /// <summary>Creates an empty roster.</summary>
        /// <param name="pool">Used to check that a pending player still holds a slot.</param>
        /// <param name="ticks">Gives the admission tick recorded by <see cref="AddPending"/>.</param>
        /// <exception cref="System.ArgumentNullException">An argument is null.</exception>
        public ZoneTickRoster(ZoneSlotPool pool, IServerTickSource ticks)
        {
            _pool = pool ?? throw new System.ArgumentNullException(nameof(pool));
            _ticks = ticks ?? throw new System.ArgumentNullException(nameof(ticks));
            _pending = new List<EntityID>(pool.MaxPlayers);
            _pendingTicks = new List<uint>(pool.MaxPlayers);
            _active = new HashSet<EntityID>(pool.MaxPlayers);
        }

        /// <summary>Number of players waiting for a later tick.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>Number of players in the tick loop.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>
        /// Queues a player to enter the tick loop at the first <see cref="ActivatePending"/> of a later tick than the
        /// current one. A player already pending or active is ignored.
        /// </summary>
        /// <param name="entityId">The admitted player's EntityID.</param>
        public void AddPending(EntityID entityId)
        {
            if (!_pending.Contains(entityId) && !_active.Contains(entityId))
            {
                _pending.Add(entityId);
                _pendingTicks.Add(_ticks.ServerTickNumber);
            }
        }

        /// <summary>
        /// Moves every pending player admitted before <paramref name="currentTick"/> whose slot is still held into the
        /// active set, drops the ones whose slot was released, and keeps the ones admitted in
        /// <paramref name="currentTick"/>. Called once per tick before the tick systems run.
        /// </summary>
        /// <param name="currentTick">The tick that is running.</param>
        public void ActivatePending(uint currentTick)
        {
            int kept = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                EntityID id = _pending[i];
                uint admittedTick = _pendingTicks[i];
                if (!_pool.TryGetPlayerSlot(id, out _))
                {
                    continue;
                }

                if (admittedTick == currentTick)
                {
                    _pending[kept] = id;
                    _pendingTicks[kept] = admittedTick;
                    kept++;
                    continue;
                }

                _active.Add(id);
            }

            _pending.RemoveRange(kept, _pending.Count - kept);
            _pendingTicks.RemoveRange(kept, _pendingTicks.Count - kept);
        }

        /// <summary>True when the player is in the tick loop (activated and not deactivated).</summary>
        /// <param name="entityId">The player's EntityID.</param>
        public bool IsInTickLoop(EntityID entityId)
        {
            return _active.Contains(entityId);
        }

        /// <summary>Removes a player from the tick loop and from the pending list (the exit path, Story 013).</summary>
        /// <param name="entityId">The player's EntityID.</param>
        public void Deactivate(EntityID entityId)
        {
            _active.Remove(entityId);
            int index = _pending.IndexOf(entityId);
            if (index >= 0)
            {
                _pending.RemoveAt(index);
                _pendingTicks.RemoveAt(index);
            }
        }
    }
}

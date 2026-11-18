using System;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Production <see cref="IGhostStatusSource"/>: a player is a ghost when its pool slot is in
    /// <see cref="ZoneSessionState.Disconnected_SessionActive"/> (Story 003). Mobs and unknown ids are not ghosts.
    /// The lookup is O(1) and allocation-free.
    /// </summary>
    public sealed class ZoneSlotGhostStatusSource : IGhostStatusSource
    {
        private readonly ZoneSlotPool _pool;

        /// <summary>Creates the adapter over <paramref name="pool"/>.</summary>
        /// <param name="pool">The zone slot pool.</param>
        /// <exception cref="ArgumentNullException"><paramref name="pool"/> is null.</exception>
        public ZoneSlotGhostStatusSource(ZoneSlotPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        /// <inheritdoc />
        public bool IsGhost(EntityID id)
        {
            return _pool.TryGetPlayer(id, out ZonePlayerSlot slot)
                && slot.State == ZoneSessionState.Disconnected_SessionActive;
        }
    }
}

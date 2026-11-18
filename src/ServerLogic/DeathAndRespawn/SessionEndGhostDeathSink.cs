using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Production <see cref="IGhostDeathSink"/> (Death and Respawn Story 010, AC-7): maps the ghost's EntityID to its
    /// account through the zone slot pool and starts the ghost-death session end on the
    /// <see cref="SessionEndCoordinator"/>. Idempotent: a repeated death while the end is in flight starts nothing.
    /// </summary>
    public sealed class SessionEndGhostDeathSink : IGhostDeathSink
    {
        private const string LOG_PREFIX = "[SessionEndGhostDeathSink]";

        private readonly ZoneSlotPool _pool;
        private readonly SessionEndCoordinator _ends;

        /// <summary>Creates the sink.</summary>
        /// <param name="pool">The zone slot pool that maps EntityID to account.</param>
        /// <param name="ends">The session-end coordinator.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SessionEndGhostDeathSink(ZoneSlotPool pool, SessionEndCoordinator ends)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _ends = ends ?? throw new ArgumentNullException(nameof(ends));
        }

        /// <inheritdoc/>
        public void OnGhostDied(EntityID entity)
        {
            if (!_pool.TryGetPlayer(entity, out ZonePlayerSlot slot))
            {
                Debug.LogWarning(
                    $"{LOG_PREFIX} Ghost death for {entity} has no player slot; no session end was started.");
                return;
            }

            _ends.BeginGhostDeath(slot.Account.RawValue, entity);
        }
    }
}

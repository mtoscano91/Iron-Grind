using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Aggro candidates for one zone (Enemy AI Story 009, AC-5): players in session state
    /// <see cref="ZoneSessionState.Connected"/> that are not dead and have a movement position. Ghosts
    /// (Disconnected_SessionActive) are not candidates (CR-AI-10: a disconnected target is invalid); dead players
    /// (Dead or Respawning per the optional <see cref="IPlayerDeathStateReader"/>, Death and Respawn Story 007) are not
    /// either. Pool order, no allocation.
    /// </summary>
    /// <remarks>
    /// <see cref="CopyPlayers"/> lists only this zone's players; <see cref="TryGetPlayerPosition"/> does not check the zone,
    /// which is moot because a zone server process holds one zone's pool. <see cref="CopyPlayers"/> appends to the list
    /// without clearing it (the caller clears).
    /// </remarks>
    public sealed class ZoneAggroCandidateSource : IAggroCandidateSource
    {
        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly IZonePlayerPositionReader _positions;
        private readonly IPlayerDeathStateReader _deathStates;

        /// <summary>Creates the source.</summary>
        /// <param name="zone">The zone whose players are listed.</param>
        /// <param name="pool">The slot pool (pool order and session state). Must not be null.</param>
        /// <param name="positions">Player position reader. Must not be null.</param>
        /// <param name="deathStates">
        /// Optional dead-ness reader; null means nobody is dead here, and only <c>MobController</c>'s target HP check
        /// (HP at or below 0 is not a target) keeps dead players out of aggro.
        /// </param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ZoneAggroCandidateSource(ZoneID zone, ZoneSlotPool pool, IZonePlayerPositionReader positions,
            IPlayerDeathStateReader deathStates = null)
        {
            _zone = zone;
            _deathStates = deathStates;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        }

        /// <inheritdoc/>
        public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)
        {
            IReadOnlyList<EntityID> ids = _pool.GetPlayerEntityIDsInZone(_zone);
            for (int i = 0; i < ids.Count; i++)
            {
                EntityID id = ids[i];
                if (TryGetPlayerPosition(id, out Vector3 position))
                {
                    into.Add((id, position));
                }
            }
        }

        /// <inheritdoc/>
        public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
        {
            if (_pool.TryGetPlayer(id, out ZonePlayerSlot slot) && slot.State == ZoneSessionState.Connected
                && (_deathStates == null || !_deathStates.IsDead(id)))
            {
                return _positions.TryGetPosition(id, out position);
            }

            position = Vector3.zero;
            return false;
        }
    }
}

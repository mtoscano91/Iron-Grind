using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The player exit entry point of Zone Instancing: releases the slot of a character whose session ended
    /// (<c>zone-instancing.md</c> CR-ZI-14 exit, CR-ZI-17, EC-ZI-11, AC-ZI-13). Zone Instancing Story 013.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wiring (Story 014).</b> Pass <see cref="ReleasePlayer"/> as the <c>broadcastPlayerLeftZone</c> callback of
    /// <c>SessionEndCoordinator</c> / <c>ConnectionStateMachine</c> and pass a no-op as <c>removeEntityFromZone</c>.
    /// <see cref="ZoneEntryReclamation.LeaveZone"/> already removes the entity and sends the single
    /// <c>PlayerLeftZone</c>, so a second removal or broadcast would duplicate it. A clean logout reports
    /// <see cref="DisconnectType.Graceful"/>; a TTL expiry reports <see cref="DisconnectType.Timeout"/>.
    /// </para>
    /// <para>
    /// <b>Ordering.</b> <c>SessionEndCoordinator</c> calls the state machine, and so this method, only from the exit
    /// save's completion callback on the tick thread. The party exit notice therefore follows the save by construction.
    /// A clean logout whose save fails never reaches this method (the session stays Connected). The guarantee holds
    /// when the callback is wired through <c>SessionEndCoordinator</c>; calling the state machine directly from
    /// elsewhere does not save first.
    /// </para>
    /// <para>
    /// <b>One slot per character.</b> The lookup relies on admission's invariant that a character holds at most one
    /// slot (<see cref="ZoneEntryReclamation.PrepareHandshake"/>). The first match is released.
    /// </para>
    /// <para>Tick thread only; no <c>await</c>. The lookup scans the occupied player slots (at most
    /// <see cref="ZoneSlotPool.MAX_PLAYERS_PER_ZONE"/>) and does not use the connection directory, because a ghost's
    /// connection is gone.</para>
    /// </remarks>
    public sealed class ZonePlayerExit
    {
        private readonly ZoneSlotPool _pool;
        private readonly ZoneEntryReclamation _reclamation;

        /// <summary>Creates the exit service for one zone.</summary>
        /// <param name="pool">The slot pool of the zone.</param>
        /// <param name="reclamation">Owner of the shared leave path.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ZonePlayerExit(ZoneSlotPool pool, ZoneEntryReclamation reclamation)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _reclamation = reclamation ?? throw new ArgumentNullException(nameof(reclamation));
        }

        /// <summary>
        /// Frees the slot held by <paramref name="characterId"/> through <see cref="ZoneEntryReclamation.LeaveZone"/>
        /// (roster off, one <c>PlayerLeftZone</c> with <paramref name="disconnectType"/>, party exit notice for a formal
        /// party only, slot and directory entry freed, occupancy change raised). A character that holds no slot
        /// changes nothing and returns false. Its signature matches the state machine's
        /// <c>broadcastPlayerLeftZone</c> callback (see the class remarks for the wiring).
        /// </summary>
        /// <param name="characterId">The raw <c>CharacterID</c> value of the leaving character.</param>
        /// <param name="disconnectType">What <c>PlayerLeftZone</c> reports.</param>
        /// <returns>True when a slot was freed.</returns>
        public bool ReleasePlayer(uint characterId, DisconnectType disconnectType)
        {
            var character = new CharacterID(characterId);
            var players = _pool.GetPlayerEntityIDsInZone(_pool.Zone);
            EntityID found = EntityID.Invalid;
            for (int i = 0; i < players.Count; i++)
            {
                if (_pool.TryGetPlayer(players[i], out ZonePlayerSlot slot) && slot.Character == character)
                {
                    found = players[i];
                    break;
                }
            }

            if (found == EntityID.Invalid)
            {
                return false;
            }

            return _reclamation.LeaveZone(found, disconnectType);
        }
    }
}

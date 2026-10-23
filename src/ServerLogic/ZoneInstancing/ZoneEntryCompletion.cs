using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Second half of player entry (<c>zone-instancing.md</c> CR-ZI-8 steps 6-10, CR-ZI-14 entry). Zone Instancing
    /// Story 010.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OnPlayerAdmitted"/> runs on the tick thread from <see cref="ZoneEntryAdmission"/>. It first maps the
    /// connection to its character in the directory, then: (6) sends the zone snapshot to the joiner, (7) sends
    /// <c>SessionReady</c> and <c>SkillCooldownSnapshot</c> to the joiner through <see cref="IZoneEntryResponder"/>,
    /// (8) calls <see cref="IPartyZonePresence.NotifyZoneEntry"/> when the character is in a formal party, (9) sends
    /// <c>PlayerJoinedZone</c> to every other player's connection, never the joiner, and (10) queues the entity for
    /// the tick loop in <see cref="ZoneTickRoster"/>.
    /// </para>
    /// <para>
    /// A missing state-source entry skips step 9 with one warning; step 10 still runs. An exception anywhere is caught
    /// and logged as an error, the remaining steps are skipped, and the call returns normally (the slot stays
    /// occupied; the exit path of Story 013 cleans up). The <c>PlayerJoinedZone</c> body is written into a buffer
    /// allocated once.
    /// </para>
    /// </remarks>
    public sealed class ZoneEntryCompletion : IZoneEntryContinuation
    {
        private const string LOG_PREFIX = "[ZoneEntryCompletion]";

        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly ZoneConnectionCharacterDirectory _directory;
        private readonly ZoneSnapshotBuilder _snapshot;
        private readonly IZoneEntryResponder _responder;
        private readonly IPartyZonePresence _party;
        private readonly IZoneEntityStateSource _state;
        private readonly IClientMessageOutbox _outbox;
        private readonly ZoneTickRoster _roster;
        private readonly byte[] _joinedBody = new byte[PlayerJoinedZone.MaxBodySize];

        /// <summary>Creates the continuation for one zone.</summary>
        /// <param name="zone">The zone this process hosts; not <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="pool">The slot pool (character lookup and the player roster).</param>
        /// <param name="directory">Receives the connection-to-character mapping.</param>
        /// <param name="snapshot">Sends the zone snapshot (step 6).</param>
        /// <param name="responder">Sends <c>SessionReady</c> and <c>SkillCooldownSnapshot</c> (step 7).</param>
        /// <param name="party">The party seam (step 8).</param>
        /// <param name="state">The read seam for name, level and position (step 9).</param>
        /// <param name="outbox">Receives the <c>PlayerJoinedZone</c> messages.</param>
        /// <param name="roster">Receives the entity for the tick loop (step 10).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is invalid.</exception>
        public ZoneEntryCompletion(ZoneID zone, ZoneSlotPool pool, ZoneConnectionCharacterDirectory directory,
            ZoneSnapshotBuilder snapshot, IZoneEntryResponder responder, IPartyZonePresence party,
            IZoneEntityStateSource state, IClientMessageOutbox outbox, ZoneTickRoster roster)
        {
            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            _zone = zone;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _responder = responder ?? throw new ArgumentNullException(nameof(responder));
            _party = party ?? throw new ArgumentNullException(nameof(party));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        /// <inheritdoc/>
        /// <remarks>Never throws: an exception is logged as an error and the remaining steps are skipped.</remarks>
        public void OnPlayerAdmitted(uint clientId, byte slot, EntityID entityId)
        {
            try
            {
                if (!_pool.TryGetPlayer(entityId, out ZonePlayerSlot player))
                {
                    Debug.LogError($"{LOG_PREFIX} admitted player {entityId} (client {clientId}) holds no slot in {_zone}; entry steps skipped.");
                    return;
                }

                _directory.Add(clientId, player.Character);

                _snapshot.SendSnapshot(clientId);

                _responder.SendSessionReady(clientId);
                _responder.SendSkillCooldownSnapshot(clientId, entityId);

                if (_party.TryGetFormalParty(player.Character, out PartyID party))
                {
                    _party.NotifyZoneEntry(party, entityId, _zone);
                }

                SendPlayerJoinedZone(clientId, entityId);

                _roster.AddPending(entityId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} entry of client {clientId} ({entityId}) failed; remaining steps skipped.\n{ex}");
            }
        }

        // Step 9: one body, enqueued for every other player's connection.
        private void SendPlayerJoinedZone(uint joinerClientId, EntityID entityId)
        {
            if (!_state.TryGetPlayerState(entityId, out ZonePlayerStateView view))
            {
                Debug.LogWarning($"{LOG_PREFIX} PlayerJoinedZoneSkipped: no state data for {entityId} in {_zone}; PlayerJoinedZone not sent.");
                return;
            }

            var message = new PlayerJoinedZone(entityId, view.CharacterName, view.Level, view.PosX, view.PosY, view.PosZ);
            int size = PlayerJoinedZoneCodec.WriteBody(_joinedBody, in message);
            IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
            for (int i = 0; i < players.Count; i++)
            {
                EntityID other = players[i];
                if (other.Equals(entityId) || !_pool.TryGetPlayer(other, out ZonePlayerSlot slot) || slot.ClientId == joinerClientId)
                {
                    continue;
                }

                _outbox.Enqueue(slot.ClientId, PlayerJoinedZone.MessageTypeId, new ReadOnlySpan<byte>(_joinedBody, 0, size), isCapExempt: false);
            }
        }
    }
}

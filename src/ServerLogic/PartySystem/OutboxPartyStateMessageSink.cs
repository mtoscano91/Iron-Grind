using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Production <see cref="IPartyStateMessageSink"/>: character to entity (registry), entity to client
    /// (<see cref="ZoneSlotPool"/>), then <see cref="ClientMessageOutbox"/> on the R-OD priority path, owning client
    /// only (Party System Story 008). A recipient with no entity, slot or outbox client is dropped silently.
    /// Allocation-free (stack buffers). Not wired into the zone composition here (Story 011).
    /// </summary>
    /// <remarks>A fault is logged once per sink and never thrown, mirroring <see cref="OutboxPartyInviteMessageSink"/>.</remarks>
    public sealed class OutboxPartyStateMessageSink : IPartyStateMessageSink
    {
        private readonly PartyRegistry _registry;
        private readonly ZoneSlotPool _pool;
        private readonly ClientMessageOutbox _outbox;
        private bool _loggedFault;

        /// <summary>Creates the sink.</summary>
        /// <param name="registry">Resolves a character to its entity.</param>
        /// <param name="pool">The zone's slot pool; resolves an entity to its client.</param>
        /// <param name="outbox">The priority-path outbox.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public OutboxPartyStateMessageSink(PartyRegistry registry, ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void SendStateUpdate(CharacterID recipient, in PartyStateUpdate message)
        {
            try
            {
                if (!TryGetClient(recipient, out uint clientId))
                {
                    return;
                }

                Span<byte> buffer = stackalloc byte[PartyStateUpdate.BodySize];
                if (message.Write(buffer))
                {
                    _outbox.Enqueue(clientId, PartyStateUpdate.MessageTypeId,
                        buffer.Slice(0, PartyStateUpdate.BodySize), isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendStateUpdateThrew", ex);
            }
        }

        /// <inheritdoc />
        public void SendDisbanded(CharacterID recipient, PartyID partyId)
        {
            try
            {
                if (!TryGetClient(recipient, out uint clientId))
                {
                    return;
                }

                Span<byte> buffer = stackalloc byte[PartyDisbanded.BodySize];
                var message = new PartyDisbanded(partyId.RawValue);
                if (message.Write(buffer))
                {
                    _outbox.Enqueue(clientId, PartyDisbanded.MessageTypeId,
                        buffer.Slice(0, PartyDisbanded.BodySize), isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendDisbandedThrew", ex);
            }
        }

        private void LogFaultOnce(string what, Exception ex)
        {
            if (_loggedFault)
            {
                return;
            }

            _loggedFault = true;
            Debug.LogError(PartyBroadcastStep.LOG_PREFIX + what + ": " + ex.GetType().Name + ": " + ex.Message
                + " (logged once).");
        }

        private bool TryGetClient(CharacterID character, out uint clientId)
        {
            if (_registry.TryGetEntity(character, out EntityID entity)
                && _pool.TryGetPlayer(entity, out ZonePlayerSlot slot)
                && _outbox.TryGetSequenceCounter(slot.ClientId, out _))
            {
                clientId = slot.ClientId;
                return true;
            }

            clientId = 0u;
            return false;
        }
    }
}

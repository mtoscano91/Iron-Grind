using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Production <see cref="IPartyInviteMessageSink"/>: character to entity (registry), entity to client
    /// (<see cref="ZoneSlotPool"/>), then <see cref="ClientMessageOutbox"/> on the R-OD priority path, owning client
    /// only (Party System Story 007). A recipient with no entity, slot or outbox client is dropped silently.
    /// Allocation-free (stack buffers). Not wired into the zone composition here (Story 011).
    /// </summary>
    /// <remarks>
    /// A fault is logged once per sink and never thrown, mirroring <c>DeathMessageEmitter</c>. The catch is
    /// defensive: <see cref="ClientMessageOutbox"/> buffers in <c>Enqueue</c> and contains its own faults (send
    /// faults surface at <c>Flush</c>), so it is not expected to fire. Tick thread only.
    /// </remarks>
    public sealed class OutboxPartyInviteMessageSink : IPartyInviteMessageSink
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
        public OutboxPartyInviteMessageSink(PartyRegistry registry, ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void SendInviteReceived(CharacterID recipient, in PartyInviteReceived message)
        {
            try
            {
                if (!TryGetClient(recipient, out uint clientId))
                {
                    return;
                }

                Span<byte> buffer = stackalloc byte[PartyInviteReceived.MaxBodySize];
                if (message.Write(buffer, out int written))
                {
                    _outbox.Enqueue(clientId, PartyInviteReceived.MessageTypeId, buffer.Slice(0, written),
                        isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendInviteReceivedThrew", ex);
            }
        }

        /// <inheritdoc />
        public void SendInviteResult(CharacterID recipient, PartyInviteResultCode code, CharacterID target)
        {
            try
            {
                if (!TryGetClient(recipient, out uint clientId))
                {
                    return;
                }

                Span<byte> buffer = stackalloc byte[PartyInviteResult.BodySize];
                var result = new PartyInviteResult(code, target.RawValue);
                if (result.Write(buffer))
                {
                    _outbox.Enqueue(clientId, PartyInviteResult.MessageTypeId,
                        buffer.Slice(0, PartyInviteResult.BodySize), isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("SendInviteResultThrew", ex);
            }
        }

        private void LogFaultOnce(string what, Exception ex)
        {
            if (_loggedFault)
            {
                return;
            }

            _loggedFault = true;
            Debug.LogError(PartyInviteService.LOG_PREFIX + what + ": " + ex.GetType().Name + ": " + ex.Message
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

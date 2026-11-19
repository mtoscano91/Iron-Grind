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
    /// Production <see cref="IPartyXpIneligibleNoticeSink"/> (Party System Story 011 AC-6, CR-PS-5): character to
    /// entity (registry), entity to client (<see cref="ZoneSlotPool"/>), then <see cref="ClientMessageOutbox"/> with
    /// <see cref="PartyXpIneligibleNotice"/> (0xE178, body <c>partyId</c>), owning client only, not cap-exempt.
    /// A character that does not resolve to a client (for example a member in disconnect grace) is dropped silently,
    /// as is a notice whose body fails to write. Allocation-free (stack buffer).
    /// </summary>
    /// <remarks>A fault is logged once per sink and never thrown, mirroring <see cref="OutboxPartyStateMessageSink"/>.</remarks>
    public sealed class OutboxPartyXpIneligibleNoticeSink : IPartyXpIneligibleNoticeSink
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
        public OutboxPartyXpIneligibleNoticeSink(PartyRegistry registry, ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public void Notify(CharacterID character, PartyID partyId)
        {
            try
            {
                if (!_registry.TryGetEntity(character, out EntityID entity)
                    || !_pool.TryGetPlayer(entity, out ZonePlayerSlot slot)
                    || !_outbox.TryGetSequenceCounter(slot.ClientId, out _))
                {
                    return;
                }

                Span<byte> buffer = stackalloc byte[PartyXpIneligibleNotice.BodySize];
                var message = new PartyXpIneligibleNotice(partyId.RawValue);
                if (message.Write(buffer))
                {
                    _outbox.Enqueue(slot.ClientId, PartyXpIneligibleNotice.MessageTypeId,
                        buffer.Slice(0, PartyXpIneligibleNotice.BodySize), isCapExempt: false);
                }
            }
            catch (Exception ex)
            {
                LogFaultOnce("NotifyThrew", ex);
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
    }
}

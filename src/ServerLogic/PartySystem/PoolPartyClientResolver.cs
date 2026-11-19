using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Production <see cref="IPartyClientResolver"/>: character to entity (<see cref="PartyRegistry"/>), entity to
    /// client (<see cref="ZoneSlotPool"/>), then a check that <see cref="ClientMessageOutbox"/> knows the client
    /// (the same lookup as <see cref="OutboxPartyStateMessageSink"/>; Party System Story 010). Allocation-free.
    /// </summary>
    public sealed class PoolPartyClientResolver : IPartyClientResolver
    {
        private readonly PartyRegistry _registry;
        private readonly ZoneSlotPool _pool;
        private readonly ClientMessageOutbox _outbox;

        /// <summary>Creates the resolver.</summary>
        /// <param name="registry">Resolves a character to its entity.</param>
        /// <param name="pool">The zone's slot pool; resolves an entity to its client.</param>
        /// <param name="outbox">Tells whether the client has a connection.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PoolPartyClientResolver(PartyRegistry registry, ZoneSlotPool pool, ClientMessageOutbox outbox)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <inheritdoc />
        public bool TryGetClient(CharacterID character, out uint clientId)
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

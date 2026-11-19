using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The real <see cref="IPartyZonePresence"/> (Party System Story 009 AC-5, D7). A solo party counts as "not in
    /// a party". Exit sets the member's out-of-zone flag, entry clears it; an unknown party or entity is a no-op.
    /// </summary>
    public sealed class PartyZonePresence : IPartyZonePresence
    {
        private readonly PartyRegistry _registry;

        /// <summary>Creates the presence adapter.</summary>
        /// <param name="registry">The party registry.</param>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
        public PartyZonePresence(PartyRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <inheritdoc/>
        public bool TryGetFormalParty(CharacterID character, out PartyID party)
        {
            PartyID id = _registry.GetPartyID(character);
            if (_registry.TryGetParty(id, out PartyState state) && !state.IsSoloParty && !state.IsFreed)
            {
                party = id;
                return true;
            }

            party = PartyID.Uninitialized;
            return false;
        }

        /// <inheritdoc/>
        public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone)
        {
            SetOutOfZone(party, entityId, false);
        }

        /// <inheritdoc/>
        public void NotifyZoneExit(PartyID party, EntityID entityId)
        {
            SetOutOfZone(party, entityId, true);
        }

        private void SetOutOfZone(PartyID party, EntityID entity, bool outOfZone)
        {
            if (_registry.TryGetCharacter(entity, out CharacterID character)
                && _registry.GetPartyID(character).RawValue == party.RawValue)
            {
                _registry.SetOutOfZone(character, outOfZone);
            }
        }
    }
}

using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The narrow view of the Party System that Zone Instancing needs (CR-ZI-8 step 8; ADR-010 dated note 2026-10-23,
    /// user decision 2026-10-23). Reached by direct calls on an injected interface. Tick thread only.
    /// </summary>
    public interface IPartyZonePresence
    {
        /// <summary>
        /// Looks up the formal party of a character. A solo pseudo-party counts as "not in a party" and returns false.
        /// </summary>
        /// <param name="character">The character.</param>
        /// <param name="party">The party, or <see cref="PartyID.Uninitialized"/> when none.</param>
        /// <returns>True when the character is in a formal party.</returns>
        bool TryGetFormalParty(CharacterID character, out PartyID party);

        /// <summary>Tells the party that a member entered the zone.</summary>
        /// <param name="party">The member's party.</param>
        /// <param name="entityId">The member's EntityID.</param>
        /// <param name="zone">The zone entered.</param>
        void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone);

        /// <summary>Tells the party that a member left the zone (called by the exit path, Story 013).</summary>
        /// <param name="party">The member's party.</param>
        /// <param name="entityId">The member's EntityID.</param>
        void NotifyZoneExit(PartyID party, EntityID entityId);
    }
}

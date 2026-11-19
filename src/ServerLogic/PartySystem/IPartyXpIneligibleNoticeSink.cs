using IronGrind.Currency;
using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Receives the "you no longer earn party XP" notice (design/gdd/party-system.md CR-PS-5, last paragraph;
    /// Party System Story 003 AC-7). The production adapter (encode <c>PartyXpIneligibleNotice</c> 0xE178, resolve
    /// the client id, enqueue to the outbox) is Story 011; tests use a recording fake.
    /// </summary>
    public interface IPartyXpIneligibleNoticeSink
    {
        /// <summary>A member went from XP-eligible to XP-ineligible because the party's leader changed.</summary>
        /// <param name="character">The member to notify.</param>
        /// <param name="partyId">The party.</param>
        void Notify(CharacterID character, PartyID partyId);
    }
}

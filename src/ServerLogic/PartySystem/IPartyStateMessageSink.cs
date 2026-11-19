using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Outbound seam of the party state broadcast (Party System Story 008, TR-party-009 / TR-party-011). Each call
    /// goes to the owning client of <c>recipient</c> only; the caller has already filtered recipients to connected
    /// members (Online or Dead). Implementations drop silently when the recipient has no client. Tick thread only.
    /// </summary>
    public interface IPartyStateMessageSink
    {
        /// <summary>Sends a full <see cref="PartyStateUpdate"/> snapshot.</summary>
        /// <param name="recipient">The receiving member.</param>
        /// <param name="message">The snapshot.</param>
        void SendStateUpdate(CharacterID recipient, in PartyStateUpdate message);

        /// <summary>Sends a <see cref="PartyDisbanded"/> notice.</summary>
        /// <param name="recipient">The receiving member.</param>
        /// <param name="partyId">The party that was disbanded.</param>
        void SendDisbanded(CharacterID recipient, PartyID partyId);
    }
}

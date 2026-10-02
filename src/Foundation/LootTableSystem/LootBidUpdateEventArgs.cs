using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="ILootAuctionService.OnLootBidUpdate"/> (ADR-010 Tier 2; mirrors the
    /// LootBidUpdate wire message). The network layer sends it to the current members of
    /// <see cref="PartyId"/> (design/gdd/loot-table-system.md CR-LT-8).
    /// </summary>
    public readonly struct LootBidUpdateEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public LootBidUpdateEventArgs(GroundItemID groundItemId, CharacterID bidderId, uint bidAmount, PartyID partyId)
        {
            GroundItemId = groundItemId;
            BidderId = bidderId;
            BidAmount = bidAmount;
            PartyId = partyId;
        }

        /// <summary>The auctioned ground item.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The member whose bid was accepted.</summary>
        public CharacterID BidderId { get; }

        /// <summary>The accepted bid, in gold.</summary>
        public uint BidAmount { get; }

        /// <summary>The auction's party; the message goes to its current members.</summary>
        public PartyID PartyId { get; }
    }
}

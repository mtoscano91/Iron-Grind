using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="ILootAuctionService.OnAuctionResolved"/> (ADR-010 Tier 2; mirrors the
    /// AuctionResolved wire message). Raised once per auction, after the item and the gold have been
    /// dealt with (design/gdd/loot-table-system.md CR-LT-9, CR-LT-10). The network layer sends it to the
    /// current members of <see cref="PartyId"/>.
    /// </summary>
    public readonly struct AuctionResolvedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public AuctionResolvedEventArgs(
            GroundItemID groundItemId,
            CharacterID winnerCharacterId,
            uint goldPerMember,
            bool isRoundRobinFallback,
            PartyID partyId)
        {
            GroundItemId = groundItemId;
            WinnerCharacterId = winnerCharacterId;
            GoldPerMember = goldPerMember;
            IsRoundRobinFallback = isRoundRobinFallback;
            PartyId = partyId;
        }

        /// <summary>The auctioned ground item.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The winning bidder; <see cref="CharacterID.Invalid"/> on a round-robin fallback.</summary>
        public CharacterID WinnerCharacterId { get; }

        /// <summary>Gold each party member received from the winning bid; 0 on a round-robin fallback or when the share rounds down to 0.</summary>
        public uint GoldPerMember { get; }

        /// <summary>
        /// True when there was no winner (CR-LT-10). The item normally goes to the next member in the
        /// round-robin, announced by <see cref="IGroundItemService.OnGroundItemAssigned"/>; when nobody
        /// could be assigned it is removed and announced by <see cref="IGroundItemService.OnGroundItemDespawned"/>.
        /// </summary>
        public bool IsRoundRobinFallback { get; }

        /// <summary>The auction's party; the message goes to its current members.</summary>
        public PartyID PartyId { get; }
    }
}

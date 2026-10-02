namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Outcome of <see cref="ILootAuctionService.SubmitBid"/> (design/gdd/loot-table-system.md CR-LT-8).
    /// Only <see cref="Accepted"/> changes state and raises an event; every rejection is silent apart
    /// from this return value (and one info log line for <see cref="WindowClosed"/>).
    /// </summary>
    public enum LootBidResult : byte
    {
        /// <summary>The bid was stored and announced through <see cref="ILootAuctionService.OnLootBidUpdate"/>.</summary>
        Accepted = 0,

        /// <summary>The ground item is unknown or is not in the <see cref="GroundItemState.Auctioning"/> state.</summary>
        NotAuctioning = 1,

        /// <summary>The bid arrived after the auction's window close tick (AC-LT-17).</summary>
        WindowClosed = 2,

        /// <summary>The bidder is not a current member of the auction's party (CR-LT-8 eligibility).</summary>
        NotPartyMember = 3,

        /// <summary>The bid is below the item's <c>SellPriceGold</c> (CR-LT-8 floor).</summary>
        BelowFloor = 4,

        /// <summary>The bid is not higher than the bidder's current bid on this auction (CR-LT-8 revision).</summary>
        NotHigherThanOwnBid = 5,
    }
}

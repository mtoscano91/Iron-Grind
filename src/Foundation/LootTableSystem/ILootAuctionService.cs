using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Rare drop auction bids (design/gdd/loot-table-system.md CR-LT-8, CR-LT-15 server authority).
    /// <c>LootBidRequest</c> arrives through a network handler that only enqueues; the tick loop calls
    /// <see cref="SubmitBid"/> (ADR-010). Disposable because the implementation subscribes to the
    /// ground item service's despawn event.
    /// </summary>
    public interface ILootAuctionService : IDisposable
    {
        /// <summary>
        /// Raised synchronously inside <see cref="SubmitBid"/> for an accepted bid. The network layer
        /// turns it into <c>LootBidUpdate</c> for the current members of the event's party.
        /// </summary>
        event Action<LootBidUpdateEventArgs> OnLootBidUpdate;

        /// <summary>
        /// Validates a bid; first failure wins and a rejection changes nothing and raises nothing:
        /// the item must be <see cref="GroundItemState.Auctioning"/>; <paramref name="receivedTick"/>
        /// must not be newer than the window close tick (otherwise one info log line); the bidder must
        /// belong to the auction's party; the amount must reach the item's <c>SellPriceGold</c>; and it
        /// must be higher than the bidder's own current bid. The bidder's gold is not checked here.
        /// </summary>
        /// <param name="bidder">The bidding character.</param>
        /// <param name="groundItemId">The auctioned ground item.</param>
        /// <param name="bidAmount">The bid in gold.</param>
        /// <param name="receivedTick">The server tick at which the bid was received.</param>
        /// <returns>The outcome.</returns>
        LootBidResult SubmitBid(CharacterID bidder, GroundItemID groundItemId, uint bidAmount, uint receivedTick);

        /// <summary>Reads a bidder's stored bid on an auction.</summary>
        /// <param name="groundItemId">The auctioned ground item.</param>
        /// <param name="bidder">The bidding character.</param>
        /// <param name="bidAmount">The stored bid when found.</param>
        /// <param name="receivedTick">The tick at which the stored bid was received when found.</param>
        /// <returns>True when the bidder has a bid on that auction.</returns>
        bool TryGetBid(GroundItemID groundItemId, CharacterID bidder, out uint bidAmount, out uint receivedTick);
    }
}

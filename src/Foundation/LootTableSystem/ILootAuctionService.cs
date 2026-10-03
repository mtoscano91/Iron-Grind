using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Rare drop auction bids and resolution (design/gdd/loot-table-system.md CR-LT-8, CR-LT-9, CR-LT-10,
    /// CR-LT-12, CR-LT-15 server authority). <c>LootBidRequest</c> arrives through a network handler that
    /// only enqueues; the tick loop calls <see cref="SubmitBid"/> and <see cref="Tick"/> (ADR-010).
    /// Disposable because the implementation subscribes to the ground item service's events.
    /// </summary>
    public interface ILootAuctionService : IDisposable
    {
        /// <summary>
        /// Raised synchronously inside <see cref="SubmitBid"/> for an accepted bid. The network layer
        /// turns it into <c>LootBidUpdate</c> for the current members of the event's party.
        /// </summary>
        event Action<LootBidUpdateEventArgs> OnLootBidUpdate;

        /// <summary>
        /// Raised from <see cref="Tick"/> once per resolved auction, after the gold and the item have
        /// been dealt with and the auction's bids dropped. The network layer turns it into
        /// <c>AuctionResolved</c> for the current members of the event's party. On a round-robin
        /// fallback the winner is <see cref="CharacterID.Invalid"/> and the gold per member is 0.
        /// </summary>
        event Action<AuctionResolvedEventArgs> OnAuctionResolved;

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

        /// <summary>
        /// Resolves every open auction that is due on this server tick: an
        /// <see cref="GroundItemState.Auctioning"/> item whose window close tick or expiry tick has been
        /// reached (equality counts; whichever comes first) is resolved once (CR-LT-9, CR-LT-10,
        /// CR-LT-12). Bids from characters who have left the party are skipped; the rest are tried in
        /// order of amount (highest first), then received tick (earliest first), then arrival; the first
        /// whose gold debit succeeds wins, the winning bid is split among the party's current members,
        /// and the item is delivered to the winner. With no winner the item is assigned by round-robin
        /// and no gold moves. Allocates nothing on a tick where no auction is due. No-op after Dispose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The tick loop calls it once per server tick. Who gets the item and the gold does not depend
        /// on whether it runs before or after <see cref="IGroundItemService.Tick"/> in the same tick:
        /// the ground item service never despawns an auctioning item. Only the first proximity pickup
        /// of a round-robin fallback moves: it happens on this tick when this call runs first, on the
        /// next one otherwise.
        /// </para>
        /// <para>
        /// An auction always leaves <see cref="GroundItemState.Auctioning"/> when it is resolved. If a
        /// step fails with an exception after the bids were taken, the failure is logged and the item
        /// goes to the winner when one has already paid, and is removed otherwise;
        /// <see cref="OnAuctionResolved"/> is still raised. No gold is refunded.
        /// </para>
        /// </remarks>
        /// <param name="currentTick">The current server tick.</param>
        void Tick(uint currentTick);
    }
}

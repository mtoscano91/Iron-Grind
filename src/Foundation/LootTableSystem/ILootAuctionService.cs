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
        /// Raised from <see cref="Tick"/> or <see cref="ResolveAllForTeardown"/> once per auction, at its final outcome: after the gold and the
        /// item have been dealt with and the auction's bids dropped. An auction held in a winner grace
        /// (CR-LT-9.1) raises nothing until the grace ends in a payment or a disqualification and the
        /// rest of the bids are worked through. The network layer turns it into
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
        /// An auction that has closed and is waiting in a winner grace (CR-LT-9.1) returns
        /// <see cref="LootBidResult.WindowClosed"/> whatever the received tick.
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
        /// order of amount (highest first), then received tick (earliest first), then arrival. A bidder
        /// with a free bag whose gold debit succeeds wins: the winning bid is split among the party's
        /// current members and the item is delivered to the winner. A bidder with a full bag who can
        /// afford the bid and is connected is held for <c>AUCTION_WINNER_GRACE_TICKS</c> (CR-LT-9.1):
        /// they are told through <see cref="IGroundItemService.OnBagFullPickupBlocked"/>, no gold moves,
        /// and the auction resolves on the first tick their inventory changed and has a free slot, or
        /// moves to the next bid when they leave the party, the grace ends, or they can no longer pay.
        /// A full-bag bidder who cannot afford the bid or is not connected is skipped silently. With
        /// no winner the item is assigned by round-robin (with a fresh pickup window when a grace ran)
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
        /// An auction in a grace stays <see cref="GroundItemState.Auctioning"/> and is not despawned,
        /// even past its expiry tick. Otherwise an auction always leaves it when it is resolved. If a
        /// step fails with an exception after the bids were taken, the failure is logged and the item
        /// goes to the winner when one has already paid, and is removed otherwise;
        /// <see cref="OnAuctionResolved"/> is still raised. No gold is refunded.
        /// </para>
        /// </remarks>
        /// <param name="currentTick">The current server tick.</param>
        void Tick(uint currentTick);

        /// <summary>
        /// Zone teardown (Story 012, AC-LT-19, CR-LT-9, CR-LT-9.1, CR-LT-10): resolves every tracked
        /// auction whose item is <see cref="GroundItemState.Auctioning"/> now, whatever its window close
        /// tick or expiry tick, including one waiting in a winner grace. The bids are tried in order (a
        /// reserved grace bid first, then the rest); a bidder who has left the party is skipped. The free
        /// slot is read afresh for every bidder; a full bag is passed over at once, with no gold moved,
        /// no blocked notice and no new grace. A bidder with room who can pay wins, as in
        /// <see cref="Tick"/>. With no winner, and at least one bidder passed over for a full bag, the item
        /// is removed (no round-robin, the cursor is not advanced); otherwise the CR-LT-10 round-robin
        /// fallback assigns it with no pickup attempt. <see cref="OnAuctionResolved"/> is raised once per
        /// auction, with <c>IsRoundRobinFallback</c> true whenever there is no winner, and the auction
        /// is then forgotten. An auction whose item is not <see cref="GroundItemState.Auctioning"/> is
        /// left to <see cref="IGroundItemService.DespawnAll"/>. A re-entrant <see cref="Tick"/> or call
        /// does nothing. No-op after Dispose.
        /// </summary>
        /// <param name="teardownTick">The server tick of the teardown.</param>
        void ResolveAllForTeardown(uint teardownTick);
    }
}

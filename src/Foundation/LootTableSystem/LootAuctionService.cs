using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.Networking;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Rare drop auction bids (design/gdd/loot-table-system.md CR-LT-8, CR-LT-15 server authority;
    /// Story 010). Owns the bids and <see cref="SubmitBid"/>; the item's state, party and window close
    /// tick belong to <see cref="IGroundItemService"/>. Drops an auction's bids when its item despawns,
    /// so it subscribes to <see cref="IGroundItemService.OnGroundItemDespawned"/> and is
    /// <see cref="IDisposable"/> (ADR-010). Called from the tick loop only. Closing the window and
    /// choosing a winner are Story 011.
    /// </summary>
    public sealed class LootAuctionService : ILootAuctionService
    {
        private struct Bid
        {
            public CharacterID Bidder;
            public uint Amount;
            public uint ReceivedTick;
        }

        // Smallest bid ever accepted, whatever the item's SellPriceGold says.
        private const int MIN_BID = 1;

        private readonly IGroundItemService _groundItems;
        private readonly IPartyService _partyService;
        private readonly LootEquipmentCache _equipmentCache;
        private readonly Dictionary<GroundItemID, List<Bid>> _bids = new Dictionary<GroundItemID, List<Bid>>();
        private bool _disposed;

        /// <inheritdoc/>
        public event Action<LootBidUpdateEventArgs> OnLootBidUpdate;

        /// <summary>Creates the service and subscribes to the ground item despawn event.</summary>
        /// <param name="groundItems">Supplies the auction item's state, party and window close tick.</param>
        /// <param name="partyService">Answers which party a bidder belongs to.</param>
        /// <param name="equipmentCache">Supplies the item's <c>SellPriceGold</c> (the bid floor).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public LootAuctionService(IGroundItemService groundItems, IPartyService partyService, LootEquipmentCache equipmentCache)
        {
            _groundItems = groundItems ?? throw new ArgumentNullException(nameof(groundItems));
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _equipmentCache = equipmentCache ?? throw new ArgumentNullException(nameof(equipmentCache));
            _groundItems.OnGroundItemDespawned += HandleGroundItemDespawned;
        }

        /// <summary>
        /// Unsubscribes from the despawn event and drops every stored bid. Safe to call more than
        /// once. After it, <see cref="SubmitBid"/> rejects every bid as
        /// <see cref="LootBidResult.NotAuctioning"/>: nothing would clean a bid up any more.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _groundItems.OnGroundItemDespawned -= HandleGroundItemDespawned;
            _bids.Clear();
        }

        // Must never throw: it runs inside the ground item service's despawn announcement.
        private void HandleGroundItemDespawned(GroundItemDespawnedEventArgs args)
        {
            _bids.Remove(args.GroundItemId);
        }

        /// <inheritdoc/>
        public LootBidResult SubmitBid(CharacterID bidder, GroundItemID groundItemId, uint bidAmount, uint receivedTick)
        {
            if (_disposed
                || !_groundItems.TryGetGroundItem(groundItemId, out GroundItem item)
                || item.State != GroundItemState.Auctioning)
            {
                return LootBidResult.NotAuctioning;
            }

            // In time when the bid is not newer than the close tick (wraparound-safe; equality is in time).
            if (StaleDiscardComparer.IsNewerVersion(item.WindowCloseTick, receivedTick))
            {
                UnityEngine.Debug.Log(
                    $"[LootAuctionService] SubmitBid: bid from {bidder} on {groundItemId} rejected; received tick {receivedTick} is after window close tick {item.WindowCloseTick}.");
                return LootBidResult.WindowClosed;
            }

            if (bidder == CharacterID.Invalid || _partyService.GetPartyID(bidder) != item.PartyId)
            {
                return LootBidResult.NotPartyMember;
            }

            // A data fault, not a player error: only an item classified Rare from this cache is
            // ever auctioned, so a miss means the cache and the auction disagree. Nothing can be
            // auctioned without a price. ItemDefinition is a ScriptableObject: explicit null check.
            if (!_equipmentCache.TryGetEquipment(item.ItemId, out ItemDefinition definition) || definition == null)
            {
                UnityEngine.Debug.LogError(
                    $"[LootAuctionService] SubmitBid: auctioned {item.ItemId} ({groundItemId}) is not in the equipment cache; bid rejected.");
                return LootBidResult.NotAuctioning;
            }

            // The floor is the item's SellPriceGold, and never below MIN_BID: an item authored
            // with a price of 0 must not let a bid of 0 through (it would count as a valid bid).
            // SellPriceGold is an int: compare as long so no cast can overflow.
            long floor = definition.SellPriceGold > MIN_BID ? definition.SellPriceGold : MIN_BID;
            if (bidAmount < floor)
            {
                return LootBidResult.BelowFloor;
            }

            if (!_bids.TryGetValue(groundItemId, out List<Bid> bids))
            {
                bids = new List<Bid>();
                _bids[groundItemId] = bids;
            }
            int existing = IndexOfBidder(bids, bidder);
            if (existing >= 0 && bidAmount <= bids[existing].Amount)
            {
                return LootBidResult.NotHigherThanOwnBid;
            }

            // A raise moves the bidder to the end, so the list stays in arrival order of each
            // member's current bid. Story 011 breaks a tie on the received tick first; arrival
            // order is what is left when two bids share an amount and a tick.
            if (existing >= 0)
            {
                bids.RemoveAt(existing);
            }
            bids.Add(new Bid { Bidder = bidder, Amount = bidAmount, ReceivedTick = receivedTick });

            RaiseBidUpdate(new LootBidUpdateEventArgs(groundItemId, bidder, bidAmount, item.PartyId));
            return LootBidResult.Accepted;
        }

        /// <inheritdoc/>
        public bool TryGetBid(GroundItemID groundItemId, CharacterID bidder, out uint bidAmount, out uint receivedTick)
        {
            if (_bids.TryGetValue(groundItemId, out List<Bid> bids))
            {
                int index = IndexOfBidder(bids, bidder);
                if (index >= 0)
                {
                    bidAmount = bids[index].Amount;
                    receivedTick = bids[index].ReceivedTick;
                    return true;
                }
            }
            bidAmount = 0u;
            receivedTick = 0u;
            return false;
        }

        private static int IndexOfBidder(List<Bid> bids, CharacterID bidder)
        {
            for (int i = 0; i < bids.Count; i++)
            {
                if (bids[i].Bidder == bidder)
                {
                    return i;
                }
            }
            return -1;
        }

        // A subscriber failure must not undo a stored bid or change the result.
        private void RaiseBidUpdate(LootBidUpdateEventArgs args)
        {
            try
            {
                OnLootBidUpdate?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Rare drop auction bids and resolution (design/gdd/loot-table-system.md CR-LT-8, CR-LT-9, CR-LT-9.1,
    /// CR-LT-10, CR-LT-12, CR-LT-15 server authority; Stories 010, 011 and 013). Owns the bids,
    /// <see cref="SubmitBid"/> and the resolution in <see cref="Tick"/>; the item's state, party, window
    /// close tick and delivery belong to <see cref="IGroundItemService"/>. Resolution is resumable: a
    /// top bidder with a full bag is held in a winner grace, and the auction stays open until its
    /// final outcome. It subscribes to
    /// <see cref="IGroundItemService.OnGroundItemSpawned"/> (to know every open auction, also one with no
    /// bids), <see cref="IGroundItemService.OnGroundItemDespawned"/> and
    /// <see cref="IInventoryService.OnInventoryChanged"/> (to see a grace bidder make room), so it is
    /// <see cref="IDisposable"/> (ADR-010). Called from the tick loop only.
    /// </summary>
    public sealed class LootAuctionService : ILootAuctionService
    {
        private struct Bid
        {
            public CharacterID Bidder;
            public uint Amount;
            public uint ReceivedTick;
        }

        // An auction that has closed but has no final outcome yet. Created when the auction is first
        // due, removed at the final outcome (or when the item despawns).
        private sealed class PendingResolution
        {
            // The bids not yet tried, in resolution order.
            public List<Bid> Remaining;

            // True while a bidder is reserved: Reserved is their bid, GraceDeadline the tick it ends.
            public bool InGrace;
            public Bid Reserved;
            public uint GraceDeadline;

            // The reserved bidder's inventory changed and HasFreeSlot has not been read since. Kept
            // until it is read, so a tick that fails before the read does not lose the change.
            public bool SlotCheckDue;

            // At least one grace was started: the delivered item gets a fresh pickup window (CR-LT-9.1).
            public bool HadGrace;

            // The outcome, filled in as it becomes known (also read by the failure recovery).
            public CharacterID Winner = CharacterID.Invalid;
            public uint GoldPerMember;
        }

        // Smallest bid ever accepted, whatever the item's SellPriceGold says.
        private const int MIN_BID = 1;

        private readonly IGroundItemService _groundItems;
        private readonly IPartyService _partyService;
        private readonly LootEquipmentCache _equipmentCache;
        private readonly ICurrencyService _currencyService;
        private readonly IInventoryService _inventoryService;
        private readonly Dictionary<GroundItemID, List<Bid>> _bids = new Dictionary<GroundItemID, List<Bid>>();
        private readonly Dictionary<GroundItemID, PendingResolution> _pending = new Dictionary<GroundItemID, PendingResolution>();
        private readonly HashSet<GroundItemID> _openAuctions = new HashSet<GroundItemID>();
        private readonly List<GroundItemID> _dueScratch = new List<GroundItemID>();

        // Characters whose inventory changed since the last Tick took its snapshot. Written by the
        // inventory handler, so a change raised during a Tick is kept for the next one. Both sets are
        // reused, never reallocated.
        private readonly HashSet<CharacterID> _inventoryChanged = new HashSet<CharacterID>();
        private readonly HashSet<CharacterID> _changedSnapshot = new HashSet<CharacterID>();
        private bool _disposed;

        // True while Tick works through the due auctions. A Tick re-entered from a subscriber does
        // nothing: it would clear the snapshot and the due list the outer Tick is still reading.
        private bool _ticking;

        /// <inheritdoc/>
        public event Action<LootBidUpdateEventArgs> OnLootBidUpdate;

        /// <inheritdoc/>
        public event Action<AuctionResolvedEventArgs> OnAuctionResolved;

        /// <summary>
        /// Creates the service and subscribes to the ground item spawn and despawn events and the
        /// inventory change event. Create it before any auction is spawned: an auction that already
        /// exists is not tracked, so it would never be resolved.
        /// </summary>
        /// <param name="groundItems">Supplies the auction item's state, party and ticks, and delivers the item.</param>
        /// <param name="partyService">Answers which party a bidder belongs to, its members and its round-robin.</param>
        /// <param name="equipmentCache">Supplies the item's <c>SellPriceGold</c> (the bid floor).</param>
        /// <param name="currencyService">Debits the winning bid and pays the pool share to the members.</param>
        /// <param name="inventoryService">Answers whether a bidder has a free slot, and raises the change event a winner grace waits for.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public LootAuctionService(
            IGroundItemService groundItems,
            IPartyService partyService,
            LootEquipmentCache equipmentCache,
            ICurrencyService currencyService,
            IInventoryService inventoryService)
        {
            _groundItems = groundItems ?? throw new ArgumentNullException(nameof(groundItems));
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _equipmentCache = equipmentCache ?? throw new ArgumentNullException(nameof(equipmentCache));
            _currencyService = currencyService ?? throw new ArgumentNullException(nameof(currencyService));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _groundItems.OnGroundItemSpawned += HandleGroundItemSpawned;
            _groundItems.OnGroundItemDespawned += HandleGroundItemDespawned;
            _inventoryService.OnInventoryChanged += HandleInventoryChanged;
        }

        /// <summary>
        /// Unsubscribes from the ground item and inventory events and drops every stored bid, pending
        /// resolution and open auction. Safe
        /// to call more than once. After it, <see cref="SubmitBid"/> rejects every bid as
        /// <see cref="LootBidResult.NotAuctioning"/> and <see cref="Tick"/> does nothing: nothing
        /// would clean a bid up any more.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _groundItems.OnGroundItemSpawned -= HandleGroundItemSpawned;
            _groundItems.OnGroundItemDespawned -= HandleGroundItemDespawned;
            _inventoryService.OnInventoryChanged -= HandleInventoryChanged;
            _bids.Clear();
            _pending.Clear();
            _openAuctions.Clear();
            _inventoryChanged.Clear();
            _changedSnapshot.Clear();
        }

        // Must never throw (an exception would propagate into the Inventory System's mutating caller)
        // and must never call into the inventory (it throws on a synchronous mutation).
        private void HandleInventoryChanged(InventoryChangedEventArgs args)
        {
            _inventoryChanged.Add(args.CharacterID);
        }

        // Must never throw: it runs inside the ground item service's spawn announcement.
        private void HandleGroundItemSpawned(GroundItemSpawnedEventArgs args)
        {
            if (args.IsAuction)
            {
                _openAuctions.Add(args.GroundItemId);
            }
        }

        // Must never throw: it runs inside the ground item service's despawn announcement.
        private void HandleGroundItemDespawned(GroundItemDespawnedEventArgs args)
        {
            _bids.Remove(args.GroundItemId);
            _pending.Remove(args.GroundItemId);
            _openAuctions.Remove(args.GroundItemId);
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

            // An auction waiting in a winner grace has closed: its bids are taken, whatever the received tick.
            if (_pending.ContainsKey(groundItemId))
            {
                return LootBidResult.WindowClosed;
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
            // member's current bid. Resolution breaks a tie on the received tick first; arrival
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

        /// <inheritdoc/>
        public void Tick(uint currentTick)
        {
            if (_ticking)
            {
                return;
            }

            // The snapshot is taken first, also on the early-return paths, so the recorded set never grows.
            SnapshotInventoryChanges();
            if (_disposed || _openAuctions.Count == 0)
            {
                return;
            }

            // Only IDs are collected here; resolution raises events and calls other systems, so no
            // enumeration may be open while it runs. An auction in a grace is due on every tick: its
            // window has closed, so the test below is true anyway, but checking it first keeps the
            // rule explicit.
            _dueScratch.Clear();
            foreach (GroundItemID id in _openAuctions)
            {
                if (!_groundItems.TryGetGroundItem(id, out GroundItem item))
                {
                    _dueScratch.Add(id);
                }
                else if (item.State == GroundItemState.Auctioning
                    && (_pending.ContainsKey(id)
                        || StaleDiscardComparer.IsTickExpired(currentTick, item.WindowCloseTick)
                        || StaleDiscardComparer.IsTickExpired(currentTick, item.ExpiryTick)))
                {
                    _dueScratch.Add(id);
                }
            }
            if (_dueScratch.Count == 0)
            {
                return;
            }

            // The list is read in place (an auction in a grace is due on every tick, so a copy would
            // allocate at tick rate); _ticking keeps a re-entered Tick from touching it.
            _ticking = true;
            try
            {
                for (int i = 0; i < _dueScratch.Count; i++)
                {
                    try
                    {
                        ResolveAuction(_dueScratch[i], currentTick);
                    }
                    catch (Exception exception)
                    {
                        // One auction's failure must not stop the others.
                        UnityEngine.Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                _dueScratch.Clear();
                _ticking = false;
            }
        }

        /// <inheritdoc/>
        public void ResolveAllForTeardown(uint teardownTick)
        {
            if (_disposed)
            {
                return;
            }
            if (_ticking)
            {
                // Re-entered from a subscriber while auctions are being resolved: the outer call is
                // still reading the due list. Whatever it leaves behind is removed by DespawnAll.
                UnityEngine.Debug.LogWarning(
                    "[LootAuctionService] ResolveAllForTeardown: called while auctions are being resolved; skipped.");
                return;
            }

            // Only IDs are collected here; resolution raises events and calls other systems, so no
            // enumeration may be open while it runs.
            _dueScratch.Clear();
            foreach (GroundItemID id in _openAuctions)
            {
                _dueScratch.Add(id);
            }
            if (_dueScratch.Count == 0)
            {
                return;
            }

            _ticking = true;
            try
            {
                for (int i = 0; i < _dueScratch.Count; i++)
                {
                    try
                    {
                        ResolveAuctionForTeardown(_dueScratch[i], teardownTick);
                    }
                    catch (Exception exception)
                    {
                        // One auction's failure must not stop the others.
                        UnityEngine.Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                _dueScratch.Clear();
                _ticking = false;
            }
        }

        // Teardown version of ResolveAuction: the auction is resolved now, never held in a grace. A
        // reserved bid is tried first, then the rest; the outcome is always final and announced once.
        private void ResolveAuctionForTeardown(GroundItemID id, uint teardownTick)
        {
            if (!_groundItems.TryGetGroundItem(id, out GroundItem item) || item.State != GroundItemState.Auctioning)
            {
                // Gone, or still Spawning: DespawnAll deals with whatever remains.
                ForgetAuction(id);
                return;
            }

            IReadOnlyList<CharacterID> members;
            try
            {
                members = _partyService.GetPartyMembers(item.PartyId);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                AbandonAuctionAtTeardown(id, item.PartyId);
                return;
            }
            int memberCount = members == null ? 0 : members.Count;

            if (!_pending.TryGetValue(id, out PendingResolution pending))
            {
                pending = new PendingResolution { Remaining = TakeValidBidsInOrder(id, members, memberCount) };
                _pending[id] = pending;
                _bids.Remove(id);
            }
            else if (pending.InGrace)
            {
                // The reserved bidder goes first; the grace ends here whatever its deadline.
                pending.Remaining.Insert(0, pending.Reserved);
                pending.InGrace = false;
            }

            bool isFallback = false;
            try
            {
                RunTeardownSequence(id, item.PartyId, pending, members, memberCount, teardownTick, out isFallback);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                RecoverFailedResolution(id, pending.Winner, teardownTick, pending.HadGrace);
                isFallback = pending.Winner == CharacterID.Invalid;
            }

            ForgetAuction(id);
            RaiseResolved(new AuctionResolvedEventArgs(id, pending.Winner, pending.GoldPerMember, isFallback, item.PartyId));
        }

        // The party could not be read. In Tick the auction would be tried again on the next tick; at
        // teardown there is none, so the item is removed and the fallback outcome announced. No gold
        // has moved. DespawnAuctionItem never throws on a subscriber failure.
        private void AbandonAuctionAtTeardown(GroundItemID id, PartyID party)
        {
            _groundItems.DespawnAuctionItem(id);
            ForgetAuction(id);
            RaiseResolved(new AuctionResolvedEventArgs(id, CharacterID.Invalid, 0u, true, party));
        }

        // Each remaining bid in order: a bidder who left is skipped, a full bag (read afresh) is passed
        // over at once with no gold moved, a bidder with room pays or is disqualified. No winner: the item
        // is removed when a full bag was passed over (no round-robin, cursor untouched), else CR-LT-10.
        private void RunTeardownSequence(
            GroundItemID id,
            PartyID party,
            PendingResolution pending,
            IReadOnlyList<CharacterID> members,
            int memberCount,
            uint teardownTick,
            out bool isFallback)
        {
            isFallback = false;
            bool passedOverForFullBag = false;
            while (pending.Remaining.Count > 0)
            {
                Bid bid = pending.Remaining[0];
                pending.Remaining.RemoveAt(0);
                if (!IsMember(members, memberCount, bid.Bidder))
                {
                    continue;
                }

                if (!_inventoryService.HasFreeSlot(bid.Bidder))
                {
                    passedOverForFullBag = true;
                    continue;
                }

                if (TryPayAndAward(id, pending, bid, members, memberCount, teardownTick))
                {
                    return;
                }
            }

            isFallback = true;
            if (passedOverForFullBag)
            {
                _groundItems.DespawnAuctionItem(id);
            }
            else
            {
                AssignByRoundRobin(id, party, teardownTick, pending.HadGrace);
            }
        }

        // Moves the "inventory changed" set into the snapshot this tick reads. Events raised later in
        // the tick land in the live set for the next tick.
        private void SnapshotInventoryChanges()
        {
            _changedSnapshot.Clear();
            if (_inventoryChanged.Count == 0)
            {
                return;
            }
            _changedSnapshot.UnionWith(_inventoryChanged);
            _inventoryChanged.Clear();
        }

        // CR-LT-9 / CR-LT-9.1 / CR-LT-10. The party is read first: if that throws, nothing has changed
        // and the auction is tried again on the next tick. Once the bids are taken, the auction is
        // either held in a grace (no outcome, nothing announced) or final: whatever fails, the item
        // leaves Auctioning and the outcome is announced once.
        private void ResolveAuction(GroundItemID id, uint currentTick)
        {
            if (!_groundItems.TryGetGroundItem(id, out GroundItem item) || item.State != GroundItemState.Auctioning)
            {
                ForgetAuction(id);
                return;
            }

            // Booked before the party read: if that read throws, the change is still known next tick.
            if (_pending.TryGetValue(id, out PendingResolution pending)
                && pending.InGrace
                && _changedSnapshot.Contains(pending.Reserved.Bidder))
            {
                pending.SlotCheckDue = true;
            }

            IReadOnlyList<CharacterID> members = _partyService.GetPartyMembers(item.PartyId);
            int memberCount = members == null ? 0 : members.Count;

            if (pending == null)
            {
                pending = new PendingResolution { Remaining = TakeValidBidsInOrder(id, members, memberCount) };
                _pending[id] = pending;
                _bids.Remove(id);
            }

            bool isFallback = false;
            bool final;
            try
            {
                final = Advance(id, item.PartyId, pending, members, memberCount, currentTick, out isFallback);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                RecoverFailedResolution(id, pending.Winner, currentTick, pending.HadGrace);
                isFallback = pending.Winner == CharacterID.Invalid;
                final = true;
            }

            if (!final)
            {
                return;
            }

            ForgetAuction(id);
            RaiseResolved(new AuctionResolvedEventArgs(id, pending.Winner, pending.GoldPerMember, isFallback, item.PartyId));
        }

        // Runs the grace check, then the close sequence. Returns true when the auction reached its
        // final outcome, false when it is (still) held in a grace.
        private bool Advance(
            GroundItemID id,
            PartyID party,
            PendingResolution pending,
            IReadOnlyList<CharacterID> members,
            int memberCount,
            uint currentTick,
            out bool isFallback)
        {
            isFallback = false;
            if (pending.InGrace)
            {
                Bid reserved = pending.Reserved;
                bool stillMember = IsMember(members, memberCount, reserved.Bidder);
                if (stillMember && !StaleDiscardComparer.IsTickExpired(currentTick, pending.GraceDeadline))
                {
                    // Only a bag that changed and now has room can end the wait; a disconnect changes nothing.
                    if (!pending.SlotCheckDue)
                    {
                        return false;
                    }
                    pending.SlotCheckDue = false;
                    if (!_inventoryService.HasFreeSlot(reserved.Bidder))
                    {
                        return false;
                    }
                    pending.InGrace = false;
                    if (TryPayAndAward(id, pending, reserved, members, memberCount, currentTick))
                    {
                        return true;
                    }
                }
                pending.InGrace = false;
            }

            return RunCloseSequence(id, party, pending, members, memberCount, currentTick, out isFallback);
        }

        // CR-LT-9.1, each remaining bid in order: pay a bidder with room, skip one who cannot pay or is
        // gone, hold the first full-bag bidder who can pay. No bid left: the round-robin fallback.
        private bool RunCloseSequence(
            GroundItemID id,
            PartyID party,
            PendingResolution pending,
            IReadOnlyList<CharacterID> members,
            int memberCount,
            uint currentTick,
            out bool isFallback)
        {
            isFallback = false;
            while (pending.Remaining.Count > 0)
            {
                Bid bid = pending.Remaining[0];
                pending.Remaining.RemoveAt(0);
                if (!IsMember(members, memberCount, bid.Bidder))
                {
                    continue;
                }

                if (_inventoryService.HasFreeSlot(bid.Bidder))
                {
                    if (TryPayAndAward(id, pending, bid, members, memberCount, currentTick))
                    {
                        return true;
                    }
                    continue;
                }

                if (_currencyService.GetBalance(bid.Bidder) < bid.Amount || !_partyService.IsMemberConnected(bid.Bidder))
                {
                    continue;
                }

                pending.InGrace = true;
                pending.SlotCheckDue = false;
                pending.Reserved = bid;
                pending.GraceDeadline = unchecked(currentTick + (uint)LootTableConstants.AUCTION_WINNER_GRACE_TICKS);
                pending.HadGrace = true;
                _groundItems.RaiseAuctionGraceBlocked(id, bid.Bidder, (uint)LootTableConstants.AUCTION_WINNER_GRACE_TICKS);
                return false;
            }

            isFallback = true;
            AssignByRoundRobin(id, party, currentTick, pending.HadGrace);
            return true;
        }

        // The bidder has a free slot: debit, split the pool, deliver. False when the debit failed (the
        // bidder is disqualified). Winner and share are recorded before the pool is paid, so a failure
        // after the debit still gives the item to the bidder who paid.
        private bool TryPayAndAward(
            GroundItemID id,
            PendingResolution pending,
            Bid bid,
            IReadOnlyList<CharacterID> members,
            int memberCount,
            uint currentTick)
        {
            if (!TrySpend(bid))
            {
                return false;
            }

            // Integer division is floor; the remainder is discarded. The bidder is a current
            // member, so memberCount is at least 1.
            uint goldPerMember = bid.Amount / (uint)memberCount;
            pending.Winner = bid.Bidder;
            pending.GoldPerMember = goldPerMember;
            PayPool(members, memberCount, goldPerMember);
            DeliverToWinner(id, bid, currentTick, pending.HadGrace);
            return true;
        }

        // The winner had a free slot when they were paid, so a failed pickup is an invariant violation
        // (the item stays assigned to them, nothing is refunded). The item being gone without
        // a delivery means the pickup threw and the ground item service removed it.
        private void DeliverToWinner(GroundItemID id, Bid winningBid, uint currentTick, bool hadGrace)
        {
            if (_groundItems.AwardAuctionItem(id, winningBid.Bidder, currentTick, hadGrace))
            {
                return;
            }

            if (_groundItems.TryGetGroundItem(id, out _))
            {
                UnityEngine.Debug.LogError(
                    $"[LootAuctionService] Resolve: winner {winningBid.Bidder} of {id} had a free slot but the pickup failed; the item stays assigned to them and the bid {winningBid.Amount} is not refunded.");
            }
            else
            {
                UnityEngine.Debug.LogError(
                    $"[LootAuctionService] Resolve: {id} was not delivered to winner {winningBid.Bidder}, who paid {winningBid.Amount}; the item was removed and the bid is not refunded.");
            }
        }

        // A step threw after the bids were taken. The item must not stay Auctioning: nothing would
        // ever resolve or despawn it. A winner who has already paid gets it; otherwise it is removed.
        // Never throws: the caller still has to announce the outcome.
        private void RecoverFailedResolution(GroundItemID id, CharacterID winner, uint currentTick, bool hadGrace)
        {
            try
            {
                if (!_groundItems.TryGetGroundItem(id, out GroundItem item) || item.State != GroundItemState.Auctioning)
                {
                    return;
                }

                if (winner != CharacterID.Invalid)
                {
                    UnityEngine.Debug.LogError(
                        $"[LootAuctionService] Resolve: {id} failed after winner {winner} paid; the item goes to the winner.");
                    _groundItems.AwardAuctionItem(id, winner, currentTick, hadGrace);
                }
                else
                {
                    UnityEngine.Debug.LogError(
                        $"[LootAuctionService] Resolve: {id} failed before it could be assigned; the item is removed.");
                    _groundItems.DespawnAuctionItem(id);
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // Drops bids of characters who left the party, then orders by amount (highest first), received
        // tick (earliest first) and arrival (the list order). A stable insertion sort keeps the arrival order.
        private List<Bid> TakeValidBidsInOrder(GroundItemID id, IReadOnlyList<CharacterID> members, int memberCount)
        {
            var ordered = new List<Bid>();
            if (!_bids.TryGetValue(id, out List<Bid> stored))
            {
                return ordered;
            }

            for (int i = 0; i < stored.Count; i++)
            {
                if (!IsMember(members, memberCount, stored[i].Bidder))
                {
                    continue;
                }
                Bid bid = stored[i];
                int position = ordered.Count;
                while (position > 0 && ComesBefore(bid, ordered[position - 1]))
                {
                    position--;
                }
                ordered.Insert(position, bid);
            }
            return ordered;
        }

        private static bool ComesBefore(Bid candidate, Bid other)
        {
            if (candidate.Amount != other.Amount)
            {
                return candidate.Amount > other.Amount;
            }
            // Earlier means the other bid's tick is newer (wraparound-safe; equal ticks are not earlier).
            return StaleDiscardComparer.IsNewerVersion(candidate.ReceivedTick, other.ReceivedTick);
        }

        private static bool IsMember(IReadOnlyList<CharacterID> members, int memberCount, CharacterID character)
        {
            for (int i = 0; i < memberCount; i++)
            {
                if (members[i] == character)
                {
                    return true;
                }
            }
            return false;
        }

        // Success wins; anything else disqualifies the bidder. Only an InsufficientFunds is expected.
        private bool TrySpend(Bid bid)
        {
            GoldMutationResult result = _currencyService.TrySpendGold(bid.Bidder, bid.Amount, GoldTransactionReason.AuctionBid);
            if (result.Success)
            {
                return true;
            }
            if (result.Error != GoldMutationError.InsufficientFunds)
            {
                UnityEngine.Debug.LogError(
                    $"[LootAuctionService] Resolve: TrySpendGold({bid.Bidder}, {bid.Amount}) failed with {result.Error}; the bidder is disqualified.");
            }
            return false;
        }

        // Every current member, the winner included, gets the share. A failed credit is logged and
        // the rest are still paid. Pays exactly the memberCount the share was divided by.
        private void PayPool(IReadOnlyList<CharacterID> members, int memberCount, uint goldPerMember)
        {
            if (goldPerMember == 0u)
            {
                return;
            }
            for (int i = 0; i < memberCount; i++)
            {
                GoldMutationResult result = _currencyService.AddGold(members[i], goldPerMember, GoldTransactionReason.MonsterDrop);
                if (!result.Success)
                {
                    UnityEngine.Debug.LogError(
                        $"[LootAuctionService] Resolve: AddGold({members[i]}, {goldPerMember}) failed with {result.Error}.");
                }
            }
        }

        // CR-LT-10, as LootDropDistributor.AssignByRoundRobin: read the cursor, resolve the member,
        // assign, then advance the cursor exactly once (also for an invalid slot, so the rotation never stalls).
        private void AssignByRoundRobin(GroundItemID id, PartyID party, uint currentTick, bool hadGrace)
        {
            int cursor = _partyService.GetRrNextIndex(party);
            CharacterID assignee = _partyService.GetMemberAtIndex(party, cursor);
            if (assignee == CharacterID.Invalid)
            {
                UnityEngine.Debug.LogError(
                    $"[LootAuctionService] Resolve: no member at round-robin cursor {cursor} of {party} for {id}; the item is removed.");
                _groundItems.DespawnAuctionItem(id);
            }
            else
            {
                _groundItems.AssignAuctionItem(id, assignee, currentTick, hadGrace);
            }
            _partyService.AdvanceRrNextIndex(party);
        }

        private void ForgetAuction(GroundItemID id)
        {
            _bids.Remove(id);
            _pending.Remove(id);
            _openAuctions.Remove(id);
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

        // A subscriber failure must not undo the resolution or stop the other due auctions.
        private void RaiseResolved(AuctionResolvedEventArgs args)
        {
            try
            {
                OnAuctionResolved?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}

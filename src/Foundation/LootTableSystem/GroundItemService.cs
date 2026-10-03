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
    /// Ground item lifecycle, TTL despawn, proximity pickup and bag-full recovery, and the state
    /// of a Rare drop under auction (design/gdd/loot-table-system.md CR-LT-7 common drop pickup,
    /// CR-LT-8 auction window, CR-LT-12 ground item timer, CR-LT-13 base rule, CR-LT-13.1 TTL pause
    /// on app background, CR-LT-13.2 blocked notice and in-radius retry, CR-LT-13.3 expiry warning,
    /// the GroundItem state table, CR-LT-15 server authority). The bids of an auction belong to
    /// <see cref="LootAuctionService"/>. Raises ADR-010 Tier 2 events; the
    /// network layer turns them into GroundItemSpawned / GroundItemDespawned / BagFullPickupBlocked /
    /// GroundItemExpiryWarning messages. Reaches Inventory, positions and item names through injected
    /// interfaces (ADR-010 Tier 1). Driven only by the server tick passed to <see cref="Tick"/>,
    /// never wall-clock time. Implements <see cref="IDisposable"/> because it subscribes to
    /// <see cref="IInventoryService.OnInventoryChanged"/> (ADR-010).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Tick"/> calls the position provider, <see cref="IInventoryService.HasFreeSlot"/> and
    /// <see cref="IInventoryService.Pickup"/>. A pickup raises the Inventory System's events
    /// synchronously, so a subscriber may call back into this service (including <see cref="Tick"/>)
    /// while a pickup is in flight; no record enumeration is open at that point.
    /// </para>
    /// <para>
    /// The inventory event handler only records which character's inventory changed. The Inventory
    /// System throws on a synchronous mutation from a subscriber, so the retry runs on the next
    /// <see cref="Tick"/>.
    /// </para>
    /// </remarks>
    public sealed class GroundItemService : IGroundItemService, IDisposable
    {
        // Mutable reference record: Tick advances state in place, so it never writes to the dictionary
        // while enumerating it and allocates nothing.
        private sealed class Record
        {
            public GroundItemID Id;
            public ItemID ItemId;
            public UnityEngine.Vector3 Position;
            public CharacterID AssignedTo;
            public uint SpawnTick;
            public uint ExpiryTick;
            public GroundItemState State;
            public int PauseBudgetRemaining;
            public bool AssigneeInside;

            // The last proximity pickup failed with InventoryFull while the assignee was inside the radius.
            public bool Blocked;

            // Sticky (CR-LT-13.1): a pickup of this item has failed with InventoryFull at least once.
            // Unlike Blocked it survives the assignee leaving the radius; it lives as long as the record.
            public bool BagFull;

            // Auction data (CR-LT-8). WindowCloseTick is set when the record leaves Spawning.
            public PartyID PartyId;
            public bool IsAuction;
            public uint WindowCloseTick;
        }

        private const int PICKUP_QUANTITY = 1;
        private const uint WARNING_TICKS = (uint)LootTableConstants.EXPIRY_WARNING_TICKS;

        private readonly LootEquipmentCache _equipmentCache;
        private readonly IInventoryService _inventoryService;
        private readonly ICharacterPositionProvider _positionProvider;
        private readonly IItemDatabase _itemDatabase;
        private readonly Dictionary<GroundItemID, Record> _items = new Dictionary<GroundItemID, Record>();
        private readonly List<GroundItemID> _expiredScratch = new List<GroundItemID>();
        private readonly List<GroundItemID> _claimScratch = new List<GroundItemID>();
        private readonly List<GroundItemID> _retryScratch = new List<GroundItemID>();
        private readonly List<GroundItemID> _warningScratch = new List<GroundItemID>();

        // Characters whose inventory changed since the last Tick took its snapshot. Written by the
        // inventory handler (also from inside this service's own Pickup calls), so a change raised
        // during a Tick lands here and is kept for the next one. Both sets are reused, never reallocated.
        private readonly HashSet<CharacterID> _inventoryChanged = new HashSet<CharacterID>();
        private readonly HashSet<CharacterID> _changedSnapshot = new HashSet<CharacterID>();

        // Server tick at which each currently backgrounded character's client went to the background
        // (CR-LT-13.1). One entry per character until the matching foreground call (or Dispose).
        private readonly Dictionary<CharacterID, uint> _backgroundedAtTick = new Dictionary<CharacterID, uint>();

        private uint _nextId = 1u;
        private bool _disposed;

        /// <inheritdoc/>
        public event Action<GroundItemSpawnedEventArgs> OnGroundItemSpawned;

        /// <inheritdoc/>
        public event Action<GroundItemDespawnedEventArgs> OnGroundItemDespawned;

        /// <inheritdoc/>
        public event Action<BagFullPickupBlockedEventArgs> OnBagFullPickupBlocked;

        /// <inheritdoc/>
        public event Action<GroundItemExpiryWarningEventArgs> OnGroundItemExpiryWarning;

        /// <inheritdoc/>
        public event Action<GroundItemAssignedEventArgs> OnGroundItemAssigned;

        /// <summary>Creates the ground item service and subscribes to the inventory change event.</summary>
        /// <param name="equipmentCache">Supplies the gear tier reported in the spawn event.</param>
        /// <param name="inventoryService">Receives the automatic pickup calls (CR-LT-7) and the change event.</param>
        /// <param name="positionProvider">Supplies the assignee's position for the pickup radius check.</param>
        /// <param name="itemDatabase">Supplies the display name carried by the blocked and warning notices.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public GroundItemService(
            LootEquipmentCache equipmentCache,
            IInventoryService inventoryService,
            ICharacterPositionProvider positionProvider,
            IItemDatabase itemDatabase)
        {
            _equipmentCache = equipmentCache ?? throw new ArgumentNullException(nameof(equipmentCache));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _positionProvider = positionProvider ?? throw new ArgumentNullException(nameof(positionProvider));
            _itemDatabase = itemDatabase ?? throw new ArgumentNullException(nameof(itemDatabase));
            _inventoryService.OnInventoryChanged += HandleInventoryChanged;
        }

        /// <summary>
        /// Unsubscribes from the inventory change event. Safe to call more than once. Call it at
        /// zone teardown and do not tick the service afterwards: it would still expire items, but
        /// it no longer sees inventory changes, so a blocked item would never get its in-radius retry.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _inventoryService.OnInventoryChanged -= HandleInventoryChanged;
            _backgroundedAtTick.Clear();
        }

        /// <summary>
        /// Records that <paramref name="characterId"/>'s client went to the background at server tick
        /// <paramref name="tick"/> (CR-LT-13.1). A second call before the matching foreground keeps the
        /// first tick. While the character is backgrounded, a bag-full item assigned to them with pause
        /// budget left is not despawned at its expiry tick: it expires at
        /// <c>expiryTick + min(currentTick - tick, PauseBudgetRemaining)</c> (hold-while-backgrounded).
        /// Nothing is written to the item here; <see cref="NotifyClientForegrounded"/> books the extension.
        /// An invalid <paramref name="characterId"/> (value 0) logs one error and records nothing.
        /// </summary>
        /// <remarks>
        /// Call it from the tick loop after the network layer has decoded and queued the
        /// <c>ClientBackgrounded</c> message, never from a network handler directly (ADR-010 Decision 5).
        /// </remarks>
        /// <param name="characterId">The character whose client was backgrounded.</param>
        /// <param name="tick">The server tick of the message.</param>
        public void NotifyClientBackgrounded(CharacterID characterId, uint tick)
        {
            if (characterId == CharacterID.Invalid)
            {
                UnityEngine.Debug.LogError("[GroundItemService] NotifyClientBackgrounded: characterId is invalid (0); nothing recorded.");
                return;
            }
            if (!_backgroundedAtTick.ContainsKey(characterId))
            {
                _backgroundedAtTick[characterId] = tick;
            }
        }

        /// <summary>
        /// Ends the background period of <paramref name="characterId"/> at server tick
        /// <paramref name="tick"/> (CR-LT-13.1). With <c>pausedTicks = tick - backgroundedAtTick</c>, every
        /// live item assigned to the character that has had a pickup fail on a full bag gets
        /// <c>extension = min(pausedTicks, PauseBudgetRemaining)</c>: its expiry tick moves by the
        /// extension and its pause budget shrinks by it. Items of other characters, and items that never
        /// failed on a full bag, are untouched. A call with no recorded background is ignored; a tick that
        /// is not at or after the recorded background tick extends nothing but still clears the record.
        /// </summary>
        /// <remarks>
        /// Call it from the tick loop after the network layer has decoded and queued the
        /// <c>ClientForegrounded</c> message, never from a network handler directly (ADR-010 Decision 5).
        /// </remarks>
        /// <param name="characterId">The character whose client returned to the foreground.</param>
        /// <param name="tick">The server tick of the message.</param>
        public void NotifyClientForegrounded(CharacterID characterId, uint tick)
        {
            EndBackgroundPeriod(characterId, tick);
        }

        /// <summary>
        /// Ends the background period of <paramref name="characterId"/> because the client disconnected
        /// or the character left the zone at server tick <paramref name="tick"/>. The pause earned up
        /// to that tick is booked exactly as <see cref="NotifyClientForegrounded"/> books it, and the
        /// record is cleared: from then on the item's timer runs normally, as the GDD says for a
        /// disconnected assignee. Without this call a background record would outlive the session,
        /// and a later background of the same character would be measured from the old tick.
        /// A call with no recorded background is ignored.
        /// </summary>
        /// <remarks>
        /// Call it from the tick loop when the network layer reports the disconnect, never from a
        /// network handler directly (ADR-010 Decision 5).
        /// </remarks>
        /// <param name="characterId">The character whose client disconnected or left the zone.</param>
        /// <param name="tick">The server tick of the disconnect.</param>
        public void NotifyClientDisconnected(CharacterID characterId, uint tick)
        {
            EndBackgroundPeriod(characterId, tick);
        }

        // Books min(pausedTicks, budget) on every bag-full item of the character and clears the
        // record. Only Assigned and Claiming items take a pause: an auction is never paused.
        private void EndBackgroundPeriod(CharacterID characterId, uint tick)
        {
            if (!_backgroundedAtTick.TryGetValue(characterId, out uint backgroundedAt))
            {
                return;
            }
            _backgroundedAtTick.Remove(characterId);
            if (!StaleDiscardComparer.IsTickExpired(tick, backgroundedAt))
            {
                return;
            }

            uint pausedTicks = unchecked(tick - backgroundedAt);
            foreach (KeyValuePair<GroundItemID, Record> pair in _items)
            {
                Record record = pair.Value;
                if (record.AssignedTo != characterId
                    || !record.BagFull
                    || (record.State != GroundItemState.Assigned && record.State != GroundItemState.Claiming)
                    || record.PauseBudgetRemaining <= 0)
                {
                    continue;
                }
                int budget = record.PauseBudgetRemaining;
                int extension = pausedTicks >= (uint)budget ? budget : (int)pausedTicks;
                record.ExpiryTick = unchecked(record.ExpiryTick + (uint)extension);
                record.PauseBudgetRemaining = budget - extension;
            }
        }

        // Must never throw (an exception would propagate into the Inventory System's mutating caller)
        // and must never call into the inventory (it throws on a synchronous mutation).
        private void HandleInventoryChanged(InventoryChangedEventArgs args)
        {
            _inventoryChanged.Add(args.CharacterID);
        }

        /// <inheritdoc/>
        public GroundItemID Spawn(ItemID itemId, UnityEngine.Vector3 position, CharacterID assignedTo, uint spawnTick)
        {
            if (itemId == ItemID.Invalid)
            {
                UnityEngine.Debug.LogError("[GroundItemService] Spawn: itemId is invalid (0); nothing spawned.");
                return GroundItemID.Invalid;
            }
            if (assignedTo == CharacterID.Invalid)
            {
                UnityEngine.Debug.LogError($"[GroundItemService] Spawn: assignedTo is invalid (0) for {itemId}; nothing spawned.");
                return GroundItemID.Invalid;
            }

            return SpawnRecord(itemId, position, assignedTo, PartyID.Uninitialized, false, spawnTick);
        }

        /// <inheritdoc/>
        public GroundItemID SpawnAuction(ItemID itemId, UnityEngine.Vector3 position, PartyID partyId, uint spawnTick)
        {
            if (itemId == ItemID.Invalid)
            {
                UnityEngine.Debug.LogError("[GroundItemService] SpawnAuction: itemId is invalid (0); nothing spawned.");
                return GroundItemID.Invalid;
            }
            if (partyId == PartyID.Uninitialized)
            {
                UnityEngine.Debug.LogError($"[GroundItemService] SpawnAuction: partyId is uninitialized (0) for {itemId}; nothing spawned.");
                return GroundItemID.Invalid;
            }

            return SpawnRecord(itemId, position, CharacterID.Invalid, partyId, true, spawnTick);
        }

        // Shared by Spawn and SpawnAuction: arguments are already validated.
        private GroundItemID SpawnRecord(
            ItemID itemId,
            UnityEngine.Vector3 position,
            CharacterID assignedTo,
            PartyID partyId,
            bool isAuction,
            uint spawnTick)
        {
            GroundItemID id = AllocateId();
            uint expiryTick = unchecked(spawnTick + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS);
            _items[id] = new Record
            {
                Id = id,
                ItemId = itemId,
                Position = position,
                AssignedTo = assignedTo,
                SpawnTick = spawnTick,
                ExpiryTick = expiryTick,
                State = GroundItemState.Spawning,
                PauseBudgetRemaining = LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS,
                PartyId = partyId,
                IsAuction = isAuction,
            };

            GearTier gearTier = GearTier.None;
            if (_equipmentCache.TryGetEquipment(itemId, out ItemDefinition definition)
                && definition != null && definition.EquipmentData != null)
            {
                gearTier = definition.EquipmentData.GearTier;
            }

            RaiseSpawned(new GroundItemSpawnedEventArgs(id, itemId, gearTier, isAuction, position, expiryTick, assignedTo, partyId));
            return id;
        }

        /// <inheritdoc/>
        public void Tick(uint currentTick)
        {
            // Order: snapshot of inventory changes, Spawning -> Assigned / Auctioning and proximity check,
            // in-radius retries, entry pickups, expiry warning, then expiry. A pickup on the expiry
            // tick is attempted (and a warning suppressed for a delivered item) before the item despawns.
            SnapshotInventoryChanges();
            CollectPickupTriggers(currentTick);

            // Take both lists before any HasFreeSlot / Pickup runs: either can re-enter Tick, and
            // the inner call clears and refills the scratch lists. The entry flags are already
            // set, so an entry lost here would not be seen again until the assignee re-entered.
            GroundItemID[] retries = TakeScratch(_retryScratch);
            GroundItemID[] claims = TakeScratch(_claimScratch);
            ProcessRetries(retries, currentTick);
            ProcessClaims(claims, currentTick);
            RaiseExpiryWarnings(currentTick);
            ExpireItems(currentTick);
        }

        // Empties a scratch list into an array the caller owns. No allocation when the list is empty.
        private static GroundItemID[] TakeScratch(List<GroundItemID> scratch)
        {
            if (scratch.Count == 0)
            {
                return Array.Empty<GroundItemID>();
            }
            GroundItemID[] taken = scratch.ToArray();
            scratch.Clear();
            return taken;
        }

        // Moves the "inventory changed" set into the snapshot this tick reads. Events raised later in
        // the tick (by this tick's own Pickup calls) land in the live set for the next tick.
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

        // Leaving Spawning (for Assigned, or Auctioning for an auction record) uses IsNewerVersion:
        // wraparound-safe, and equality means "not newer".
        // Only collects IDs: HasFreeSlot and Pickup must never run while this enumeration is open,
        // because Pickup raises OnInventoryChanged synchronously and a subscriber may call back into
        // this service.
        private void CollectPickupTriggers(uint currentTick)
        {
            _claimScratch.Clear();
            _retryScratch.Clear();
            foreach (KeyValuePair<GroundItemID, Record> pair in _items)
            {
                Record record = pair.Value;
                if (record.State == GroundItemState.Spawning
                    && StaleDiscardComparer.IsNewerVersion(record.SpawnTick, currentTick))
                {
                    if (record.IsAuction)
                    {
                        // CR-LT-8: the window runs from the tick the auction opens.
                        record.State = GroundItemState.Auctioning;
                        record.WindowCloseTick = unchecked(currentTick + (uint)LootTableConstants.AUCTION_WINDOW_TICKS);
                    }
                    else
                    {
                        record.State = GroundItemState.Assigned;
                    }
                }
                if (record.State == GroundItemState.Assigned)
                {
                    CollectTrigger(record);
                }
            }
        }

        // Edge-triggered entry: only an outside -> inside transition attempts a pickup. Leaving the
        // radius clears the blocked flag. A blocked item whose assignee stayed inside and whose
        // inventory changed is a retry candidate (the free-slot check happens after the enumeration).
        private void CollectTrigger(Record record)
        {
            bool inside = IsAssigneeInsideRadius(record);
            bool entered = inside && !record.AssigneeInside;
            record.AssigneeInside = inside;
            if (!inside)
            {
                record.Blocked = false;
            }

            if (entered)
            {
                _claimScratch.Add(record.Id);
            }
            else if (record.Blocked && _changedSnapshot.Contains(record.AssignedTo))
            {
                _retryScratch.Add(record.Id);
            }
        }

        // An unknown position (disconnected, not in the zone) counts as outside the radius. The
        // distance is 3D: height counts, so an item on another level is out of reach. A NaN
        // position compares false and so counts as outside.
        private bool IsAssigneeInsideRadius(Record record)
        {
            if (!_positionProvider.TryGetPosition(record.AssignedTo, out UnityEngine.Vector3 position))
            {
                return false;
            }
            float radius = LootTableConstants.PICKUP_RADIUS_UNITS;
            return (position - record.Position).sqrMagnitude <= radius * radius;
        }

        // In-radius retries run before the entry pickups of the same tick.
        private void ProcessRetries(GroundItemID[] retries, uint currentTick)
        {
            for (int i = 0; i < retries.Length; i++)
            {
                // HasFreeSlot is asked per candidate: an earlier retry may have used the free slot.
                if (_items.TryGetValue(retries[i], out Record record)
                    && record.State == GroundItemState.Assigned
                    && record.Blocked
                    && _inventoryService.HasFreeSlot(record.AssignedTo))
                {
                    TryClaim(retries[i], currentTick, true);
                }
            }
        }

        private void ProcessClaims(GroundItemID[] claims, uint currentTick)
        {
            for (int i = 0; i < claims.Length; i++)
            {
                TryClaim(claims[i], currentTick, false);
            }
        }

        // Assigned -> Claiming -> Inventory on success; back to Assigned (same assignee, same
        // ExpiryTick) on a failed result. Pickup is synchronous, so Claiming never outlives this call.
        // Returns true only when the item reached the inventory.
        private bool TryClaim(GroundItemID id, uint currentTick, bool isRetry)
        {
            if (!_items.TryGetValue(id, out Record record) || record.State != GroundItemState.Assigned)
            {
                return false;
            }

            record.State = GroundItemState.Claiming;
            if (!TryInvokePickup(record, out PickupResult result))
            {
                RemoveAfterThrownPickup(record);
                return false;
            }

            if (result.Success)
            {
                record.State = GroundItemState.Inventory;
                record.Blocked = false;
                _items.Remove(id);
                return true;
            }

            record.State = GroundItemState.Assigned;
            HandlePickupFailure(record, result.Reason, currentTick, isRetry);
            return false;
        }

        /// <inheritdoc/>
        public bool AssignAuctionItem(GroundItemID id, CharacterID assignee, uint currentTick)
        {
            if (!_items.TryGetValue(id, out Record record) || record.State != GroundItemState.Auctioning)
            {
                return false;
            }

            if (assignee == CharacterID.Invalid)
            {
                UnityEngine.Debug.LogError(
                    $"[GroundItemService] AssignAuctionItem: assignee is invalid (0) for {id}; nobody could claim it, so it is removed.");
                DespawnAuctionItem(id);
                return false;
            }

            ReassignAuctionRecord(record, assignee, currentTick);
            RaiseAssigned(new GroundItemAssignedEventArgs(id, assignee));
            return true;
        }

        /// <inheritdoc/>
        public bool AwardAuctionItem(GroundItemID id, CharacterID winner, uint currentTick)
        {
            if (winner == CharacterID.Invalid
                || !_items.TryGetValue(id, out Record record)
                || record.State != GroundItemState.Auctioning)
            {
                return false;
            }

            ReassignAuctionRecord(record, winner, currentTick);

            // The delivery below is this assignee's pickup attempt. Without the real "inside" flag a
            // winner standing on the item would count as newly entered on the next Tick and, with a
            // full bag, get the blocked notice a second time.
            record.AssigneeInside = IsAssigneeInsideRadius(record);
            return TryClaim(id, currentTick, false);
        }

        /// <inheritdoc/>
        public bool DespawnAuctionItem(GroundItemID id)
        {
            if (!_items.TryGetValue(id, out Record record) || record.State != GroundItemState.Auctioning)
            {
                return false;
            }

            record.State = GroundItemState.Despawned;
            _items.Remove(id);
            RaiseDespawned(new GroundItemDespawnedEventArgs(id));
            return true;
        }

        // Auctioning -> Assigned. Resets the per-assignment data, and gives the new assignee a full
        // pickup window when the stored expiry tick has already been reached (CR-LT-12).
        private static void ReassignAuctionRecord(Record record, CharacterID assignee, uint currentTick)
        {
            record.State = GroundItemState.Assigned;
            record.AssignedTo = assignee;
            record.WindowCloseTick = 0u;
            record.AssigneeInside = false;
            record.Blocked = false;
            record.BagFull = false;
            record.PauseBudgetRemaining = LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS;
            if (StaleDiscardComparer.IsTickExpired(currentTick, record.ExpiryTick))
            {
                record.ExpiryTick = unchecked(currentTick + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS);
            }
        }

        // A subscriber failure must not undo the assignment.
        private void RaiseAssigned(GroundItemAssignedEventArgs args)
        {
            try
            {
                OnGroundItemAssigned?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private bool TryInvokePickup(Record record, out PickupResult result)
        {
            try
            {
                result = _inventoryService.Pickup(record.AssignedTo, record.ItemId, PICKUP_QUANTITY);
                return true;
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                result = default;
                return false;
            }
        }

        // The Inventory System commits the item before it raises OnInventoryChanged, and a subscriber
        // exception propagates to the Pickup call. The item may therefore already be in the bag.
        // Keeping it on the ground could deliver it twice, so it is removed: losing a drop is the
        // lesser harm.
        private void RemoveAfterThrownPickup(Record record)
        {
            UnityEngine.Debug.LogError(
                $"[GroundItemService] Tick: pickup of {record.ItemId} for {record.AssignedTo} threw; whether it was delivered is unknown, so {record.Id} is removed to rule out a second delivery.");
            record.State = GroundItemState.Despawned;
            record.Blocked = false;
            _items.Remove(record.Id);
            RaiseDespawned(new GroundItemDespawnedEventArgs(record.Id));
        }

        // InventoryFull: the item is blocked. The notice goes out once, on the entry that found the
        // bag full (a failed retry is silent: the client's modal is already showing), and not when
        // the item expires on this same tick. Any other reason keeps the base behaviour: one error.
        private void HandlePickupFailure(Record record, PickupFailReason reason, uint currentTick, bool isRetry)
        {
            if (reason != PickupFailReason.InventoryFull)
            {
                record.Blocked = false;
                UnityEngine.Debug.LogError(
                    $"[GroundItemService] Tick: pickup of {record.ItemId} for {record.AssignedTo} failed with {reason}; the item stays assigned.");
                return;
            }

            record.Blocked = true;
            record.BagFull = true;

            // The suppression compares against the stored expiry tick. An item held past it while
            // its assignee is backgrounded (CR-LT-13.1) therefore gets no notice on a new entry:
            // accepted, because that client is in the background and the remaining time could not
            // be stated truthfully (the extension is only booked on foreground).
            if (!isRetry && !StaleDiscardComparer.IsTickExpired(currentTick, record.ExpiryTick))
            {
                RaiseBagFullBlocked(new BagFullPickupBlockedEventArgs(
                    record.Id,
                    record.ItemId,
                    LookUpDisplayName(record.ItemId),
                    unchecked(record.ExpiryTick - currentTick),
                    record.AssignedTo));
            }
        }

        // Equality on the remaining ticks makes the warning fire once per deadline. Runs after the
        // pickup step, so an item delivered on the threshold tick is gone and gets none; an item in
        // Claiming (visible only to a re-entrant Tick) gets none either.
        private void RaiseExpiryWarnings(uint currentTick)
        {
            // Only IDs are collected here: the name lookup is an external call and must not run
            // while the enumeration is open.
            _warningScratch.Clear();
            foreach (KeyValuePair<GroundItemID, Record> pair in _items)
            {
                Record record = pair.Value;
                if (record.State == GroundItemState.Assigned
                    && unchecked(record.ExpiryTick - currentTick) == WARNING_TICKS)
                {
                    _warningScratch.Add(record.Id);
                }
            }

            // Taken out: a re-entrant Tick from a handler would reuse the scratch list.
            GroundItemID[] warnings = TakeScratch(_warningScratch);
            for (int i = 0; i < warnings.Length; i++)
            {
                // An earlier handler may have removed or changed the item.
                if (_items.TryGetValue(warnings[i], out Record record) && record.State == GroundItemState.Assigned)
                {
                    RaiseExpiryWarning(new GroundItemExpiryWarningEventArgs(
                        record.Id,
                        record.ItemId,
                        LookUpDisplayName(record.ItemId),
                        WARNING_TICKS,
                        record.AssignedTo));
                }
            }
        }

        // A notice without a name is still useful, so a failing lookup gives an empty name and the
        // tick goes on. ItemDefinition is a ScriptableObject: explicit null checks, never ?. or ??.
        private string LookUpDisplayName(ItemID itemId)
        {
            try
            {
                if (_itemDatabase.TryGetItem(itemId, out ItemDefinition definition)
                    && definition != null && definition.DisplayName != null)
                {
                    return definition.DisplayName;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
            return string.Empty;
        }

        // Expiry uses IsTickExpired: wraparound-safe, and equality means "expired". A claim in
        // flight (only visible to a re-entrant Tick) is not destroyed. An auction is never despawned
        // here (AC-LT-16): the auction service resolves it, so it never disappears silently.
        private void ExpireItems(uint currentTick)
        {
            _expiredScratch.Clear();
            foreach (KeyValuePair<GroundItemID, Record> pair in _items)
            {
                Record record = pair.Value;
                if (record.State != GroundItemState.Claiming
                    && record.State != GroundItemState.Auctioning
                    && IsExpired(record, currentTick))
                {
                    record.State = GroundItemState.Despawned;
                    _expiredScratch.Add(record.Id);
                }
            }

            if (_expiredScratch.Count == 0)
            {
                return;
            }

            // Taken out: a re-entrant Tick from a handler would reuse the scratch list.
            GroundItemID[] expired = TakeScratch(_expiredScratch);
            for (int i = 0; i < expired.Length; i++)
            {
                _items.Remove(expired[i]);
            }
            for (int i = 0; i < expired.Length; i++)
            {
                RaiseDespawned(new GroundItemDespawnedEventArgs(expired[i]));
            }
        }

        // CR-LT-13.1 hold-while-backgrounded: only an item that has reached its stored expiry tick
        // consults the background records. A bag-full item with budget left whose assignee is
        // backgrounded expires at expiryTick + min(currentTick - backgroundedAtTick, budget). Nothing is
        // written here: the foreground call books the extension, so it is never counted twice.
        private bool IsExpired(Record record, uint currentTick)
        {
            if (!StaleDiscardComparer.IsTickExpired(currentTick, record.ExpiryTick))
            {
                return false;
            }
            // Only an Assigned item is held: an auction is never paused.
            if (!record.BagFull
                || record.State != GroundItemState.Assigned
                || record.PauseBudgetRemaining <= 0
                || !_backgroundedAtTick.TryGetValue(record.AssignedTo, out uint backgroundedAt))
            {
                return true;
            }

            uint accrued = 0u;
            if (StaleDiscardComparer.IsTickExpired(currentTick, backgroundedAt))
            {
                uint elapsed = unchecked(currentTick - backgroundedAt);
                uint budget = (uint)record.PauseBudgetRemaining;
                accrued = elapsed >= budget ? budget : elapsed;
            }
            return StaleDiscardComparer.IsTickExpired(currentTick, unchecked(record.ExpiryTick + accrued));
        }

        // The counter is scoped to the zone session. It never yields 0, and after a uint wrap it
        // skips any ID that still belongs to a live item, so a live record is never overwritten.
        private GroundItemID AllocateId()
        {
            GroundItemID id;
            do
            {
                id = new GroundItemID(_nextId);
                _nextId = unchecked(_nextId + 1u);
                if (_nextId == 0u)
                {
                    _nextId = 1u;
                }
            }
            while (_items.ContainsKey(id));
            return id;
        }

        // A subscriber failure must not lose the ID of a stored record or abort the zone tick.
        private void RaiseSpawned(GroundItemSpawnedEventArgs args)
        {
            try
            {
                OnGroundItemSpawned?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // The record is already removed: if this propagated, the remaining expired items would
        // never be announced and clients would keep showing them.
        private void RaiseDespawned(GroundItemDespawnedEventArgs args)
        {
            try
            {
                OnGroundItemDespawned?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // A subscriber failure must not abort the zone tick or skip the remaining pickups.
        private void RaiseBagFullBlocked(BagFullPickupBlockedEventArgs args)
        {
            try
            {
                OnBagFullPickupBlocked?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        // A subscriber failure must not abort the zone tick or skip the remaining warnings.
        private void RaiseExpiryWarning(GroundItemExpiryWarningEventArgs args)
        {
            try
            {
                OnGroundItemExpiryWarning?.Invoke(args);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Looks up a live ground item. A despawned (removed) item returns false.
        /// </summary>
        /// <param name="id">The ground item ID.</param>
        /// <param name="item">A snapshot of the record when found.</param>
        /// <returns>True when the item is live.</returns>
        public bool TryGetGroundItem(GroundItemID id, out GroundItem item)
        {
            if (_items.TryGetValue(id, out Record r))
            {
                item = new GroundItem(r.Id, r.ItemId, r.Position, r.AssignedTo, r.SpawnTick, r.ExpiryTick, r.State, r.PauseBudgetRemaining, r.PartyId, r.WindowCloseTick);
                return true;
            }
            item = default;
            return false;
        }
    }
}

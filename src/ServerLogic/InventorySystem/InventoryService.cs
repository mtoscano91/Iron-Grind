#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// In-memory, server-authoritative implementation of <see cref="IInventoryService"/> — the
    /// character's personal 20-slot item bag (GDD Rule 1).
    /// </summary>
    /// <remarks>
    /// <para><b>Persistence scope note:</b> Story 009 adds the GDD's <see cref="InventorySnapshot"/>
    /// save/load contract (<see cref="ExportSnapshot"/>/<see cref="ImportSnapshot"/>) as an
    /// in-memory value type — this class still holds all live state in-memory, exactly like
    /// <c>CurrencySystem</c> and <c>CharacterStats</c> before their respective persistence layers
    /// existed. Character Persistence (a system that does not exist yet in this codebase) owns
    /// mapping <see cref="InventorySnapshot"/> to/from the <c>character_records.inventory_slots</c>
    /// JSONB column (ADR-006) and the SQL/<c>SaveSession</c> orchestration — this class exposes
    /// only the snapshot type and its export/import.</para>
    ///
    /// <para><b>Lifecycle:</b> a <see cref="CharacterID"/> must be explicitly registered — via
    /// <see cref="RegisterCharacter"/> (fresh empty inventory) or <see cref="ImportSnapshot"/>
    /// (registers and seeds from a saved snapshot in one call) — before any read API returns
    /// meaningful data for it; an unregistered character is always a caller bug, never a
    /// player-facing condition, and is reported via <see cref="Debug.LogError(object)"/> on every
    /// affected read API. <see cref="UnregisterCharacter"/> is the counterpart that releases a
    /// character's inventory state entirely (e.g. on logout), after which it is unregistered again.</para>
    ///
    /// <para><b>Thread safety:</b> unlike <c>CurrencySystem</c> (which added per-character
    /// locking in its Story 004), this story does not require concurrent-call safety — no AC or
    /// Implementation Note in Story 001 calls for it. Storage uses plain
    /// <see cref="Dictionary{TKey,TValue}"/>. Revisit if/when a future story requires concurrent
    /// access, mirroring <c>CurrencySystem</c>'s precedent.</para>
    ///
    /// <para><b>Mutation seam contract:</b> every mutation entry point (Story 002
    /// <see cref="Pickup"/>, Story 004 <see cref="LockSlot"/>/<see cref="UnlockSlot"/>/
    /// <see cref="RemoveItem"/>, Story 005 <see cref="Discard"/>, Story 006 <see cref="Move"/>,
    /// Story 007 <see cref="MoveItemOut"/>/<see cref="MoveItemIn"/>/<see cref="ForceInsert"/>,
    /// Story 008 <see cref="SellItem"/>/<see cref="ConsumeItem"/>) MUST call <see cref="ThrowIfDispatching"/>
    /// first. Entry points that change slot <em>content</em> then record their per-slot results
    /// via <see cref="RecordSlotChange"/> and fire the broadcast via
    /// <see cref="EmitInventoryChanged"/> — record changes only once the mutation is
    /// unconditionally committing (all validation done), and if a mutation must abort after
    /// recording, it MUST call <see cref="DiscardPendingChanges"/> — the change buffer is
    /// service-wide, and leftover entries would otherwise be broadcast with a later dispatch.
    /// Pending changes are bound to a single character: recording for, or emitting to, a
    /// different character while changes are pending throws. <b>Exception:</b>
    /// <see cref="LockSlot"/>/<see cref="UnlockSlot"/> only flip the per-slot lock flag — they
    /// call <see cref="ThrowIfDispatching"/> for re-entrancy safety but never call
    /// <see cref="RecordSlotChange"/>/<see cref="EmitInventoryChanged"/>, since a lock/unlock
    /// alone is never broadcast (GDD Rule 5). Story 009's <see cref="ImportSnapshot"/> and
    /// <see cref="UnregisterCharacter"/> extend this same exception: both call
    /// <see cref="ThrowIfDispatching"/> first, but write slots/release state directly and never
    /// call <see cref="RecordSlotChange"/>/<see cref="EmitInventoryChanged"/> — a load or an
    /// unregister is never broadcast as a mutation.</para>
    /// </remarks>
    public sealed class InventoryService : IInventoryService
    {
        private readonly Dictionary<CharacterID, InventorySlot[]> _inventories = new Dictionary<CharacterID, InventorySlot[]>();
        private readonly Dictionary<CharacterID, bool[]> _locks = new Dictionary<CharacterID, bool[]>();

        // Reused, service-owned change buffer backing every InventoryChangedEventArgs dispatch
        // (ADR-010 Decision 3 — zero per-emit heap allocation). Capacity InventoryConstants.INVENTORY_SLOT_COUNT
        // because a single atomic mutation can touch at most every slot once. The buffer is
        // service-wide, so pending changes are bound to exactly one character at a time
        // (_pendingCharacterId) — see RecordSlotChange / EmitInventoryChanged guards.
        private readonly SlotChange[] _changeBuffer = new SlotChange[InventoryConstants.INVENTORY_SLOT_COUNT];
        private int _changeCount;
        private CharacterID _pendingCharacterId = CharacterID.Invalid;
        private bool _isDispatching;

        private readonly IItemDatabase _itemDatabase;

        // Reused, service-owned pickup plan (Story 002 performance note — zero heap allocation per
        // pickup): _pickupPlan[i] is slot i's planned post-pickup quantity, 0 = slot untouched.
        private readonly int[] _pickupPlan = new int[InventoryConstants.INVENTORY_SLOT_COUNT];

        private readonly Func<uint> _currentTick;

        // Injected upper bound for enhancement levels (Enhancement System's MAX_ENHANCEMENT_LEVEL
        // tuning knob). The Inventory System only uses it as a bound — never computes with it.
        private readonly byte _maxEnhancementLevel;

        // Bag-full dedup (GDD Rule 4.10): per character, the server tick at which the current
        // notification window expires. Absent = no active window (the next blocked pickup fires).
        private readonly Dictionary<CharacterID, uint> _bagFullWindowExpiry = new Dictionary<CharacterID, uint>();

        /// <summary>Creates an inventory service.</summary>
        /// <param name="itemDatabase">Item Database used to look up <c>StackLimit</c> on pickup (Tier 1, ADR-010).</param>
        /// <param name="currentTick">Returns the current server tick (production: <c>() =&gt; serverTickLoop.ServerTickNumber</c>); used for the bag-full dedup window.</param>
        /// <param name="maxEnhancementLevel">Inclusive upper bound for slot enhancement levels (Enhancement System's <c>MAX_ENHANCEMENT_LEVEL</c>; Story 010). Inventory only bounds by it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="itemDatabase"/> or <paramref name="currentTick"/> is <see langword="null"/>.</exception>
        public InventoryService(IItemDatabase itemDatabase, Func<uint> currentTick, byte maxEnhancementLevel)
        {
            _itemDatabase = itemDatabase ?? throw new ArgumentNullException(nameof(itemDatabase));
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
            _maxEnhancementLevel = maxEnhancementLevel;
        }

        /// <inheritdoc/>
        public event Action<InventoryChangedEventArgs>? OnInventoryChanged;

        /// <inheritdoc/>
        public event Action<InventoryFullEventArgs>? OnInventoryFull;

        /// <inheritdoc/>
        /// <remarks>
        /// Direct write — (re)initializes <paramref name="charId"/>'s inventory to
        /// <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> empty slots and clears all locks unconditionally.
        /// A freshly allocated <see cref="InventorySlot"/> array already defaults to
        /// <see cref="InventorySlot.Empty"/> per-element (<see langword="default"/>(<see cref="ItemID"/>)
        /// equals <see cref="ItemID.Invalid"/>), so no explicit per-slot initialization loop is
        /// needed. Does not call <see cref="ThrowIfDispatching"/> — this is a bootstrap/reset seam,
        /// not a slot mutation subject to the re-entrancy contract, and it fires no
        /// <see cref="OnInventoryChanged"/> event (mirrors <c>CurrencySystem.RegisterCharacter</c>,
        /// which is likewise a silent direct write). Also clears the character's bag-full dedup
        /// window, so the next blocked pickup notifies immediately.
        /// </remarks>
        public void RegisterCharacter(CharacterID charId)
        {
            _inventories[charId] = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT];
            _locks[charId] = new bool[InventoryConstants.INVENTORY_SLOT_COUNT];
            _bagFullWindowExpiry.Remove(charId);
        }

        /// <inheritdoc/>
        public InventorySlot GetSlot(CharacterID charId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] GetSlot: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return InventorySlot.Empty;
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] GetSlot: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return InventorySlot.Empty;
            }

            return slots[slotIndex];
        }

        /// <inheritdoc/>
        public bool IsFull(CharacterID charId)
        {
            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] IsFull: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return false;
            }

            return CountFilledSlots(slots) >= InventoryConstants.INVENTORY_SLOT_COUNT;
        }

        /// <inheritdoc/>
        public int FilledSlots(CharacterID charId)
        {
            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] FilledSlots: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return 0;
            }

            return CountFilledSlots(slots);
        }

        private static int CountFilledSlots(InventorySlot[] slots)
        {
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Quantity > 0)
                    count++;
            }
            return count;
        }

        /// <inheritdoc/>
        public bool HasFreeSlot(CharacterID charId)
        {
            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] HasFreeSlot: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsEmpty)
                    return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public bool IsSlotLocked(CharacterID charId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] IsSlotLocked: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return false;
            }

            if (!_locks.TryGetValue(charId, out var locks))
            {
                Debug.LogError($"[InventoryService] IsSlotLocked: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return false;
            }

            return locks[slotIndex];
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order (fail fast, first match wins): (1) <see cref="ItemID.Invalid"/> always
        /// returns <see langword="false"/> with no log — this is a normal, expected query result,
        /// not a caller bug. (2) an unregistered <paramref name="charId"/> logs a server error and
        /// returns <see langword="false"/>.
        /// </remarks>
        public bool HasItem(CharacterID charId, ItemID itemId)
        {
            if (itemId == ItemID.Invalid)
                return false;

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] HasItem: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Quantity > 0 && slots[i].ItemId == itemId)
                    return true;
            }
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Plan-then-commit: Steps 1–3 run against <see cref="_pickupPlan"/> without touching any
        /// slot, so a failed pickup never writes and never records — no
        /// <see cref="DiscardPendingChanges"/> call is needed on any path. Step 1 skips locked
        /// slots (GDD Rule 5.12) and is skipped entirely for <c>StackLimit</c> = 1 items. An item
        /// whose <c>StackLimit</c> is below 1 is a data error and is rejected as
        /// <see cref="PickupFailReason.UnknownItem"/> rather than reported as a full bag.
        /// </remarks>
        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity)
        {
            ThrowIfDispatching();

            if (quantity <= 0)
                return PickupResult.Fail(PickupFailReason.InvalidQuantity);

            if (!_inventories.TryGetValue(characterId, out var slots))
            {
                Debug.LogError($"[InventoryService] Pickup: {characterId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return PickupResult.Fail(PickupFailReason.CharacterNotRegistered);
            }

            if (!TryGetStackLimit(nameof(Pickup), itemId, out int stackLimit))
                return PickupResult.Fail(PickupFailReason.UnknownItem);

            Array.Clear(_pickupPlan, 0, _pickupPlan.Length);
            int remainder = PlanPartialStacks(slots, _locks[characterId], itemId, stackLimit, quantity);
            remainder = PlanEmptySlots(slots, stackLimit, remainder);

            // Step 3 — atomic failure: nothing has been written or recorded. A blocked drop.
            if (remainder > 0)
            {
                NotifyInventoryFull(characterId);
                return PickupResult.Fail(PickupFailReason.InventoryFull);
            }

            CommitPickupPlan(characterId, slots, itemId);
            _bagFullWindowExpiry.Remove(characterId); // GDD Rule 4.10: a successful pickup resets the window.
            EmitInventoryChanged(characterId);
            return PickupResult.Succeeded;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="GetSlot"/>: out-of-range <paramref name="slotIndex"/>
        /// first, then unregistered <paramref name="charId"/>, then the empty-slot warning (GDD
        /// Lock State Edge Cases). Calls <see cref="ThrowIfDispatching"/> but never
        /// <see cref="RecordSlotChange"/>/<see cref="EmitInventoryChanged"/> — see this class's
        /// mutation seam contract remarks.
        /// </remarks>
        public void LockSlot(CharacterID charId, int slotIndex)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] LockSlot: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return;
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] LockSlot: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return;
            }

            if (slots[slotIndex].IsEmpty)
            {
                Debug.LogWarning($"[InventoryService] LockSlot: slot {slotIndex} for {charId} is empty; no lock applied.");
                return;
            }

            _locks[charId][slotIndex] = true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="GetSlot"/>: out-of-range <paramref name="slotIndex"/>
        /// first, then unregistered <paramref name="charId"/>. Unlocking an already-unlocked slot
        /// is a silent no-op — defensive double-unlock (Enhancement timeout paths) must never
        /// throw or log. Calls <see cref="ThrowIfDispatching"/> but never
        /// <see cref="RecordSlotChange"/>/<see cref="EmitInventoryChanged"/> — see this class's
        /// mutation seam contract remarks.
        /// </remarks>
        public void UnlockSlot(CharacterID charId, int slotIndex)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] UnlockSlot: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return;
            }

            if (!_inventories.TryGetValue(charId, out _))
            {
                Debug.LogError($"[InventoryService] UnlockSlot: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return;
            }

            _locks[charId][slotIndex] = false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="GetSlot"/>: out-of-range <paramref name="slotIndex"/>
        /// first, then unregistered <paramref name="charId"/>. Works on any occupied slot, locked
        /// or unlocked (Implementation Notes — resolved 2026-09-26 at /story-readiness): clears
        /// the whole stack regardless of <see cref="InventorySlot.Quantity"/>, clears the lock
        /// flag if held, and fires exactly one <see cref="OnInventoryChanged"/>. An empty in-range
        /// slot is a silent no-op — no log, no event; removing nothing is not a caller bug.
        /// </remarks>
        public void RemoveItem(CharacterID charId, int slotIndex)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] RemoveItem: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return;
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] RemoveItem: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return;
            }

            if (slots[slotIndex].IsEmpty)
                return;

            RecordSlotChange(charId, slotIndex, ItemID.Invalid, 0);
            slots[slotIndex] = InventorySlot.Empty;
            _locks[charId][slotIndex] = false;
            EmitInventoryChanged(charId);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors the wire GDD's <c>DiscardRequest</c> validation order (also
        /// mirrors <see cref="RemoveItem"/>'s guard structure): out-of-range
        /// <paramref name="slotIndex"/> first — server warning, not error, since the wire
        /// <c>slotIndex</c> is a <see langword="byte"/> and 20–255 is reachable by a
        /// buggy/malicious client; then unregistered <paramref name="charId"/> — server error;
        /// then an empty in-range slot — no log, a normal client-triggerable outcome; then a
        /// locked slot — no log (GDD Rule 5); then an out-of-bounds <paramref name="quantity"/> —
        /// no log. Every rejection path returns without mutating state or firing
        /// <see cref="OnInventoryChanged"/> (decided 2026-09-27: out-of-range slot and
        /// unregistered character both map to <see cref="DiscardFailReason.SlotEmpty"/> — the
        /// wire enum has no dedicated invalid-slot value, and a slot that does not exist holds no
        /// item). On success, discarding the full stack empties the slot
        /// (<see cref="ItemID.Invalid"/>, <c>Quantity = 0</c>) exactly like
        /// <see cref="RemoveItem"/>; discarding a partial quantity keeps the same item at the
        /// reduced quantity. A locked slot is always rejected before this point, so the
        /// "an empty slot is never locked" invariant never needs re-establishing here.
        /// </remarks>
        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogWarning($"[InventoryService] Discard: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return DiscardResult.Fail(DiscardFailReason.SlotEmpty);
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] Discard: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return DiscardResult.Fail(DiscardFailReason.SlotEmpty);
            }

            var slot = slots[slotIndex];
            if (slot.IsEmpty)
                return DiscardResult.Fail(DiscardFailReason.SlotEmpty);

            if (_locks[charId][slotIndex])
                return DiscardResult.Fail(DiscardFailReason.SlotLocked);

            if (quantity <= 0 || quantity > slot.Quantity)
                return DiscardResult.Fail(DiscardFailReason.InvalidQuantity);

            int newQuantity = slot.Quantity - quantity;
            ItemID newItemId = newQuantity == 0 ? ItemID.Invalid : slot.ItemId;

            RecordSlotChange(charId, slotIndex, newItemId, newQuantity);
            slots[slotIndex] = new InventorySlot(newItemId, newQuantity);
            EmitInventoryChanged(charId);
            return DiscardResult.Succeeded;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="Discard"/>'s wire-GDD-aligned validation order (range →
        /// source lock → dest lock), extended with the same-slot no-op the wire GDD requires for
        /// Move: out-of-range <paramref name="fromSlot"/>/<paramref name="toSlot"/> logs a server
        /// warning naming the offending index and fails with <see cref="MoveFailReason.InvalidSlot"/>,
        /// echoing <see cref="InventorySlot.Empty"/> for both slots; an unregistered
        /// <paramref name="charId"/> logs a server error and fails the same way; <c>fromSlot ==
        /// toSlot</c> is always a no-op success echoing that slot's current state as both
        /// <see cref="MoveResult.FromSlot"/> and <see cref="MoveResult.ToSlot"/>, even if the slot
        /// is empty or locked; an empty source fails with <see cref="MoveFailReason.InvalidSlot"/>,
        /// no log; a locked source fails with <see cref="MoveFailReason.SourceLocked"/>; a locked
        /// destination fails with <see cref="MoveFailReason.DestLocked"/>. Every rejection path
        /// echoes both slots' unchanged current state (or <see cref="InventorySlot.Empty"/> for the
        /// range/registration failures) and never records, mutates, or emits.
        /// </remarks>
        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot)
        {
            ThrowIfDispatching();

            if (fromSlot < 0 || fromSlot >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogWarning($"[InventoryService] Move: slotIndex {fromSlot} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return MoveResult.Fail(MoveFailReason.InvalidSlot, InventorySlot.Empty, InventorySlot.Empty);
            }

            if (toSlot < 0 || toSlot >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogWarning($"[InventoryService] Move: slotIndex {toSlot} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return MoveResult.Fail(MoveFailReason.InvalidSlot, InventorySlot.Empty, InventorySlot.Empty);
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] Move: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return MoveResult.Fail(MoveFailReason.InvalidSlot, InventorySlot.Empty, InventorySlot.Empty);
            }

            if (fromSlot == toSlot)
            {
                var unchanged = slots[fromSlot];
                return MoveResult.Succeeded(unchanged, unchanged);
            }

            var source = slots[fromSlot];
            var dest = slots[toSlot];
            if (source.IsEmpty)
                return MoveResult.Fail(MoveFailReason.InvalidSlot, source, dest);

            var locks = _locks[charId];
            if (locks[fromSlot])
                return MoveResult.Fail(MoveFailReason.SourceLocked, source, dest);

            if (locks[toSlot])
                return MoveResult.Fail(MoveFailReason.DestLocked, source, dest);

            return ExecuteMove(charId, slots, fromSlot, toSlot, source, dest);
        }

        /// <summary>
        /// Performs the validated move (GDD Rule 7): the same <see cref="ItemID"/> in both slots
        /// merges — overflow beyond <c>StackLimit</c> stays in <paramref name="fromSlot"/>, and a
        /// full destination (nothing to transfer) is a no-op success with no event; anything else
        /// (different items, or an empty destination) swaps the two slots' full contents, which is
        /// exactly a relocate when the destination is empty. Records the source entry before the
        /// destination entry, then fires one <see cref="EmitInventoryChanged"/>.
        /// </summary>
        private MoveResult ExecuteMove(CharacterID charId, InventorySlot[] slots, int fromSlot, int toSlot, InventorySlot source, InventorySlot dest)
        {
            bool sameItem = source.ItemId == dest.ItemId;

            // Rule 7.20 (Story 010): only stackable items merge. StackLimit == 1 items (or an
            // unresolvable limit) never merge — identical item AND level is a no-op, else swap.
            if (sameItem && TryGetStackLimit(nameof(Move), source.ItemId, out int stackLimit) && stackLimit > 1)
                return MergeSlots(charId, slots, fromSlot, toSlot, source, dest, stackLimit);

            if (sameItem && source.EnhancementLevel == dest.EnhancementLevel)
                return MoveResult.Succeeded(source, dest);

            return SwapSlots(charId, slots, fromSlot, toSlot, source, dest);
        }

        /// <summary>
        /// Merges <paramref name="fromSlot"/> into <paramref name="toSlot"/> (same stackable item):
        /// overflow beyond <paramref name="stackLimit"/> stays in the source; a full destination is a
        /// no-op success with no event. Stacks are always level 0.
        /// </summary>
        private MoveResult MergeSlots(CharacterID charId, InventorySlot[] slots, int fromSlot, int toSlot, InventorySlot source, InventorySlot dest, int stackLimit)
        {
            int transfer = Math.Min(source.Quantity, stackLimit - dest.Quantity);
            if (transfer <= 0)
                return MoveResult.Succeeded(source, dest);

            int newSourceQuantity = source.Quantity - transfer;
            var newSource = newSourceQuantity == 0 ? InventorySlot.Empty : new InventorySlot(source.ItemId, newSourceQuantity);
            var newDest = new InventorySlot(dest.ItemId, dest.Quantity + transfer);

            RecordSlotChange(charId, fromSlot, newSource.ItemId, newSource.Quantity, newSource.EnhancementLevel);
            RecordSlotChange(charId, toSlot, newDest.ItemId, newDest.Quantity, newDest.EnhancementLevel);
            slots[fromSlot] = newSource;
            slots[toSlot] = newDest;
            EmitInventoryChanged(charId);
            return MoveResult.Succeeded(newSource, newDest);
        }

        /// <summary>
        /// Swaps <paramref name="fromSlot"/>'s and <paramref name="toSlot"/>'s full contents —
        /// covers both the Rule 7.20 swap and relocate-to-empty, since relocating onto an empty
        /// destination is a swap with <see cref="InventorySlot.Empty"/>.
        /// </summary>
        private MoveResult SwapSlots(CharacterID charId, InventorySlot[] slots, int fromSlot, int toSlot, InventorySlot source, InventorySlot dest)
        {
            RecordSlotChange(charId, fromSlot, dest.ItemId, dest.Quantity, dest.EnhancementLevel);
            RecordSlotChange(charId, toSlot, source.ItemId, source.Quantity, source.EnhancementLevel);
            slots[fromSlot] = dest;
            slots[toSlot] = source;
            EmitInventoryChanged(charId);
            return MoveResult.Succeeded(dest, source);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="RemoveItem"/>: out-of-range <paramref name="slotIndex"/>
        /// first — server error (Tier 1, never wire-reachable — an out-of-range index here is
        /// always a caller bug, unlike <see cref="Discard"/>'s wire-facing warning); then
        /// unregistered <paramref name="charId"/> — server error; then an empty slot — no log
        /// (GDD Rule 8, a normal outcome the Equipment System anticipates); then a locked slot — no
        /// log (GDD Rule 5 — the Equipment System can legitimately hit a locked slot); then a
        /// stack of more than one unit — server error, no mutation (decided 2026-09-27: the result
        /// carries no quantity, so moving a stack out would silently lose it). A locked slot is
        /// always rejected before this point, so its lock flag needs no clearing on success.
        /// </remarks>
        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] MoveItemOut: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return MoveItemOutResult.Fail(MoveItemOutCode.SlotEmpty);
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] MoveItemOut: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return MoveItemOutResult.Fail(MoveItemOutCode.SlotEmpty);
            }

            var slot = slots[slotIndex];
            if (slot.IsEmpty)
                return MoveItemOutResult.Fail(MoveItemOutCode.SlotEmpty);

            if (_locks[charId][slotIndex])
                return MoveItemOutResult.Fail(MoveItemOutCode.SlotLocked);

            if (slot.Quantity > 1)
            {
                Debug.LogError($"[InventoryService] MoveItemOut: slot {slotIndex} for {charId} holds a stack of {slot.Quantity} {slot.ItemId}; only single equipment items can be moved out.");
                return MoveItemOutResult.Fail(MoveItemOutCode.SlotEmpty);
            }

            ItemID removedItemId = slot.ItemId;
            byte removedLevel = slot.EnhancementLevel;
            RecordSlotChange(charId, slotIndex, ItemID.Invalid, 0);
            slots[slotIndex] = InventorySlot.Empty;
            EmitInventoryChanged(charId);
            return MoveItemOutResult.Succeeded(removedItemId, removedLevel);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order: unregistered <paramref name="charId"/> — server error; then item validity
        /// via <see cref="TryGetStackLimit"/> (reused purely for its unknown-item / DB-not-ready
        /// error log — the resolved stack limit is unused, since equipment is always placed at
        /// quantity 1); then <see cref="PlaceInLowestEmptySlot"/>. Never calls
        /// <see cref="NotifyInventoryFull"/> — a caller-visible full bag is reported only through
        /// <see cref="MoveItemInResult.Failed"/>, not the broadcast event (see
        /// <see cref="ForceInsert"/> for the variant that does notify).
        /// </remarks>
        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            ThrowIfDispatching();

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] MoveItemIn: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return MoveItemInResult.Failed;
            }

            if (!ValidatePlacementLevel(nameof(MoveItemIn), itemId, enhancementLevel))
                return MoveItemInResult.Failed;

            if (PlaceInLowestEmptySlot(charId, slots, itemId, enhancementLevel, out int slotIndex))
                return MoveItemInResult.Succeeded(slotIndex);

            return MoveItemInResult.Failed;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Identical guard order to <see cref="MoveItemIn"/> (unregistered character → item
        /// validity via <see cref="TryGetStackLimit"/> → <see cref="PlaceInLowestEmptySlot"/>), differing
        /// only in the no-free-slot path: this calls <see cref="NotifyInventoryFull"/> for
        /// <paramref name="charId"/> (GDD Rule 4.10) rather than failing silently. Neither this nor
        /// <see cref="MoveItemIn"/> touches the bag-full dedup window on a successful placement.
        /// </remarks>
        public bool ForceInsert(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            ThrowIfDispatching();

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] ForceInsert: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return false;
            }

            if (!ValidatePlacementLevel(nameof(ForceInsert), itemId, enhancementLevel))
                return false;

            if (PlaceInLowestEmptySlot(charId, slots, itemId, enhancementLevel, out _))
                return true;

            NotifyInventoryFull(charId);
            return false;
        }

        /// <summary>
        /// Shared placement helper for <see cref="MoveItemIn"/> and <see cref="ForceInsert"/>:
        /// finds the lowest-index empty slot, places <paramref name="itemId"/> there at quantity 1
        /// (never merges), records the change, writes the slot, and fires the single
        /// <see cref="EmitInventoryChanged"/> dispatch. Returns <see langword="false"/> with
        /// <paramref name="slotIndex"/> = -1 and no side effects if no slot is free — callers decide
        /// how to report that (silently for <see cref="MoveItemIn"/>, via
        /// <see cref="NotifyInventoryFull"/> for <see cref="ForceInsert"/>). Unlike the side-effect-free
        /// <see cref="TryGetStackLimit"/>, this <em>is</em> the mutation when it returns
        /// <see langword="true"/>.
        /// </summary>
        private bool PlaceInLowestEmptySlot(CharacterID charId, InventorySlot[] slots, ItemID itemId, byte enhancementLevel, out int slotIndex)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty)
                    continue;

                RecordSlotChange(charId, i, itemId, 1, enhancementLevel);
                slots[i] = new InventorySlot(itemId, 1, enhancementLevel);
                EmitInventoryChanged(charId);
                slotIndex = i;
                return true;
            }

            slotIndex = -1;
            return false;
        }

        /// <summary>
        /// Story 010 guard shared by <see cref="MoveItemIn"/>/<see cref="ForceInsert"/>, in the
        /// GDD order: level above the injected maximum (server error) → item validity via
        /// <see cref="TryGetStackLimit"/> → non-zero level for a <c>StackLimit</c> &gt; 1 item
        /// (server error). Exactly one log per failure; no state is touched.
        /// </summary>
        private bool ValidatePlacementLevel(string caller, ItemID itemId, byte enhancementLevel)
        {
            if (enhancementLevel > _maxEnhancementLevel)
            {
                Debug.LogError($"[InventoryService] {caller}: enhancement level {enhancementLevel} for {itemId} exceeds the maximum {_maxEnhancementLevel}.");
                return false;
            }

            if (!TryGetStackLimit(caller, itemId, out int stackLimit))
                return false;

            if (enhancementLevel != 0 && stackLimit > 1)
            {
                Debug.LogError($"[InventoryService] {caller}: enhancement level {enhancementLevel} is not allowed for stackable item {itemId} (StackLimit {stackLimit}).");
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Story 010 (GDD Rule 5.14a, AC-INV-17/22). Guard order: <see cref="ThrowIfDispatching"/>
        /// → slot range → registered → empty → not locked → item is <c>StackLimit</c> = 1 (an
        /// unresolvable item is rejected too) → <c>Quantity</c> == 1 → level within the injected
        /// maximum → same level (no-op success, no event, no log) → record, write, emit. Each
        /// rejection logs exactly one server error via <see cref="RejectSetLevel"/>. The slot's lock
        /// is left untouched.
        /// </remarks>
        public bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
                return RejectSetLevel($"slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");

            if (!_inventories.TryGetValue(charId, out var slots))
                return RejectSetLevel($"{charId} is not a registered character. Call RegisterCharacter before mutating inventory.");

            var slot = slots[slotIndex];
            if (slot.IsEmpty)
                return RejectSetLevel($"slot {slotIndex} for {charId} is empty.");

            if (!_locks[charId][slotIndex])
                return RejectSetLevel($"slot {slotIndex} for {charId} is not locked; the Enhancement System must lock the slot first.");

            if (!_itemDatabase.TryGetItem(slot.ItemId, out var definition) || definition == null || definition.StackLimit != 1)
                return RejectSetLevel($"{slot.ItemId} in slot {slotIndex} for {charId} is not a StackLimit 1 item (or is unknown); it cannot carry an enhancement level.");

            if (slot.Quantity != 1)
                return RejectSetLevel($"slot {slotIndex} for {charId} holds Quantity {slot.Quantity}; only a single item can carry an enhancement level.");

            if (level > _maxEnhancementLevel)
                return RejectSetLevel($"enhancement level {level} for slot {slotIndex} for {charId} exceeds the maximum {_maxEnhancementLevel}.");

            if (level == slot.EnhancementLevel)
                return true;

            RecordSlotChange(charId, slotIndex, slot.ItemId, slot.Quantity, level);
            slots[slotIndex] = new InventorySlot(slot.ItemId, slot.Quantity, level);
            EmitInventoryChanged(charId);
            return true;
        }

        /// <summary>Logs the single server error for a <see cref="SetEnhancementLevel"/> rejection and returns <see langword="false"/>.</summary>
        private static bool RejectSetLevel(string reason)
        {
            Debug.LogError($"[InventoryService] SetEnhancementLevel: {reason}");
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order (first match wins, matches NPC Shop GDD CR-SHOP-7 step 15 b → c → d → f):
        /// out-of-range <paramref name="slotIndex"/> — server error (Tier 1, never
        /// wire-reachable — NPC Shop range-validates before calling, mirrors
        /// <see cref="MoveItemOut"/>'s guard); then unregistered <paramref name="charId"/> —
        /// server error; then <paramref name="itemId"/> == <see cref="ItemID.Invalid"/> or the
        /// slot's current <c>ItemID</c> not matching <paramref name="itemId"/> (covers an empty
        /// slot) — <see cref="SellItemFailReason.ItemMismatch"/>, no log; then a locked slot —
        /// <see cref="SellItemFailReason.SlotLocked"/>, no log (GDD Rule 5.12, checked before
        /// quantity bounds); then <c>quantity &lt;= 0</c> or <c>quantity &gt;</c> the slot's
        /// current <see cref="InventorySlot.Quantity"/> — <see cref="SellItemFailReason.InvalidQuantity"/>,
        /// no log. Category-agnostic: no Item Database lookup and no <c>SellPriceGold</c> check —
        /// sellability is NPC Shop's own rule (CR-SHOP-7 step 15e), and gold is never touched here.
        /// On success, selling the full stack empties the slot exactly like <see cref="Discard"/>;
        /// selling a partial quantity keeps the same item at the reduced quantity.
        /// </remarks>
        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            ThrowIfDispatching();

            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogError($"[InventoryService] SellItem: slotIndex {slotIndex} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");
                return SellItemResult.Fail(SellItemFailReason.InvalidSlot);
            }

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] SellItem: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return SellItemResult.Fail(SellItemFailReason.InvalidSlot);
            }

            var slot = slots[slotIndex];
            if (itemId == ItemID.Invalid || slot.ItemId != itemId)
                return SellItemResult.Fail(SellItemFailReason.ItemMismatch);

            if (_locks[charId][slotIndex])
                return SellItemResult.Fail(SellItemFailReason.SlotLocked);

            if (quantity <= 0 || quantity > slot.Quantity)
                return SellItemResult.Fail(SellItemFailReason.InvalidQuantity);

            int newQuantity = slot.Quantity - quantity;
            ItemID newItemId = newQuantity == 0 ? ItemID.Invalid : slot.ItemId;

            RecordSlotChange(charId, slotIndex, newItemId, newQuantity);
            slots[slotIndex] = new InventorySlot(newItemId, newQuantity);
            EmitInventoryChanged(charId);
            return SellItemResult.Succeeded(quantity);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order mirrors <see cref="Pickup"/>: <c>quantity &lt;= 0</c> fails first with
        /// <see cref="ConsumeItemFailReason.InvalidQuantity"/>, no log; then an unregistered
        /// <paramref name="charId"/> — server error; then <paramref name="itemId"/> ==
        /// <see cref="ItemID.Invalid"/> fails immediately with
        /// <see cref="ConsumeItemFailReason.InsufficientQuantity"/>, no log (an invalid item can
        /// never be "held"); then plan-then-commit via <see cref="HasSufficientUnlockedQuantity"/>
        /// and <see cref="CommitConsume"/> — locked slots are neither decremented nor counted
        /// toward the available total (GDD Rule 5.12), so <see cref="HasItem"/> can report
        /// <see langword="true"/> for a locked-only stack that this still rejects. Nothing is
        /// written or recorded unless the full requested quantity is confirmed available in
        /// unlocked slots first — a failed call never needs <see cref="DiscardPendingChanges"/>.
        /// No Item Database lookup and no category check (Story 007 precedent) — this decrements
        /// whatever <paramref name="itemId"/> it is given.
        /// </remarks>
        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity)
        {
            ThrowIfDispatching();

            if (quantity <= 0)
                return ConsumeItemResult.Fail(ConsumeItemFailReason.InvalidQuantity);

            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] ConsumeItem: {charId} is not a registered character. Call RegisterCharacter before mutating inventory.");
                return ConsumeItemResult.Fail(ConsumeItemFailReason.CharacterNotRegistered);
            }

            if (itemId == ItemID.Invalid)
                return ConsumeItemResult.Fail(ConsumeItemFailReason.InsufficientQuantity);

            var locks = _locks[charId];
            if (!HasSufficientUnlockedQuantity(slots, locks, itemId, quantity))
                return ConsumeItemResult.Fail(ConsumeItemFailReason.InsufficientQuantity);

            CommitConsume(charId, slots, locks, itemId, quantity);
            EmitInventoryChanged(charId);
            return ConsumeItemResult.Succeeded;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A read: does not call <see cref="ThrowIfDispatching"/>, mutates nothing, and fires no
        /// event — mirrors <see cref="GetSlot"/>'s guard. An unregistered <paramref name="charId"/>
        /// logs a server error and returns <see cref="InventorySnapshot.Empty"/>.
        /// </remarks>
        public InventorySnapshot ExportSnapshot(CharacterID charId)
        {
            if (!_inventories.TryGetValue(charId, out var slots))
            {
                Debug.LogError($"[InventoryService] ExportSnapshot: {charId} is not a registered character. Call RegisterCharacter before reading inventory.");
                return InventorySnapshot.Empty;
            }

            var entries = new List<InventorySnapshotEntry>();
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.IsEmpty)
                    continue;

                entries.Add(new InventorySnapshotEntry((byte)i, slot.ItemId.RawValue, slot.Quantity, slot.EnhancementLevel));
            }
            return new InventorySnapshot(entries);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Guard order (first match wins): <see cref="ThrowIfDispatching"/>; a null
        /// <paramref name="snapshot"/>; the Item Database not being ready — checked before the
        /// reset below so a too-early import can never wipe an existing bag. Otherwise resets
        /// <paramref name="charId"/> exactly like <see cref="RegisterCharacter"/> (registers if
        /// new) and applies every entry via <see cref="ApplySnapshotEntry"/>. Never calls
        /// <see cref="RecordSlotChange"/>/<see cref="EmitInventoryChanged"/> — see this class's
        /// mutation seam contract remarks.
        /// </remarks>
        public bool ImportSnapshot(CharacterID charId, InventorySnapshot? snapshot)
        {
            ThrowIfDispatching();

            if (snapshot == null)
            {
                Debug.LogError($"[InventoryService] ImportSnapshot: snapshot is null for {charId}; nothing changed.");
                return false;
            }

            if (!_itemDatabase.IsReady)
            {
                Debug.LogError($"[InventoryService] ImportSnapshot: Item Database is not ready; aborting import for {charId} with nothing changed.");
                return false;
            }

            RegisterCharacter(charId);
            var slots = _inventories[charId];

            var claimed = new bool[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < snapshot.Slots.Count; i++)
                ApplySnapshotEntry(charId, slots, claimed, snapshot.Slots[i]);

            return true;
        }

        /// <summary>
        /// <see cref="ImportSnapshot"/>'s per-entry validation and write (Implementation Notes —
        /// first match wins, every rejection is a <see cref="Debug.LogWarning(object)"/> naming
        /// <paramref name="charId"/> and the offending entry, none throw): an out-of-range
        /// <see cref="InventorySnapshotEntry.SlotIndex"/>; a slot already claimed by an earlier
        /// in-range entry in this same snapshot (<paramref name="claimed"/> is set as soon as an
        /// in-range entry is seen, even one later cleared by a rule below); <c>ItemId == 0</c>;
        /// <c>Quantity &lt;= 0</c>; an unknown item (<see cref="IItemDatabase.TryGetItem"/> fails —
        /// called directly, never <c>TryGetStackLimit</c>, which logs its own error). A quantity
        /// above the item's <c>StackLimit</c> still loads as-is, at the full quantity, after a
        /// warning (Implementation Notes — clamping would destroy player items). Writes
        /// <paramref name="slots"/> directly.
        /// </summary>
        private void ApplySnapshotEntry(CharacterID charId, InventorySlot[] slots, bool[] claimed, InventorySnapshotEntry entry)
        {
            if (!TryClaimSnapshotSlot(charId, claimed, entry))
                return;

            int slotIndex = entry.SlotIndex;
            if (entry.ItemId == 0)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} has ItemId 0; slot left empty.");
                return;
            }

            if (entry.Quantity <= 0)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} has non-positive Quantity {entry.Quantity}; slot left empty.");
                return;
            }

            var itemId = new ItemID(entry.ItemId);
            if (!_itemDatabase.TryGetItem(itemId, out var definition) || definition == null)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} references unknown item {itemId}; slot left empty.");
                return;
            }

            if (entry.Quantity > definition.StackLimit)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} has Quantity {entry.Quantity} above StackLimit {definition.StackLimit} for {itemId}; loading as-is.");
            }

            byte level = NormalizeSnapshotLevel(charId, slotIndex, entry, definition.StackLimit);
            slots[slotIndex] = new InventorySlot(itemId, entry.Quantity, level);
        }

        /// <summary>
        /// First two <see cref="ApplySnapshotEntry"/> rules: rejects (one warning each) an entry whose
        /// slot index is out of range or whose slot an earlier entry of the same snapshot already
        /// claimed; otherwise marks the slot claimed and returns <see langword="true"/>.
        /// </summary>
        private static bool TryClaimSnapshotSlot(CharacterID charId, bool[] claimed, InventorySnapshotEntry entry)
        {
            if (entry.SlotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry slotIndex {entry.SlotIndex} for {charId} is out of range [0, {InventoryConstants.INVENTORY_SLOT_COUNT}); entry rejected.");
                return false;
            }

            if (claimed[entry.SlotIndex])
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: duplicate entry for slot {entry.SlotIndex} for {charId}; entry rejected.");
                return false;
            }
            claimed[entry.SlotIndex] = true;
            return true;
        }

        /// <summary>
        /// Story 010 (AC-INV-20/22) load rule for <see cref="InventorySnapshotEntry.EnhancementLevel"/>:
        /// a non-zero level on a stackable item or a quantity above 1 is warned about and zeroed;
        /// otherwise a level above the injected maximum is warned about and clamped to it. Level 0
        /// logs nothing. Each normalisation logs exactly one warning.
        /// </summary>
        private byte NormalizeSnapshotLevel(CharacterID charId, int slotIndex, InventorySnapshotEntry entry, int stackLimit)
        {
            byte level = entry.EnhancementLevel;
            if (level == 0)
                return 0;

            if (stackLimit > 1 || entry.Quantity > 1)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} has enhancement level {level} on a stack (StackLimit {stackLimit}, Quantity {entry.Quantity}); enhancement level reset to 0.");
                return 0;
            }

            if (level > _maxEnhancementLevel)
            {
                Debug.LogWarning($"[InventoryService] ImportSnapshot: entry for slot {slotIndex} for {charId} has enhancement level {level} above the maximum {_maxEnhancementLevel}; clamped to {_maxEnhancementLevel}.");
                return _maxEnhancementLevel;
            }
            return level;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <see cref="ThrowIfDispatching"/> runs first. Removes <paramref name="charId"/> from
        /// every backing dictionary (<see cref="_inventories"/>, <see cref="_locks"/>,
        /// <see cref="_bagFullWindowExpiry"/>) — after this call <paramref name="charId"/> is
        /// unregistered for every API, exactly as if <see cref="RegisterCharacter"/> had never
        /// been called for it. A character that is not currently registered is a silent no-op
        /// (idempotent) — <see cref="Dictionary{TKey,TValue}.Remove(TKey)"/> returning
        /// <see langword="false"/> for an absent key is not a caller bug. Fires no event.
        /// </remarks>
        public void UnregisterCharacter(CharacterID charId)
        {
            ThrowIfDispatching();

            _inventories.Remove(charId);
            _locks.Remove(charId);
            _bagFullWindowExpiry.Remove(charId);
        }

        /// <summary>
        /// <see cref="ConsumeItem"/>'s plan step: sums <see cref="InventorySlot.Quantity"/> across
        /// every unlocked slot holding <paramref name="itemId"/> (ascending order, short-circuiting
        /// once the running total reaches <paramref name="quantity"/>), without touching any slot.
        /// </summary>
        private static bool HasSufficientUnlockedQuantity(InventorySlot[] slots, bool[] locks, ItemID itemId, int quantity)
        {
            int total = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (locks[i] || slots[i].ItemId != itemId)
                    continue;

                total += slots[i].Quantity;
                if (total >= quantity)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// <see cref="ConsumeItem"/>'s commit step: decrements unlocked slots holding
        /// <paramref name="itemId"/> in ascending order until <paramref name="remaining"/> reaches
        /// 0, recording each changed slot. Only called once
        /// <see cref="HasSufficientUnlockedQuantity"/> has confirmed enough is available, so this
        /// never under-runs.
        /// </summary>
        /// <remarks>
        /// Each slot is recorded before it is written, so a seam-contract violation (stale pending
        /// changes for another character) throws on the first slot, before any write. After the
        /// first record, <see cref="RecordSlotChange"/> cannot throw under current invariants:
        /// not dispatching, at most <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> changed
        /// slots, and every entry is either a valid ItemID with quantity &gt; 0 or
        /// <see cref="ItemID.Invalid"/> with quantity 0. If a future change breaks any of these, a
        /// mid-loop throw would leave committed slots with no event — keep them intact or switch
        /// to validate-all-then-write (same caveat as <see cref="CommitPickupPlan"/>).
        /// </remarks>
        private void CommitConsume(CharacterID charId, InventorySlot[] slots, bool[] locks, ItemID itemId, int remaining)
        {
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (locks[i] || slots[i].ItemId != itemId)
                    continue;

                var slot = slots[i];
                int taken = Math.Min(remaining, slot.Quantity);
                int newQuantity = slot.Quantity - taken;
                ItemID newItemId = newQuantity == 0 ? ItemID.Invalid : slot.ItemId;

                RecordSlotChange(charId, i, newItemId, newQuantity);
                slots[i] = new InventorySlot(newItemId, newQuantity);
                remaining -= taken;
            }
        }

        /// <summary>
        /// The single bag-full notification policy (GDD Rule 4.10): fires
        /// <see cref="OnInventoryFull"/> for <paramref name="characterId"/> unless a dedup window
        /// is active, then opens a new <see cref="InventoryServerConstants.BAG_FULL_DEDUP_WINDOW_TICKS"/>
        /// window. Every blocked-for-space path MUST call this — Story 002 <see cref="Pickup"/>
        /// and Story 007 <c>ForceInsert</c> — never fire <see cref="OnInventoryFull"/> directly.
        /// </summary>
        /// <remarks>
        /// The window is <c>[t, t + window)</c>: expiry is checked with the wraparound-safe,
        /// boundary-inclusive <see cref="StaleDiscardComparer.IsTickExpired"/>. The new window is
        /// stored before dispatch, so a throwing subscriber still consumes it. Dispatch sets the
        /// same re-entrancy guard as <see cref="EmitInventoryChanged"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called during a dispatch, or a subscriber mutated the inventory synchronously.</exception>
        internal void NotifyInventoryFull(CharacterID characterId)
        {
            ThrowIfDispatching();

            uint now = _currentTick();
            if (_bagFullWindowExpiry.TryGetValue(characterId, out uint expiry)
                && !StaleDiscardComparer.IsTickExpired(now, expiry))
                return;

            _bagFullWindowExpiry[characterId] = unchecked(now + InventoryServerConstants.BAG_FULL_DEDUP_WINDOW_TICKS);

            _isDispatching = true;
            try
            {
                OnInventoryFull?.Invoke(new InventoryFullEventArgs(characterId));
            }
            finally
            {
                _isDispatching = false;
            }
        }

        /// <summary>
        /// Resolves <paramref name="itemId"/>'s <c>StackLimit</c> from the Item Database, logging a
        /// server error when the item is invalid, unknown, the database is not ready, or the
        /// definition's <c>StackLimit</c> is below 1 (a data error — never reported as a full bag).
        /// </summary>
        /// <param name="caller">The public entry point used as the log prefix (e.g. <c>nameof(Pickup)</c>), so a failure names the operation that hit it.</param>
        private bool TryGetStackLimit(string caller, ItemID itemId, out int stackLimit)
        {
            stackLimit = 0;
            if (itemId == ItemID.Invalid || !_itemDatabase.IsReady
                || !_itemDatabase.TryGetItem(itemId, out var definition) || definition == null)
            {
                Debug.LogError($"[InventoryService] {caller}: {itemId} is not a known item (or the Item Database is not ready).");
                return false;
            }

            stackLimit = definition.StackLimit;
            if (stackLimit < 1)
            {
                Debug.LogError($"[InventoryService] {caller}: {itemId} has invalid StackLimit {stackLimit}.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// GDD Rule 3 Step 1 (F-INV-2): plans top-ups of unlocked partial stacks of
        /// <paramref name="itemId"/> in ascending slot order. Skipped for <c>StackLimit</c> = 1
        /// items. Returns the units still unplaced.
        /// </summary>
        private int PlanPartialStacks(InventorySlot[] slots, bool[] locks, ItemID itemId, int stackLimit, int remainder)
        {
            if (stackLimit == 1)
                return remainder;

            for (int i = 0; i < slots.Length && remainder > 0; i++)
            {
                var slot = slots[i];
                if (slot.ItemId != itemId || locks[i] || slot.Quantity >= stackLimit)
                    continue;

                int added = Math.Min(remainder, stackLimit - slot.Quantity);
                _pickupPlan[i] = slot.Quantity + added;
                remainder -= added;
            }
            return remainder;
        }

        /// <summary>
        /// GDD Rule 3 Step 2: plans placement into empty slots in ascending order, up to
        /// <paramref name="stackLimit"/> per slot. Returns the units still unplaced.
        /// </summary>
        private int PlanEmptySlots(InventorySlot[] slots, int stackLimit, int remainder)
        {
            for (int i = 0; i < slots.Length && remainder > 0; i++)
            {
                if (!slots[i].IsEmpty)
                    continue;

                int added = Math.Min(remainder, stackLimit);
                _pickupPlan[i] = added;
                remainder -= added;
            }
            return remainder;
        }

        /// <summary>
        /// Writes every planned slot in ascending order, recording each change for the single
        /// <see cref="EmitInventoryChanged"/> dispatch that follows.
        /// </summary>
        /// <remarks>
        /// Each slot is recorded before it is written, so a seam-contract violation (stale pending
        /// changes for another character) throws on the first slot, before any write. After the
        /// first record, <see cref="RecordSlotChange"/> cannot throw under current invariants:
        /// not dispatching, at most <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> planned
        /// slots, and every planned entry is a valid ItemID with quantity &gt; 0. If a future change
        /// breaks any of these, a mid-loop throw would leave committed slots with no event —
        /// keep them intact or switch to validate-all-then-write.
        /// </remarks>
        private void CommitPickupPlan(CharacterID characterId, InventorySlot[] slots, ItemID itemId)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (_pickupPlan[i] == 0)
                    continue;

                RecordSlotChange(characterId, i, itemId, _pickupPlan[i]);
                slots[i] = new InventorySlot(itemId, _pickupPlan[i]);
            }
        }

        /// <summary>
        /// Guards against synchronous re-entrant mutation from inside an
        /// <see cref="OnInventoryChanged"/> subscriber (ADR-010 Decision 3 re-entrancy contract).
        /// Every mutation entry point — this story's <see cref="RecordSlotChange"/> and
        /// <see cref="SeedSlotForTesting(CharacterID, int, ItemID, int, byte)"/>, plus every future mutation entry point added by
        /// Stories 002 and 004–008 (Pickup, Lock/Unlock, Discard, Move/Merge/Swap, Equipment
        /// interop, Sell/Consume) — MUST call this first, before touching any slot state.
        /// </summary>
        /// <exception cref="InvalidOperationException">A dispatch of <see cref="OnInventoryChanged"/> is currently in progress.</exception>
        private void ThrowIfDispatching()
        {
            if (_isDispatching)
            {
                throw new InvalidOperationException(
                    "Inventory mutated synchronously from an OnInventoryChanged subscriber.");
            }
        }

        /// <summary>
        /// Test-only seam: directly sets the contents of a single slot, bypassing all mutation
        /// rules (pickup/merge/lock checks) that real mutation stories will enforce. Required by
        /// later stories' "seeded inventory state via test harness injection" acceptance criteria
        /// (e.g. AC-INV-5/10/13).
        /// </summary>
        /// <remarks>
        /// Enforces the no-phantom-slot invariant (GDD Edge Cases): <paramref name="itemId"/> and
        /// <paramref name="quantity"/> must agree — both "empty" (<see cref="ItemID.Invalid"/>,
        /// 0) or both "populated" (a valid <see cref="ItemID"/>, quantity &gt; 0). A mismatched
        /// pair throws rather than silently correcting the caller's input, since a test seam
        /// producing a phantom slot by accident would be a silent test-authoring bug.
        /// </remarks>
        /// <param name="charId">The character whose inventory to seed. Must already be registered.</param>
        /// <param name="slotIndex">The slot index to set. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <param name="itemId">The item to place, or <see cref="ItemID.Invalid"/> to clear the slot.</param>
        /// <param name="quantity">The quantity to place. Must be 0 iff <paramref name="itemId"/> is <see cref="ItemID.Invalid"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotIndex"/> is out of range, or <paramref name="quantity"/> is negative.</exception>
        /// <exception cref="ArgumentException"><paramref name="itemId"/>/<paramref name="quantity"/> form a phantom slot, or <paramref name="charId"/> is not registered.</exception>
        internal void SeedSlotForTesting(CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            SeedSlotForTesting(charId, slotIndex, itemId, quantity, 0);
        }

        /// <summary>
        /// Story 010 overload of <see cref="SeedSlotForTesting(CharacterID, int, ItemID, int)"/>
        /// that also seeds the slot's enhancement level. The level must be 0 for an empty slot or a
        /// quantity above 1 (<see cref="ValidateSlotContents"/> throws <see cref="ArgumentException"/>).
        /// </summary>
        /// <param name="charId">The character whose inventory to seed. Must already be registered.</param>
        /// <param name="slotIndex">The slot index to set.</param>
        /// <param name="itemId">The item to place, or <see cref="ItemID.Invalid"/> to clear the slot.</param>
        /// <param name="quantity">The quantity to place.</param>
        /// <param name="enhancementLevel">The enhancement level to seed.</param>
        internal void SeedSlotForTesting(CharacterID charId, int slotIndex, ItemID itemId, int quantity, byte enhancementLevel)
        {
            ThrowIfDispatching();
            ValidateSlotContents(nameof(SeedSlotForTesting), slotIndex, itemId, quantity, enhancementLevel);

            if (!_inventories.TryGetValue(charId, out var slots))
                throw new ArgumentException($"SeedSlotForTesting: {charId} is not a registered character. Call RegisterCharacter first.", nameof(charId));

            slots[slotIndex] = new InventorySlot(itemId, quantity, enhancementLevel);
        }

        /// <summary>
        /// Test/mutation-story seam: appends one slot's post-mutation state to the pending change
        /// buffer for the next <see cref="EmitInventoryChanged"/> call. A single atomic mutation
        /// (e.g. a pickup that fills a partial stack and opens a new slot) calls this once per
        /// affected slot before calling <see cref="EmitInventoryChanged"/> once.
        /// </summary>
        /// <remarks>
        /// The first recorded change binds the pending buffer to <paramref name="charId"/>; every
        /// further change until the next emit/discard must be for the same character. Entries are
        /// validated with the same no-phantom-slot invariant as <see cref="SeedSlotForTesting(CharacterID, int, ItemID, int, byte)"/>.
        /// </remarks>
        /// <param name="charId">The character whose slot changed.</param>
        /// <param name="slotIndex">The slot index that changed. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <param name="itemId">The slot's item after the mutation.</param>
        /// <param name="quantity">The slot's quantity after the mutation. 0 means the slot became empty.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotIndex"/> is out of range, or <paramref name="quantity"/> is negative.</exception>
        /// <exception cref="ArgumentException"><paramref name="itemId"/>/<paramref name="quantity"/> form a phantom slot.</exception>
        /// <exception cref="InvalidOperationException">A dispatch is currently in progress; changes for a different character are already pending; or more than <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> changes have already been recorded for the pending dispatch (a programming error — no single atomic mutation can touch more than every slot once).</exception>
        internal void RecordSlotChange(CharacterID charId, int slotIndex, ItemID itemId, int quantity, byte enhancementLevel = 0)
        {
            ThrowIfDispatching();
            ValidateSlotContents(nameof(RecordSlotChange), slotIndex, itemId, quantity, enhancementLevel);

            if (_changeCount > 0 && charId != _pendingCharacterId)
            {
                throw new InvalidOperationException(
                    $"RecordSlotChange: changes for {_pendingCharacterId} are still pending; cannot record a change for {charId}. Emit or discard the pending changes first.");
            }

            if (_changeCount >= InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                throw new InvalidOperationException(
                    $"RecordSlotChange overflow: attempted to record more than {InventoryConstants.INVENTORY_SLOT_COUNT} changes for a single dispatch.");
            }

            _pendingCharacterId = charId;
            _changeBuffer[_changeCount] = new SlotChange(slotIndex, itemId, quantity, enhancementLevel);
            _changeCount++;
        }

        /// <summary>
        /// Drops every change recorded since the last dispatch without firing
        /// <see cref="OnInventoryChanged"/>. Mutation paths that abort after calling
        /// <see cref="RecordSlotChange"/> MUST call this so the stale entries are never broadcast
        /// with a later, unrelated dispatch.
        /// </summary>
        /// <exception cref="InvalidOperationException">A dispatch is currently in progress.</exception>
        internal void DiscardPendingChanges()
        {
            ThrowIfDispatching();
            ResetPendingChanges();
        }

        private void ResetPendingChanges()
        {
            _changeCount = 0;
            _pendingCharacterId = CharacterID.Invalid;
        }

        /// <summary>
        /// Shared slot-contents validation for the internal write seams: slot index in range,
        /// non-negative quantity, and the no-phantom-slot invariant (GDD Edge Cases) — a slot is
        /// either fully empty (<see cref="ItemID.Invalid"/>, 0) or fully populated (valid
        /// <see cref="ItemID"/>, quantity &gt; 0).
        /// </summary>
        private static void ValidateSlotContents(string caller, int slotIndex, ItemID itemId, int quantity, byte enhancementLevel = 0)
        {
            if (slotIndex < 0 || slotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
                throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, $"{caller}: slotIndex must be within [0, {InventoryConstants.INVENTORY_SLOT_COUNT}).");

            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, $"{caller}: quantity must not be negative.");

            if ((itemId != ItemID.Invalid) != (quantity > 0))
            {
                throw new ArgumentException(
                    $"{caller} rejected a phantom slot: itemId={itemId}, quantity={quantity}. " +
                    "A slot must be either fully empty (ItemID.Invalid, quantity 0) or fully populated (valid ItemID, quantity > 0).");
            }

            if (enhancementLevel != 0 && (itemId == ItemID.Invalid || quantity > 1))
            {
                throw new ArgumentException(
                    $"{caller} rejected enhancement level {enhancementLevel}: the level must be 0 for an empty slot or a quantity above 1 (itemId={itemId}, quantity={quantity}).");
            }
        }

        /// <summary>
        /// Fires <see cref="OnInventoryChanged"/> with the entries accumulated via
        /// <see cref="RecordSlotChange"/> since the last dispatch, then resets dispatch state.
        /// </summary>
        /// <remarks>
        /// Internal (not private) so tests can drive the event directly (this story defines the
        /// emit helper; mutation stories 002/004–008 are the ones that call it from real mutation
        /// code paths). Sets <see cref="_isDispatching"/> for the duration of the
        /// <see cref="OnInventoryChanged"/> invocation so a subscriber that attempts to mutate the
        /// inventory synchronously trips <see cref="ThrowIfDispatching"/> and throws
        /// <see cref="InvalidOperationException"/> to this method's caller. The <see langword="finally"/>
        /// block always resets <see cref="_isDispatching"/> and the pending changes, even if a
        /// subscriber throws — a later dispatch is never contaminated by a prior one's leftover
        /// state. With no pending changes this is a no-op: no event fires (a mutation that changed
        /// nothing broadcasts nothing).
        /// </remarks>
        /// <param name="charId">The character whose inventory changed. Must match the character the pending changes were recorded for.</param>
        /// <exception cref="InvalidOperationException">A dispatch is already in progress (re-entrant call), or <paramref name="charId"/> does not match the pending changes' character — in which case the pending changes are discarded before throwing, so they can never leak into a later dispatch.</exception>
        internal void EmitInventoryChanged(CharacterID charId)
        {
            ThrowIfDispatching();

            if (_changeCount == 0)
                return;

            if (charId != _pendingCharacterId)
            {
                var pending = _pendingCharacterId;
                ResetPendingChanges();
                throw new InvalidOperationException(
                    $"EmitInventoryChanged: pending changes were recorded for {pending} but emitted for {charId}. Pending changes discarded.");
            }

            _isDispatching = true;
            try
            {
                OnInventoryChanged?.Invoke(new InventoryChangedEventArgs(charId, _changeBuffer, _changeCount));
            }
            finally
            {
                _isDispatching = false;
                ResetPendingChanges();
            }
        }
    }
}

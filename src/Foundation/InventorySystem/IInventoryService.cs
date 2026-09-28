using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Read/write contract for the server-authoritative inventory — a fixed 20-slot bag per
    /// character (GDD Rule 1). Inject this interface into any system that needs to read or
    /// mutate a character's inventory — never depend on the concrete
    /// <see cref="InventoryService"/> class directly (ADR-010 Tier 1).
    /// </summary>
    /// <remarks>
    /// Story 001 establishes slot storage, the read surface (<see cref="GetSlot"/>,
    /// <see cref="IsFull"/>, <see cref="FilledSlots"/>, <see cref="HasFreeSlot"/>,
    /// <see cref="IsSlotLocked"/>, <see cref="HasItem"/>), the <see cref="RegisterCharacter"/>
    /// bootstrap seam, and the <see cref="OnInventoryChanged"/> Tier 2 broadcast event contract
    /// (ADR-010 Decision 3). Story 002 adds the first mutator, <see cref="Pickup"/>. Story 004
    /// (Slot Locks) adds <see cref="LockSlot"/>/<see cref="UnlockSlot"/> (flag-only — never fire
    /// <see cref="OnInventoryChanged"/>) and <see cref="RemoveItem"/> (fires
    /// <see cref="OnInventoryChanged"/>); Story 005 (Discard) adds <see cref="Discard"/> (fires
    /// <see cref="OnInventoryChanged"/> on success). Story 006 (Move/Merge/Swap) adds
    /// <see cref="Move"/> (fires <see cref="OnInventoryChanged"/> on any mutating success; a
    /// same-slot move and a full-destination merge are no-op successes that fire nothing).
    /// Story 007 (Equipment interface) adds <see cref="MoveItemOut"/>, <see cref="MoveItemIn"/>,
    /// and <see cref="ForceInsert"/> (each fires <see cref="OnInventoryChanged"/> only on a
    /// mutating success); Story 008 (Sell/Consume) adds its own mutator(s).
    /// </remarks>
    public interface IInventoryService
    {
        /// <summary>
        /// Tier 2 broadcast event (ADR-010 Decision 3) fired after a mutation commits, carrying
        /// the set of slots that changed as a result. Argument is a <see langword="readonly struct"/>
        /// (<see cref="InventoryChangedEventArgs"/>) wrapping a reused, service-owned buffer —
        /// zero per-emit heap allocation, never boxes. See
        /// <see cref="InventoryChangedEventArgs"/>'s doc comments for the "valid only during
        /// synchronous dispatch" contract. This story defines the event's shape and the
        /// service-internal emit helper; mutation stories (002, 004–008) are the ones that
        /// actually fire it. A mutation that changes no slot fires nothing.
        /// </summary>
        /// <remarks>
        /// Subscribers must not throw on normal paths: a subscriber exception propagates to the
        /// mutating caller, and — standard multicast-delegate behavior — every subscriber after
        /// the throwing one is skipped for that dispatch (the service's own dispatch state is
        /// still reset). Subscribers must also not mutate the inventory synchronously; doing so
        /// throws <see cref="InvalidOperationException"/>.
        /// </remarks>
        event Action<InventoryChangedEventArgs> OnInventoryChanged;

        /// <summary>
        /// Tier 2 broadcast event (ADR-010 Decision 3) fired when a pickup is blocked because the
        /// bag has no room (GDD Rule 4.10) — the network layer turns it into the 0-byte
        /// <c>InventoryFullNotification</c> wire message. Deduplicated: at most one per character
        /// per <see cref="InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS"/>; a successful pickup
        /// or <see cref="RegisterCharacter"/> resets the window. Invalid requests (bad quantity,
        /// unknown item, unregistered character) never fire it.
        /// </summary>
        /// <remarks>
        /// Same subscriber rules as <see cref="OnInventoryChanged"/>: subscribers must not throw
        /// and must not mutate the inventory synchronously (doing so throws
        /// <see cref="InvalidOperationException"/> to the mutating caller). The dedup window is
        /// consumed before dispatch, so a throwing subscriber does not cause a re-fire.
        /// </remarks>
        event Action<InventoryFullEventArgs> OnInventoryFull;

        /// <summary>
        /// Registers <paramref name="charId"/> with a fresh, fully empty 20-slot inventory
        /// (GDD Rule 1.1) and clears all slot locks, making it a valid target for every read API
        /// on this interface and for future mutation APIs.
        /// </summary>
        /// <remarks>
        /// This is a bootstrap seam analogous to <see cref="ICurrencyService.RegisterCharacter"/>
        /// — in production this is called by Character Persistence on character spawn/login (real
        /// content seeding from a saved <c>InventorySnapshot</c> is Story 009's responsibility, not
        /// this story's). Calling it again on an already-registered character unconditionally
        /// resets that character's inventory to empty and all locks to unlocked — callers must not
        /// rely on this preserving prior contents; it is a reset, not a merge.
        /// </remarks>
        /// <param name="charId">The character to register.</param>
        void RegisterCharacter(CharacterID charId);

        /// <summary>
        /// Returns a snapshot of the slot at <paramref name="slotIndex"/> for <paramref name="charId"/>.
        /// </summary>
        /// <remarks>
        /// Out-of-range <paramref name="slotIndex"/> (&lt; 0 or &gt;= <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>)
        /// never throws — it logs a server error and returns <see cref="InventorySlot.Empty"/>,
        /// with no state change (GDD Rule 1 / TR-inv-001). An unregistered <paramref name="charId"/>
        /// likewise logs a server error and returns <see cref="InventorySlot.Empty"/> — this is
        /// always a caller bug (RegisterCharacter was skipped), never a player-facing condition.
        /// </remarks>
        /// <param name="charId">The character whose inventory to read.</param>
        /// <param name="slotIndex">The slot index to read. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        InventorySlot GetSlot(CharacterID charId, int slotIndex);

        /// <summary>
        /// Returns <see langword="true"/> iff all <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>
        /// slots are occupied (F-INV-3 quick-rejection gate — does not account for partial-stack
        /// merge room; see F-INV-3's note in the GDD). An unregistered <paramref name="charId"/>
        /// logs a server error and returns <see langword="false"/>.
        /// </summary>
        /// <param name="charId">The character whose inventory to check.</param>
        bool IsFull(CharacterID charId);

        /// <summary>
        /// Returns the number of slots with <c>Quantity &gt; 0</c> (F-INV-3). An unregistered
        /// <paramref name="charId"/> logs a server error and returns 0.
        /// </summary>
        /// <param name="charId">The character whose inventory to check.</param>
        int FilledSlots(CharacterID charId);

        /// <summary>
        /// Returns <see langword="true"/> iff at least one slot is <see cref="ItemID.Invalid"/>
        /// with <c>Quantity == 0</c> (GDD Rule 8.23). An unregistered <paramref name="charId"/>
        /// logs a server error and returns <see langword="false"/>.
        /// </summary>
        /// <param name="charId">The character whose inventory to check.</param>
        bool HasFreeSlot(CharacterID charId);

        /// <summary>
        /// Returns whether the slot at <paramref name="slotIndex"/> is locked (GDD Rule 5.14).
        /// Every slot of a freshly registered inventory is unlocked. See <see cref="LockSlot"/>
        /// and <see cref="UnlockSlot"/> (Story 004) for the lock-mutation API.
        /// </summary>
        /// <remarks>
        /// Out-of-range <paramref name="slotIndex"/> and an unregistered <paramref name="charId"/>
        /// both log a server error and return <see langword="false"/>, with no state change —
        /// mirrors <see cref="GetSlot"/>'s guard behavior.
        /// </remarks>
        /// <param name="charId">The character whose inventory to check.</param>
        /// <param name="slotIndex">The slot index to check. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        bool IsSlotLocked(CharacterID charId, int slotIndex);

        /// <summary>
        /// Returns <see langword="true"/> iff any slot holds <paramref name="itemId"/> with
        /// <c>Quantity &gt; 0</c> (Consumable Use System read surface). <see cref="ItemID.Invalid"/>
        /// always returns <see langword="false"/> — it can never be "held." An unregistered
        /// <paramref name="charId"/> logs a server error and returns <see langword="false"/>.
        /// </summary>
        /// <param name="charId">The character whose inventory to check.</param>
        /// <param name="itemId">The item to look for.</param>
        bool HasItem(CharacterID charId, ItemID itemId);

        /// <summary>
        /// Atomically places <paramref name="quantity"/> units of <paramref name="itemId"/> into
        /// <paramref name="characterId"/>'s inventory (GDD Rule 3): first topping up existing
        /// unlocked partial stacks of the same item in ascending slot order (Step 1, F-INV-2),
        /// then filling empty slots in ascending order, up to <c>StackLimit</c> each (Step 2).
        /// If any units would remain, the pickup fails with nothing written (Step 3).
        /// </summary>
        /// <remarks>
        /// <para>Tier 1 call (ADR-010) — Loot Table System calls this directly and acts on the
        /// result. On success exactly one <see cref="OnInventoryChanged"/> event fires, listing
        /// every changed slot in ascending slot order, and the character's bag-full dedup window
        /// is reset. On any failure no slot is mutated and <see cref="OnInventoryChanged"/> does
        /// not fire; a <see cref="PickupFailReason.InventoryFull"/> failure fires
        /// <see cref="OnInventoryFull"/> subject to the dedup window.</para>
        ///
        /// <para>Guard order (first match wins): <c>quantity &lt;= 0</c> →
        /// <see cref="PickupFailReason.InvalidQuantity"/>; unregistered character →
        /// <see cref="PickupFailReason.CharacterNotRegistered"/> (server error logged); invalid or
        /// unknown item, or Item Database not ready → <see cref="PickupFailReason.UnknownItem"/>
        /// (server error logged); remainder after Step 2 →
        /// <see cref="PickupFailReason.InventoryFull"/>. Quantities above a single stack are
        /// accepted and span slots.</para>
        ///
        /// <para>Calls are processed in the order received (GDD Rule 10) — there is no reordering.</para>
        /// </remarks>
        /// <param name="characterId">The character receiving the items.</param>
        /// <param name="itemId">The item being picked up.</param>
        /// <param name="quantity">Units to place. Must be &gt; 0.</param>
        /// <returns>The outcome; check <see cref="PickupResult.Success"/> / <see cref="PickupResult.Reason"/>.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity);

        /// <summary>
        /// Locks the slot at <paramref name="slotIndex"/> so its item cannot be moved, equipped,
        /// sold, or discarded until <see cref="UnlockSlot"/> is called (GDD Rule 5.12/5.13,
        /// States and Transitions: Occupied-Available → Occupied-Locked). Only the Enhancement
        /// System calls this, when an enhancement attempt begins (ADR-010 Tier 1).
        /// </summary>
        /// <remarks>
        /// <para>Never mutates <see cref="InventorySlot.ItemId"/>/<see cref="InventorySlot.Quantity"/>
        /// and never fires <see cref="OnInventoryChanged"/> — locking is a session-scoped flag
        /// flip, not a slot-content mutation (GDD: lock flags are never persisted).</para>
        ///
        /// <para>Guard order (first match wins): out-of-range <paramref name="slotIndex"/>
        /// (&lt; 0 or &gt;= <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>) logs a server
        /// error and no-ops (mirrors <see cref="GetSlot"/>'s guard); an unregistered
        /// <paramref name="charId"/> logs a server error and no-ops; an empty slot logs a server
        /// warning and no-ops without setting the lock flag (GDD Lock State Edge Cases — e.g. the
        /// item was discarded in the same tick before this call arrived).</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="slotIndex">The slot index to lock. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        void LockSlot(CharacterID charId, int slotIndex);

        /// <summary>
        /// Clears the lock on the slot at <paramref name="slotIndex"/> (GDD Rule 5.13, States and
        /// Transitions: Occupied-Locked → Occupied-Available). Only the Enhancement System calls
        /// this, when an enhancement attempt resolves — success, failure, or server timeout
        /// (ADR-010 Tier 1).
        /// </summary>
        /// <remarks>
        /// <para>Never mutates <see cref="InventorySlot.ItemId"/>/<see cref="InventorySlot.Quantity"/>
        /// and never fires <see cref="OnInventoryChanged"/>. Calling this on a slot that is not
        /// currently locked is a safe, silent no-op (GDD Lock State Edge Cases) — Enhancement
        /// System timeout and failure paths call <see cref="UnlockSlot"/> defensively, and a
        /// double-unlock must never throw or log.</para>
        ///
        /// <para>Guard order (first match wins): out-of-range <paramref name="slotIndex"/> logs a
        /// server error and no-ops; an unregistered <paramref name="charId"/> logs a server error
        /// and no-ops.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="slotIndex">The slot index to unlock. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        void UnlockSlot(CharacterID charId, int slotIndex);

        /// <summary>
        /// Destroys the item in the slot at <paramref name="slotIndex"/>, clearing it to
        /// <see cref="ItemID.Invalid"/>/<c>Quantity = 0</c> and releasing its lock if held (GDD
        /// Rule 5, States and Transitions: the only path from Occupied-Locked to Empty; also used
        /// by the Enhancement System on an <i>unlocked</i> scroll slot per Enhancement GDD
        /// CR-ENH-15 step 4). Only the Enhancement System calls this — on item destruction and on
        /// scroll consumption (ADR-010 Tier 1).
        /// </summary>
        /// <remarks>
        /// <para>Works on any occupied slot, locked or unlocked, and clears the entire stack
        /// regardless of <see cref="InventorySlot.Quantity"/> — this is not a quantity-aware
        /// removal (Implementation Notes: a cross-GDD conflict with stackable Enhancement Scrolls
        /// is open and owned by the Enhancement System GDD; out of scope for this story). On
        /// success, fires exactly one <see cref="OnInventoryChanged"/> with a single entry
        /// <c>{ slotIndex, itemId: 0, quantity: 0 }</c>. On an empty in-range slot: no-op, no
        /// event, no log (removing nothing is not a caller bug).</para>
        ///
        /// <para>Guard order (first match wins): out-of-range <paramref name="slotIndex"/>
        /// (&lt; 0 or &gt;= <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>) logs a server
        /// error and no-ops, no exception (GDD Cross-System Interface Edge Cases); an unregistered
        /// <paramref name="charId"/> logs a server error and no-ops.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="slotIndex">The slot index to clear. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        void RemoveItem(CharacterID charId, int slotIndex);

        /// <summary>
        /// Destroys <paramref name="quantity"/> units of the item in the slot at
        /// <paramref name="slotIndex"/> for <paramref name="charId"/> (GDD Rule 6 — Discard). If
        /// <paramref name="quantity"/> equals the slot's full <see cref="InventorySlot.Quantity"/>
        /// the slot becomes fully empty (<see cref="ItemID.Invalid"/>, <c>Quantity = 0</c>);
        /// otherwise the slot keeps the same item at the reduced quantity (GDD Rule 6.17/6.18).
        /// The client's <c>DiscardRequest</c> is enqueued by its network handler and processed in
        /// the zone tick loop (ADR-010 Decision 5), which calls this directly (Tier 1) and maps the
        /// result to the wire <c>DiscardResult</c> message.
        /// </summary>
        /// <remarks>
        /// <para>On success, fires exactly one <see cref="OnInventoryChanged"/> with a single
        /// entry describing the slot's post-discard state. On any failure no slot is mutated and
        /// <see cref="OnInventoryChanged"/> does not fire.</para>
        ///
        /// <para>Guard order (first match wins, matches the wire GDD's <c>DiscardRequest</c>
        /// validation order): out-of-range <paramref name="slotIndex"/> (&lt; 0 or &gt;=
        /// <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>) logs a server warning and fails
        /// with <see cref="DiscardFailReason.SlotEmpty"/>; an unregistered <paramref name="charId"/>
        /// logs a server error and fails with <see cref="DiscardFailReason.SlotEmpty"/> (decided
        /// 2026-09-27: the wire enum has no dedicated invalid-slot value — a slot that does not
        /// exist holds no item); an empty in-range slot fails with
        /// <see cref="DiscardFailReason.SlotEmpty"/>, no log (a client-triggerable condition, not a
        /// caller bug); a locked slot fails with <see cref="DiscardFailReason.SlotLocked"/>, no log
        /// (GDD Rule 5); <c>quantity &lt;= 0</c> or <c>quantity &gt;</c> the slot's current
        /// <see cref="InventorySlot.Quantity"/> fails with <see cref="DiscardFailReason.InvalidQuantity"/>,
        /// no log. None of these rejection paths throw, mutate state, or fire
        /// <see cref="OnInventoryChanged"/>.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="slotIndex">The slot index to discard from. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <param name="quantity">Units to destroy. Must satisfy <c>1 &lt;= quantity &lt;=</c> the slot's current <see cref="InventorySlot.Quantity"/>.</param>
        /// <returns>The outcome; check <see cref="DiscardResult.Success"/> / <see cref="DiscardResult.Reason"/>.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        DiscardResult Discard(CharacterID charId, int slotIndex, int quantity);

        /// <summary>
        /// Moves, merges, or swaps the contents of <paramref name="fromSlot"/> and
        /// <paramref name="toSlot"/> for <paramref name="charId"/> (GDD Rule 7 — Slot Move). The
        /// same <see cref="ItemID"/> in both slots merges (topping up <paramref name="toSlot"/>
        /// from <paramref name="fromSlot"/>; overflow beyond <c>StackLimit</c> stays in
        /// <paramref name="fromSlot"/>); an empty destination relocates the whole stack; anything
        /// else (different items) swaps the two slots' full contents. The client's
        /// <c>MoveRequest</c> is enqueued by its network handler and processed in the zone tick
        /// loop (ADR-010 Decision 5), which calls this directly (Tier 1) and maps the result to
        /// the wire <c>MoveResultMessage</c>.
        /// </summary>
        /// <remarks>
        /// <para>On any mutating success, fires exactly one <see cref="OnInventoryChanged"/> with
        /// both slots' post-operation states, recording the source slot's entry before the
        /// destination's. A same-slot move and a full-destination merge (nothing to transfer) are
        /// no-op successes: no <see cref="OnInventoryChanged"/> fires. On any failure no slot is
        /// mutated and <see cref="OnInventoryChanged"/> does not fire.</para>
        ///
        /// <para>Guard order (first match wins, matches the wire GDD's validation order — range →
        /// source lock → dest lock): out-of-range <paramref name="fromSlot"/>/<paramref name="toSlot"/>
        /// (&lt; 0 or &gt;= <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/> — wire slot
        /// fields are <see langword="byte"/>, so 20–255 is reachable) logs a server warning naming
        /// the offending index and fails with <see cref="MoveFailReason.InvalidSlot"/>; an
        /// unregistered <paramref name="charId"/> logs a server error and fails with
        /// <see cref="MoveFailReason.InvalidSlot"/> (both cases echo <see cref="InventorySlot.Empty"/>
        /// for both slots — decided 2026-09-27); <c>fromSlot == toSlot</c> is always a no-op
        /// success echoing that slot's current state as both <see cref="MoveResult.FromSlot"/> and
        /// <see cref="MoveResult.ToSlot"/>, even when the slot is empty or locked (an empty slot
        /// can never be locked, but this check runs before either is inspected); an empty source
        /// slot fails with <see cref="MoveFailReason.InvalidSlot"/>, no log (a normal
        /// client-triggerable outcome); a locked source fails with
        /// <see cref="MoveFailReason.SourceLocked"/>; a locked destination fails with
        /// <see cref="MoveFailReason.DestLocked"/>. None of these rejection paths throw, mutate
        /// state, or fire <see cref="OnInventoryChanged"/>.</para>
        ///
        /// <para>Merge <c>StackLimit</c> is resolved from the Item Database exactly like
        /// <see cref="Pickup"/>; if it cannot be resolved (unreachable in production under the
        /// <see cref="Pickup"/> guard), this logs a server error and falls back to a swap.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="fromSlot">The source slot index. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <param name="toSlot">The destination slot index. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <returns>The outcome; check <see cref="MoveResult.Success"/> / <see cref="MoveResult.Reason"/> / <see cref="MoveResult.FromSlot"/> / <see cref="MoveResult.ToSlot"/>.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        MoveResult Move(CharacterID charId, int fromSlot, int toSlot);

        /// <summary>
        /// Removes the single item from the slot at <paramref name="slotIndex"/> and empties it
        /// (equip from bag — GDD Interactions table, Equipment System row). Only the Equipment
        /// System calls this, to take an item out of the bag while equipping it (ADR-010 Tier 1).
        /// </summary>
        /// <remarks>
        /// <para>On success, fires exactly one <see cref="OnInventoryChanged"/> with a single entry
        /// <c>{ slotIndex, itemId: 0, quantity: 0 }</c>. On any failure no slot is mutated and
        /// <see cref="OnInventoryChanged"/> does not fire.</para>
        ///
        /// <para>Guard order (first match wins): out-of-range <paramref name="slotIndex"/> (&lt; 0
        /// or &gt;= <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>) logs a server error and
        /// fails with <see cref="MoveItemOutCode.SlotEmpty"/>; an unregistered
        /// <paramref name="charId"/> logs a server error and fails the same way; an empty slot fails
        /// with <see cref="MoveItemOutCode.SlotEmpty"/>, no log (a normal, caller-anticipated
        /// outcome); a locked slot fails with <see cref="MoveItemOutCode.SlotLocked"/>, no log (the
        /// Equipment System can legitimately hit a locked slot — GDD Rule 5); a slot holding more
        /// than one unit (a consumable stack — never an equipment item) logs a server error and
        /// fails with <see cref="MoveItemOutCode.SlotEmpty"/> with no mutation, so a stack is never
        /// silently lost (decided 2026-09-27; caller bug — only equipment, StackLimit 1, may be
        /// moved out). Every failure carries
        /// <see cref="MoveItemOutResult.ItemId"/> equal to <see cref="ItemID.Invalid"/>.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="slotIndex">The slot index to empty. Valid range: [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>).</param>
        /// <returns>The outcome; check <see cref="MoveItemOutResult.Code"/> / <see cref="MoveItemOutResult.ItemId"/>.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex);

        /// <summary>
        /// Places one unit of <paramref name="itemId"/> into the lowest-index empty slot (GDD Rule
        /// 8 — Unequip to Bag). Only the Equipment System calls this, to return a
        /// previously-equipped item to the bag (ADR-010 Tier 1). Never merges into an existing stack of the same
        /// item — equipment is always placed at quantity 1 into an empty slot.
        /// </summary>
        /// <remarks>
        /// <para>On success, fires exactly one <see cref="OnInventoryChanged"/> with a single entry
        /// <c>{ slotIndex, itemId, quantity: 1 }</c>. On any failure no slot is mutated,
        /// <see cref="OnInventoryChanged"/> does not fire, and — unlike <see cref="ForceInsert"/> —
        /// <see cref="OnInventoryFull"/> never fires either (the caller keeps the item and decides
        /// how to react). A successful call does not reset the bag-full dedup window (GDD Rule
        /// 4.10, literal) — only a successful <see cref="Pickup"/> does.</para>
        ///
        /// <para>Guard order (first match wins): an unregistered <paramref name="charId"/> logs a
        /// server error and fails; an invalid or unknown <paramref name="itemId"/>, or Item Database
        /// not ready, logs a server error and fails (these are caller bugs — this is a Tier 1,
        /// never-wire-reachable call, so no category check is performed); no empty slot fails with
        /// no mutation, no event, and no <see cref="OnInventoryFull"/>. The free-slot check happens
        /// at call time, not at any earlier query time — a caller that checked
        /// <see cref="HasFreeSlot"/> and then lost the race to another mutation must handle a
        /// failure here rather than assume success.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="itemId">The item to place. Must be a known item in the Item Database.</param>
        /// <returns>The outcome; check <see cref="MoveItemInResult.Success"/> / <see cref="MoveItemInResult.SlotIndex"/>.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId);

        /// <summary>
        /// Places one unit of <paramref name="itemId"/> into the lowest-index empty slot, identical
        /// to <see cref="MoveItemIn"/> except that a full bag reports the shared bag-full
        /// notification (GDD Rule 4.10) instead of silently failing. Used by the Equipment System
        /// to place a merge result, and (per equipment-system.md CR-EQS-8) as the fallback when a
        /// plain <see cref="MoveItemIn"/> loses a same-tick race for the last free slot (ADR-010
        /// Tier 1).
        /// </summary>
        /// <remarks>
        /// <para>On success, fires exactly one <see cref="OnInventoryChanged"/> with a single entry
        /// <c>{ slotIndex, itemId, quantity: 1 }</c>; never merges into an existing stack. On a full
        /// bag, no slot is mutated, <see cref="OnInventoryChanged"/> does not fire, and the shared
        /// bag-full notification policy runs — <see cref="OnInventoryFull"/> fires only outside the
        /// active <see cref="InventoryConstants.BAG_FULL_DEDUP_WINDOW_TICKS"/> dedup window. On an
        /// item-validation or character-registration failure, no slot is mutated, no event fires,
        /// and <see cref="OnInventoryFull"/> never fires — only a full bag counts as "blocked for
        /// space." Neither a successful call nor a full-bag call ever resets the bag-full dedup
        /// window — only a successful <see cref="Pickup"/> does.</para>
        ///
        /// <para>Guard order (first match wins): an unregistered <paramref name="charId"/> logs a
        /// server error and fails; an invalid or unknown <paramref name="itemId"/>, or Item Database
        /// not ready, logs a server error and fails (no category check — this also places Equipment
        /// merge results); no empty slot fails and fires the shared bag-full notification for
        /// <paramref name="charId"/>.</para>
        /// </remarks>
        /// <param name="charId">The character whose inventory to mutate.</param>
        /// <param name="itemId">The item to place. Must be a known item in the Item Database.</param>
        /// <returns><see langword="true"/> iff the item was placed.</returns>
        /// <exception cref="InvalidOperationException">Called synchronously from an <see cref="OnInventoryChanged"/> subscriber.</exception>
        bool ForceInsert(CharacterID charId, ItemID itemId);
    }
}

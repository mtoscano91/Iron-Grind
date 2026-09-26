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
    /// (ADR-010 Decision 3). Story 002 adds the first mutator, <see cref="Pickup"/>; Story 004
    /// (Slot Locks), Story 005 (Discard), Story 006 (Move/Merge/Swap), Story 007 (Equipment
    /// interface), and Story 008 (Sell/Consume) each add their own mutator(s), all of which fire
    /// <see cref="OnInventoryChanged"/>.
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
        /// Every slot of a freshly registered inventory is unlocked. No lock-mutation API exists
        /// yet — Story 004 adds <c>LockSlot</c>/<c>UnlockSlot</c>.
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
        /// every changed slot in ascending slot order. On any failure no slot is mutated and no
        /// event fires.</para>
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
    }
}

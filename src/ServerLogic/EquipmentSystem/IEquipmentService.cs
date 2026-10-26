#nullable enable

using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Server-authoritative gear slots for every character on the server (design/gdd/equipment-system.md).
    /// </summary>
    /// <remarks>
    /// <para>The GDD writes the entry points for one character; the server holds many, so every
    /// method takes the <see cref="CharacterID"/> first, as <c>IInventoryService</c> does. Character
    /// Stats is keyed by <see cref="EntityID"/>, so <see cref="RegisterCharacter"/> records both.</para>
    ///
    /// <para>Story 001 establishes the slot registry, its lifecycle and the read surface
    /// (TR-equip-001, TR-equip-008). Reads never throw and never allocate: a caller bug
    /// (unregistered character, slot outside <c>[0, 6]</c>) logs a server error and returns the
    /// empty value, as <c>IInventoryService.GetSlot</c> does.</para>
    /// </remarks>
    public interface IEquipmentService
    {
        /// <summary>
        /// Raised at the end of an <see cref="Equip"/> or <see cref="Unequip"/> call when the
        /// character's <c>equipmentAppearanceFlags</c> byte (<see cref="GetAppearanceFlags"/>) changed
        /// (CR-EQS-11, EC-EQS-8, Story 005) — never on <see cref="EquipResult.NoChange"/> or a failure
        /// that left the slots unchanged, and never when the byte is unchanged. The one failure that
        /// can raise it is the swap double failure described on <see cref="Equip"/>, which empties
        /// the slot. Raised after the slot write and after <see cref="GetAppearanceFlags"/> already
        /// returns the new byte, so a subscriber may call <see cref="Equip"/> / <see cref="Unequip"/>
        /// (the nested change raises its own event). A subscriber's exception is logged and contained;
        /// the subscribers after it on the same event are skipped (multicast delegate). If
        /// <see cref="Equip"/> or <see cref="Unequip"/> throws, the byte is not recomputed and nothing
        /// is raised. This is the only event the Equipment System raises (CR-EQS-10); stat changes
        /// come from Character Stats.
        /// </summary>
        event System.Action<EquipmentAppearanceChangedEventArgs>? OnAppearanceChanged;

        /// <summary>
        /// Returns the character's cached <c>equipmentAppearanceFlags</c> byte (CR-EQS-11): WeaponTier
        /// <c>[7:6]</c> and ElementType <c>[5:3]</c> of the Weapon slot's item, PrestigeBand
        /// <c>[2:1]</c> from the Weapon slot's enhancement level only, ArmorTier <c>[0]</c> = 1 when
        /// the highest equipped armor (Helmet, Chest, Legs, Boots) is Steel or Dark Steel. 0 for a
        /// character with every slot empty (EC-EQS-11).
        /// </summary>
        /// <param name="charId">A registered character.</param>
        /// <returns>The byte; 0 for an unregistered character (also logs a server error).</returns>
        byte GetAppearanceFlags(CharacterID charId);

        /// <summary>
        /// Creates 7 empty gear slots for <paramref name="charId"/> and maps
        /// <paramref name="entityId"/> to it.
        /// </summary>
        /// <param name="charId">The character to register.</param>
        /// <param name="entityId">The character's entity in Character Stats.</param>
        /// <exception cref="System.ArgumentException">
        /// <paramref name="charId"/> is <see cref="CharacterID.Invalid"/> or <paramref name="entityId"/>
        /// is <see cref="EntityID.Invalid"/> — a caller bug. No state changes.
        /// </exception>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="charId"/> is already registered, or <paramref name="entityId"/> is already
        /// mapped to a character. No state changes. A reload calls <see cref="UnregisterCharacter"/>
        /// first: resetting occupied slots would orphan their modifiers in Character Stats.
        /// </exception>
        void RegisterCharacter(CharacterID charId, EntityID entityId);

        /// <summary>
        /// Drops <paramref name="charId"/>'s gear slots and both map entries. Modifiers in Character
        /// Stats are not removed — the entity is discarded with the session. An unknown
        /// <paramref name="charId"/> is a silent no-op.
        /// </summary>
        /// <param name="charId">The character to release.</param>
        void UnregisterCharacter(CharacterID charId);

        /// <summary>Returns a gear slot's item and state (GDD UI Requirements).</summary>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The gear slot.</param>
        /// <returns>
        /// The slot's item and state; <see cref="EquipmentSlotState.Empty"/> for an empty slot, an
        /// unregistered character or an out-of-range slot (the last two also log a server error).
        /// </returns>
        EquipmentSlotState GetEquipmentSlotState(CharacterID charId, GearSlot slot);

        /// <summary>Returns the enhancement level of the item in a gear slot.</summary>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The gear slot.</param>
        /// <returns>The level; 0 for an empty slot, an unregistered character or an out-of-range slot.</returns>
        byte GetSlotEnhancementLevel(CharacterID charId, GearSlot slot);

        /// <summary>Returns whether a gear slot is mid-operation (CR-EQS-9). HUD subscribers defer rendering while true.</summary>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The gear slot.</param>
        /// <returns>The flag; false for an unregistered character or an out-of-range slot.</returns>
        bool IsSlotTransitioning(CharacterID charId, GearSlot slot);

        /// <summary>Returns the equipped weapon (CR-EQS-12, AC-EQS-12).</summary>
        /// <param name="charId">A registered character.</param>
        /// <returns>The weapon's item id; <see cref="ItemID.Invalid"/> when no weapon is equipped or the character is unregistered.</returns>
        ItemID GetEquippedWeaponItemID(CharacterID charId);

        /// <summary>
        /// Returns the equipped weapon's enhancement level — the same value as
        /// <see cref="GetSlotEnhancementLevel"/> for <see cref="GearSlot.Weapon"/>.
        /// </summary>
        /// <param name="charId">A registered character.</param>
        /// <returns>The level; 0 when no weapon is equipped or the character is unregistered.</returns>
        byte GetEquippedWeaponEnhancementLevel(CharacterID charId);

        /// <summary>
        /// Moves an item from the bag into an empty gear slot and registers its stat modifiers
        /// (CR-EQS-5, Story 002). Each modifier's flat bonus is the enhanced value for the level the
        /// item carried in the bag (F-ENH-1), never the raw Item Database value.
        /// </summary>
        /// <remarks>
        /// <para>Validation runs first, in this order, and rejects before any Inventory or Character
        /// Stats call that changes state (CR-EQS-2/3/4/13, Story 003): slot in <c>[0, 6]</c>
        /// (else <see cref="EquipResult.SlotMismatch"/>, logged) → <paramref name="itemId"/> is not
        /// <see cref="ItemID.Invalid"/> → bag slot index in <c>[0, 19]</c> and holding
        /// <paramref name="itemId"/> (stale-render guard, else <see cref="EquipResult.ItemNotInInventory"/>)
        /// → the item has equipment data for this gear slot (else <see cref="EquipResult.SlotMismatch"/>;
        /// a non-equipment item is silent, an item missing from the Item Database is also logged)
        /// → same item and level already in an occupied target slot (<see cref="EquipResult.NoChange"/>)
        /// → base stat meets the requirement (else <see cref="EquipResult.StatRequirementNotMet"/>;
        /// buffs and equipment do not count; items with no requirement stat are never gated; a
        /// requirement stat outside <see cref="StatID"/> is logged and rejected the same way).</para>
        /// <para>An occupied target slot that passes validation auto-swaps (CR-EQS-6/8): the bag must
        /// have a free slot (else <see cref="EquipResult.InventoryFull"/>, nothing changed); the old
        /// item's modifiers are removed, the old item returns to the bag at its level, the new item
        /// leaves the bag and its enhanced modifiers are registered. The slot reads
        /// <see cref="SlotState.Transitioning"/> (<see cref="IsSlotTransitioning"/> true) from the first
        /// modifier removal until the new entry is written or the swap is rolled back (CR-EQS-9).
        /// <see cref="EquipResult.InventoryError"/> means the old item could not return to the bag (or
        /// its definition is missing) and the swap was aborted in place; if the new item then cannot
        /// leave the bag the old item is reclaimed and the result is <see cref="EquipResult.ItemLocked"/>
        /// or <see cref="EquipResult.ItemNotInInventory"/>. The one exception to "nothing changed": if
        /// that reclaim also fails (not expected on a single-threaded tick), the result is
        /// <see cref="EquipResult.InventoryError"/>, a critical error is logged, the old item stays in
        /// the bag and the gear slot is left empty, so no item is lost or duplicated.</para>
        /// <para>An unregistered character is a caller bug: it logs a server error and returns
        /// <see cref="EquipResult.NoChange"/> with nothing changed.</para>
        /// </remarks>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The target gear slot.</param>
        /// <param name="itemId">The item to equip.</param>
        /// <param name="inventorySlotIndex">The bag slot holding the item.</param>
        /// <returns>
        /// <see cref="EquipResult.Success"/>, a validation code above, <see cref="EquipResult.ItemLocked"/>
        /// when the bag slot is locked, or <see cref="EquipResult.ItemNotInInventory"/> when
        /// <c>MoveItemOut</c> finds the bag slot empty, <see cref="EquipResult.InventoryFull"/> or
        /// <see cref="EquipResult.InventoryError"/> on the swap path. On any failure nothing changed,
        /// except the swap's double-failure case described above.
        /// </returns>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="itemId"/> is <see cref="ItemID.Invalid"/> and the service was built to throw
        /// on it (dev builds); release builds return <see cref="EquipResult.ItemNotInInventory"/> instead.
        /// </exception>
        EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex);

        /// <summary>
        /// Returns the item in a gear slot to the bag at its enhancement level and removes its stat
        /// modifiers (CR-EQS-7, Story 002).
        /// </summary>
        /// <remarks>
        /// An empty slot returns <see cref="EquipResult.NoChange"/> (no GDD rule names a code for
        /// it). A slot outside <c>[0, 6]</c> logs a server error and returns
        /// <see cref="EquipResult.SlotMismatch"/> before any array access (CR-EQS-13); an
        /// unregistered character logs and returns <see cref="EquipResult.NoChange"/>. If the item definition has been
        /// removed from the Item Database, the slot is cleared without touching modifiers and the
        /// call returns <see cref="EquipResult.Success"/> after a server error is logged.
        /// </remarks>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The gear slot to empty.</param>
        /// <returns>
        /// <see cref="EquipResult.Success"/>; <see cref="EquipResult.InventoryFull"/> when the bag
        /// has no free slot (nothing changed); <see cref="EquipResult.InventoryError"/> when
        /// <c>MoveItemIn</c> failed (modifiers restored, slot unchanged).
        /// </returns>
        EquipResult Unequip(CharacterID charId, GearSlot slot);

        /// <summary>
        /// Merges three identical accessories (Ring / Necklace) in the bag into one item of the next
        /// level (CR-EQS-14, TR-equip-009, Story 006). All-or-nothing; never reads or writes a gear
        /// slot, never touches <see cref="IsSlotTransitioning"/>, registers no modifiers and raises no
        /// appearance event.
        /// </summary>
        /// <remarks>
        /// <para>Preconditions, all checked before any Inventory mutation, in this order: indices in
        /// <c>[0, INVENTORY_SLOT_COUNT)</c> and pairwise distinct, the three slots hold the same
        /// non-empty <see cref="ItemID"/> and none is locked, the item is equipment with
        /// <see cref="GearSlot.Ring"/> or <see cref="GearSlot.Necklace"/> (all else
        /// <see cref="MergeResult.RejectedInvalidSources"/>) → defense-in-depth equipped check
        /// (<see cref="MergeResult.RejectedEquipped"/>) → <c>MergeResultItemID</c> non-null
        /// (<see cref="MergeResult.RejectedMaxLevel"/>) → a free bag slot
        /// (<see cref="MergeResult.RejectedInventoryFull"/>).</para>
        /// <para>Execution: the three sources leave the bag, the result is inserted at level 0. If the
        /// insert fails the sources are restored: <see cref="MergeResult.RejectedMergeError"/> when all
        /// are back, <see cref="MergeResult.CriticalRollbackFailed"/> (with a critical error log naming
        /// the entity and the unrestored count) otherwise. An unregistered character is a caller bug:
        /// it logs a server error and returns <see cref="MergeResult.RejectedInvalidSources"/>.</para>
        /// </remarks>
        /// <param name="charId">A registered character.</param>
        /// <param name="slotIdx1">First source bag slot.</param>
        /// <param name="slotIdx2">Second source bag slot.</param>
        /// <param name="slotIdx3">Third source bag slot.</param>
        /// <returns>The outcome; see <see cref="MergeResult"/>.</returns>
        MergeResult RequestMerge(CharacterID charId, int slotIdx1, int slotIdx2, int slotIdx3);
    }
}

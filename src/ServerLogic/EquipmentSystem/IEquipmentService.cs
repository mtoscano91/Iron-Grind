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
        /// <para>Story 002 covers the empty-slot path only: validation (slot type, stat gate,
        /// same-item guard) is Story 003, auto-swap into an occupied slot Story 004. Until then an
        /// occupied target slot throws <see cref="System.NotSupportedException"/>.</para>
        /// <para>A caller bug — unregistered character, slot outside <c>[0, 6]</c>, or an item
        /// without equipment data — logs a server error and returns <see cref="EquipResult.NoChange"/>
        /// with nothing changed.</para>
        /// </remarks>
        /// <param name="charId">A registered character.</param>
        /// <param name="slot">The target gear slot.</param>
        /// <param name="itemId">The item to equip.</param>
        /// <param name="inventorySlotIndex">The bag slot holding the item.</param>
        /// <returns>
        /// <see cref="EquipResult.Success"/>; <see cref="EquipResult.ItemLocked"/> when the bag slot
        /// is locked; <see cref="EquipResult.ItemNotInInventory"/> when the bag slot is empty. On any
        /// failure nothing changed.
        /// </returns>
        /// <exception cref="System.NotSupportedException">The target slot is occupied (removed by Story 004).</exception>
        EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex);

        /// <summary>
        /// Returns the item in a gear slot to the bag at its enhancement level and removes its stat
        /// modifiers (CR-EQS-7, Story 002).
        /// </summary>
        /// <remarks>
        /// An empty slot returns <see cref="EquipResult.NoChange"/> (no GDD rule names a code for
        /// it; Story 003 may revisit). An unregistered character or out-of-range slot logs a server
        /// error and returns <see cref="EquipResult.NoChange"/>. If the item definition has been
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
    }
}

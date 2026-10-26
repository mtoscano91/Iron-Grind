#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using UnityEngine;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// In-memory, server-authoritative implementation of <see cref="IEquipmentService"/>, and the
    /// <see cref="IEquippedWeaponQuery"/> Damage Calculation reads the attacker's weapon through.
    /// </summary>
    /// <remarks>
    /// <para><b>Storage:</b> each registered character holds a fixed array of 7
    /// <see cref="EquipmentSlotEntry"/> values indexed by <c>(int)GearSlot</c> (CR-EQS-1) — never a
    /// dictionary keyed by the enum (IL2CPP). The service keeps <see cref="CharacterID"/> → slots and
    /// entity, and <see cref="EntityID"/> → <see cref="CharacterID"/> for the
    /// <see cref="IEquippedWeaponQuery"/> reads.</para>
    ///
    /// <para><b>Reads</b> are allocation-free (Damage Calculation calls them on every hit). A caller
    /// bug — unregistered character, slot outside <c>[0, 6]</c> — logs a server error and returns
    /// the empty value. The <see cref="IEquippedWeaponQuery"/> reads are the exception: an unknown
    /// entity is normal there (a mob has no gear) and returns the empty value silently (EC-EQS-9).</para>
    ///
    /// <para><b>Thread safety:</b> none, as <c>InventoryService</c>; all calls come from the zone
    /// server's main thread.</para>
    /// </remarks>
    public sealed class EquipmentService : IEquipmentService, IEquippedWeaponQuery
    {
        /// <summary>Number of gear slots per character: <see cref="GearSlot.Weapon"/> (0) … <see cref="GearSlot.Necklace"/> (6).</summary>
        public const int GEAR_SLOT_COUNT = 7;

        private const string LOG_PREFIX = "[EquipmentService]";

        private sealed class CharacterGear
        {
            public readonly EquipmentSlotEntry[] Slots = new EquipmentSlotEntry[GEAR_SLOT_COUNT];
            public readonly EntityID EntityId;

            public CharacterGear(EntityID entityId)
            {
                EntityId = entityId;
            }
        }

        private readonly Dictionary<CharacterID, CharacterGear> _characters = new Dictionary<CharacterID, CharacterGear>();
        private readonly Dictionary<EntityID, CharacterID> _entityToCharacter = new Dictionary<EntityID, CharacterID>();

        // Used from Story 002 on (equip/unequip, validation, auto-swap, merge); taken now so the
        // constructor does not change between stories.
        private readonly IInventoryService _inventory;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly IItemDatabase _itemDatabase;
        private readonly IEnhancementBonusProvider _bonuses;
        private readonly EnhancementConfig _enhancementConfig;

        /// <summary>Creates the service over its collaborators.</summary>
        /// <param name="inventory">The inventory items move in and out of.</param>
        /// <param name="stats">Character Stats, where equipment modifiers are registered.</param>
        /// <param name="itemDatabase">Item definitions (gear slot, requirements, modifiers).</param>
        /// <param name="bonuses">Flat enhancement bonus per level.</param>
        /// <param name="enhancementConfig">Enhancement tuning (maximum level).</param>
        /// <exception cref="ArgumentNullException">Any parameter is null.</exception>
        public EquipmentService(
            IInventoryService inventory,
            IronGrind.CharacterStats.CharacterStats stats,
            IItemDatabase itemDatabase,
            IEnhancementBonusProvider bonuses,
            EnhancementConfig enhancementConfig)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _itemDatabase = itemDatabase ?? throw new ArgumentNullException(nameof(itemDatabase));
            _bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
            _enhancementConfig = enhancementConfig ?? throw new ArgumentNullException(nameof(enhancementConfig));
        }

        /// <inheritdoc/>
        public void RegisterCharacter(CharacterID charId, EntityID entityId)
        {
            if (charId == CharacterID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} RegisterCharacter: CharacterID.Invalid cannot be registered.", nameof(charId));
            }

            if (entityId == EntityID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} RegisterCharacter: EntityID.Invalid cannot be registered.", nameof(entityId));
            }

            if (_characters.ContainsKey(charId))
            {
                throw new InvalidOperationException($"{LOG_PREFIX} RegisterCharacter: {charId} is already registered. Call UnregisterCharacter first.");
            }

            if (_entityToCharacter.TryGetValue(entityId, out var owner))
            {
                throw new InvalidOperationException($"{LOG_PREFIX} RegisterCharacter: {entityId} is already mapped to {owner}.");
            }

            _characters.Add(charId, new CharacterGear(entityId));
            _entityToCharacter.Add(entityId, charId);
        }

        /// <inheritdoc/>
        public void UnregisterCharacter(CharacterID charId)
        {
            if (!_characters.TryGetValue(charId, out var gear))
            {
                return;
            }

            _entityToCharacter.Remove(gear.EntityId);
            _characters.Remove(charId);
        }

        /// <inheritdoc/>
        public EquipmentSlotState GetEquipmentSlotState(CharacterID charId, GearSlot slot)
        {
            if (!TryReadSlot(charId, slot, nameof(GetEquipmentSlotState), out var entry))
            {
                return EquipmentSlotState.Empty;
            }

            // The flag is checked first so this read always agrees with IsSlotTransitioning.
            if (entry.IsTransitioning)
            {
                return new EquipmentSlotState(entry.ItemId, SlotState.Transitioning);
            }

            return entry.IsEmpty ? EquipmentSlotState.Empty : new EquipmentSlotState(entry.ItemId, SlotState.Occupied);
        }

        /// <inheritdoc/>
        public byte GetSlotEnhancementLevel(CharacterID charId, GearSlot slot)
        {
            return TryReadSlot(charId, slot, nameof(GetSlotEnhancementLevel), out var entry) ? entry.EnhancementLevel : (byte)0;
        }

        /// <inheritdoc/>
        public bool IsSlotTransitioning(CharacterID charId, GearSlot slot)
        {
            return TryReadSlot(charId, slot, nameof(IsSlotTransitioning), out var entry) && entry.IsTransitioning;
        }

        /// <inheritdoc/>
        public ItemID GetEquippedWeaponItemID(CharacterID charId)
        {
            return TryReadSlot(charId, GearSlot.Weapon, nameof(GetEquippedWeaponItemID), out var entry) ? entry.ItemId : ItemID.Invalid;
        }

        /// <inheritdoc/>
        public byte GetEquippedWeaponEnhancementLevel(CharacterID charId)
        {
            return TryReadSlot(charId, GearSlot.Weapon, nameof(GetEquippedWeaponEnhancementLevel), out var entry) ? entry.EnhancementLevel : (byte)0;
        }

        /// <inheritdoc/>
        /// <remarks>An entity with no registered character (a mob, or a logged-out player) returns <see cref="ItemID.Invalid"/> without logging (EC-EQS-9).</remarks>
        public ItemID GetEquippedWeaponID(EntityID entityId)
        {
            return TryGetGear(entityId, out var gear) ? gear.Slots[(int)GearSlot.Weapon].ItemId : ItemID.Invalid;
        }

        /// <inheritdoc/>
        /// <remarks>An entity with no registered character returns 0 without logging (EC-EQS-9).</remarks>
        public byte GetEquippedWeaponEnhancementLevel(EntityID entityId)
        {
            return TryGetGear(entityId, out var gear) ? gear.Slots[(int)GearSlot.Weapon].EnhancementLevel : (byte)0;
        }

        /// <inheritdoc/>
        public EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex)
        {
            if (!TryResolveSlot(charId, slot, nameof(Equip), out var gear))
            {
                return EquipResult.NoChange;
            }

            int slotIndex = (int)slot;
            if (!gear.Slots[slotIndex].IsEmpty)
            {
                throw new NotSupportedException($"{LOG_PREFIX} Equip: {slot} is occupied; auto-swap is not implemented until Story 004.");
            }

            EquipmentData? equipment = GetEquipmentData(itemId);
            if (equipment == null)
            {
                Debug.LogError($"{LOG_PREFIX} Equip: {itemId} has no equipment data in the Item Database.");
                return EquipResult.NoChange;
            }

            // CR-EQS-5 step 2: nothing else is called when the bag refuses to release the item.
            MoveItemOutResult moved = _inventory.MoveItemOut(charId, inventorySlotIndex);
            if (moved.Code != MoveItemOutCode.Success)
            {
                return moved.Code == MoveItemOutCode.SlotLocked ? EquipResult.ItemLocked : EquipResult.ItemNotInInventory;
            }

            // CR-EQS-5 step 3. The modifier layer cap cannot be reached with valid data (story 002).
            // moved.EnhancementLevel is within the cap GetFlatBonus enforces: the inventory rejects a
            // level above the same EnhancementConfig maximum when an item is placed (MoveItemIn,
            // ForceInsert), so no bag slot can hold one.
            RegisterEnhancedModifiers(gear.EntityId, itemId, equipment, moved.EnhancementLevel);

            // CR-EQS-5 step 4. Step 5 (appearance flags) is Story 005.
            gear.Slots[slotIndex] = new EquipmentSlotEntry(itemId, false, moved.EnhancementLevel);
            return EquipResult.Success;
        }

        /// <inheritdoc/>
        public EquipResult Unequip(CharacterID charId, GearSlot slot)
        {
            if (!TryResolveSlot(charId, slot, nameof(Unequip), out var gear))
            {
                return EquipResult.NoChange;
            }

            int slotIndex = (int)slot;
            EquipmentSlotEntry entry = gear.Slots[slotIndex];
            if (entry.IsEmpty)
            {
                return EquipResult.NoChange;
            }

            // CR-EQS-7 step 1.
            if (!_inventory.HasFreeSlot(charId))
            {
                return EquipResult.InventoryFull;
            }

            // CR-EQS-7 step 2: the definition vanished while the item was equipped. Its modifiers
            // cannot be removed safely; clearing the slot is the least-corrupt outcome.
            EquipmentData? equipment = GetEquipmentData(entry.ItemId);
            if (equipment == null)
            {
                Debug.LogError($"{LOG_PREFIX} Unequip GetItem null: {entry.ItemId}");
                gear.Slots[slotIndex] = EquipmentSlotEntry.Empty;
                return EquipResult.Success;
            }

            // CR-EQS-7 steps 3-4: OnStatChanged fires while the slot reads Transitioning.
            gear.Slots[slotIndex] = new EquipmentSlotEntry(entry.ItemId, true, entry.EnhancementLevel);
            RemoveModifiers(gear.EntityId, entry.ItemId, equipment);

            // CR-EQS-7 step 5: the bag lost a race for its last free slot.
            if (!_inventory.MoveItemIn(charId, entry.ItemId, entry.EnhancementLevel).Success)
            {
                RegisterEnhancedModifiers(gear.EntityId, entry.ItemId, equipment, entry.EnhancementLevel);
                gear.Slots[slotIndex] = entry;
                return EquipResult.InventoryError;
            }

            // CR-EQS-7 step 6. Step 7 (appearance flags) is Story 005.
            gear.Slots[slotIndex] = EquipmentSlotEntry.Empty;
            return EquipResult.Success;
        }

        // The one place equipment modifiers are added (equip, swap and rollback paths), so the
        // enhanced-registration rule cannot be bypassed: the flat bonus passed to Character Stats is
        // IEnhancementBonusProvider.GetFlatBonus(level, base, tier), never the raw database value.
        // GetFlatBonus takes an int base while StatModifierEntry.FlatBonus is a float; a fractional
        // value is a data defect, logged and rounded half away from zero, never thrown on the equip
        // path. A non-finite or out-of-int-range value has no meaningful rounding: logged and skipped.
        // PctBonus is 0: StatModifierEntry carries flat bonuses only.
        private void RegisterEnhancedModifiers(EntityID entityId, ItemID itemId, EquipmentData equipment, byte level)
        {
            StatModifierEntry[] modifiers = equipment.StatModifiers;
            for (int i = 0; i < modifiers.Length; i++)
            {
                StatModifierEntry modifier = modifiers[i];
                float rounded = MathF.Round(modifier.FlatBonus, MidpointRounding.AwayFromZero);
                if (!float.IsFinite(rounded) || rounded < int.MinValue || rounded >= int.MaxValue)
                {
                    Debug.LogError($"{LOG_PREFIX} {itemId} stat {modifier.StatId}: FlatBonus {modifier.FlatBonus} is not a usable number; modifier skipped. Fix the item data.");
                    continue;
                }

                int baseFlat = (int)rounded;
                if (modifier.FlatBonus != rounded)
                {
                    Debug.LogError($"{LOG_PREFIX} {itemId} stat {modifier.StatId}: FlatBonus {modifier.FlatBonus} is not a whole number; using {baseFlat}. Fix the item data.");
                }

                int enhancedFlat = _bonuses.GetFlatBonus(level, baseFlat, equipment.GearTier);
                _stats.AddEquipmentModifier(entityId, modifier.StatId, new EquipmentModifierEntry(enhancedFlat, 0f, itemId));
            }
        }

        // Counterpart of RegisterEnhancedModifiers: removes the item's entry from every stat it touches.
        private void RemoveModifiers(EntityID entityId, ItemID itemId, EquipmentData equipment)
        {
            StatModifierEntry[] modifiers = equipment.StatModifiers;
            for (int i = 0; i < modifiers.Length; i++)
            {
                _stats.RemoveEquipmentModifier(entityId, modifiers[i].StatId, itemId);
            }
        }

        private EquipmentData? GetEquipmentData(ItemID itemId)
        {
            ItemDefinition? definition = _itemDatabase.GetItem(itemId);
            return definition != null ? definition.EquipmentData : null;
        }

        private bool TryGetGear(EntityID entityId, out CharacterGear gear)
        {
            if (_entityToCharacter.TryGetValue(entityId, out var charId) && _characters.TryGetValue(charId, out var found))
            {
                gear = found;
                return true;
            }

            gear = null!;
            return false;
        }

        // The range check runs before the array is indexed (CR-EQS-13 bullet 2): a value above
        // Necklace (6) can arrive from a cast or a decoded wire field. The unsigned compare covers
        // both bounds, should GearSlot's underlying type ever become signed.
        private bool TryReadSlot(CharacterID charId, GearSlot slot, string caller, out EquipmentSlotEntry entry)
        {
            if (!TryResolveSlot(charId, slot, caller, out var gear))
            {
                entry = EquipmentSlotEntry.Empty;
                return false;
            }

            entry = gear.Slots[(int)slot];
            return true;
        }

        // Shared range + registration guard for reads and the Equip/Unequip entry points.
        private bool TryResolveSlot(CharacterID charId, GearSlot slot, string caller, [NotNullWhen(true)] out CharacterGear? gear)
        {
            if ((uint)(int)slot >= (uint)GEAR_SLOT_COUNT)
            {
                Debug.LogError($"{LOG_PREFIX} {caller}: gear slot {(int)slot} is out of range [0, {GEAR_SLOT_COUNT}).");
                gear = null;
                return false;
            }

            if (!_characters.TryGetValue(charId, out var found))
            {
                Debug.LogError($"{LOG_PREFIX} {caller}: {charId} is not a registered character. Call RegisterCharacter before using equipment.");
                gear = null;
                return false;
            }

            gear = found;
            return true;
        }
    }
}

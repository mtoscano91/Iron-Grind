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

        // _enhancementConfig is for a later story (accessory merge); taken now so the constructor
        // does not change between stories.
        private readonly IInventoryService _inventory;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly IItemDatabase _itemDatabase;
        private readonly IEnhancementBonusProvider _bonuses;
        private readonly EnhancementConfig _enhancementConfig;
        private readonly bool _throwOnInvalidItem;

        /// <summary>Creates the service over its collaborators.</summary>
        /// <param name="inventory">The inventory items move in and out of.</param>
        /// <param name="stats">Character Stats, where equipment modifiers are registered.</param>
        /// <param name="itemDatabase">Item definitions (gear slot, requirements, modifiers).</param>
        /// <param name="bonuses">Flat enhancement bonus per level.</param>
        /// <param name="enhancementConfig">Enhancement tuning (maximum level).</param>
        /// <param name="throwOnInvalidItem">
        /// Whether <see cref="Equip"/> with <see cref="ItemID.Invalid"/> throws
        /// <see cref="InvalidOperationException"/> (dev builds) instead of returning
        /// <see cref="EquipResult.ItemNotInInventory"/> (release builds) — CR-EQS-13. The
        /// <see langword="null"/> default (<see cref="Debug.isDebugBuild"/>) is meant for the editor and
        /// tests; the server composition root should pass it explicitly, so the behaviour does not hang
        /// on the Development Build flag of the server build.
        /// </param>
        /// <exception cref="ArgumentNullException">Any collaborator is null.</exception>
        public EquipmentService(
            IInventoryService inventory,
            IronGrind.CharacterStats.CharacterStats stats,
            IItemDatabase itemDatabase,
            IEnhancementBonusProvider bonuses,
            EnhancementConfig enhancementConfig,
            bool? throwOnInvalidItem = null)
        {
            _throwOnInvalidItem = throwOnInvalidItem ?? Debug.isDebugBuild;
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
            EquipResult validation = ValidateEquip(charId, slot, itemId, inventorySlotIndex, out var validGear, out var validEquipment);
            if (validation != EquipResult.Success)
            {
                return validation;
            }

            // Non-null whenever ValidateEquip returns Success.
            CharacterGear gear = validGear!;
            EquipmentData equipment = validEquipment!;
            int slotIndex = (int)slot;
            if (!gear.Slots[slotIndex].IsEmpty)
            {
                // CR-EQS-6 steps 1-2 (same-item guard, stat gate) already ran in ValidateEquip.
                return Swap(charId, gear, slotIndex, itemId, equipment, inventorySlotIndex);
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

        // CR-EQS-6 steps 3-9 (auto-swap into an occupied slot), CR-EQS-8 and CR-EQS-9. Step 10
        // (appearance flags) is Story 005. No BeginStatTransaction / EndStatTransaction (CR-EQS-15):
        // OnStatChanged fires on the remove and again on the add, with the slot reading Transitioning
        // in between. ForceInsert is never used here (CR-EQS-8).
        private EquipResult Swap(
            CharacterID charId, CharacterGear gear, int slotIndex, ItemID newItemId, EquipmentData newEquipment, int newItemBagSlot)
        {
            EquipmentSlotEntry entry = gear.Slots[slotIndex];

            // Step 3: before any mutation.
            if (!_inventory.HasFreeSlot(charId))
            {
                return EquipResult.InventoryFull;
            }

            // Not a GDD rule (Story 004 code review): the old item's definition vanished while equipped.
            // Its modifiers cannot be enumerated and the Inventory rejects an unknown item in
            // MoveItemIn, so the swap could never complete; refuse it before any mutation.
            // Unequip clears such a slot (CR-EQS-7 step 2).
            EquipmentData? oldEquipment = GetEquipmentData(entry.ItemId);
            if (oldEquipment == null)
            {
                Debug.LogError($"{LOG_PREFIX} Equip swap GetItem null: {entry.ItemId}; swap refused, Unequip clears the slot. Entity {gear.EntityId}.");
                return EquipResult.InventoryError;
            }

            // Steps 4-5: OnStatChanged fires while the slot reads Transitioning.
            gear.Slots[slotIndex] = new EquipmentSlotEntry(entry.ItemId, true, entry.EnhancementLevel);
            RemoveModifiers(gear.EntityId, entry.ItemId, oldEquipment);

            // Step 6 / CR-EQS-8: abort in place; nothing has moved yet.
            MoveItemInResult returned = _inventory.MoveItemIn(charId, entry.ItemId, entry.EnhancementLevel);
            if (!returned.Success)
            {
                RestoreOldItem(gear, slotIndex, entry, oldEquipment);
                Debug.LogError($"{LOG_PREFIX} Auto-swap aborted: old item {entry.ItemId} could not return to inventory. Entity {gear.EntityId}.");
                return EquipResult.InventoryError;
            }

            return CompleteSwap(charId, gear, slotIndex, entry, oldEquipment, returned.SlotIndex, newItemId, newEquipment, newItemBagSlot);
        }

        // CR-EQS-6 steps 7-9. The old item is in the bag at oldItemReturnedSlotIndex, its modifiers
        // removed, and the slot still reads Transitioning.
        private EquipResult CompleteSwap(
            CharacterID charId, CharacterGear gear, int slotIndex, EquipmentSlotEntry entry, EquipmentData oldEquipment,
            int oldItemReturnedSlotIndex, ItemID newItemId, EquipmentData newEquipment, int newItemBagSlot)
        {
            // Step 7: the new item leaves the bag.
            MoveItemOutResult moved = _inventory.MoveItemOut(charId, newItemBagSlot);
            if (moved.Code != MoveItemOutCode.Success)
            {
                return RollBackSwap(charId, gear, slotIndex, entry, oldEquipment, oldItemReturnedSlotIndex, newItemId, newItemBagSlot, moved.Code);
            }

            // Step 8, then step 9 (flag cleared by the new entry). Step 10 (appearance flags) is Story 005.
            RegisterEnhancedModifiers(gear.EntityId, newItemId, newEquipment, moved.EnhancementLevel);
            gear.Slots[slotIndex] = new EquipmentSlotEntry(newItemId, false, moved.EnhancementLevel);
            return EquipResult.Success;
        }

        // CR-EQS-6 step 7 failure: reclaim the old item from the bag slot step 6 placed it in, then
        // restore it (SlotLocked → ItemLocked, SlotEmpty → ItemNotInInventory).
        private EquipResult RollBackSwap(
            CharacterID charId, CharacterGear gear, int slotIndex, EquipmentSlotEntry entry, EquipmentData oldEquipment,
            int oldItemReturnedSlotIndex, ItemID newItemId, int newItemBagSlot, MoveItemOutCode failure)
        {
            if (_inventory.MoveItemOut(charId, oldItemReturnedSlotIndex).Code != MoveItemOutCode.Success)
            {
                // Not a GDD rule (Story 004 code review, option A): the reclaim cannot fail on a
                // single-threaded tick. If it does, the old item stays in the bag and the slot is left
                // Empty with its modifiers removed - consistent, nothing lost. Restoring the slot
                // instead would duplicate the item.
                Debug.LogError($"{LOG_PREFIX} CRITICAL: auto-swap rollback failed. Old item {entry.ItemId} could not be reclaimed from bag slot {oldItemReturnedSlotIndex} after new item {newItemId} failed to leave bag slot {newItemBagSlot} ({failure}); the slot is left empty and the old item stays in the bag. Entity {gear.EntityId}.");
                gear.Slots[slotIndex] = EquipmentSlotEntry.Empty;
                return EquipResult.InventoryError;
            }

            RestoreOldItem(gear, slotIndex, entry, oldEquipment);
            return failure == MoveItemOutCode.SlotLocked ? EquipResult.ItemLocked : EquipResult.ItemNotInInventory;
        }

        // Swap rollback: old item's enhanced modifiers back at the entry's level, entry restored with the flag cleared.
        private void RestoreOldItem(CharacterGear gear, int slotIndex, EquipmentSlotEntry entry, EquipmentData oldEquipment)
        {
            RegisterEnhancedModifiers(gear.EntityId, entry.ItemId, oldEquipment, entry.EnhancementLevel);
            gear.Slots[slotIndex] = entry;
        }

        /// <inheritdoc/>
        public EquipResult Unequip(CharacterID charId, GearSlot slot)
        {
            if (!IsSlotInRange(slot, nameof(Unequip)))
            {
                return EquipResult.SlotMismatch;
            }

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

        // Equip validation (CR-EQS-2/3/4/13) in its fixed order, rejecting before any Inventory
        // mutation or Character Stats write. The order matches the wire EquipRequest validation
        // (networking-wire-protocol.md), with the same-item guard before the stat gate (CR-EQS-6), so
        // the Story 004 swap path inherits every check. On Success, gear and equipment are non-null.
        private EquipResult ValidateEquip(
            CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex, out CharacterGear? gear, out EquipmentData? equipment)
        {
            gear = null;
            equipment = null;

            // 1. Slot range, before any _slots access.
            if (!IsSlotInRange(slot, nameof(Equip)))
            {
                return EquipResult.SlotMismatch;
            }

            // 2. ItemID.Invalid. The wire routes itemId 0 to Unequip, so this is defence only.
            if (itemId == ItemID.Invalid)
            {
                return RejectInvalidItem();
            }

            if (!TryResolveSlot(charId, slot, nameof(Equip), out gear))
            {
                return EquipResult.NoChange;
            }

            // 3. Stale-render guard.
            if (!TryReadBagSlot(charId, itemId, inventorySlotIndex, out InventorySlot bagSlot))
            {
                return EquipResult.ItemNotInInventory;
            }

            // 4. The item is equipment for this slot.
            equipment = GetEquipmentDataLogged(itemId);
            if (equipment == null || equipment.GearSlot != slot)
            {
                return EquipResult.SlotMismatch;
            }

            // 5. Same item and level already equipped (EC-EQS-8). 6. Stat gate on the base value.
            if (IsSameItem(gear.Slots[(int)slot], itemId, bagSlot.EnhancementLevel))
            {
                return EquipResult.NoChange;
            }

            return MeetsRequirement(gear.EntityId, itemId, equipment) ? EquipResult.Success : EquipResult.StatRequirementNotMet;
        }

        private EquipResult RejectInvalidItem()
        {
            if (_throwOnInvalidItem)
            {
                throw new InvalidOperationException($"{LOG_PREFIX} Equip: ItemID.Invalid is not an item; call Unequip to empty a slot.");
            }

            return EquipResult.ItemNotInInventory;
        }

        // Stale-render guard: the bag slot must still hold the item the client saw. The index is
        // range-checked here because a stale index is a normal race, not a caller bug GetSlot would log.
        private bool TryReadBagSlot(CharacterID charId, ItemID itemId, int inventorySlotIndex, out InventorySlot bagSlot)
        {
            if ((uint)inventorySlotIndex >= (uint)InventoryConstants.INVENTORY_SLOT_COUNT)
            {
                bagSlot = InventorySlot.Empty;
                return false;
            }

            bagSlot = _inventory.GetSlot(charId, inventorySlotIndex);
            return bagSlot.ItemId == itemId;
        }

        // A definition with no EquipmentData (a consumable) is normal client input and returns null
        // silently. A bag item with no definition at all is a data or desync defect and is logged.
        private EquipmentData? GetEquipmentDataLogged(ItemID itemId)
        {
            ItemDefinition? definition = _itemDatabase.GetItem(itemId);
            if (definition == null)
            {
                Debug.LogError($"{LOG_PREFIX} Equip: {itemId} is in the bag but not in the Item Database.");
                return null;
            }

            return definition.EquipmentData;
        }

        // Only an occupied slot can hold the same item; an empty slot never matches (ItemID.Invalid).
        private static bool IsSameItem(EquipmentSlotEntry current, ItemID itemId, byte level)
        {
            return !current.IsEmpty && current.ItemId == itemId && current.EnhancementLevel == level;
        }

        // F-EQS-3: base value >= minimum, compared as float through the getter matching the stat's
        // schema type; buffs and equipment never count. An item with no requirement stat
        // (accessories) is never gated (CR-EQS-3). A requirement stat outside the StatID range is a
        // data defect: logged and rejected, since Character Stats does not bounds-check the index.
        private bool MeetsRequirement(EntityID entityId, ItemID itemId, EquipmentData equipment)
        {
            if (!(equipment.EquipRequirementStat is StatID stat))
            {
                return true;
            }

            if ((int)stat > (int)StatID.MovementSpeed)
            {
                Debug.LogError($"{LOG_PREFIX} Equip: {itemId} requires stat {(int)stat}, which is not a StatID. Fix the item data.");
                return false;
            }

            float baseValue = StatSchema.IsFloatStat(stat) ? _stats.GetBaseStatFloat(entityId, stat) : _stats.GetBaseStat(entityId, stat);
            return baseValue >= equipment.EquipRequirementMin;
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

        // Logs and returns false for a slot outside [0, 6]. Equip and Unequip call it first so an
        // out-of-range slot returns SlotMismatch rather than the registration guard's NoChange.
        private static bool IsSlotInRange(GearSlot slot, string caller)
        {
            if ((uint)(int)slot < (uint)GEAR_SLOT_COUNT)
            {
                return true;
            }

            Debug.LogError($"{LOG_PREFIX} {caller}: gear slot {(int)slot} is out of range [0, {GEAR_SLOT_COUNT}).");
            return false;
        }

        // Shared range + registration guard for reads and the Equip/Unequip entry points.
        private bool TryResolveSlot(CharacterID charId, GearSlot slot, string caller, [NotNullWhen(true)] out CharacterGear? gear)
        {
            if (!IsSlotInRange(slot, caller))
            {
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

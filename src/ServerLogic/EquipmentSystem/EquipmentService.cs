#nullable enable

using System;
using System.Collections.Generic;
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
            if ((uint)(int)slot >= (uint)GEAR_SLOT_COUNT)
            {
                Debug.LogError($"{LOG_PREFIX} {caller}: gear slot {(int)slot} is out of range [0, {GEAR_SLOT_COUNT}).");
                entry = EquipmentSlotEntry.Empty;
                return false;
            }

            if (!_characters.TryGetValue(charId, out var gear))
            {
                Debug.LogError($"{LOG_PREFIX} {caller}: {charId} is not a registered character. Call RegisterCharacter before reading equipment.");
                entry = EquipmentSlotEntry.Empty;
                return false;
            }

            entry = gear.Slots[(int)slot];
            return true;
        }
    }
}

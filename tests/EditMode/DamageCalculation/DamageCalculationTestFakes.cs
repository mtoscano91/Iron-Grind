#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;

namespace IronGrind.Tests.EditMode.DamageCalculation
{
    /// <summary>
    /// Configurable <see cref="IEquippedWeaponQuery"/> fake. Defaults to "no weapon equipped" and counts
    /// calls so tests can prove which queries the resolver made.
    /// </summary>
    internal sealed class FakeEquippedWeaponQuery : IEquippedWeaponQuery
    {
        /// <summary>Weapon id returned for every entity; <see cref="ItemID.Invalid"/> means none.</summary>
        public ItemID WeaponId { get; set; } = ItemID.Invalid;

        /// <summary>Enhancement level returned for every entity.</summary>
        public byte EnhancementLevel { get; set; }

        /// <summary>Number of <see cref="GetEquippedWeaponID"/> calls.</summary>
        public int WeaponIdCallCount { get; private set; }

        /// <summary>Number of <see cref="GetEquippedWeaponEnhancementLevel"/> calls.</summary>
        public int EnhancementLevelCallCount { get; private set; }

        /// <summary>Entity passed to the last <see cref="GetEquippedWeaponID"/> call.</summary>
        public EntityID LastWeaponIdEntity { get; private set; }

        /// <summary>Entity passed to the last <see cref="GetEquippedWeaponEnhancementLevel"/> call.</summary>
        public EntityID LastEnhancementLevelEntity { get; private set; }

        /// <inheritdoc/>
        public ItemID GetEquippedWeaponID(EntityID entityId)
        {
            WeaponIdCallCount++;
            LastWeaponIdEntity = entityId;
            return WeaponId;
        }

        /// <inheritdoc/>
        public byte GetEquippedWeaponEnhancementLevel(EntityID entityId)
        {
            EnhancementLevelCallCount++;
            LastEnhancementLevelEntity = entityId;
            return EnhancementLevel;
        }
    }

    /// <summary>
    /// In-memory <see cref="IItemDatabase"/> fake that counts <see cref="GetItem"/> and
    /// <see cref="TryGetItem"/> calls. Does not own the registered definitions.
    /// </summary>
    internal sealed class CountingItemDatabase : IItemDatabase
    {
        private readonly Dictionary<ItemID, ItemDefinition> _items = new Dictionary<ItemID, ItemDefinition>();
        private readonly HashSet<ItemID> _foundButNull = new HashSet<ItemID>();

        /// <summary>Number of <see cref="GetItem"/> calls.</summary>
        public int GetItemCallCount { get; private set; }

        /// <summary>Number of <see cref="TryGetItem"/> calls.</summary>
        public int TryGetItemCallCount { get; private set; }

        /// <inheritdoc/>
        public bool IsReady => true;

        /// <inheritdoc/>
        public event Action OnDatabaseReady
        {
            add { }
            remove { }
        }

        /// <summary>Registers <paramref name="definition"/> under its own item id.</summary>
        public void Add(ItemDefinition definition) => _items[definition.ItemId] = definition;

        /// <summary>Makes <see cref="TryGetItem"/> report <paramref name="id"/> as found with a null item.</summary>
        public void AddFoundButNull(ItemID id) => _foundButNull.Add(id);

        /// <inheritdoc/>
        public ItemDefinition? GetItem(ItemID id)
        {
            GetItemCallCount++;
            return _items.TryGetValue(id, out ItemDefinition? item) ? item : null;
        }

        /// <inheritdoc/>
        public bool TryGetItem(ItemID id, out ItemDefinition? item)
        {
            TryGetItemCallCount++;
            if (_foundButNull.Contains(id))
            {
                item = null;
                return true;
            }

            return _items.TryGetValue(id, out item);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
        {
            return Array.Empty<ItemDefinition>();
        }
    }

    /// <summary>
    /// <see cref="IEnhancementBonusProvider"/> fake that records every elemental-bonus call and returns a
    /// configured value, or throws a configured exception.
    /// </summary>
    internal sealed class RecordingBonusProvider : IEnhancementBonusProvider
    {
        /// <summary>Value returned by <see cref="GetElementalBonus"/>.</summary>
        public int ElementalBonusToReturn { get; set; }

        /// <summary>When set, <see cref="GetElementalBonus"/> throws it after recording the call.</summary>
        public Exception? ExceptionToThrow { get; set; }

        /// <summary>Number of <see cref="GetElementalBonus"/> calls.</summary>
        public int ElementalCallCount { get; private set; }

        /// <summary>Level argument of the last call.</summary>
        public int LastLevel { get; private set; }

        /// <summary>Base elemental damage argument of the last call.</summary>
        public int LastBaseElementalDamage { get; private set; }

        /// <summary>Gear tier argument of the last call.</summary>
        public GearTier LastGearTier { get; private set; }

        /// <summary>IsWeapon argument of the last call.</summary>
        public bool LastIsWeapon { get; private set; }

        /// <inheritdoc/>
        public int GetFlatBonus(int level, int baseFlatBonus, GearTier gearTier) => baseFlatBonus;

        /// <inheritdoc/>
        public int GetElementalBonus(int level, int baseElementalDamage, GearTier gearTier, bool isWeapon)
        {
            ElementalCallCount++;
            LastLevel = level;
            LastBaseElementalDamage = baseElementalDamage;
            LastGearTier = gearTier;
            LastIsWeapon = isWeapon;
            if (ExceptionToThrow != null)
                throw ExceptionToThrow;
            return ElementalBonusToReturn;
        }
    }
}

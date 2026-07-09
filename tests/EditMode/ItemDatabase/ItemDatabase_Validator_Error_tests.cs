using System.Linq;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode unit tests for <see cref="ItemDefinitionValidator"/> — error/rejection paths
    /// for the 15 BLOCKING acceptance criteria that exercise the import validator directly:
    /// AC-3, AC-4, AC-5, AC-7, AC-8, AC-10, AC-11, AC-12, AC-14, AC-15, AC-21, AC-22, AC-25,
    /// AC-26, AC-41.
    /// </summary>
    /// <remarks>
    /// This file's primary focus is rejection/error paths (per its "_Error_" naming). It also
    /// includes the accept/boundary-case tests called for by the story's QA Test Cases section
    /// for each of those same rules (e.g. the value one step inside the valid range, immediately
    /// adjacent to a reject case) — these assert <c>IsValid == true</c> to guard against a
    /// regression that adds a spurious error elsewhere. Advisory (warning-severity) paths remain
    /// out of scope here; see Story 003.
    ///
    /// AC-5 is split into three independent test methods per the GDD's explicit annotation:
    /// "Test independently: one test for GearSlot ≠ None with GearTier == None; one test for
    /// GearTier ≠ None with GearSlot == None; one test for both conditions simultaneously."
    ///
    /// AC-8 is likewise split into independent sub-condition tests (ElementType set alone;
    /// ElementalDamage set alone) per the same QA Test Cases annotation style, plus a Weapon-slot
    /// accept case confirming both fields may be set together there.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_Validator_Error_Tests
    {
        private List<ItemDefinition> _itemsToDestroy;

        [SetUp]
        public void SetUp()
        {
            _itemsToDestroy = new List<ItemDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in _itemsToDestroy)
            {
                if (def != null)
                    UnityEngine.Object.DestroyImmediate(def);
            }
            _itemsToDestroy.Clear();
        }

        // Helper — creates a definition and registers it for cleanup.
        private ItemDefinition MakeItem(
            uint id,
            string name,
            ItemCategory category,
            int sellPriceGold = 10,
            bool isUpgradeable = false,
            int stackLimit = 1,
            EquipmentData equipmentData = null,
            ConsumableData consumableData = null)
        {
            var def = ItemDefinitionBuilder.Build(
                id, name, category, sellPriceGold, isUpgradeable, stackLimit, equipmentData, consumableData);
            _itemsToDestroy.Add(def);
            return def;
        }

        private static bool HasSeverity(ValidationResult result, ValidationSeverity severity) =>
            result.Issues.Any(i => i.Severity == severity);

        // -----------------------------------------------------------------------
        // AC-3: two records sharing the same ItemID — fatal error naming both records,
        //       and the second record is not registered by the batch validator.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_DuplicateItemId_ReturnsFatalNamingBothRecords()
        {
            // Arrange
            var equipment = EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze);
            var itemA = MakeItem(5u, "Bronze Sword", ItemCategory.Equipment, equipmentData: equipment);
            var itemB = MakeItem(5u, "Iron Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron));

            // Act
            var result = ItemDefinitionValidator.ValidateBatch(new[] { itemA, itemB });

            // Assert
            Assert.IsFalse(result.IsValid, "A duplicate ItemID must invalidate the batch.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Fatal), "Duplicate ItemID must be Fatal.");
            Assert.IsTrue(
                result.Issues.Any(i => i.Message.Contains("Bronze Sword") && i.Message.Contains("Iron Sword")),
                "The fatal issue must name both conflicting records.");
        }

        // -----------------------------------------------------------------------
        // AC-4: equipment record with StackLimit != 1 — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_EquipmentStackLimitNotOne_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Bronze Sword", ItemCategory.Equipment,
                stackLimit: 5,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "StackLimit != 1 on equipment must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-5: consumable record with GearSlot != None or GearTier != None — error, reject.
        // Split into 3 independent cases per the GDD annotation.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableWithGearSlotSet_GearTierNone_ReturnsError()
        {
            // Arrange — EquipmentData present with a real slot but GearTier left at None.
            var item = MakeItem(1u, "Miscategorized Potion", ItemCategory.Consumable,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.None),
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 50f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A consumable with EquipmentData set must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ConsumableWithGearTierSet_GearSlotDefault_ReturnsError()
        {
            // Arrange — EquipmentData present with a real tier; GearSlot left at its default value
            // (GearSlot has no explicit "None" member, so its default value stands in for "unset").
            var item = MakeItem(1u, "Miscategorized Potion", ItemCategory.Consumable,
                equipmentData: EquipmentData.CreateForTesting(default, GearTier.Bronze),
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 50f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A consumable with EquipmentData set must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ConsumableWithBothGearSlotAndGearTierSet_ReturnsError()
        {
            // Arrange — EquipmentData present with both a real slot and a real tier.
            var item = MakeItem(1u, "Miscategorized Potion", ItemCategory.Consumable,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Ring, GearTier.Iron),
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 50f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A consumable with EquipmentData set must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-7: equipment record with an undefined GearSlot value — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_EquipmentUndefinedGearSlot_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Malformed Gear", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting((GearSlot)99, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "An undefined GearSlot value must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-8: non-weapon equipment record with elemental fields set — error, reject.
        // Split into independent sub-conditions per the QA Test Cases spec: ElementType
        // set alone, and ElementalDamage set alone. An accept case confirms the Weapon
        // slot may freely carry both fields together.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_NonWeaponEquipmentWithElementTypeSet_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Flaming Helmet", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Helmet, GearTier.Bronze, elementType: ElementType.Fire, elementalDamage: 0));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A non-weapon slot with ElementType set (alone) must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_NonWeaponEquipmentWithElementalDamageSet_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Reinforced Helmet", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Helmet, GearTier.Bronze, elementType: ElementType.None, elementalDamage: 10));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A non-weapon slot with ElementalDamage set (alone) must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_WeaponWithElementTypeAndElementalDamage_IsValid()
        {
            // Arrange
            var item = MakeItem(1u, "Flaming Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: ElementType.Fire, elementalDamage: 10));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "A weapon with ElementType and ElementalDamage both set must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-10: equipment record with GearTier = None — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_EquipmentGearTierNone_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Tierless Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.None));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "GearTier.None on equipment must reject the record.");
            var errorMessage = result.Issues.First(i => i.Severity == ValidationSeverity.Error).Message;
            StringAssert.Contains("Equipment", errorMessage, "Error message must correctly say 'Equipment' (not a typo).");
        }

        // -----------------------------------------------------------------------
        // AC-11: equipment StatModifierEntry with FlatBonus = 0.0 — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_StatModifierZeroFlatBonus_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Inert Ring", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Ring, GearTier.Bronze,
                    statModifiers: new[] { StatModifierEntry.CreateForTesting(StatID.Strength, 0f) }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A zero-contribution StatModifierEntry must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-12: weapon record with ElementType.None and ElementalDamage > 0 — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_WeaponElementNoneWithElementalDamage_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Confused Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: ElementType.None, elementalDamage: 5));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "ElementType.None with ElementalDamage > 0 must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-14: weapon record with ElementalDamage = 10,000 (one above ceiling) — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_WeaponElementalDamageOverCeiling_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Overtuned Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.DarkSteel, elementType: ElementType.Fire, elementalDamage: 10_000));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "ElementalDamage above the 9,999 ceiling must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_WeaponElementalDamageAtCeiling_IsValid()
        {
            // Arrange — boundary: exactly at the 9,999 ceiling, one below the reject case above.
            var item = MakeItem(1u, "MaxRoll Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.DarkSteel, elementType: ElementType.Fire, elementalDamage: 9_999));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "ElementalDamage at the 9,999 ceiling must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-15: equipment record with 3+ StatModifier entries — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_EquipmentTooManyStatModifiers_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Overloaded Chestplate", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Chest, GearTier.Bronze,
                    statModifiers: new[]
                    {
                        StatModifierEntry.CreateForTesting(StatID.Vitality, 5f),
                        StatModifierEntry.CreateForTesting(StatID.Defense, 3f),
                        StatModifierEntry.CreateForTesting(StatID.Strength, 2f)
                    }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "More than 2 StatModifier entries must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_EquipmentExactlyTwoStatModifiers_IsValid()
        {
            // Arrange — boundary: exactly at the authoring cap of 2, one below the reject case above.
            var item = MakeItem(1u, "Balanced Chestplate", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Chest, GearTier.Bronze,
                    statModifiers: new[]
                    {
                        StatModifierEntry.CreateForTesting(StatID.Vitality, 5f),
                        StatModifierEntry.CreateForTesting(StatID.Defense, 3f)
                    }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "Exactly 2 StatModifier entries (the authoring cap) must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-21: consumable record with EffectMagnitude <= 0 — error, reject.
        // Zero and negative are tested as two distinct cases per the QA Test Cases spec.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableEffectMagnitudeNotPositive_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Useless Potion", ItemCategory.Consumable,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 0f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "EffectMagnitude <= 0 must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ConsumableEffectMagnitudeNegative_ReturnsError()
        {
            // Arrange — distinct from the ==0 case above per the QA Test Cases spec.
            var item = MakeItem(1u, "Cursed Potion", ItemCategory.Consumable,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, -1f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A negative EffectMagnitude must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ConsumableEffectMagnitudeJustAbovePositive_IsValid()
        {
            // Arrange — boundary: a magnitude just above 0.
            var item = MakeItem(1u, "Faint Potion", ItemCategory.Consumable,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 0.001f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "An EffectMagnitude just above 0 must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-22: consumable record with StackLimit = 0 — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableStackLimitZero_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Uncarryable Potion", ItemCategory.Consumable,
                stackLimit: 0,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 50f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "StackLimit = 0 on a consumable must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ConsumableStackLimitOne_IsValid()
        {
            // Arrange — boundary: one above the reject case above.
            var item = MakeItem(1u, "Carryable Potion", ItemCategory.Consumable,
                stackLimit: 1,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 50f, 30f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "StackLimit = 1 on a consumable must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-25: any item record with SellPriceGold < 0 — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_NegativeSellPriceGold_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Underwater Sword", ItemCategory.Equipment,
                sellPriceGold: -1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A negative SellPriceGold must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_SellPriceGoldZero_IsValid()
        {
            // Arrange — accepted at the error-rule level; Story 003 handles 0 as an advisory warning.
            var item = MakeItem(1u, "Worthless Trinket", ItemCategory.Equipment,
                sellPriceGold: 0,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "SellPriceGold = 0 must be accepted at the error-rule level.");
        }

        // -----------------------------------------------------------------------
        // AC-26: record with ItemID = 0 — fatal error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ItemIdZero_ReturnsFatal()
        {
            // Arrange
            var item = MakeItem(0u, "Idless Item", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "ItemID(0) must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Fatal), "ItemID(0) must be Fatal, not merely Error.");
        }

        // -----------------------------------------------------------------------
        // AC-41: equipment StatModifierEntry with a StatId not defined in StatID — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_StatModifierUndefinedStatId_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Corrupted Necklace", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Necklace, GearTier.Bronze,
                    statModifiers: new[] { StatModifierEntry.CreateForTesting((StatID)250, 5f) }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "An undefined StatID on a StatModifierEntry must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }
    }
}

using System.Linq;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode unit tests for <see cref="ItemDefinitionValidator"/> — advisory (warning)
    /// paths for Story 003's 4 acceptance criteria: AC-13, AC-17, AC-32, AC-35. Reject/error
    /// paths are covered by <c>ItemDatabase_Validator_Error_tests.cs</c> (Story 002).
    /// </summary>
    /// <remarks>
    /// Every test here asserts <c>result.IsValid == true</c> as a central assertion — a
    /// warning must never invalidate a record. That invariant is the single most important
    /// thing this file protects against regressing.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_Validator_Warning_Tests
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
        // AC-13: ElementType non-None + ElementalDamage = 0 — accepted, no warning.
        // Confirms the Story 002 no-op path; not a new validator check.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_WeaponFireElementZeroDamage_IsValidNoIssues()
        {
            // Arrange — Bronze weapon at exactly its F-1 price, no stat modifiers, to
            // isolate this check from AC-32/AC-17 noise.
            var item = MakeItem(1u, "Ember Blade", ItemCategory.Equipment,
                sellPriceGold: 10,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: ElementType.Fire, elementalDamage: 0));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "Weapon with an elemental affinity but 0 damage must be accepted.");
            Assert.AreEqual(0, result.Issues.Count, "No error or warning should be produced for this combination.");
        }

        [Test]
        public void ItemDefinitionValidator_WeaponPhysicalElementNoneZeroDamage_IsValidNoIssues()
        {
            // Arrange — a plain physical weapon, otherwise identical setup.
            var item = MakeItem(1u, "Plain Sword", ItemCategory.Equipment,
                sellPriceGold: 10,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: ElementType.None, elementalDamage: 0));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "A purely physical weapon must be accepted.");
            Assert.AreEqual(0, result.Issues.Count, "No error or warning should be produced for this combination.");
        }

        // -----------------------------------------------------------------------
        // AC-17: StatModifierEntry.FlatBonus < 0 — warning, accepted.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_StatModifierNegativeFlatBonus_ReturnsWarning()
        {
            // Arrange
            var item = MakeItem(1u, "Cursed Boots", ItemCategory.Equipment,
                sellPriceGold: 10,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Boots, GearTier.Bronze,
                    statModifiers: new[] { StatModifierEntry.CreateForTesting(StatID.MovementSpeed, -5.0f) }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "A negative FlatBonus must not reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Warning));
            Assert.IsTrue(
                result.Issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("MovementSpeed")),
                "The warning must name the affected StatId.");
        }

        [Test]
        public void ItemDefinitionValidator_TwoNegativeStatModifiers_ReturnsTwoWarnings()
        {
            // Arrange
            var item = MakeItem(1u, "Doubly Cursed Ring", ItemCategory.Equipment,
                sellPriceGold: 10,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Ring, GearTier.Bronze,
                    statModifiers: new[]
                    {
                        StatModifierEntry.CreateForTesting(StatID.Strength, -3.0f),
                        StatModifierEntry.CreateForTesting(StatID.Dexterity, -2.0f),
                    }));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "Two negative FlatBonus entries must not reject the record.");
            var warnings = result.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
            Assert.AreEqual(2, warnings.Count, "Each negative entry must produce its own warning.");
            Assert.IsTrue(warnings.Any(i => i.Message.Contains("Strength")));
            Assert.IsTrue(warnings.Any(i => i.Message.Contains("Dexterity")));
        }

        // -----------------------------------------------------------------------
        // AC-32: SellPriceGold deviates from F-1 TierBasePrice by more than ±5% (exclusive)
        // — warning, accepted. Bronze TierBasePrice = 10; tolerance window [9g, 11g] exclusive.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_SellPriceDeviatesMoreThan5Percent_ReturnsWarning()
        {
            // Arrange — Bronze, SellPriceGold=8 (20% below expected 10g).
            var item = MakeItem(1u, "Underpriced Axe", ItemCategory.Equipment,
                sellPriceGold: 8,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "A price deviation must not reject the record.");
            Assert.IsTrue(
                result.Issues.Any(i => i.Severity == ValidationSeverity.Warning
                    && i.Message.Contains("Underpriced Axe") && i.Message.Contains("8") && i.Message.Contains("10")),
                "The warning must name the item, the authored value, and the expected value.");
        }

        [Test]
        public void ItemDefinitionValidator_SellPriceExactlyAtF1_NoWarningFromRule()
        {
            // Arrange — Bronze, SellPriceGold=10 (exactly the F-1 baseline).
            var item = MakeItem(1u, "Fairly Priced Axe", ItemCategory.Equipment,
                sellPriceGold: 10,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Issues.Count, "A price exactly at the F-1 baseline must produce no issues at all.");
        }

        [Test]
        public void ItemDefinitionValidator_SellPriceGold9_ReturnsWarningBoundary()
        {
            // Arrange — Bronze, SellPriceGold=9 (10% below expected — just outside the
            // exclusive ±5% tolerance window [9.5g, 10.5g]).
            var item = MakeItem(1u, "Boundary Axe", ItemCategory.Equipment,
                sellPriceGold: 9,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(
                HasSeverity(result, ValidationSeverity.Warning),
                "SellPriceGold=9 sits outside the exclusive tolerance window and must warn.");
        }

        [Test]
        public void ItemDefinitionValidator_SellPriceGold11_ReturnsWarningBoundary()
        {
            // Arrange — Bronze, SellPriceGold=11 (10% above expected — the mirror case of
            // the =9 test above; same |deviation| = 0.10, just on the other side of the
            // 10g baseline, and equally outside the exclusive [9.5g, 10.5g] window).
            var item = MakeItem(1u, "Overpriced Axe", ItemCategory.Equipment,
                sellPriceGold: 11,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(
                HasSeverity(result, ValidationSeverity.Warning),
                "SellPriceGold=11 sits outside the exclusive tolerance window and must warn, symmetrically with SellPriceGold=9.");
        }

        [Test]
        public void ItemDefinitionValidator_IronTierSellPriceDeviates_ReturnsWarning()
        {
            // Arrange — Iron TierBasePrice=30; SellPriceGold=20 is a 33% deviation.
            // Exercises the GetTierBasePrice Iron branch (Bronze is the only tier covered
            // by the other AC-32 tests in this file).
            var item = MakeItem(1u, "Underpriced Iron Blade", ItemCategory.Equipment,
                sellPriceGold: 20,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsTrue(
                result.Issues.Any(i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("30")),
                "The warning must name the Iron tier's expected 30g baseline.");
        }

        // -----------------------------------------------------------------------
        // AC-35: SellPriceGold = 0 — warning, accepted. Applies to any category.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_SellPriceGoldZero_ReturnsWarning()
        {
            // Arrange — on a Bronze (non-zero-baseline) equipment record, SellPriceGold=0
            // necessarily co-fires both the AC-35 zero-price rule AND the AC-32 deviation
            // rule (100% below the 10g baseline). Both are asserted explicitly below rather
            // than accepting "a" warning, so a future regression that drops either one is caught.
            var item = MakeItem(1u, "Worthless Trinket", ItemCategory.Equipment,
                sellPriceGold: 0,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Ring, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "SellPriceGold=0 must not reject the record.");
            var warnings = result.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
            Assert.AreEqual(2, warnings.Count, "SellPriceGold=0 on Bronze equipment must co-fire both AC-35 and AC-32.");
            Assert.IsTrue(
                warnings.Any(i => i.Message.Contains("SellPriceGold=0") && i.Message.Contains("Worthless Trinket")),
                "The AC-35 zero-price warning must name the item.");
            Assert.IsTrue(
                warnings.Any(i => i.Message.Contains("deviates from F-1") && i.Message.Contains("Worthless Trinket")),
                "The AC-32 deviation warning must also fire and name the item.");
        }

        [Test]
        public void ItemDefinitionValidator_SellPriceGoldOne_NoWarningFromZeroRule()
        {
            // Arrange — SellPriceGold=1 does not trigger AC-35, though AC-32's deviation
            // rule independently fires here (Bronze expects 10g) — that co-firing is
            // expected and not suppressed.
            var item = MakeItem(1u, "Nearly Worthless Trinket", ItemCategory.Equipment,
                sellPriceGold: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Ring, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.IsFalse(
                result.Issues.Any(i => i.Message.Contains("SellPriceGold=0")),
                "SellPriceGold=1 must not trigger the AC-35-specific zero-price message.");
        }
    }
}

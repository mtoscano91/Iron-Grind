using System.Collections.Generic;
using System.Linq;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode unit tests for the Enhancement Scroll rules of <see cref="ItemDefinitionValidator"/>
    /// (Story 005): AC-42, AC-43, AC-44, AC-45, AC-46.
    /// </summary>
    /// <remarks>
    /// Each test validates one rule in isolation via <see cref="ItemDefinitionValidator.ValidateRecord"/>.
    /// Accept cases adjacent to each reject case guard against a regression that adds a
    /// spurious error elsewhere.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_Validator_Scroll_Tests
    {
        private const int SCROLL_STACK_LIMIT = 99;

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
            ConsumableData consumableData = null,
            ScrollData scrollData = null)
        {
            var def = ItemDefinitionBuilder.Build(
                id, name, category, sellPriceGold, isUpgradeable, stackLimit,
                equipmentData, consumableData, scrollData);
            _itemsToDestroy.Add(def);
            return def;
        }

        // Helper — a well-formed scroll record (AC-46 shape) with overridable parts.
        private ItemDefinition MakeScroll(
            GearTier targetTier = GearTier.Bronze,
            int stackLimit = SCROLL_STACK_LIMIT,
            EquipmentData equipmentData = null)
        {
            return MakeItem(1u, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                sellPriceGold: 0,
                isUpgradeable: false,
                stackLimit: stackLimit,
                equipmentData: equipmentData,
                scrollData: ScrollData.CreateForTesting(targetTier));
        }

        private static bool HasSeverity(ValidationResult result, ValidationSeverity severity) =>
            result.Issues.Any(i => i.Severity == severity);

        // -----------------------------------------------------------------------
        // AC-42: consumable record with neither ConsumableData nor ScrollData — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableWithNeitherSubSchema_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Empty Consumable", ItemCategory.Consumable,
                stackLimit: SCROLL_STACK_LIMIT);

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A consumable with neither sub-schema must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // AC-43: consumable record with both ConsumableData and ScrollData — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableWithBothSubSchemas_ReturnsError()
        {
            // Arrange
            var item = MakeItem(1u, "Confused Consumable", ItemCategory.Consumable,
                stackLimit: SCROLL_STACK_LIMIT,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 80f, 20f),
                scrollData: ScrollData.CreateForTesting(GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A consumable with both sub-schemas must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_PotionWithOnlyConsumableData_IsValid()
        {
            // Arrange — same values as the reject case above, but ScrollData is null.
            var item = MakeItem(1u, "Plain Potion", ItemCategory.Consumable,
                stackLimit: SCROLL_STACK_LIMIT,
                consumableData: ConsumableData.CreateForTesting(EffectType.RestoreHP, 80f, 20f));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, "A potion with only ConsumableData set must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-44: equipment record with ScrollData set — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_EquipmentWithScrollData_ReturnsErrorNamingScrollData()
        {
            // Arrange
            var item = MakeItem(1u, "Bronze Sword", ItemCategory.Equipment,
                sellPriceGold: 10,
                stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze),
                scrollData: ScrollData.CreateForTesting(GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "Equipment with ScrollData set must be rejected.");
            var errors = result.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
            Assert.AreEqual(1, errors.Count, "Exactly one error is expected for the ScrollData violation.");
            StringAssert.Contains("ScrollData", errors[0].Message);
        }

        [Test]
        public void ItemDefinitionValidator_EquipmentWithoutScrollData_IsValidWithZeroIssues()
        {
            // Arrange — the same record as above with ScrollData == null.
            var item = MakeItem(1u, "Bronze Sword", ItemCategory.Equipment,
                sellPriceGold: 10,
                stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Issues.Count, "A well-formed Bronze weapon must produce no issues.");
        }

        // -----------------------------------------------------------------------
        // AC-45: scroll with TargetGearTier None or undefined — error, reject.
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ScrollTargetGearTierNone_ReturnsError()
        {
            // Arrange
            var item = MakeScroll(targetTier: GearTier.None);

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "GearTier.None as a scroll target must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ScrollTargetGearTierUndefined_ReturnsError()
        {
            // Arrange — GearTier is byte-backed with members 0-4; 255 is outside the defined set.
            var item = MakeScroll(targetTier: (GearTier)255);

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "An undefined GearTier value as a scroll target must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [TestCase(GearTier.Bronze)]
        [TestCase(GearTier.Iron)]
        [TestCase(GearTier.Steel)]
        [TestCase(GearTier.DarkSteel)]
        public void ItemDefinitionValidator_ScrollTargetGearTierDefinedRealTier_IsValid(GearTier tier)
        {
            // Arrange
            var item = MakeScroll(targetTier: tier);

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid, $"A scroll targeting {tier} must be accepted.");
        }

        // -----------------------------------------------------------------------
        // AC-46: well-formed scroll — valid, zero issues (no error, no warning).
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_WellFormedScroll_IsValidWithZeroIssues()
        {
            // Arrange
            var item = MakeScroll();

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Issues.Count,
                "A well-formed scroll must raise no error and no warning (no zero-sell-price warning).");
        }

        [Test]
        public void ItemDefinitionValidator_ScrollStackLimitZero_ReturnsError()
        {
            // Arrange — AC-22 applies to scrolls as well as potions.
            var item = MakeScroll(stackLimit: 0);

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "StackLimit = 0 on a scroll must reject the record.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        [Test]
        public void ItemDefinitionValidator_ScrollWithEquipmentData_ReturnsError()
        {
            // Arrange — AC-5 / Rule 13 item 34: a scroll must not carry EquipmentData.
            var item = MakeScroll(
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid, "A scroll with EquipmentData set must be rejected.");
            Assert.IsTrue(HasSeverity(result, ValidationSeverity.Error));
        }

        // -----------------------------------------------------------------------
        // Error accumulation: a consumable with several faults reports every one of them
        // (the validator must not stop at the first consumable error).
        // -----------------------------------------------------------------------

        [Test]
        public void ItemDefinitionValidator_ConsumableWithThreeFaults_ReturnsThreeErrors()
        {
            // Arrange — neither sub-schema (AC-42), EquipmentData set (AC-5), StackLimit = 0 (AC-22).
            var item = MakeItem(1u, "Broken Consumable", ItemCategory.Consumable,
                stackLimit: 0,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));

            // Act
            var result = ItemDefinitionValidator.ValidateRecord(item);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(3, result.Issues.Count(i => i.Severity == ValidationSeverity.Error),
                "Each of the three faults must be reported.");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode tests for the MVP record set (Story 004, extended by Story 006): AC-18,
    /// AC-24, AC-30, AC-31, AC-33, AC-34, AC-47, the Rule 13 scroll ItemIDs, and the potion
    /// record values. Verified against the in-memory record set built by
    /// <see cref="MvpItemRecordData.BuildAll"/> — the same data table the Editor seeder
    /// uses to author the real <c>.asset</c> files. This decouples AC verification from
    /// whether the seeder has actually been run in the Unity Editor.
    /// </summary>
    /// <remarks>
    /// This does not replace the story's required manual smoke check (running the
    /// seeder in-editor and confirming <c>production/qa/smoke-[date]-item-database.md</c>)
    /// — it verifies the data model is correct independent of that manual step.
    /// </remarks>
    [TestFixture]
    internal sealed class ItemDatabase_MvpRecords_Tests
    {
        private const int TOTAL_RECORDS = 38;
        private const int EQUIPMENT_RECORDS = 28;
        private const int CONSUMABLE_RECORDS = 10;
        private const int POTION_RECORDS = 6;
        private const int SCROLL_RECORDS = 4;
        private const int CONSUMABLE_STACK_LIMIT = 99;

        private List<ItemDefinition> _records;

        [SetUp]
        public void SetUp()
        {
            _records = MvpItemRecordData.BuildAll();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var def in _records)
            {
                if (def != null)
                    UnityEngine.Object.DestroyImmediate(def);
            }
            _records.Clear();
        }

        private ItemDefinition RecordWithId(uint id) =>
            _records.First(r => r.ItemId == new ItemID(id));

        // -----------------------------------------------------------------------
        // AC-18: 28 Equipment records, 0 Consumable records in the Equipment query;
        // the Consumable query returns the 10 potions and scrolls.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_GetItemsByCategoryEquipment_Returns28EquipmentOnly()
        {
            // Arrange
            var db = new IronGrind.ItemDatabase.ItemDatabase();
            db.Initialize(_records);

            // Act
            var equipment = db.GetItemsByCategory(ItemCategory.Equipment);

            // Assert
            Assert.AreEqual(EQUIPMENT_RECORDS, equipment.Count, "Exactly 28 records must be Equipment.");
            Assert.IsTrue(equipment.All(i => i.ItemCategory == ItemCategory.Equipment));
            Assert.IsFalse(equipment.Any(i => i.ItemCategory == ItemCategory.Consumable));
        }

        [Test]
        public void MvpRecords_GetItemsByCategoryConsumable_Returns10ConsumablesOnly()
        {
            // Arrange
            var db = new IronGrind.ItemDatabase.ItemDatabase();
            db.Initialize(_records);

            // Act
            var consumables = db.GetItemsByCategory(ItemCategory.Consumable);

            // Assert
            Assert.AreEqual(CONSUMABLE_RECORDS, consumables.Count, "Exactly 10 records must be Consumable.");
            Assert.IsTrue(consumables.All(i => i.ItemCategory == ItemCategory.Consumable));
        }

        // -----------------------------------------------------------------------
        // AC-24: every Equipment record IsUpgradeable==true; every Consumable
        // (potions and Enhancement Scrolls) false.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_IsUpgradeableFlag_MatchesCategoryWithNoExceptions()
        {
            // Assert
            Assert.IsTrue(
                _records.Where(r => r.ItemCategory == ItemCategory.Equipment).All(r => r.IsUpgradeable),
                "Every Equipment record must have IsUpgradeable=true.");
            Assert.IsTrue(
                _records.Where(r => r.ItemCategory == ItemCategory.Consumable).All(r => !r.IsUpgradeable),
                "Every Consumable record must have IsUpgradeable=false.");
        }

        // -----------------------------------------------------------------------
        // AC-30 / AC-31: Bronze Sword = 10g, DarkSteel Sword = 270g.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_BronzeSword_SellPriceGoldIs10()
        {
            var bronzeSword = _records.First(r => r.DisplayName == "Bronze Sword");
            Assert.AreEqual(10, bronzeSword.SellPriceGold);
        }

        [Test]
        public void MvpRecords_DarkSteelSword_SellPriceGoldIs270()
        {
            var darkSteelSword = _records.First(r => r.DisplayName == "DarkSteel Sword");
            Assert.AreEqual(270, darkSteelSword.SellPriceGold);
        }

        // -----------------------------------------------------------------------
        // AC-33: HP/MP Potion sell prices — Small=2, Medium=6, Large=18.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_HpPotionSellPrices_MatchF2Table()
        {
            Assert.AreEqual(2, _records.First(r => r.DisplayName == "HP Potion (Small)").SellPriceGold);
            Assert.AreEqual(6, _records.First(r => r.DisplayName == "HP Potion (Medium)").SellPriceGold);
            Assert.AreEqual(18, _records.First(r => r.DisplayName == "HP Potion (Large)").SellPriceGold);
        }

        [Test]
        public void MvpRecords_MpPotionSellPrices_MatchF2Table()
        {
            Assert.AreEqual(2, _records.First(r => r.DisplayName == "MP Potion (Small)").SellPriceGold);
            Assert.AreEqual(6, _records.First(r => r.DisplayName == "MP Potion (Medium)").SellPriceGold);
            Assert.AreEqual(18, _records.First(r => r.DisplayName == "MP Potion (Large)").SellPriceGold);
        }

        // -----------------------------------------------------------------------
        // AC-34: exactly 28 Equipment + 10 Consumable (6 with ConsumableData, 4 with
        // ScrollData), mutually exclusive.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_CategoryCounts_Are28EquipmentAnd10Consumable()
        {
            // Act
            var consumables = _records.Where(r => r.ItemCategory == ItemCategory.Consumable).ToList();
            int equipmentCount = _records.Count(r => r.ItemCategory == ItemCategory.Equipment);

            // Assert
            Assert.AreEqual(TOTAL_RECORDS, _records.Count, "Total record count must be exactly 38.");
            Assert.AreEqual(EQUIPMENT_RECORDS, equipmentCount);
            Assert.AreEqual(CONSUMABLE_RECORDS, consumables.Count);
            Assert.AreEqual(POTION_RECORDS, consumables.Count(r => r.ConsumableData != null),
                "Exactly 6 Consumable records must carry ConsumableData (potions).");
            Assert.AreEqual(SCROLL_RECORDS, consumables.Count(r => r.ScrollData != null),
                "Exactly 4 Consumable records must carry ScrollData (Enhancement Scrolls).");
            Assert.IsTrue(
                consumables.All(r => (r.ConsumableData != null) != (r.ScrollData != null)),
                "Every Consumable record must carry exactly one of ConsumableData / ScrollData.");
        }

        // -----------------------------------------------------------------------
        // AC-47: exactly 4 scroll records, one per gear tier, each with the scroll shape.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_ScrollRecords_AreOnePerGearTierWithScrollShape()
        {
            // Act
            var scrolls = _records.Where(r => r.ScrollData != null).ToList();
            var tiers = scrolls.Select(r => r.ScrollData.TargetGearTier).ToList();

            // Assert
            Assert.AreEqual(SCROLL_RECORDS, scrolls.Count, "Exactly 4 records must have ScrollData.");
            CollectionAssert.AreEquivalent(
                new[] { GearTier.Bronze, GearTier.Iron, GearTier.Steel, GearTier.DarkSteel }, tiers,
                "Each gear tier must be targeted by exactly one scroll.");

            foreach (var scroll in scrolls)
            {
                Assert.AreEqual(ItemCategory.Consumable, scroll.ItemCategory, scroll.DisplayName);
                Assert.IsNull(scroll.ConsumableData, scroll.DisplayName);
                Assert.IsNull(scroll.EquipmentData, scroll.DisplayName);
                Assert.AreEqual(CONSUMABLE_STACK_LIMIT, scroll.StackLimit, scroll.DisplayName);
                Assert.AreEqual(0, scroll.SellPriceGold, scroll.DisplayName);
                Assert.IsFalse(scroll.IsUpgradeable, scroll.DisplayName);
            }
        }

        // -----------------------------------------------------------------------
        // Rule 13 item 37: scrolls take ItemIDs 35–38 in tier order; IDs 1–34 keep
        // their records (Rule 11 — IDs are never reused or renumbered).
        // -----------------------------------------------------------------------

        [TestCase(35u, "Bronze Enhancement Scroll", GearTier.Bronze)]
        [TestCase(36u, "Iron Enhancement Scroll", GearTier.Iron)]
        [TestCase(37u, "Steel Enhancement Scroll", GearTier.Steel)]
        [TestCase(38u, "Dark Steel Enhancement Scroll", GearTier.DarkSteel)]
        public void MvpRecords_ScrollRecord_HasExpectedIdNameAndTier(uint id, string displayName, GearTier tier)
        {
            // Act
            var scroll = RecordWithId(id);

            // Assert
            Assert.AreEqual(displayName, scroll.DisplayName);
            Assert.IsNotNull(scroll.ScrollData, "The record must be an Enhancement Scroll.");
            Assert.AreEqual(tier, scroll.ScrollData.TargetGearTier);
        }

        [TestCase(1u, "Bronze Sword")]
        [TestCase(28u, "DarkSteel Necklace")]
        [TestCase(29u, "HP Potion (Small)")]
        [TestCase(34u, "MP Potion (Large)")]
        public void MvpRecords_ExistingRecord_KeepsItsItemId(uint id, string displayName)
        {
            Assert.AreEqual(displayName, RecordWithId(id).DisplayName);
        }

        // -----------------------------------------------------------------------
        // Potion record values (Story 006): EffectMagnitude and CooldownSeconds from
        // consumable-use-system.md (F-CUS-3 table, "CooldownSeconds — Authored
        // Constants"); StackLimit 99 from design/registry/entities.yaml.
        // -----------------------------------------------------------------------

        [TestCase("HP Potion (Small)", EffectType.RestoreHP, 80f, 20f)]
        [TestCase("HP Potion (Medium)", EffectType.RestoreHP, 220f, 30f)]
        [TestCase("HP Potion (Large)", EffectType.RestoreHP, 500f, 45f)]
        [TestCase("MP Potion (Small)", EffectType.RestoreMP, 80f, 20f)]
        [TestCase("MP Potion (Medium)", EffectType.RestoreMP, 220f, 30f)]
        [TestCase("MP Potion (Large)", EffectType.RestoreMP, 500f, 45f)]
        public void MvpRecords_PotionRecord_HasDesignValues(
            string displayName, EffectType effect, float magnitude, float cooldownSeconds)
        {
            // Act
            var potion = _records.First(r => r.DisplayName == displayName);

            // Assert — authored constants, so exact equality.
            Assert.IsNotNull(potion.ConsumableData, "A potion must carry ConsumableData.");
            Assert.AreEqual(effect, potion.ConsumableData.EffectType);
            Assert.AreEqual(magnitude, potion.ConsumableData.EffectMagnitude);
            Assert.AreEqual(cooldownSeconds, potion.ConsumableData.CooldownSeconds);
            Assert.AreEqual(CONSUMABLE_STACK_LIMIT, potion.StackLimit);
        }

        // -----------------------------------------------------------------------
        // Validation Pass (story requirement): all 38 records must produce zero
        // issues — no fatal, no error, no warning — from the validator.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_ValidateBatch_ZeroIssues()
        {
            // Act
            var result = ItemDefinitionValidator.ValidateBatch(_records);

            // Assert
            Assert.IsTrue(result.IsValid,
                "All 38 MVP records must pass the validator with zero errors/fatals. " +
                "Failures: " + string.Join(" | ", result.Issues.Where(i => i.Severity != ValidationSeverity.Warning).Select(i => i.Message)));
            Assert.AreEqual(0, result.Issues.Count,
                "All 38 MVP records must validate with zero warnings. " +
                "Issues: " + string.Join(" | ", result.Issues.Select(i => i.Message)));
        }

        [Test]
        public void MvpRecords_AllItemIds_AreUniqueAndNeverZero()
        {
            var ids = _records.Select(r => r.ItemId).ToList();
            var distinctIds = ids.Distinct().ToList();

            Assert.AreEqual(ids.Count, distinctIds.Count, "All 38 ItemIDs must be unique.");
            Assert.IsFalse(ids.Any(id => id == ItemID.Invalid), "No record may use ItemID(0).");
        }
    }
}

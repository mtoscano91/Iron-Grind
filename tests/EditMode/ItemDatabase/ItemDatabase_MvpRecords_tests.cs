using System.Collections.Generic;
using System.Linq;
using IronGrind.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.ItemDatabase
{
    /// <summary>
    /// EditMode tests for Story 004's 6 blocking acceptance criteria (AC-18, AC-24,
    /// AC-30, AC-31, AC-33, AC-34), verified against the in-memory record set built by
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

        // -----------------------------------------------------------------------
        // AC-18: 28 Equipment records, 0 Consumable records in the Equipment query.
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
            Assert.AreEqual(28, equipment.Count, "Exactly 28 records must be Equipment.");
            Assert.IsTrue(equipment.All(i => i.ItemCategory == ItemCategory.Equipment));
            Assert.IsFalse(equipment.Any(i => i.ItemCategory == ItemCategory.Consumable));
        }

        // -----------------------------------------------------------------------
        // AC-24: every Equipment record IsUpgradeable==true; every Consumable false.
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
        // AC-34: exactly 28 Equipment + 6 Consumable, mutually exclusive.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_CategoryCounts_Are28EquipmentAnd6Consumable()
        {
            int equipmentCount = _records.Count(r => r.ItemCategory == ItemCategory.Equipment);
            int consumableCount = _records.Count(r => r.ItemCategory == ItemCategory.Consumable);

            Assert.AreEqual(28, equipmentCount);
            Assert.AreEqual(6, consumableCount);
            Assert.AreEqual(34, _records.Count, "Total record count must be exactly 34.");
        }

        // -----------------------------------------------------------------------
        // Validation Pass (story requirement): all 34 records must produce zero
        // fatal errors and zero errors when run through the Story 002/003 validator.
        // -----------------------------------------------------------------------

        [Test]
        public void MvpRecords_ValidateBatch_ZeroErrorsZeroFatals()
        {
            // Act
            var result = ItemDefinitionValidator.ValidateBatch(_records);

            // Assert
            Assert.IsTrue(result.IsValid,
                "All 34 MVP records must pass the validator with zero errors/fatals. " +
                "Failures: " + string.Join(" | ", result.Issues.Where(i => i.Severity != ValidationSeverity.Warning).Select(i => i.Message)));
        }

        [Test]
        public void MvpRecords_AllItemIds_AreUniqueAndNeverZero()
        {
            var ids = _records.Select(r => r.ItemId).ToList();
            var distinctIds = ids.Distinct().ToList();

            Assert.AreEqual(ids.Count, distinctIds.Count, "All 34 ItemIDs must be unique.");
            Assert.IsFalse(ids.Any(id => id == IronGrind.CharacterStats.ItemID.Invalid), "No record may use ItemID(0).");
        }
    }
}

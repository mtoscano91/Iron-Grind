using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.LootTableSystem
{
    /// <summary>
    /// EditMode unit tests for Enhancement Story 008: the scroll exclusion validator rule
    /// (loot-table-system.md CR-LT-16 / AC-LT-26, enhancement-system.md AC-ENH-24).
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_ScrollExclusion_Tests
    {
        private const uint SWORD_ID = 1001u;
        private const uint POTION_ID = 1002u;
        private const uint SCROLL_ID = 1003u;
        private const uint ODD_SCROLL_ID = 4242u;
        private const uint UNKNOWN_ID = 9999u;

        private const int VALID_GOLD_MIN = 4;
        private const int VALID_GOLD_MAX = 8;
        private const int BELOW_PARTY_GOLD_MIN = 3;
        private const float DROP_CHANCE = 0.5f;
        private const float POTION_MAGNITUDE = 50f;
        private const float POTION_COOLDOWN_SECONDS = 30f;

        private const bool SCROLLS_NOT_ALLOWED = false;
        private const bool SCROLLS_ALLOWED = true;

        private static readonly MobTypeID Mob1 = new MobTypeID(1u);
        private static readonly MobTypeID Mob2 = new MobTypeID(2u);

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private StubItemDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _database = new StubItemDatabase();
            Register(SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));
            Register(POTION_ID, "Health Potion", ItemCategory.Consumable,
                consumableData: ConsumableData.CreateForTesting(
                    EffectType.RestoreHP, POTION_MAGNITUDE, POTION_COOLDOWN_SECONDS));
            Register(SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                scrollData: ScrollData.CreateForTesting(GearTier.Bronze));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition def in _created)
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
            _created.Clear();
        }

        private void Register(
            uint id, string name, ItemCategory category,
            EquipmentData equipmentData = null,
            ConsumableData consumableData = null,
            ScrollData scrollData = null)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id, name, category,
                equipmentData: equipmentData,
                consumableData: consumableData,
                scrollData: scrollData);
            _created.Add(def);
            _database.Add(def);
        }

        private static LootTableDefinition Table(int goldMin, params uint[] itemIds)
        {
            var entries = new List<LootTableEntry>();
            foreach (uint itemId in itemIds)
            {
                entries.Add(new LootTableEntry(new ItemID(itemId), DROP_CHANCE));
            }
            return new LootTableDefinition(entries, goldMin, VALID_GOLD_MAX);
        }

        private static List<KeyValuePair<MobTypeID, LootTableDefinition>> Pairs(
            params KeyValuePair<MobTypeID, LootTableDefinition>[] pairs)
        {
            return new List<KeyValuePair<MobTypeID, LootTableDefinition>>(pairs);
        }

        private static KeyValuePair<MobTypeID, LootTableDefinition> Pair(MobTypeID id, LootTableDefinition table)
        {
            return new KeyValuePair<MobTypeID, LootTableDefinition>(id, table);
        }

        private IReadOnlyList<LootTableValidationIssue> ValidateSingle(LootTableDefinition table, bool allow)
        {
            return LootTableValidator.Validate(Pairs(Pair(Mob1, table)), _database, allow);
        }

        // -----------------------------------------------------------------------
        // Scroll entry rejected
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_SwordAndScrollEntries_OneIssueNamingIndexAndItemId()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, SWORD_ID, SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("Entries[1]", issues[0].Message);
            StringAssert.Contains($"{new ItemID(SCROLL_ID)}", issues[0].Message);
        }

        [Test]
        public void TryCreate_SwordAndScrollEntries_FailsWithNullRegistryAndSameSingleIssue()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, Table(VALID_GOLD_MIN, SWORD_ID, SCROLL_ID)));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, _database, SCROLLS_NOT_ALLOWED,
                out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("Entries[1]", issues[0].Message);
            StringAssert.Contains($"{new ItemID(SCROLL_ID)}", issues[0].Message);
        }

        // -----------------------------------------------------------------------
        // One issue per offending entry
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_TwoScrollEntriesInOneTable_TwoIssuesWithIndicesZeroAndOne()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, SCROLL_ID, SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(2, issues.Count);
            StringAssert.Contains("Entries[0]", issues[0].Message);
            StringAssert.Contains("Entries[1]", issues[1].Message);
        }

        [Test]
        public void Validate_OneScrollEntryInEachOfTwoTables_TwoIssuesOnePerMobType()
        {
            // Arrange
            var pairs = Pairs(
                Pair(Mob1, Table(VALID_GOLD_MIN, SCROLL_ID)),
                Pair(Mob2, Table(VALID_GOLD_MIN, SCROLL_ID)));

            // Act
            IReadOnlyList<LootTableValidationIssue> issues =
                LootTableValidator.Validate(pairs, _database, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(2, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            Assert.AreEqual(Mob2, issues[1].MobTypeId);
        }

        // -----------------------------------------------------------------------
        // Identified by data
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_PotionAndSwordEntries_NoIssue()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, POTION_ID, SWORD_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void Validate_ScrollWithUnrelatedNameAndId_IsReported()
        {
            // Arrange
            Register(ODD_SCROLL_ID, "Rusty Spoon", ItemCategory.Consumable,
                scrollData: ScrollData.CreateForTesting(GearTier.Bronze));
            LootTableDefinition table = Table(VALID_GOLD_MIN, ODD_SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("Entries[0]", issues[0].Message);
            StringAssert.Contains($"{new ItemID(ODD_SCROLL_ID)}", issues[0].Message);
        }

        // -----------------------------------------------------------------------
        // Unknown item
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_UnknownItemId_NoIssueFromThisRule()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, UNKNOWN_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // Switch on
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_ScrollEntryWithSwitchOn_NoIssue()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, SWORD_ID, SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_ALLOWED);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void TryCreate_ScrollEntryWithSwitchOn_CreatesRegistryWithTheTable()
        {
            // Arrange
            LootTableDefinition table = Table(VALID_GOLD_MIN, SWORD_ID, SCROLL_ID);
            var pairs = Pairs(Pair(Mob1, table));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, _database, SCROLLS_ALLOWED,
                out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsTrue(created);
            Assert.AreEqual(0, issues.Count);
            Assert.IsTrue(registry.TryGetTable(Mob1, out LootTableDefinition found));
            Assert.AreSame(table, found);
        }

        // -----------------------------------------------------------------------
        // Default
        // -----------------------------------------------------------------------

        [Test]
        public void Constants_AllowEnhancementScrollDrops_IsFalseAtMvp()
        {
            // Arrange / Act / Assert
            Assert.IsFalse(LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS);
        }

        // -----------------------------------------------------------------------
        // Combined with an existing rule
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_GoldMinBelowPartySizeAndScrollEntry_ReportsBothIssues()
        {
            // Arrange
            LootTableDefinition table = Table(BELOW_PARTY_GOLD_MIN, SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_NOT_ALLOWED);

            // Assert
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues[0].Message.Contains("GoldMin"));
            StringAssert.Contains("Entries[0]", issues[1].Message);
        }

        // -----------------------------------------------------------------------
        // Null item database
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_NullItemDatabase_ThrowsArgumentNullException()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, Table(VALID_GOLD_MIN, SWORD_ID)));

            // Act / Assert
            var ex = Assert.Throws<ArgumentNullException>(
                () => LootTableValidator.Validate(pairs, null, SCROLLS_NOT_ALLOWED));
            Assert.AreEqual("itemDatabase", ex.ParamName);
        }

        [Test]
        public void TryCreate_NullItemDatabase_ThrowsArgumentNullException()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, Table(VALID_GOLD_MIN, SWORD_ID)));

            // Act / Assert
            Assert.Throws<ArgumentNullException>(
                () => LootTableRegistry.TryCreate(pairs, null, SCROLLS_NOT_ALLOWED,
                    out _, out _));
        }

        // -----------------------------------------------------------------------
        // Item database not ready
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_ItemDatabaseNotReadyWithSwitchOff_ThrowsInvalidOperationException()
        {
            // Arrange — a database that is not ready answers "not found" for every item
            _database.IsReady = false;
            var pairs = Pairs(Pair(Mob1, Table(VALID_GOLD_MIN, SCROLL_ID)));

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => LootTableValidator.Validate(pairs, _database, SCROLLS_NOT_ALLOWED));
        }

        [Test]
        public void TryCreate_ItemDatabaseNotReadyWithSwitchOff_ThrowsInvalidOperationException()
        {
            // Arrange
            _database.IsReady = false;
            var pairs = Pairs(Pair(Mob1, Table(VALID_GOLD_MIN, SCROLL_ID)));

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => LootTableRegistry.TryCreate(pairs, _database, SCROLLS_NOT_ALLOWED,
                    out _, out _));
        }

        [Test]
        public void Validate_ItemDatabaseNotReadyWithSwitchOn_DoesNotThrow()
        {
            // Arrange — with the switch on the rule needs no lookup
            _database.IsReady = false;
            LootTableDefinition table = Table(VALID_GOLD_MIN, SCROLL_ID);

            // Act
            IReadOnlyList<LootTableValidationIssue> issues = ValidateSingle(table, SCROLLS_ALLOWED);

            // Assert
            Assert.AreEqual(0, issues.Count);
        }
    }
}

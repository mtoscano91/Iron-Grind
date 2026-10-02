using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.LootTableSystem
{
    /// <summary>
    /// EditMode unit tests for Loot Table Story 002: drop roll, equipment cache, tier classification
    /// and PRNG seeding (design/gdd/loot-table-system.md, CR-LT-1 / CR-LT-2 / CR-LT-5).
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_DropRoll_Tests
    {
        private static readonly ItemID ItemA = new ItemID(101u);
        private static readonly ItemID ItemB = new ItemID(102u);
        private static readonly ItemID ItemC = new ItemID(103u);

        private const uint BRONZE_ID = 1001u;
        private const uint IRON_ID = 1002u;
        private const uint STEEL_ID = 1003u;
        private const uint DARK_STEEL_ID = 1004u;
        private const uint POTION_ID = 2001u;
        private const int SEED_SAMPLE_COUNT = 5;
        private const int FIXED_SEED = 12345;

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition def in _created)
            {
                if (def != null)
                {
                    UnityEngine.Object.DestroyImmediate(def);
                }
            }
            _created.Clear();
        }

        // -----------------------------------------------------------------------
        // Helpers and fakes
        // -----------------------------------------------------------------------

        private sealed class CountingRandom : System.Random
        {
            public int NextDoubleCalls { get; private set; }

            public CountingRandom(int seed) : base(seed) { }

            public override double NextDouble()
            {
                NextDoubleCalls++;
                return base.NextDouble();
            }
        }

        // Returns the queued values in order, so a test controls each draw exactly.
        private sealed class ScriptedRandom : System.Random
        {
            private readonly Queue<double> _draws;

            public ScriptedRandom(params double[] draws)
            {
                _draws = new Queue<double>(draws);
            }

            public override double NextDouble()
            {
                return _draws.Dequeue();
            }
        }

        private sealed class FakeItemDatabase : IItemDatabase
        {
            private readonly List<ItemDefinition> _equipment;

            public int GetItemsByCategoryCalls { get; private set; }
            public int GetItemCalls { get; private set; }
            public int TryGetItemCalls { get; private set; }

            public FakeItemDatabase(List<ItemDefinition> equipment)
            {
                _equipment = equipment;
            }

            public bool IsReady { get; set; } = true;

            public event Action OnDatabaseReady
            {
                add { }
                remove { }
            }

            public ItemDefinition GetItem(ItemID id)
            {
                GetItemCalls++;
                return null;
            }

            public bool TryGetItem(ItemID id, out ItemDefinition item)
            {
                TryGetItemCalls++;
                item = null;
                return false;
            }

            public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
            {
                GetItemsByCategoryCalls++;
                return category == ItemCategory.Equipment ? _equipment : new List<ItemDefinition>();
            }
        }

        private static LootTableDefinition Table(params LootTableEntry[] entries)
        {
            return new LootTableDefinition(entries, 0, 0);
        }

        private ItemDefinition BuildEquipment(uint id, GearTier tier)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id,
                "Item" + id,
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, tier));
            _created.Add(def);
            return def;
        }

        private FakeItemDatabase BuildDatabase()
        {
            var equipment = new List<ItemDefinition>
            {
                BuildEquipment(BRONZE_ID, GearTier.Bronze),
                BuildEquipment(IRON_ID, GearTier.Iron),
                BuildEquipment(STEEL_ID, GearTier.Steel),
                BuildEquipment(DARK_STEEL_ID, GearTier.DarkSteel),
            };
            return new FakeItemDatabase(equipment);
        }

        // -----------------------------------------------------------------------
        // AC-LT-1: independent per-entry rolls
        // -----------------------------------------------------------------------

        [Test]
        public void Roll_ChancesOneZeroOne_ReturnsFirstAndThirdAndEvaluatesAllEntries()
        {
            // Arrange
            LootTableDefinition table = Table(
                new LootTableEntry(ItemA, 1.0f),
                new LootTableEntry(ItemB, 0.0f),
                new LootTableEntry(ItemC, 1.0f));
            var rng = new CountingRandom(FIXED_SEED);

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, rng);

            // Assert
            Assert.AreEqual(2, drops.Count);
            Assert.AreEqual(ItemA, drops[0]);
            Assert.AreEqual(ItemC, drops[1]);
            Assert.AreEqual(3, rng.NextDoubleCalls);
        }

        [Test]
        public void Roll_EarlyMissThenHit_ReturnsSecondAndStillDrawsTwice()
        {
            // Arrange
            LootTableDefinition table = Table(
                new LootTableEntry(ItemA, 0.0f),
                new LootTableEntry(ItemB, 1.0f));
            var rng = new CountingRandom(FIXED_SEED);

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, rng);

            // Assert
            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(ItemB, drops[0]);
            Assert.AreEqual(2, rng.NextDoubleCalls);
        }

        [Test]
        public void Roll_EmptyTable_ReturnsEmptyListWithoutDrawing()
        {
            // Arrange
            LootTableDefinition table = Table();
            var rng = new CountingRandom(FIXED_SEED);

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, rng);

            // Assert
            Assert.IsNotNull(drops);
            Assert.AreEqual(0, drops.Count);
            Assert.AreEqual(0, rng.NextDoubleCalls);
        }

        // Comparison rule: an entry drops when draw < DropChance, compared in double.

        [Test]
        public void Roll_DrawEqualToChance_MissesAndDrawBelowChance_Hits()
        {
            // Arrange — both draws are 0.5: not below a 0.5 chance, below a 0.51 chance.
            LootTableDefinition table = Table(
                new LootTableEntry(ItemA, 0.5f),
                new LootTableEntry(ItemB, 0.51f));

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, new ScriptedRandom(0.5, 0.5));

            // Assert
            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(ItemB, drops[0]);
        }

        [Test]
        public void Roll_DrawZeroAgainstChanceZero_Misses()
        {
            // Arrange
            LootTableDefinition table = Table(new LootTableEntry(ItemA, 0.0f));

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, new ScriptedRandom(0.0));

            // Assert
            Assert.AreEqual(0, drops.Count);
        }

        [Test]
        public void Roll_DrawJustBelowOneAgainstChanceOne_Hits()
        {
            // Arrange — 0.99999999 rounds to exactly 1.0f if cast to float, which would miss.
            LootTableDefinition table = Table(new LootTableEntry(ItemA, 1.0f));

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, new ScriptedRandom(0.99999999));

            // Assert
            Assert.AreEqual(1, drops.Count);
            Assert.AreEqual(ItemA, drops[0]);
        }

        [Test]
        public void Roll_NullTable_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LootDropRoller.Roll(null, new CountingRandom(FIXED_SEED)));
        }

        [Test]
        public void Roll_NullRng_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => LootDropRoller.Roll(Table(), null));
        }

        // -----------------------------------------------------------------------
        // AC-LT-2: cache built once
        // -----------------------------------------------------------------------

        [Test]
        public void Cache_ConstructRollAndClassify_QueriesEquipmentCategoryExactlyOnce()
        {
            // Arrange
            FakeItemDatabase database = BuildDatabase();

            // Act
            var cache = new LootEquipmentCache(database);
            int callsAfterInit = database.GetItemsByCategoryCalls;
            LootDropRoller.Roll(Table(new LootTableEntry(ItemA, 1.0f)), new CountingRandom(FIXED_SEED));
            cache.Classify(new ItemID(STEEL_ID));
            cache.Classify(new ItemID(BRONZE_ID));
            cache.Classify(new ItemID(POTION_ID));
            cache.TryGetEquipment(new ItemID(IRON_ID), out _);

            // Assert
            Assert.AreEqual(1, callsAfterInit);
            Assert.AreEqual(1, database.GetItemsByCategoryCalls);
        }

        [Test]
        public void Cache_NullDatabase_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new LootEquipmentCache(null));
        }

        [Test]
        public void Cache_DatabaseNotReady_ThrowsWithoutQuerying()
        {
            // Arrange
            FakeItemDatabase database = BuildDatabase();
            database.IsReady = false;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => new LootEquipmentCache(database));
            Assert.AreEqual(0, database.GetItemsByCategoryCalls);
        }

        [Test]
        public void Cache_TryGetEquipment_FindsCachedItemAndMissesUncachedWithoutQueryingDatabase()
        {
            // Arrange
            FakeItemDatabase database = BuildDatabase();
            var cache = new LootEquipmentCache(database);

            // Act
            bool foundSteel = cache.TryGetEquipment(new ItemID(STEEL_ID), out ItemDefinition steel);
            bool foundPotion = cache.TryGetEquipment(new ItemID(POTION_ID), out ItemDefinition potion);

            // Assert
            Assert.IsTrue(foundSteel);
            Assert.AreEqual(new ItemID(STEEL_ID), steel.ItemId);
            Assert.AreEqual(GearTier.Steel, steel.EquipmentData.GearTier);
            Assert.IsFalse(foundPotion);
            Assert.IsTrue(potion == null);
            Assert.AreEqual(0, database.GetItemCalls);
            Assert.AreEqual(0, database.TryGetItemCalls);
        }

        // -----------------------------------------------------------------------
        // AC-LT-6: tier classification without GetItem
        // -----------------------------------------------------------------------

        [Test]
        public void Classify_FiveItems_CommonForBronzeIronAndPotionRareForSteelAndDarkSteel()
        {
            // Arrange
            FakeItemDatabase database = BuildDatabase();
            var cache = new LootEquipmentCache(database);

            // Act
            DropTier bronze = cache.Classify(new ItemID(BRONZE_ID));
            DropTier iron = cache.Classify(new ItemID(IRON_ID));
            DropTier potion = cache.Classify(new ItemID(POTION_ID));
            DropTier steel = cache.Classify(new ItemID(STEEL_ID));
            DropTier darkSteel = cache.Classify(new ItemID(DARK_STEEL_ID));

            // Assert
            Assert.AreEqual(DropTier.Common, bronze);
            Assert.AreEqual(DropTier.Common, iron);
            Assert.AreEqual(DropTier.Common, potion);
            Assert.AreEqual(DropTier.Rare, steel);
            Assert.AreEqual(DropTier.Rare, darkSteel);
            Assert.AreEqual(0, database.GetItemCalls);
            Assert.AreEqual(0, database.TryGetItemCalls);
        }

        // -----------------------------------------------------------------------
        // CR-LT-1 seeding
        // -----------------------------------------------------------------------

        [Test]
        public void Factory_CreateSeededFromEntropy_LogsSeedAndReturnsReproducibleRandom()
        {
            // Arrange
            LogAssert.Expect(LogType.Log, new Regex(@"^\[LootTable\] PRNG seed: -?\d+$"));

            // Act
            System.Random rng = LootRandomFactory.CreateSeededFromEntropy(out int seed);
            var reference = new System.Random(seed);

            // Assert
            Assert.IsNotNull(rng);
            for (int i = 0; i < SEED_SAMPLE_COUNT; i++)
            {
                Assert.AreEqual(reference.NextDouble(), rng.NextDouble());
            }
        }

        // -----------------------------------------------------------------------
        // Zero-drop result
        // -----------------------------------------------------------------------

        [Test]
        public void Roll_AllEntriesMiss_ReturnsNonNullEmptyList()
        {
            // Arrange
            LootTableDefinition table = Table(
                new LootTableEntry(ItemA, 0.0f),
                new LootTableEntry(ItemB, 0.0f));

            // Act
            List<ItemID> drops = LootDropRoller.Roll(table, new CountingRandom(FIXED_SEED));

            // Assert
            Assert.IsNotNull(drops);
            Assert.AreEqual(0, drops.Count);
        }
    }
}

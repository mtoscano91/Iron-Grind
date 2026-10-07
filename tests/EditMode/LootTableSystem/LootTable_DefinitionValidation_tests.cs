using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.LootTableSystem
{
    /// <summary>
    /// EditMode unit tests for Loot Table Story 001: definitions, validator and registry.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_DefinitionValidation_Tests
    {
        private static readonly MobTypeID Mob1 = new MobTypeID(1u);
        private static readonly MobTypeID Mob2 = new MobTypeID(2u);
        private static readonly MobTypeID Mob3 = new MobTypeID(3u);
        private static readonly ItemID Item = new ItemID(500u);
        private static readonly EmptyItemDatabase NoItems = new EmptyItemDatabase();

        private static LootTableDefinition Table(int goldMin, int goldMax, params float[] chances)
        {
            var entries = new List<LootTableEntry>();
            foreach (float chance in chances)
            {
                entries.Add(new LootTableEntry(Item, chance));
            }
            return new LootTableDefinition(entries, goldMin, goldMax);
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

        private static IReadOnlyList<LootTableValidationIssue> Validate(MobTypeID id, LootTableDefinition table)
        {
            return LootTableValidator.Validate(Pairs(Pair(id, table)), NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS);
        }

        // Counts issues for a mob type whose message names the given field — order-independent.
        private static int CountIssues(IReadOnlyList<LootTableValidationIssue> issues, MobTypeID id, string field)
        {
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].MobTypeId == id && issues[i].Message.Contains(field))
                {
                    count++;
                }
            }
            return count;
        }

        // -----------------------------------------------------------------------
        // One table per mob type
        // -----------------------------------------------------------------------

        [Test]
        public void Registry_TwoValidTables_FindsRegisteredAndNotUnregistered()
        {
            // Arrange
            LootTableDefinition t1 = Table(4, 8, 0.5f);
            LootTableDefinition t2 = Table(5, 9, 0.25f);
            var pairs = Pairs(Pair(Mob1, t1), Pair(Mob2, t2));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);
            bool found1 = registry.TryGetTable(Mob1, out LootTableDefinition got1);
            bool found3 = registry.TryGetTable(Mob3, out LootTableDefinition got3);

            // Assert
            Assert.IsTrue(created);
            Assert.AreEqual(0, issues.Count);
            Assert.IsTrue(found1);
            Assert.AreSame(t1, got1);
            Assert.IsFalse(found3);
            Assert.IsNull(got3);
        }

        [Test]
        public void Registry_DuplicateMobTypeId_FailsAndNamesTheId()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, Table(4, 8)), Pair(Mob1, Table(4, 8)));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("MobTypeID", issues[0].Message);
        }

        [Test]
        public void Registry_InvalidMobTypeIdKey_Fails()
        {
            // Arrange
            var pairs = Pairs(Pair(MobTypeID.Invalid, Table(4, 8)));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(MobTypeID.Invalid, issues[0].MobTypeId);
        }

        // -----------------------------------------------------------------------
        // Immutable at runtime
        // -----------------------------------------------------------------------

        [Test]
        public void Definition_CallerMutatesOriginalList_EntriesUnchanged()
        {
            // Arrange
            var original = new List<LootTableEntry> { new LootTableEntry(Item, 0.5f) };
            var definition = new LootTableDefinition(original, 4, 8);

            // Act
            original.Add(new LootTableEntry(new ItemID(501u), 0.9f));
            original[0] = new LootTableEntry(new ItemID(502u), 0.1f);

            // Assert
            Assert.AreEqual(1, definition.Entries.Count);
            Assert.AreEqual(Item, definition.Entries[0].ItemId);
            Assert.AreEqual(0.5f, definition.Entries[0].DropChance);
        }

        [Test]
        public void Definition_EntriesCastToMutableCollection_CannotBeWritten()
        {
            // Arrange
            var definition = new LootTableDefinition(
                new List<LootTableEntry> { new LootTableEntry(Item, 0.5f) }, 4, 8);
            var replacement = new LootTableEntry(new ItemID(502u), 0.1f);

            // Act
            bool isArray = definition.Entries is LootTableEntry[];
            var asList = (IList<LootTableEntry>)definition.Entries;

            // Assert
            Assert.IsFalse(isArray, "Entries must not expose the backing array.");
            Assert.IsTrue(asList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => asList[0] = replacement);
            Assert.Throws<NotSupportedException>(() => asList.Add(replacement));
            Assert.AreEqual(1, definition.Entries.Count);
            Assert.AreEqual(Item, definition.Entries[0].ItemId);
        }

        [Test]
        public void Registry_CallerMutatesPairListAfterCreate_LookupsUnchanged()
        {
            // Arrange
            LootTableDefinition t1 = Table(4, 8);
            var pairs = Pairs(Pair(Mob1, t1));
            LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out _);

            // Act
            pairs[0] = Pair(Mob1, Table(5, 9));
            pairs.Add(Pair(Mob2, Table(4, 8)));

            // Assert
            Assert.IsTrue(registry.TryGetTable(Mob1, out LootTableDefinition got1));
            Assert.AreSame(t1, got1);
            Assert.IsFalse(registry.TryGetTable(Mob2, out _));
        }

        // -----------------------------------------------------------------------
        // GoldMin >= 4
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_GoldMinBelowPartySize_OneErrorNamingGoldMin()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(3, 8));

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("GoldMin", issues[0].Message);
        }

        [Test]
        public void Validate_GoldMinZero_OneErrorNamingGoldMin()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(0, 8));

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("GoldMin", issues[0].Message);
        }

        [Test]
        public void Validate_GoldMinEqualsPartySize_NoError()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(4, 8));

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // GoldMax <= GOLD_CAP
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_GoldMaxAboveCap_OneErrorNamingGoldMax()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(4, 10_000_000));

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("GoldMax", issues[0].Message);
        }

        [Test]
        public void Validate_GoldMaxEqualsCap_NoError()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(4, 9_999_999));

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // Non-empty gold range
        // -----------------------------------------------------------------------

        [Test]
        public void Validate_GoldMaxBelowGoldMin_OneError()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(10, 9));

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("GoldMax", issues[0].Message);
        }

        [Test]
        public void Validate_GoldMaxEqualsGoldMin_NoError()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(10, 10));

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // DropChance in [0.0, 1.0]
        // -----------------------------------------------------------------------

        [TestCase(1.01f)]
        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Validate_DropChanceOutOfRange_OneErrorNamingEntry(float chance)
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(4, 8, chance));

            // Assert
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob1, issues[0].MobTypeId);
            StringAssert.Contains("Entries[0]", issues[0].Message);
            StringAssert.Contains("DropChance", issues[0].Message);
        }

        [TestCase(0.0f)]
        [TestCase(1.0f)]
        public void Validate_DropChanceAtBoundary_NoError(float chance)
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> issues = Validate(Mob1, Table(4, 8, chance));

            // Assert
            Assert.AreEqual(0, issues.Count);
        }

        // -----------------------------------------------------------------------
        // Enforced at startup
        // -----------------------------------------------------------------------

        [Test]
        public void Registry_OneValidOneInvalidTable_FailsWithGoldMinIssue()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, Table(4, 8)), Pair(Mob2, Table(3, 8)));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(Mob2, issues[0].MobTypeId);
            StringAssert.Contains("GoldMin", issues[0].Message);
        }

        [Test]
        public void Registry_TableWithTwoDistinctErrors_FailsAndReportsBoth()
        {
            // Arrange: GoldMin below 4 and a DropChance above 1.
            var pairs = Pairs(Pair(Mob1, Table(3, 8, 1.5f)));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(2, issues.Count);
            Assert.AreEqual(1, CountIssues(issues, Mob1, "GoldMin"));
            Assert.AreEqual(1, CountIssues(issues, Mob1, "DropChance"));
        }

        [Test]
        public void Registry_NullDefinition_FailsWithOneIssueAndNoException()
        {
            // Arrange
            var pairs = Pairs(Pair(Mob1, null));

            // Act
            bool created = LootTableRegistry.TryCreate(pairs, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.IsFalse(created);
            Assert.IsNull(registry);
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(1, CountIssues(issues, Mob1, "Definition"));
        }

        [Test]
        public void Registry_NullTableSet_CreatesEmptyRegistry()
        {
            // Arrange / Act
            IReadOnlyList<LootTableValidationIssue> validated = LootTableValidator.Validate(null, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS);
            bool created = LootTableRegistry.TryCreate(null, NoItems, LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out IReadOnlyList<LootTableValidationIssue> issues);

            // Assert
            Assert.AreEqual(0, validated.Count);
            Assert.IsTrue(created);
            Assert.AreEqual(0, issues.Count);
            Assert.IsFalse(registry.TryGetTable(Mob1, out _));
        }
    }
}

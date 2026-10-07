using System;
using System.Collections.Generic;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.EnhancementSystem
{
    /// <summary>
    /// EditMode unit tests for Enhancement System Story 001 — <see cref="EnhancementConfig"/>:
    /// defaults, F-ENH-4 probability table, construction validation (TK-ENH-1, TK-ENH-4 to
    /// TK-ENH-6), outcome resolution (CR-ENH-9), prestige band mapping (CR-ENH-12) and the
    /// F-ENH-5 expected-scrolls function.
    /// </summary>
    [TestFixture]
    internal sealed class EnhancementSystem_Config_Tests
    {
        private const byte DefaultCap = 10;
        private const byte DefaultMid = 5;
        private const byte DefaultGlow = 7;
        private const byte DefaultHigh = 8;
        private const int DefaultCeiling = 9999;
        private const double ProbabilityEpsilon = 1e-12;

        private static readonly double[] DefaultTable =
            { 0.95, 0.90, 0.85, 0.80, 0.65, 0.50, 0.35, 0.22, 0.12, 0.06 };

        // ---------- helpers ----------

        /// <summary>Builds a valid config; a test overrides only the arguments it cares about.</summary>
        private static EnhancementConfig Build(
            byte cap = DefaultCap,
            byte mid = DefaultMid,
            byte glow = DefaultGlow,
            byte high = DefaultHigh,
            IReadOnlyList<double> table = null)
        {
            return new EnhancementConfig(
                cap, mid, glow, high,
                table ?? DefaultTable,
                3, 4, 6, 10,
                1, 2, 3, 5,
                DefaultCeiling);
        }

        /// <summary>Strictly decreasing table of <paramref name="count"/> values starting at 0.95 (all stay at or above 0.11 for count up to 13).</summary>
        private static double[] DecreasingTable(int count)
        {
            const double start = 0.95;
            const double step = 0.07;
            var table = new double[count];
            for (int i = 0; i < count; i++)
                table[i] = start - step * i;
            return table;
        }

        private static double[] DefaultTableWith(int index, double value)
        {
            var table = (double[])DefaultTable.Clone();
            table[index] = value;
            return table;
        }

        // ---------- defaults ----------

        [Test]
        public void Default_LevelCapAndThresholds_MatchGdd()
        {
            var config = EnhancementConfig.Default;

            Assert.AreEqual(10, config.MaxEnhancementLevel);
            Assert.AreEqual(5, config.PrestigeMidThreshold);
            Assert.AreEqual(7, config.EnhancementGlowThreshold);
            Assert.AreEqual(8, config.PrestigeHighThreshold);
            Assert.AreEqual(9999, config.ElementalDamageCeiling);
        }

        [TestCase(GearTier.Bronze, 3)]
        [TestCase(GearTier.Iron, 4)]
        [TestCase(GearTier.Steel, 6)]
        [TestCase(GearTier.DarkSteel, 10)]
        public void Default_BonusPerLevel_MatchesGddForTier(GearTier tier, int expected)
        {
            Assert.AreEqual(expected, EnhancementConfig.Default.GetBonusPerLevel(tier));
        }

        [TestCase(GearTier.Bronze, 1)]
        [TestCase(GearTier.Iron, 2)]
        [TestCase(GearTier.Steel, 3)]
        [TestCase(GearTier.DarkSteel, 5)]
        public void Default_ElementalBonusPerLevel_MatchesGddForTier(GearTier tier, int expected)
        {
            Assert.AreEqual(expected, EnhancementConfig.Default.GetElementalBonusPerLevel(tier));
        }

        [Test]
        public void Constants_MatchGddDefaults()
        {
            Assert.AreEqual(DefaultCap, EnhancementConstants.MAX_ENHANCEMENT_LEVEL);
            Assert.AreEqual(DefaultMid, EnhancementConstants.PRESTIGE_MID_THRESHOLD);
            Assert.AreEqual(DefaultGlow, EnhancementConstants.ENHANCEMENT_GLOW_THRESHOLD);
            Assert.AreEqual(DefaultHigh, EnhancementConstants.PRESTIGE_HIGH_THRESHOLD);
            Assert.AreEqual(DefaultCeiling, EnhancementConstants.ELEMENTAL_DAMAGE_CEILING);
        }

        [TestCase(GearTier.None)]
        [TestCase((GearTier)99)]
        public void GetBonusPerLevel_NoneOrUndefinedTier_Throws(GearTier tier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetBonusPerLevel(tier));
        }

        [TestCase(GearTier.None)]
        [TestCase((GearTier)99)]
        public void GetElementalBonusPerLevel_NoneOrUndefinedTier_Throws(GearTier tier)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetElementalBonusPerLevel(tier));
        }

        // ---------- table values ----------

        [TestCase(0, 0.95)]
        [TestCase(1, 0.90)]
        [TestCase(2, 0.85)]
        [TestCase(3, 0.80)]
        [TestCase(4, 0.65)]
        [TestCase(5, 0.50)]
        [TestCase(6, 0.35)]
        [TestCase(7, 0.22)]
        [TestCase(8, 0.12)]
        [TestCase(9, 0.06)]
        public void GetSuccessProbability_DefaultTable_MatchesFEnh4(int level, double expected)
        {
            Assert.AreEqual(expected, EnhancementConfig.Default.GetSuccessProbability(level), ProbabilityEpsilon);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        public void GetDestructionProbability_EveryLevel_IsOneMinusSuccessAndSumsToOne(int level)
        {
            var config = EnhancementConfig.Default;

            double success = config.GetSuccessProbability(level);
            double destruction = config.GetDestructionProbability(level);

            Assert.AreEqual(1.0 - DefaultTable[level], destruction, ProbabilityEpsilon);
            Assert.AreEqual(1.0, success + destruction, ProbabilityEpsilon);
        }

        [Test]
        public void GetDestructionProbability_LevelZero_IsNonZero()
        {
            Assert.AreEqual(0.05, EnhancementConfig.Default.GetDestructionProbability(0), ProbabilityEpsilon);
        }

        [Test]
        public void Constructor_MutatingSourceTableAfterwards_DoesNotChangeConfig()
        {
            var table = (double[])DefaultTable.Clone();
            var config = Build(table: table);

            table[0] = 0.01;

            Assert.AreEqual(0.95, config.GetSuccessProbability(0), ProbabilityEpsilon);
        }

        // ---------- at the cap ----------

        [TestCase(10)]
        [TestCase(11)]
        public void GetSuccessProbability_AtOrAboveCap_Throws(int level)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetSuccessProbability(level));
        }

        [TestCase(10)]
        [TestCase(11)]
        public void GetDestructionProbability_AtOrAboveCap_Throws(int level)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetDestructionProbability(level));
        }

        [TestCase(10)]
        [TestCase(11)]
        public void ResolveOutcome_AtOrAboveCap_Throws(int level)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.ResolveOutcome(level, 0.0));
        }

        [Test]
        public void GetSuccessProbability_NegativeLevel_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetSuccessProbability(-1));
        }

        // ---------- probability validation ----------

        [Test]
        public void Constructor_TwoEqualNeighbours_Throws()
        {
            var table = DefaultTableWith(2, 0.90);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        [Test]
        public void Constructor_IncreasingEntry_Throws()
        {
            var table = DefaultTableWith(3, 0.88);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        [Test]
        public void Constructor_ValueAboveMaximum_Throws()
        {
            var table = DefaultTableWith(0, 0.96);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        [Test]
        public void Constructor_ValueBelowMinimum_Throws()
        {
            var table = DefaultTableWith(9, 0.009);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        [Test]
        public void Constructor_NaNValue_Throws()
        {
            // A NaN fails every ordered comparison, so a plain range check would let it through.
            var table = DefaultTableWith(4, double.NaN);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        [Test]
        public void Constructor_BoundaryValues_AreAccepted()
        {
            var table = DefaultTableWith(9, 0.01);

            var config = Build(table: table);

            Assert.AreEqual(0.95, config.GetSuccessProbability(0), ProbabilityEpsilon);
            Assert.AreEqual(0.01, config.GetSuccessProbability(9), ProbabilityEpsilon);
        }

        [Test]
        public void Constructor_NullTable_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new EnhancementConfig(
                DefaultCap, DefaultMid, DefaultGlow, DefaultHigh, null,
                3, 4, 6, 10, 1, 2, 3, 5, DefaultCeiling));
        }

        [TestCase(9)]
        [TestCase(11)]
        public void Constructor_TableLengthDiffersFromCap_Throws(int length)
        {
            var table = DecreasingTable(length);

            Assert.Throws<ArgumentException>(() => Build(table: table));
        }

        // ---------- safe ranges not enforced ----------

        [Test]
        public void Constructor_CapBelowSafeRange_IsAccepted()
        {
            var config = Build(cap: 8, mid: 3, glow: 5, high: 7, table: DecreasingTable(8));

            Assert.AreEqual(8, config.MaxEnhancementLevel);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.GetSuccessProbability(8));
        }

        [Test]
        public void Constructor_CapAboveSafeRange_IsAccepted()
        {
            var config = Build(cap: 13, table: DecreasingTable(13));

            Assert.AreEqual(13, config.MaxEnhancementLevel);
            Assert.DoesNotThrow(() => config.GetSuccessProbability(12));
            Assert.Throws<ArgumentOutOfRangeException>(() => config.GetSuccessProbability(13));
        }

        [Test]
        public void Constructor_ThresholdsBelowSafeRanges_AreAccepted()
        {
            var config = Build(mid: 2, glow: 3, high: 4);

            Assert.AreEqual(2, config.PrestigeMidThreshold);
            Assert.AreEqual(3, config.EnhancementGlowThreshold);
            Assert.AreEqual(4, config.PrestigeHighThreshold);
        }

        // ---------- threshold ordering ----------

        [Test]
        public void Constructor_MidEqualToGlow_Throws()
        {
            Assert.Throws<ArgumentException>(() => Build(mid: 7, glow: 7, high: 8));
        }

        [Test]
        public void Constructor_GlowEqualToHigh_Throws()
        {
            Assert.Throws<ArgumentException>(() => Build(mid: 5, glow: 8, high: 8));
        }

        [Test]
        public void Constructor_MidAboveGlow_Throws()
        {
            Assert.Throws<ArgumentException>(() => Build(mid: 8, glow: 7, high: 9));
        }

        [Test]
        public void Constructor_HighAboveCap_Throws()
        {
            Assert.Throws<ArgumentException>(() => Build(mid: 5, glow: 7, high: 11));
        }

        [Test]
        public void Constructor_HighEqualToCap_IsAccepted()
        {
            var config = Build(mid: 5, glow: 7, high: 10);

            Assert.AreEqual(10, config.PrestigeHighThreshold);
        }

        // ---------- outcome resolution ----------

        [TestCase(2, 0.00, EnhancementOutcome.Success)]
        [TestCase(2, 0.849, EnhancementOutcome.Success)]
        [TestCase(2, 0.85, EnhancementOutcome.Destruction)]
        [TestCase(2, 0.90, EnhancementOutcome.Destruction)]
        [TestCase(0, 0.96, EnhancementOutcome.Destruction)]
        public void ResolveOutcome_DrawAgainstSuccessProbability_ReturnsExpected(int level, double draw, EnhancementOutcome expected)
        {
            var outcome = EnhancementConfig.Default.ResolveOutcome(level, draw);

            Assert.AreEqual(expected, outcome);
        }

        [Test]
        public void ResolveOutcome_NegativeLevel_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.ResolveOutcome(-1, 0.5));
        }

        // ---------- band mapping ----------

        [TestCase(0, PrestigeBand.None)]
        [TestCase(4, PrestigeBand.None)]
        [TestCase(5, PrestigeBand.VisibleNoGlow)]
        [TestCase(6, PrestigeBand.VisibleNoGlow)]
        [TestCase(7, PrestigeBand.GlowLow)]
        [TestCase(8, PrestigeBand.High)]
        [TestCase(10, PrestigeBand.High)]
        public void GetPrestigeBand_DefaultThresholds_MapsLevelToBand(int level, PrestigeBand expected)
        {
            Assert.AreEqual(expected, EnhancementConfig.Default.GetPrestigeBand((byte)level));
        }

        [TestCase(3, PrestigeBand.None)]
        [TestCase(4, PrestigeBand.VisibleNoGlow)]
        [TestCase(5, PrestigeBand.VisibleNoGlow)]
        [TestCase(6, PrestigeBand.GlowLow)]
        [TestCase(8, PrestigeBand.GlowLow)]
        [TestCase(9, PrestigeBand.High)]
        public void GetPrestigeBand_CustomThresholds4_6_9_BoundariesMove(int level, PrestigeBand expected)
        {
            var config = Build(mid: 4, glow: 6, high: 9);

            Assert.AreEqual(expected, config.GetPrestigeBand((byte)level));
        }

        [TestCase(11)]
        [TestCase(255)]
        public void GetPrestigeBand_LevelAboveCap_ReturnsHighWithoutThrowing(int level)
        {
            PrestigeBand band = PrestigeBand.None;

            Assert.DoesNotThrow(() => band = EnhancementConfig.Default.GetPrestigeBand((byte)level));
            Assert.AreEqual(PrestigeBand.High, band);
        }

        [Test]
        public void PrestigeBand_NumericValues_AreWireBits()
        {
            Assert.AreEqual(0, (byte)PrestigeBand.None);
            Assert.AreEqual(1, (byte)PrestigeBand.VisibleNoGlow);
            Assert.AreEqual(2, (byte)PrestigeBand.GlowLow);
            Assert.AreEqual(3, (byte)PrestigeBand.High);
        }

        // ---------- expected scrolls ----------

        // Every row of the GDD's F-ENH-5 table; each tolerance is half the last printed digit
        // (+10 is printed as "~45,500").
        [TestCase(0, 0.0, 1e-12)]
        [TestCase(1, 1.1, 0.05)]
        [TestCase(2, 2.3, 0.05)]
        [TestCase(3, 3.9, 0.05)]
        [TestCase(4, 6.1, 0.05)]
        [TestCase(5, 10.9, 0.05)]
        [TestCase(6, 23.8, 0.05)]
        [TestCase(7, 70.8, 0.05)]
        [TestCase(8, 326.0, 0.5)]
        [TestCase(9, 2727.0, 0.5)]
        [TestCase(10, 45500.0, 50.0)]
        public void GetExpectedScrolls_DefaultTable_MatchesFEnh5(int target, double expected, double tolerance)
        {
            Assert.AreEqual(expected, EnhancementConfig.Default.GetExpectedScrolls(target), tolerance);
        }

        [TestCase(-1)]
        [TestCase(11)]
        public void GetExpectedScrolls_TargetOutsideRange_Throws(int target)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EnhancementConfig.Default.GetExpectedScrolls(target));
        }

        [Test]
        public void GetExpectedScrolls_TargetAtCap_IsAccepted()
        {
            Assert.DoesNotThrow(() => EnhancementConfig.Default.GetExpectedScrolls(DefaultCap));
        }
    }
}

using System;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.EnhancementSystem
{
    /// <summary>
    /// EditMode unit tests for Enhancement System Story 002 — <see cref="EnhancementBonusProvider"/>:
    /// F-ENH-1 flat bonus, F-ENH-2 elemental bonus, AC-ENH-19 to AC-ENH-21, input validation,
    /// config-driven values and the F-ENH-3 tier parity constraint.
    /// </summary>
    [TestFixture]
    internal sealed class EnhancementSystem_BonusProvider_Tests
    {
        private const int CustomIronBonusPerLevel = 5;
        private const int CustomCeiling = 500;

        // F-ENH-3: GDD midpoints of each tier's flat bonus range.
        private const int MidpointBronze = 10;
        private const int MidpointIron = 25;
        private const int MidpointSteel = 37;
        private const int MidpointDarkSteel = 63;
        private const int ParityLevel = 5;

        // ---------- helpers ----------

        /// <summary>Provider over the GDD default config.</summary>
        private static EnhancementBonusProvider BuildProvider()
        {
            return new EnhancementBonusProvider(EnhancementConfig.Default);
        }

        /// <summary>Provider over a copy of the default config with one or two values overridden.</summary>
        private static EnhancementBonusProvider BuildCustomProvider(
            int ironBonusPerLevel = EnhancementConstants.BONUS_PER_LEVEL_IRON,
            int elementalDamageCeiling = EnhancementConstants.ELEMENTAL_DAMAGE_CEILING)
        {
            EnhancementConfig defaults = EnhancementConfig.Default;
            var table = new double[defaults.MaxEnhancementLevel];
            for (int k = 0; k < table.Length; k++)
                table[k] = defaults.GetSuccessProbability(k);

            var config = new EnhancementConfig(
                defaults.MaxEnhancementLevel,
                defaults.PrestigeMidThreshold,
                defaults.EnhancementGlowThreshold,
                defaults.PrestigeHighThreshold,
                table,
                EnhancementConstants.BONUS_PER_LEVEL_BRONZE,
                ironBonusPerLevel,
                EnhancementConstants.BONUS_PER_LEVEL_STEEL,
                EnhancementConstants.BONUS_PER_LEVEL_DARK_STEEL,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_BRONZE,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_IRON,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_STEEL,
                EnhancementConstants.ELEMENTAL_BONUS_PER_LEVEL_DARK_STEEL,
                elementalDamageCeiling);
            return new EnhancementBonusProvider(config);
        }

        // ---------- construction ----------

        [Test]
        public void Constructor_NullConfig_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => new EnhancementBonusProvider(null));
        }

        // ---------- flat bonus ----------

        [Test]
        public void GetFlatBonus_Level5Base10Bronze_Returns25()
        {
            var provider = BuildProvider();

            int result = provider.GetFlatBonus(5, 10, GearTier.Bronze);

            Assert.AreEqual(25, result);
        }

        [TestCase(GearTier.Bronze, 13)]
        [TestCase(GearTier.Iron, 14)]
        [TestCase(GearTier.Steel, 16)]
        [TestCase(GearTier.DarkSteel, 20)]
        public void GetFlatBonus_LevelOneBase10_AddsOnePerLevelBonus(GearTier tier, int expected)
        {
            var provider = BuildProvider();

            int result = provider.GetFlatBonus(1, 10, tier);

            Assert.AreEqual(expected, result);
        }

        [TestCase(GearTier.Bronze)]
        [TestCase(GearTier.Iron)]
        [TestCase(GearTier.Steel)]
        [TestCase(GearTier.DarkSteel)]
        public void GetFlatBonus_LevelZero_ReturnsBase(GearTier tier)
        {
            var provider = BuildProvider();

            int result = provider.GetFlatBonus(0, 37, tier);

            Assert.AreEqual(37, result);
        }

        [Test]
        public void GetFlatBonus_MaxLevelDarkSteelBase68_Returns168()
        {
            var provider = BuildProvider();

            int result = provider.GetFlatBonus(10, 68, GearTier.DarkSteel);

            Assert.AreEqual(168, result);
        }

        [Test]
        public void GetFlatBonus_NegativeBase_IsNotValidated()
        {
            var provider = BuildProvider();

            int result = provider.GetFlatBonus(2, -5, GearTier.Bronze);

            Assert.AreEqual(1, result);
        }

        // ---------- elemental bonus ----------

        [Test]
        public void GetElementalBonus_Level7Base15DarkSteelWeapon_Returns50()
        {
            var provider = BuildProvider();

            int result = provider.GetElementalBonus(7, 15, GearTier.DarkSteel, true);

            Assert.AreEqual(50, result);
        }

        [Test]
        public void GetElementalBonus_LevelZeroBase15DarkSteelWeapon_ReturnsBase()
        {
            var provider = BuildProvider();

            int result = provider.GetElementalBonus(0, 15, GearTier.DarkSteel, true);

            Assert.AreEqual(15, result);
        }

        [Test]
        public void GetElementalBonus_Level10Base9990DarkSteelWeapon_ClampsToCeiling()
        {
            var provider = BuildProvider();

            int result = provider.GetElementalBonus(10, 9990, GearTier.DarkSteel, true);

            Assert.AreEqual(9999, result);
        }

        [TestCase(GearTier.Bronze, 16)]
        [TestCase(GearTier.Iron, 17)]
        [TestCase(GearTier.Steel, 18)]
        [TestCase(GearTier.DarkSteel, 20)]
        public void GetElementalBonus_LevelOneBase15_AddsOnePerLevelBonus(GearTier tier, int expected)
        {
            var provider = BuildProvider();

            int result = provider.GetElementalBonus(1, 15, tier, true);

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void GetElementalBonus_LandsExactlyOnCeiling_Returns9999()
        {
            var provider = BuildProvider();

            // 9949 + 10 * 5 = 9999
            int result = provider.GetElementalBonus(10, 9949, GearTier.DarkSteel, true);

            Assert.AreEqual(9999, result);
        }

        [Test]
        public void GetElementalBonus_OneAboveCeiling_ClampsTo9999()
        {
            var provider = BuildProvider();

            // 9950 + 10 * 5 = 10000
            int result = provider.GetElementalBonus(10, 9950, GearTier.DarkSteel, true);

            Assert.AreEqual(9999, result);
        }

        // ---------- non-weapon ----------

        [TestCase(5, 0, GearTier.Bronze)]
        [TestCase(10, 500, GearTier.DarkSteel)]
        [TestCase(0, 15, GearTier.Iron)]
        public void GetElementalBonus_NonWeapon_ReturnsZero(int level, int baseDamage, GearTier tier)
        {
            var provider = BuildProvider();

            int result = provider.GetElementalBonus(level, baseDamage, tier, false);

            Assert.AreEqual(0, result);
        }

        [Test]
        public void GetElementalBonus_NonWeaponWithInvalidLevelAndTier_ReturnsZeroWithoutThrowing()
        {
            var provider = BuildProvider();
            int result = -1;

            Assert.DoesNotThrow(() => result = provider.GetElementalBonus(11, 0, GearTier.None, false));
            Assert.AreEqual(0, result);
        }

        // ---------- level range ----------

        [TestCase(-1)]
        [TestCase(11)]
        public void GetFlatBonus_LevelOutOfRange_Throws(int level)
        {
            var provider = BuildProvider();

            Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetFlatBonus(level, 10, GearTier.Bronze));
        }

        [TestCase(0)]
        [TestCase(10)]
        public void GetFlatBonus_LevelAtBoundary_DoesNotThrow(int level)
        {
            var provider = BuildProvider();

            Assert.DoesNotThrow(() => provider.GetFlatBonus(level, 10, GearTier.Bronze));
        }

        [TestCase(-1)]
        [TestCase(11)]
        public void GetElementalBonus_LevelOutOfRange_Throws(int level)
        {
            var provider = BuildProvider();

            Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetElementalBonus(level, 10, GearTier.Bronze, true));
        }

        [TestCase(0)]
        [TestCase(10)]
        public void GetElementalBonus_LevelAtBoundary_DoesNotThrow(int level)
        {
            var provider = BuildProvider();

            Assert.DoesNotThrow(() => provider.GetElementalBonus(level, 10, GearTier.Bronze, true));
        }

        // ---------- tier validity ----------

        [TestCase(GearTier.None)]
        [TestCase((GearTier)99)]
        public void GetFlatBonus_NoneOrUndefinedTier_Throws(GearTier tier)
        {
            var provider = BuildProvider();

            Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetFlatBonus(3, 10, tier));
        }

        [TestCase(GearTier.None)]
        [TestCase((GearTier)99)]
        public void GetElementalBonus_NoneOrUndefinedTierWeapon_Throws(GearTier tier)
        {
            var provider = BuildProvider();

            Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetElementalBonus(3, 10, tier, true));
        }

        // ---------- config-driven ----------

        [Test]
        public void GetFlatBonus_CustomIronBonusPerLevel_ChangesIronOnly()
        {
            var provider = BuildCustomProvider(ironBonusPerLevel: CustomIronBonusPerLevel);

            int iron = provider.GetFlatBonus(2, 10, GearTier.Iron);
            int bronze = provider.GetFlatBonus(2, 10, GearTier.Bronze);

            Assert.AreEqual(20, iron);
            Assert.AreEqual(16, bronze);
        }

        [Test]
        public void GetElementalBonus_CustomCeiling_ClampsToTheConfigValue()
        {
            // The clamp must come from the injected config, not from the default constant.
            var provider = BuildCustomProvider(elementalDamageCeiling: CustomCeiling);

            // 498 + 1 * 5 = 503, above the custom ceiling of 500 and far below the default 9999.
            int clamped = provider.GetElementalBonus(1, 498, GearTier.DarkSteel, true);
            // 400 + 1 * 5 = 405, below the custom ceiling.
            int unclamped = provider.GetElementalBonus(1, 400, GearTier.DarkSteel, true);

            Assert.AreEqual(CustomCeiling, clamped);
            Assert.AreEqual(405, unclamped);
        }

        // ---------- F-ENH-3 tier parity ----------

        [Test]
        public void TierParity_BronzeToIron_DeltaIsZero()
        {
            var provider = BuildProvider();

            int delta = provider.GetFlatBonus(ParityLevel, MidpointBronze, GearTier.Bronze) - MidpointIron;

            Assert.AreEqual(0, delta);
        }

        [Test]
        public void TierParity_IronToSteel_DeltaIsPlus8()
        {
            var provider = BuildProvider();

            int delta = provider.GetFlatBonus(ParityLevel, MidpointIron, GearTier.Iron) - MidpointSteel;

            Assert.AreEqual(8, delta);
        }

        [Test]
        public void TierParity_SteelToDarkSteel_DeltaIsPlus4()
        {
            var provider = BuildProvider();

            int delta = provider.GetFlatBonus(ParityLevel, MidpointSteel, GearTier.Steel) - MidpointDarkSteel;

            Assert.AreEqual(4, delta);
        }
    }
}

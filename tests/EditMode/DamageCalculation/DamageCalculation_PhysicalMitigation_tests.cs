using System;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.Randomness;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.DamageCalculation
{
    /// <summary>
    /// EditMode unit tests for Damage Calculation Story 001 - result types, tuning config, physical
    /// mitigation (F-DC-1), the absolute-1 floor (F-DC-4), guards, context echo and constructor checks.
    /// All cases use no elemental weapon and no crit.
    /// </summary>
    [TestFixture]
    internal sealed class DamageCalculation_PhysicalMitigation_Tests
    {
        private const uint ATTACKER_RAW_ID = 1001u;
        private const uint TARGET_RAW_ID = 2001u;
        private const int TARGET_SETUP_MAX_HP = 99999; // StatSchema MaxHP ceiling
        private const int TARGET_MAX_HP = 1000;
        private const float TARGET_START_HP = 100f;
        private const int ATTACKER_START_XP = 250;
        private const int LEVEL_BELOW_CAP = 1; // AddExperience is a no-op at the level cap
        private const int MAX_DEFENSE = 9999;
        private const uint UNKNOWN_RAW_ID = 3001u;
        private const uint ARMOR_RAW_ITEM_ID = 501u;
        private const float DEFAULT_MIN_DAMAGE_FRACTION = 0.05f;
        private const float DEFAULT_K_MAGIC = 200f;
        private const float DEFAULT_MIN_ELEMENTAL_FRACTION = 0.10f;
        private const int DEFAULT_MAX_BASE_DAMAGE = 99999;
        private const int SEED = 20261008; // provider seed; no case here depends on the roll (attackers have no crit stats)

        private static readonly EntityID Attacker = new EntityID(ATTACKER_RAW_ID);
        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly EntityID UnknownEntity = new EntityID(UNKNOWN_RAW_ID);

        private IronGrind.CharacterStats.CharacterStats _stats;
        private IronGrind.CharacterStats.CharacterStats.EntityDiedHandler _diedHandler;
        private int _diedCount;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.CreateWithLeveling(new AllPlayersLevelingService());
            _diedCount = 0;
            _diedHandler = OnEntityDied;
            _stats.Subscribe(_diedHandler);

            // Story 004: the target must be alive and out of reach so no case logs the dead-entity error
            // or flips IsKill. MaxHP is set first because SetCurrentHP clamps to the effective MaxHP.
            _stats.SetBaseStat(Target, StatID.MaxHP, TARGET_SETUP_MAX_HP);
            _stats.SetCurrentHP(Target, TARGET_SETUP_MAX_HP);
        }

        [TearDown]
        public void TearDown()
        {
            _stats?.Unsubscribe(_diedHandler);
        }

        // ---------- helpers ----------

        private void OnEntityDied(EntityID entityId)
        {
            _diedCount++;
        }

        /// <summary>The single place the calculator is built; later stories change only this method.</summary>
        private DamageCalculator CreateCalculator(DamageCalculationConfig config = null)
        {
            // No weapon equipped: every Story 001 case stays purely physical.
            return new DamageCalculator(
                _stats,
                new FakeEquippedWeaponQuery(),
                new CountingItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default),
                config ?? DamageCalculationConfig.Default,
                new SystemRandomProvider(new System.Random(SEED)));
        }

        private void GivenTargetDefense(int defense)
        {
            _stats.SetBaseStat(Target, StatID.Defense, defense);
        }

        private static DamageCalculationConfig ConfigWithMinDamageFraction(float minDamageFraction)
        {
            return new DamageCalculationConfig(
                minDamageFraction, DEFAULT_K_MAGIC, DEFAULT_MIN_ELEMENTAL_FRACTION, DEFAULT_MAX_BASE_DAMAGE);
        }

        private static void AssertRejected(DamageResult result, DamageContext expectedContext)
        {
            Assert.AreEqual(0, result.FinalDamage);
            Assert.AreEqual(0, result.PhysicalDamage);
            Assert.AreEqual(0, result.ElementalDamage);
            Assert.IsFalse(result.IsCrit);
            Assert.IsFalse(result.IsKill);
            Assert.IsFalse(result.HasElementalContribution);
            Assert.AreEqual(expectedContext, result.DamageContext);
        }

        private void GivenTargetHp(float hp)
        {
            _stats.SetBaseStat(Target, StatID.MaxHP, TARGET_MAX_HP);
            _stats.SetCurrentHP(Target, hp);
        }

        // ---------- physical mitigation and floors ----------

        [Test]
        public void DamageCalculation_Calculate_Base768Defense394_PhysicalAndFinal374() // AC-DC-F-01
        {
            // Arrange
            GivenTargetDefense(394);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(374, result.PhysicalDamage);
            Assert.AreEqual(374, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_Base100Defense9999_MinFractionFloorGives5() // AC-DC-F-02
        {
            // Arrange
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(5, result.PhysicalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_Base768Defense0_Unmitigated768() // AC-DC-F-03
        {
            // Arrange
            GivenTargetDefense(0);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(768, result.PhysicalDamage);
            Assert.AreEqual(768, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_Base1Defense1_AbsoluteOneFloor() // AC-DC-F-10
        {
            // Arrange
            GivenTargetDefense(1);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(1, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(1, result.FinalDamage);
            Assert.AreEqual(0, result.PhysicalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_Base19Defense9999_FinalOneViaAbsoluteFloor() // AC-DC-F-10b
        {
            // Arrange
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(19, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(1, result.FinalDamage);
            Assert.AreEqual(0, result.PhysicalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_Base20Defense9999_FinalOneViaMinFractionPath() // AC-DC-F-10c
        {
            // Arrange
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(20, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(1, result.FinalDamage);
            Assert.AreEqual(1, result.PhysicalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_FractionalMitigatedValue_FinalDamageIsFloored()
        {
            // Arrange - 110 x 0.05 = 5.5: rounding or ceiling would give 6
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(110, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(5, result.PhysicalDamage);
            Assert.AreEqual(5, result.FinalDamage);
        }

        [TestCase(94, 6)] // subtraction still above the fraction floor
        [TestCase(95, 5)] // both terms equal
        [TestCase(96, 5)] // fraction floor takes over
        public void DamageCalculation_Calculate_Base100AtDefenseCrossover_TakesLargerTerm(int defense, int expected)
        {
            // Arrange
            GivenTargetDefense(defense);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(expected, result.PhysicalDamage);
            Assert.AreEqual(expected, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_DefenseFromEquipmentModifier_UsesEffectiveStat()
        {
            // Arrange - base 294 + 100 flat from equipment = effective 394
            GivenTargetDefense(294);
            _stats.AddEquipmentModifier(Target, StatID.Defense,
                new EquipmentModifierEntry(100f, 0f, new ItemID(ARMOR_RAW_ITEM_ID)));
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(374, result.PhysicalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_UnknownTarget_TreatedAsDefenseZero()
        {
            // Arrange
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("dead-entity guard")); // no HP record reads as 0

            // Act
            DamageResult result = calculator.Calculate(100, Attacker, UnknownEntity, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(100, result.PhysicalDamage);
            Assert.AreEqual(100, result.FinalDamage);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_BaseDamageAtDefaultMaximum_IsResolved()
        {
            // Arrange
            GivenTargetDefense(0);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(
                DEFAULT_MAX_BASE_DAMAGE, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert - 99999 damage on the 99999 HP set in SetUp is a kill at equality
            Assert.AreEqual(DEFAULT_MAX_BASE_DAMAGE, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_BaseDamageAtHighestConfigurableMaximum_IsExact()
        {
            // Arrange - 2^24 is the last base that converts to float without loss
            GivenTargetDefense(0);
            var config = new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, DEFAULT_K_MAGIC, DEFAULT_MIN_ELEMENTAL_FRACTION,
                DamageCalculationConfig.MAX_BASE_DAMAGE_LIMIT);
            DamageCalculator calculator = CreateCalculator(config);

            // Act
            DamageResult result = calculator.Calculate(
                DamageCalculationConfig.MAX_BASE_DAMAGE_LIMIT, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(DamageCalculationConfig.MAX_BASE_DAMAGE_LIMIT, result.PhysicalDamage);
            Assert.AreEqual(DamageCalculationConfig.MAX_BASE_DAMAGE_LIMIT, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }

        // ---------- context echo ----------

        [TestCase(DamageContext.PhysicalSkill)] // AC-DC-F-13
        [TestCase(DamageContext.PhysicalAuto)]  // AC-DC-F-13b
        [TestCase(DamageContext.MagicalSkill)]  // AC-DC-F-14b
        public void DamageCalculation_Calculate_AnyContext_EchoedUnchanged(DamageContext context)
        {
            // Arrange
            GivenTargetDefense(394);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(768, Attacker, Target, context);

            // Assert
            Assert.AreEqual(context, result.DamageContext);
            Assert.AreEqual(374, result.FinalDamage);
        }

        // ---------- guards ----------

        [Test]
        public void DamageCalculation_Calculate_BaseDamageZero_ReturnsZeroAndNoSideEffects() // AC-DC-E-01
        {
            // Arrange
            GivenTargetDefense(394);
            GivenTargetHp(TARGET_START_HP);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at least 1"));

            // Act
            DamageResult result = calculator.Calculate(0, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertRejected(result, DamageContext.PhysicalAuto);
            Assert.AreEqual(TARGET_START_HP, _stats.GetCurrentHP(Target));
            Assert.AreEqual(0, _diedCount);
        }

        [Test]
        public void DamageCalculation_Calculate_AttackerEqualsTarget_ReturnsZeroAndNoSideEffects() // AC-DC-E-02
        {
            // Arrange - Target is both attacker and target here, so it is the entity that could gain XP.
            // It is a player (see SetUp) below the level cap, so a stray AddExperience would show.
            GivenTargetHp(TARGET_START_HP);
            _stats.SetBaseStat(Target, StatID.Level, LEVEL_BELOW_CAP);
            _stats.SetBaseStat(Target, StatID.Experience, ATTACKER_START_XP);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("AttackerID equals TargetID"));

            // Act
            DamageResult result = calculator.Calculate(100, Target, Target, DamageContext.MagicalSkill);

            // Assert
            AssertRejected(result, DamageContext.MagicalSkill);
            Assert.AreEqual(TARGET_START_HP, _stats.GetCurrentHP(Target));
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Target, StatID.Experience));
            Assert.AreEqual(0, _diedCount);
        }

        [Test]
        public void DamageCalculation_Calculate_NegativeBaseDamage_ReturnsZero()
        {
            // Arrange
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at least 1"));

            // Act
            DamageResult result = calculator.Calculate(-5, Attacker, Target, DamageContext.MagicalSkill);

            // Assert
            AssertRejected(result, DamageContext.MagicalSkill);
        }

        [Test]
        public void DamageCalculation_Calculate_BaseDamageAboveMaximum_ReturnsRejected()
        {
            // Arrange
            GivenTargetDefense(0);
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at most"));

            // Act
            DamageResult result = calculator.Calculate(
                DEFAULT_MAX_BASE_DAMAGE + 1, Attacker, Target, DamageContext.PhysicalSkill);

            // Assert
            AssertRejected(result, DamageContext.PhysicalSkill);
        }

        [Test]
        public void DamageCalculation_Calculate_IntMaxBaseDamage_ReturnsRejected()
        {
            // Arrange
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at most"));

            // Act
            DamageResult result = calculator.Calculate(int.MaxValue, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            AssertRejected(result, DamageContext.PhysicalAuto);
        }

        [Test]
        public void DamageCalculation_Calculate_DefaultEntityIdsOnBothSides_RejectedAsSelfDamage()
        {
            // Arrange
            DamageCalculator calculator = CreateCalculator();
            LogAssert.Expect(LogType.Error, new Regex("AttackerID equals TargetID"));

            // Act
            DamageResult result = calculator.Calculate(
                100, default(EntityID), default(EntityID), DamageContext.PhysicalAuto);

            // Assert
            AssertRejected(result, DamageContext.PhysicalAuto);
        }

        [Test]
        public void DamageResult_Rejected_EchoesContextWithNoDamageAndNoFlags()
        {
            // Arrange / Act
            DamageResult result = DamageResult.Rejected(DamageContext.PhysicalSkill);

            // Assert
            AssertRejected(result, DamageContext.PhysicalSkill);
        }

        // ---------- result invariants for this story ----------

        [Test]
        public void DamageCalculation_Calculate_NormalHit_ElementalAndFlagsAreInactive()
        {
            // Arrange
            GivenTargetDefense(394);
            GivenTargetHp(TARGET_MAX_HP); // above the 374 dealt: a non-lethal hit (kill detection is live since Story 004)
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = calculator.Calculate(768, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(0, result.ElementalDamage);
            Assert.IsFalse(result.HasElementalContribution);
            Assert.IsFalse(result.IsCrit);
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual((float)TARGET_MAX_HP, _stats.GetCurrentHP(Target));
            Assert.AreEqual(0, _diedCount);
        }

        // ---------- config ----------

        [Test]
        public void DamageCalculationConfig_Default_HoldsGddValues()
        {
            // Arrange / Act
            DamageCalculationConfig config = DamageCalculationConfig.Default;

            // Assert
            Assert.AreEqual(0.05f, config.MinDamageFraction, 0f);
            Assert.AreEqual(200f, config.KMagic, 0f);
            Assert.AreEqual(0.10f, config.MinElementalFraction, 0f);
            Assert.AreEqual(99999, config.MaxBaseDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_InjectedMinDamageFraction_IsUsed()
        {
            // Arrange
            const float injectedFraction = 0.20f;
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator(ConfigWithMinDamageFraction(injectedFraction));

            // Act
            DamageResult result = calculator.Calculate(100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(20, result.PhysicalDamage);
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void DamageCalculationConfig_InvalidMinDamageFraction_Throws(float value)
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => ConfigWithMinDamageFraction(value));
        }

        [Test]
        public void DamageCalculation_Calculate_MinDamageFractionOne_DefenseHasNoEffect()
        {
            // Arrange
            GivenTargetDefense(MAX_DEFENSE);
            DamageCalculator calculator = CreateCalculator(ConfigWithMinDamageFraction(1f));

            // Act
            DamageResult result = calculator.Calculate(100, Attacker, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(100, result.PhysicalDamage);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void DamageCalculationConfig_InvalidKMagic_Throws(float value)
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, value, DEFAULT_MIN_ELEMENTAL_FRACTION, DEFAULT_MAX_BASE_DAMAGE));
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void DamageCalculationConfig_InvalidMinElementalFraction_Throws(float value)
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, DEFAULT_K_MAGIC, value, DEFAULT_MAX_BASE_DAMAGE));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(DamageCalculationConfig.MAX_BASE_DAMAGE_LIMIT + 1)]
        public void DamageCalculationConfig_InvalidMaxBaseDamage_Throws(int value)
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamageCalculationConfig(
                DEFAULT_MIN_DAMAGE_FRACTION, DEFAULT_K_MAGIC, DEFAULT_MIN_ELEMENTAL_FRACTION, value));
        }

        [Test]
        public void DamageCalculationConfig_BoundaryOneFractions_AreAccepted()
        {
            // Arrange / Act
            var config = new DamageCalculationConfig(1f, 1f, 1f, 1);

            // Assert
            Assert.AreEqual(1f, config.MinDamageFraction, 0f);
            Assert.AreEqual(1f, config.MinElementalFraction, 0f);
            Assert.AreEqual(1, config.MaxBaseDamage);
        }

        // ---------- constructor null checks ----------

        [Test]
        public void DamageCalculator_Constructor_NullStats_Throws()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentNullException>(() => new DamageCalculator(
                null, new FakeEquippedWeaponQuery(), new CountingItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default), DamageCalculationConfig.Default,
                new SystemRandomProvider(new System.Random(SEED))));
        }

        [Test]
        public void DamageCalculator_Constructor_NullConfig_Throws()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentNullException>(() => new DamageCalculator(
                _stats, new FakeEquippedWeaponQuery(), new CountingItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default), null,
                new SystemRandomProvider(new System.Random(SEED))));
        }
    }
}

using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.ItemDatabase;
using IronGrind.Randomness;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.DamageCalculation
{
    /// <summary>
    /// EditMode unit tests for Damage Calculation Story 003 - critical strike (Steps 1 and 8, F-DC-3) with
    /// an injected <see cref="IRandomProvider"/> (ADR-013). The roll is scripted with
    /// <see cref="ScriptedRandomProvider"/> except in the seeded statistical case (AC-DC-E-05).
    /// Character Stats clamps CritChance to 0.75, so "guaranteed crit" is chance 0.75 and roll 0.0.
    /// </summary>
    [TestFixture]
    internal sealed class DamageCalculation_CriticalStrike_Tests
    {
        private const uint ATTACKER_RAW_ID = 1001u;
        private const uint TARGET_RAW_ID = 2001u;
        private const uint WEAPON_RAW_ITEM_ID = 701u;
        private const int TARGET_MAX_HP = 99999; // StatSchema MaxHP ceiling: no hit in this file kills
        private const int BASE_ELEMENTAL_50 = 50;
        private const int MAGIC_DEFENSE_SAMPLE = 8;
        private const int DEFENSE_394 = 394;
        private const int BASE_DAMAGE_768 = 768;
        private const int BASE_DAMAGE_100 = 100;
        private const int BASE_DAMAGE_1 = 1;
        private const int DEFENSE_1 = 1;
        private const int PHYSICAL_374 = 374;
        private const int ELEMENTAL_48 = 48;
        private const int FINAL_CRIT_633 = 633;
        private const int FINAL_NORMAL_422 = 422;
        private const int BASE_DAMAGE_30 = 30;
        private const int DEFENSE_100 = 100;
        private const int FINAL_FRACTIONAL_CRIT_4 = 4; // 30 x 0.05 = 1.5; x3.0 = 4.5 floors to 4
        private const float TARGET_HP_500 = 500f; // between the normal hit (422) and the crit (633)
        private const float CRIT_CHANCE_MAX = 0.75f;
        private const float CRIT_CHANCE_ZERO = 0f;
        private const float CRIT_MULTIPLIER_1_5 = 1.5f;
        private const float CRIT_MULTIPLIER_1_0 = 1f;
        private const float CRIT_MULTIPLIER_3_0 = 3f;
        private const float ROLL_GUARANTEED_CRIT = 0.0f;
        private const float ROLL_AT_THRESHOLD = 0.75f;
        private const float ROLL_JUST_BELOW_THRESHOLD = 0.74999994f; // the float directly below 0.75
        private const float ROLL_MIDDLE = 0.5f;
        private const int SEED = 20261008;
        private const int SEEDED_CALL_COUNT = 10000;
        private const double SEEDED_RATE_MIN = 0.737;
        private const double SEEDED_RATE_MAX = 0.763;
        private const string CRIT_MULTIPLIER_ERROR = "CritMultiplier below 1.0";

        private static readonly EntityID Attacker = new EntityID(ATTACKER_RAW_ID);
        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly ItemID WeaponId = new ItemID(WEAPON_RAW_ITEM_ID);

        private IronGrind.CharacterStats.CharacterStats _stats;
        private FakeEquippedWeaponQuery _weapons;
        private CountingItemDatabase _items;
        private List<ItemDefinition> _createdDefinitions;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.CreateWithLeveling(new AllPlayersLevelingService());
            _weapons = new FakeEquippedWeaponQuery();
            _items = new CountingItemDatabase();
            _createdDefinitions = new List<ItemDefinition>();

            // The target must be alive and out of reach: a target with no HP logs the dead-entity error.
            // MaxHP is set first because SetCurrentHP clamps to the effective MaxHP.
            _stats.SetBaseStat(Target, StatID.MaxHP, TARGET_MAX_HP);
            _stats.SetCurrentHP(Target, TARGET_MAX_HP);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _createdDefinitions)
                UnityEngine.Object.DestroyImmediate(definition);
            _createdDefinitions.Clear();
        }

        // ---------- helpers ----------

        /// <summary>The single place the calculator is built.</summary>
        private DamageCalculator CreateCalculator(IRandomProvider random)
        {
            return new DamageCalculator(
                _stats,
                _weapons,
                _items,
                new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default,
                random);
        }

        private static ScriptedRandomProvider ScriptedRolls(params float[] rolls)
        {
            var provider = new ScriptedRandomProvider();
            foreach (float roll in rolls)
                provider.EnqueueFloat(roll);
            return provider;
        }

        private void GivenAttackerCritChance(float critChance)
        {
            _stats.SetBaseStatFloat(Attacker, StatID.CritChance, critChance);
        }

        private void GivenAttackerCrit(float critChance, float critMultiplier)
        {
            _stats.SetBaseStatFloat(Attacker, StatID.CritChance, critChance);
            _stats.SetBaseStatFloat(Attacker, StatID.CritMultiplier, critMultiplier);
        }

        private void GivenTargetDefenses(int defense, int magicDefense)
        {
            _stats.SetBaseStat(Target, StatID.Defense, defense);
            _stats.SetBaseStat(Target, StatID.MagicDefense, magicDefense);
        }

        /// <summary>Registers a Bronze weapon with the given element at +0 and equips it on the attacker.</summary>
        private void GivenWeapon(ElementType element, int baseElementalDamage)
        {
            ItemDefinition weapon = ItemDefinitionBuilder.Build(
                WEAPON_RAW_ITEM_ID,
                "TestWeapon",
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(
                    GearSlot.Weapon, GearTier.Bronze, elementType: element, elementalDamage: baseElementalDamage));
            _createdDefinitions.Add(weapon);
            _items.Add(weapon);
            _weapons.WeaponId = WeaponId;
            _weapons.EnhancementLevel = 0;
        }

        /// <summary>The AC-DC-I-03 configuration: Defense 394, MagicDefense 8, Fire 50, crit multiplier 1.5.</summary>
        private void GivenSampleHitSetup()
        {
            GivenTargetDefenses(DEFENSE_394, MAGIC_DEFENSE_SAMPLE);
            GivenWeapon(ElementType.Fire, BASE_ELEMENTAL_50);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
        }

        private DamageResult Hit(DamageCalculator calculator, int baseDamage, DamageContext context = DamageContext.PhysicalAuto)
        {
            return calculator.Calculate(baseDamage, Attacker, Target, context);
        }

        // ---------- crit outcome ----------

        [Test]
        public void DamageCalculation_Calculate_GuaranteedCritBase768Fire50_Final633WithPreCritComponents() // AC-DC-F-07, AC-DC-I-04
        {
            // Arrange - pre-crit 374 + 48.08 = 422.08; x1.5 = 633.12 floors to 633
            GivenSampleHitSetup();
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_768);

            // Assert
            Assert.IsTrue(result.IsCrit);
            Assert.AreEqual(FINAL_CRIT_633, result.FinalDamage);
            Assert.AreEqual(PHYSICAL_374, result.PhysicalDamage);
            Assert.AreEqual(ELEMENTAL_48, result.ElementalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_SampleHitWithRollAtThreshold_NoCritAndNormalDamage()
        {
            // Arrange - same setup as AC-DC-F-07, but the roll misses
            GivenSampleHitSetup();
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_AT_THRESHOLD));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_768);

            // Assert
            Assert.IsFalse(result.IsCrit);
            Assert.AreEqual(FINAL_NORMAL_422, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_RollEqualsCritChance_NoCrit() // AC-DC-F-09
        {
            // Arrange
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_AT_THRESHOLD));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsFalse(result.IsCrit);
        }

        [Test]
        public void DamageCalculation_Calculate_RollJustBelowCritChance_Crit() // AC-DC-F-09b
        {
            // Arrange
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_JUST_BELOW_THRESHOLD));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsTrue(result.IsCrit);
        }

        [Test]
        public void DamageCalculation_Calculate_CritOnBase1Defense1Multiplier3_FinalDamageStaysOne() // AC-DC-F-11
        {
            // Arrange - 0.05 x 3.0 = 0.15 floors to 0; the absolute floor lifts it to 1
            GivenTargetDefenses(DEFENSE_1, 0);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_3_0);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_1);

            // Assert
            Assert.AreEqual(1, result.FinalDamage);
            Assert.IsTrue(result.IsCrit);
        }

        [Test]
        public void DamageCalculation_Calculate_SameStatsAndRollInThreeContexts_SameDamageAndCrit() // AC-DC-F-14, AC-DC-F-14c
        {
            // Arrange
            GivenSampleHitSetup();
            DamageCalculator calculator = CreateCalculator(
                ScriptedRolls(ROLL_GUARANTEED_CRIT, ROLL_GUARANTEED_CRIT, ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult auto = Hit(calculator, BASE_DAMAGE_768, DamageContext.PhysicalAuto);
            DamageResult physicalSkill = Hit(calculator, BASE_DAMAGE_768, DamageContext.PhysicalSkill);
            DamageResult magicalSkill = Hit(calculator, BASE_DAMAGE_768, DamageContext.MagicalSkill);

            // Assert
            Assert.AreEqual(FINAL_CRIT_633, auto.FinalDamage);
            Assert.AreEqual(auto.FinalDamage, physicalSkill.FinalDamage);
            Assert.AreEqual(auto.FinalDamage, magicalSkill.FinalDamage);
            Assert.AreEqual(auto.PhysicalDamage, physicalSkill.PhysicalDamage);
            Assert.AreEqual(auto.PhysicalDamage, magicalSkill.PhysicalDamage);
            Assert.AreEqual(auto.ElementalDamage, physicalSkill.ElementalDamage);
            Assert.AreEqual(auto.ElementalDamage, magicalSkill.ElementalDamage);
            Assert.AreEqual(auto.IsCrit, physicalSkill.IsCrit);
            Assert.AreEqual(auto.IsCrit, magicalSkill.IsCrit);
            Assert.AreEqual(auto.IsKill, physicalSkill.IsKill);
            Assert.AreEqual(auto.IsKill, magicalSkill.IsKill);
            Assert.AreEqual(auto.HasElementalContribution, physicalSkill.HasElementalContribution);
            Assert.AreEqual(auto.HasElementalContribution, magicalSkill.HasElementalContribution);
            Assert.AreEqual(DamageContext.PhysicalAuto, auto.DamageContext);
            Assert.AreEqual(DamageContext.PhysicalSkill, physicalSkill.DamageContext);
            Assert.AreEqual(DamageContext.MagicalSkill, magicalSkill.DamageContext);
        }

        [Test]
        public void DamageCalculation_Calculate_CritMultiplierOne_CritReportedAndDamageUnchanged() // AC-DC-E-03
        {
            // Arrange
            GivenTargetDefenses(0, 0);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_0);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsTrue(result.IsCrit);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_CritOnFractionalPreCritSum_MultipliesBeforeFlooring()
        {
            // Arrange - pre-crit 30 x 0.05 = 1.5; x3.0 = 4.5 floors to 4 (flooring first would give 3)
            GivenTargetDefenses(DEFENSE_100, 0);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_3_0);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_30);

            // Assert
            Assert.IsTrue(result.IsCrit);
            Assert.AreEqual(FINAL_FRACTIONAL_CRIT_4, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_CritLiftsDamageToTargetHp_IsKill()
        {
            // Arrange - kill detection reads the post-crit damage: 633 against 500 HP
            GivenSampleHitSetup();
            _stats.SetCurrentHP(Target, TARGET_HP_500);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_768);

            // Assert
            Assert.IsTrue(result.IsCrit);
            Assert.AreEqual(FINAL_CRIT_633, result.FinalDamage);
            Assert.IsTrue(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_SameHitWithoutCritBelowTargetHp_NoKill()
        {
            // Arrange - same setup as the crit kill, but the roll misses: 422 against 500 HP
            GivenSampleHitSetup();
            _stats.SetCurrentHP(Target, TARGET_HP_500);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_AT_THRESHOLD));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_768);

            // Assert
            Assert.IsFalse(result.IsCrit);
            Assert.AreEqual(FINAL_NORMAL_422, result.FinalDamage);
            Assert.IsFalse(result.IsKill);
        }

        [Test]
        public void DamageCalculation_Calculate_SeededProvider10000Calls_CritOnlyBelowThresholdAndRateInBand() // AC-DC-E-05
        {
            // Arrange
            GivenTargetDefenses(0, 0);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
            var recording = new RecordingRandomProvider(new SystemRandomProvider(new System.Random(SEED)));
            DamageCalculator calculator = CreateCalculator(recording);
            int critsAtOrAboveThreshold = 0;
            int critCount = 0;

            // Act
            for (int i = 0; i < SEEDED_CALL_COUNT; i++)
            {
                DamageResult result = Hit(calculator, BASE_DAMAGE_100);
                if (result.IsCrit)
                {
                    critCount++;
                    if (recording.LastFloat >= CRIT_CHANCE_MAX)
                        critsAtOrAboveThreshold++;
                }
            }

            // Assert
            double rate = (double)critCount / SEEDED_CALL_COUNT;
            Assert.AreEqual(0, critsAtOrAboveThreshold);
            Assert.AreEqual(SEEDED_CALL_COUNT, recording.FloatDrawCount);
            Assert.GreaterOrEqual(rate, SEEDED_RATE_MIN);
            Assert.LessOrEqual(rate, SEEDED_RATE_MAX);
        }

        [Test]
        public void DamageCalculation_Calculate_SeededProvider_EveryRollBelowThresholdIsACrit()
        {
            // Arrange - the converse of AC-DC-E-05: the strict < holds in both directions
            GivenTargetDefenses(0, 0);
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
            var recording = new RecordingRandomProvider(new SystemRandomProvider(new System.Random(SEED)));
            DamageCalculator calculator = CreateCalculator(recording);
            int mismatches = 0;

            // Act
            for (int i = 0; i < SEEDED_CALL_COUNT; i++)
            {
                DamageResult result = Hit(calculator, BASE_DAMAGE_100);
                if (result.IsCrit != (recording.LastFloat < CRIT_CHANCE_MAX))
                    mismatches++;
            }

            // Assert
            Assert.AreEqual(0, mismatches);
        }

        // ---------- draw count ----------

        [Test]
        public void DamageCalculation_Calculate_ZeroCritChance_NoCritButStillOneDraw()
        {
            // Arrange
            GivenAttackerCritChance(CRIT_CHANCE_ZERO);
            ScriptedRandomProvider random = ScriptedRolls(ROLL_GUARANTEED_CRIT);
            DamageCalculator calculator = CreateCalculator(random);

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsFalse(result.IsCrit);
            Assert.AreEqual(1, random.FloatDrawCount);
        }

        [Test]
        public void DamageCalculation_Calculate_OneAcceptedCall_ExactlyOneFloatDrawAndNoOther()
        {
            // Arrange
            GivenAttackerCrit(CRIT_CHANCE_MAX, CRIT_MULTIPLIER_1_5);
            ScriptedRandomProvider random = ScriptedRolls(ROLL_MIDDLE);
            DamageCalculator calculator = CreateCalculator(random);

            // Act
            Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.AreEqual(1, random.DrawCount);
            Assert.AreEqual(1, random.FloatDrawCount);
            Assert.AreEqual(0, random.DoubleDrawCount);
            Assert.AreEqual(0, random.IntDrawCount);
        }

        [Test]
        public void DamageCalculation_Calculate_BaseDamageZero_RejectedWithoutDraw()
        {
            // Arrange
            var random = new ScriptedRandomProvider();
            DamageCalculator calculator = CreateCalculator(random);
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at least 1"));

            // Act
            DamageResult result = Hit(calculator, 0);

            // Assert
            Assert.AreEqual(DamageResult.Rejected(DamageContext.PhysicalAuto), result);
            Assert.AreEqual(0, random.DrawCount);
        }

        [Test]
        public void DamageCalculation_Calculate_BaseDamageAboveMaximum_RejectedWithoutDraw()
        {
            // Arrange
            var random = new ScriptedRandomProvider();
            DamageCalculator calculator = CreateCalculator(random);
            LogAssert.Expect(LogType.Error, new Regex("BaseDamage must be at most"));

            // Act
            DamageResult result = Hit(calculator, DamageCalculationConfig.Default.MaxBaseDamage + 1);

            // Assert
            Assert.AreEqual(DamageResult.Rejected(DamageContext.PhysicalAuto), result);
            Assert.AreEqual(0, random.DrawCount);
        }

        [Test]
        public void DamageCalculation_Calculate_AttackerEqualsTarget_RejectedWithoutDraw()
        {
            // Arrange
            var random = new ScriptedRandomProvider();
            DamageCalculator calculator = CreateCalculator(random);
            LogAssert.Expect(LogType.Error, new Regex("AttackerID equals TargetID"));

            // Act
            DamageResult result = calculator.Calculate(BASE_DAMAGE_100, Target, Target, DamageContext.PhysicalAuto);

            // Assert
            Assert.AreEqual(DamageResult.Rejected(DamageContext.PhysicalAuto), result);
            Assert.AreEqual(0, random.DrawCount);
        }

        // ---------- CritMultiplier below 1.0 ----------

        [Test]
        public void DamageCalculation_Calculate_CritWithCritMultiplierNeverSet_UsesOneAndLogsError()
        {
            // Arrange - an unset CritMultiplier reads 0f; the crit uses 1.0 instead
            GivenTargetDefenses(0, 0);
            GivenAttackerCritChance(CRIT_CHANCE_MAX);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_GUARANTEED_CRIT));
            LogAssert.Expect(LogType.Error, new Regex(CRIT_MULTIPLIER_ERROR));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsTrue(result.IsCrit);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
        }

        [Test]
        public void DamageCalculation_Calculate_NonCritWithCritMultiplierNeverSet_LogsNothing()
        {
            // Arrange - the multiplier check runs only on a crit
            GivenTargetDefenses(0, 0);
            DamageCalculator calculator = CreateCalculator(ScriptedRolls(ROLL_MIDDLE));

            // Act
            DamageResult result = Hit(calculator, BASE_DAMAGE_100);

            // Assert
            Assert.IsFalse(result.IsCrit);
            Assert.AreEqual(BASE_DAMAGE_100, result.FinalDamage);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------- constructor ----------

        [Test]
        public void DamageCalculator_Constructor_NullRandom_Throws()
        {
            // Arrange / Act / Assert
            var ex = Assert.Throws<System.ArgumentNullException>(() => new DamageCalculator(
                _stats, _weapons, _items, new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default, null));
            Assert.AreEqual("random", ex.ParamName);
        }
    }
}

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode unit tests for Enemy AI Story 007: Enraged spawn roll and stat scaling
    /// (CR-AI-12, F-AI-E-1, EC-AI-11, AC-AI-13, AC-AI-14, AC-AI-15).
    /// </summary>
    /// <remarks>
    /// Mathf.RoundToInt rounds .5 to even: 3 x 1.5 = 4.5 gives 4, 5 x 1.5 = 7.5 gives 8.
    /// </remarks>
    [TestFixture]
    internal sealed class EnemyAI_Enraged_Tests
    {
        private const uint MOB_ENTITY_RAW_ID = 2001u;
        private const int BASE_MAX_HP = 100;
        private const int BASE_ATTACK_POWER = 10;
        private const int BASE_DEFENSE = 5;
        private const int KILL_XP = 10;
        private const float HP_MULTIPLIER = 1.5f;
        private const float ATTACK_MULTIPLIER = 1.3f;
        private const float XP_MULTIPLIER = 1.5f;
        private const int LOOT_TIER = 1;
        private const int EXPECTED_ENRAGED_MAX_HP = 150;
        private const int EXPECTED_ENRAGED_ATTACK = 13;
        private const int EXPECTED_ENRAGED_XP = 15;

        /// <summary>Arbitrary roll for tests whose chance (0.0 or 1.0) already fixes the outcome.</summary>
        private const double NON_ENRAGED_ROLL = 0.5;

        private static readonly EntityID MobId = new EntityID(MOB_ENTITY_RAW_ID);

        private static MobDefinition Def(
            float chance = 1.0f,
            int maxHp = BASE_MAX_HP,
            int attack = BASE_ATTACK_POWER,
            int defense = BASE_DEFENSE,
            int killXp = KILL_XP,
            float hpMult = HP_MULTIPLIER,
            float attackMult = ATTACK_MULTIPLIER,
            float xpMult = XP_MULTIPLIER,
            int tier = LOOT_TIER)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u),
                displayName: "Test Mob",
                baseMaxHP: maxHp,
                baseAttackPower: attack,
                baseDefense: defense,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: killXp,
                mobTier: MobTier.Common,
                enragedChance: chance,
                enragedMaxHPMultiplier: hpMult,
                enragedAttackPowerMultiplier: attackMult,
                enragedXPMultiplier: xpMult,
                enragedLootQualityBonusTier: tier);
        }

        private static ScriptedRandomProvider Rng(double roll)
        {
            var rng = new ScriptedRandomProvider();
            rng.EnqueueDouble(roll);
            return rng;
        }

        private enum CallKind
        {
            SetBaseStat,
            SetCurrentHP,
        }

        private readonly struct Call
        {
            public Call(CallKind kind, StatID stat, float value)
            {
                Kind = kind;
                Stat = stat;
                Value = value;
            }

            public CallKind Kind { get; }
            public StatID Stat { get; }
            public float Value { get; }
        }

        private sealed class SpyWriter : IMobStatWriter
        {
            public readonly List<Call> Calls = new List<Call>();

            public void SetBaseStat(EntityID entityId, StatID statId, int value)
            {
                Calls.Add(new Call(CallKind.SetBaseStat, statId, value));
            }

            public void SetCurrentHP(EntityID entityId, float value)
            {
                Calls.Add(new Call(CallKind.SetCurrentHP, StatID.CurrentHP, value));
            }

            public float BaseValue(StatID stat)
            {
                foreach (Call c in Calls)
                {
                    if (c.Kind == CallKind.SetBaseStat && c.Stat == stat) return c.Value;
                }

                Assert.Fail("no SetBaseStat call for " + stat);
                return 0f;
            }

            public float CurrentHp()
            {
                foreach (Call c in Calls)
                {
                    if (c.Kind == CallKind.SetCurrentHP) return c.Value;
                }

                Assert.Fail("no SetCurrentHP call");
                return 0f;
            }
        }

        private static int BuffCount(IronGrind.CharacterStats.CharacterStats stats, EntityID id, StatID stat)
        {
            return stats.EntityBuffCount.TryGetValue(id, out int[] counts) ? counts[(int)stat] : 0;
        }

        // -----------------------------------------------------------------------
        // QA 1-4: roll outcome (AC-AI-13)
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_rollZeroChanceOne_isEnraged()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f), Rng(0.0), spy);

            // Assert
            Assert.IsTrue(v.IsEnraged);
        }

        [Test]
        public void test_seed_rollOneChanceNinetyNine_notEnraged()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.99f), Rng(1.0), spy);

            // Assert
            Assert.IsFalse(v.IsEnraged);
        }

        [Test]
        public void test_seed_rollZeroChanceZero_notEnraged()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.0f), Rng(0.0), spy);

            // Assert
            Assert.IsFalse(v.IsEnraged);
        }

        [Test]
        public void test_seed_rollEqualsChance_notEnraged()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.5f), Rng(0.5), spy);

            // Assert
            Assert.IsFalse(v.IsEnraged);
        }

        [Test]
        public void test_seed_rollDoubleNinetyNineChanceFloatNinetyNine_isEnraged()
        {
            // Arrange: (double)0.99f = 0.9900000095... > 0.99, so the double compare gives Enraged;
            // a float compare ((float)0.99 == 0.99f) would give not Enraged. Pins the widening.
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.99f), Rng(0.99), spy);

            // Assert
            Assert.IsTrue(v.IsEnraged);
        }

        // -----------------------------------------------------------------------
        // QA 5: exactly one NextDouble
        // -----------------------------------------------------------------------

        [TestCase(0.0f)]
        [TestCase(0.07f)]
        [TestCase(1.0f)]
        public void test_seed_anyChance_exactlyOneNextDoubleAndNoOtherDraws(float chance)
        {
            // Arrange
            ScriptedRandomProvider rng = Rng(NON_ENRAGED_ROLL);
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: chance), rng, spy);

            // Assert
            Assert.AreEqual(1, rng.DoubleDrawCount);
            Assert.AreEqual(0, rng.FloatDrawCount);
            Assert.AreEqual(0, rng.IntDrawCount);
        }

        // -----------------------------------------------------------------------
        // QA 6: not Enraged writes base values
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_notEnraged_writesBaseStatsAndFullHp()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: 0.0f), Rng(NON_ENRAGED_ROLL), spy);

            // Assert
            Assert.AreEqual(BASE_MAX_HP, spy.BaseValue(StatID.MaxHP));
            Assert.AreEqual(BASE_ATTACK_POWER, spy.BaseValue(StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, spy.BaseValue(StatID.Defense));
            Assert.AreEqual((float)BASE_MAX_HP, spy.CurrentHp());
        }

        // -----------------------------------------------------------------------
        // QA 7, 8: Enraged scaling (AC-AI-14)
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_enraged_scalesMaxHpAndSetsCurrentHpToIt()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f), Rng(0.0), spy);

            // Assert
            Assert.AreEqual((float)EXPECTED_ENRAGED_MAX_HP, spy.BaseValue(StatID.MaxHP));
            Assert.AreEqual((float)EXPECTED_ENRAGED_MAX_HP, spy.CurrentHp());
        }

        [Test]
        public void test_seed_enraged_scalesAttackPowerAndLeavesDefenseUnchanged()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f), Rng(0.0), spy);

            // Assert (10 x 1.3 = 13)
            Assert.AreEqual((float)EXPECTED_ENRAGED_ATTACK, spy.BaseValue(StatID.AttackPower));
            Assert.AreEqual((float)BASE_DEFENSE, spy.BaseValue(StatID.Defense));
        }

        // -----------------------------------------------------------------------
        // QA 9: round half to even
        // -----------------------------------------------------------------------

        [TestCase(3, 4)]
        [TestCase(5, 8)]
        public void test_seed_enraged_roundsHalfToEven(int baseMaxHp, int expectedMaxHp)
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f, maxHp: baseMaxHp, hpMult: 1.5f), Rng(0.0), spy);

            // Assert
            Assert.AreEqual((float)expectedMaxHp, spy.BaseValue(StatID.MaxHP));
            Assert.AreEqual((float)expectedMaxHp, spy.CurrentHp());
        }

        // -----------------------------------------------------------------------
        // QA 10: write order and counts (AC-AI-15)
        // -----------------------------------------------------------------------

        [TestCase(0.0f, NON_ENRAGED_ROLL)]
        [TestCase(1.0f, 0.0)]
        public void test_seed_anyVariant_writesInOrderWithThreeBaseStatsAndOneCurrentHp(float chance, double roll)
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: chance), Rng(roll), spy);

            // Assert
            Assert.AreEqual(4, spy.Calls.Count);
            Assert.AreEqual(CallKind.SetBaseStat, spy.Calls[0].Kind);
            Assert.AreEqual(StatID.MaxHP, spy.Calls[0].Stat);
            Assert.AreEqual(CallKind.SetBaseStat, spy.Calls[1].Kind);
            Assert.AreEqual(StatID.AttackPower, spy.Calls[1].Stat);
            Assert.AreEqual(CallKind.SetBaseStat, spy.Calls[2].Kind);
            Assert.AreEqual(StatID.Defense, spy.Calls[2].Stat);
            Assert.AreEqual(CallKind.SetCurrentHP, spy.Calls[3].Kind);
            Assert.AreEqual(spy.Calls[0].Value, spy.Calls[3].Value);
        }

        // -----------------------------------------------------------------------
        // QA 11: real CharacterStats through the adapter (AC-AI-14, AC-AI-15)
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_enraged_realCharacterStats_effectiveValuesMatchAndNoBuffModifier()
        {
            // Arrange
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.Create();
            var writer = new CharacterStatsMobStatWriter(stats);

            // Act
            MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f), Rng(0.0), writer);

            // Assert
            Assert.AreEqual(EXPECTED_ENRAGED_MAX_HP, stats.GetEffectiveStat(MobId, StatID.MaxHP));
            Assert.AreEqual(EXPECTED_ENRAGED_ATTACK, stats.GetEffectiveStat(MobId, StatID.AttackPower));
            Assert.AreEqual(BASE_DEFENSE, stats.GetEffectiveStat(MobId, StatID.Defense));
            Assert.AreEqual((float)EXPECTED_ENRAGED_MAX_HP, stats.GetCurrentHP(MobId));
            Assert.AreEqual(0, BuffCount(stats, MobId, StatID.MaxHP));
            Assert.AreEqual(0, BuffCount(stats, MobId, StatID.AttackPower));
        }

        // -----------------------------------------------------------------------
        // QA 12: EffectiveKillXP
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_enraged_effectiveKillXpIsScaled()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f), Rng(0.0), spy);

            // Assert
            Assert.AreEqual(EXPECTED_ENRAGED_XP, v.EffectiveKillXP);
        }

        [Test]
        public void test_seed_notEnraged_effectiveKillXpIsBase()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.0f), Rng(NON_ENRAGED_ROLL), spy);

            // Assert
            Assert.AreEqual(KILL_XP, v.EffectiveKillXP);
        }

        // -----------------------------------------------------------------------
        // QA 13: TierShift
        // -----------------------------------------------------------------------

        [TestCase(1, 1)]
        [TestCase(0, 0)]
        [TestCase(2, 2)]
        public void test_seed_enraged_tierShiftIsDefinitionBonusTier(int definitionTier, int expectedShift)
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 1.0f, tier: definitionTier), Rng(0.0), spy);

            // Assert
            Assert.AreEqual(expectedShift, v.TierShift);
        }

        [Test]
        public void test_seed_notEnraged_tierShiftIsZero()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act
            MobSpawnVariant v = MobSpawnSeeder.Seed(MobId, Def(chance: 0.0f, tier: 2), Rng(NON_ENRAGED_ROLL), spy);

            // Assert
            Assert.AreEqual(0, v.TierShift);
        }

        // -----------------------------------------------------------------------
        // QA 14: null guards
        // -----------------------------------------------------------------------

        [Test]
        public void test_seed_nullDefinition_throwsWithoutRngCallOrWrite()
        {
            // Arrange
            ScriptedRandomProvider rng = Rng(0.0);
            var spy = new SpyWriter();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => MobSpawnSeeder.Seed(MobId, null, rng, spy));
            Assert.AreEqual(0, rng.DrawCount);
            Assert.AreEqual(0, spy.Calls.Count);
        }

        [Test]
        public void test_seed_nullRandom_throwsWithoutWrite()
        {
            // Arrange
            var spy = new SpyWriter();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => MobSpawnSeeder.Seed(MobId, Def(), null, spy));
            Assert.AreEqual(0, spy.Calls.Count);
        }

        [Test]
        public void test_seed_nullWriter_throwsWithoutRngCall()
        {
            // Arrange
            ScriptedRandomProvider rng = Rng(0.0);

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => MobSpawnSeeder.Seed(MobId, Def(), rng, null));
            Assert.AreEqual(0, rng.DrawCount);
        }

        [Test]
        public void test_adapter_nullCharacterStats_throws()
        {
            // Arrange / Act / Assert
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsMobStatWriter(null));
        }
    }
}

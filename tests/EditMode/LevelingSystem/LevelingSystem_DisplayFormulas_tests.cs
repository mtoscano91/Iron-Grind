using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// EditMode tests for Leveling System Story 014 (client read model): pins the outputs of the
    /// shared <see cref="LevelingDisplayFormulas"/> class (F-3-F-9 derived stats, CR-4.3 respec
    /// floor, auto-alloc increment lookup). Expected values are computed by hand from the GDD
    /// formulas and written as constants; the formulas never produce their own expectations.
    /// </summary>
    /// <remarks>
    /// All derived-stat tests use primaries STR = DEX = VIT = INT = 10 (the Level-1 baseline), so
    /// the hand computation is: MaxHP = floor((200 + 10*20) * t) = floor(400 t); MaxMP =
    /// min(floor((100 + 10*12) * t), 9999) = floor(220 t); AttackPower = floor((10 + 10*2) * t) =
    /// floor(30 t); Defense = floor((5 + 10*1.5) * t) = floor(20 t); MagicDefense =
    /// floor(10*0.4 * t) = floor(4 t); CritChance = 0.05 + 10*0.0015*t; AttackSpeedMultiplier =
    /// 1.0 + 10*0.003*t.
    /// </remarks>
    [TestFixture]
    internal sealed class LevelingSystem_DisplayFormulas_Tests
    {
        private const int PRIMARY_TOTAL = 10;
        private const float FLOAT_TOLERANCE = 1e-6f;

        // Tier 1.0
        private const int T1_MAX_HP = 400;
        private const int T1_MAX_MP = 220;
        private const int T1_ATTACK_POWER = 30;
        private const int T1_DEFENSE = 20;
        private const int T1_MAGIC_DEFENSE = 4;
        private const float T1_CRIT = 0.065f;
        private const float T1_ATTACK_SPEED = 1.03f;

        // Tier 1.2 (floor of 480, 264, 36, 24, 4.8)
        private const float TIER_1_2 = 1.2f;
        private const int T12_MAX_HP = 480;
        private const int T12_MAX_MP = 264;
        private const int T12_ATTACK_POWER = 36;
        private const int T12_DEFENSE = 24;
        private const int T12_MAGIC_DEFENSE = 4;
        private const float T12_CRIT = 0.068f;
        private const float T12_ATTACK_SPEED = 1.036f;

        // Tier 1.5 (floor of 600, 330, 45, 30, 6)
        private const float TIER_1_5 = 1.5f;
        private const int T15_MAX_HP = 600;
        private const int T15_MAX_MP = 330;
        private const int T15_ATTACK_POWER = 45;
        private const int T15_DEFENSE = 30;
        private const int T15_MAGIC_DEFENSE = 6;
        private const float T15_CRIT = 0.0725f;
        private const float T15_ATTACK_SPEED = 1.045f;

        // Tier 2.0 (floor of 800, 440, 60, 40, 8)
        private const float TIER_2_0 = 2.0f;
        private const int T20_MAX_HP = 800;
        private const int T20_MAX_MP = 440;
        private const int T20_ATTACK_POWER = 60;
        private const int T20_DEFENSE = 40;
        private const int T20_MAGIC_DEFENSE = 8;
        private const float T20_CRIT = 0.08f;
        private const float T20_ATTACK_SPEED = 1.06f;

        // MaxMP cap: (100 + 1000*12) * 2.0 = 24200, clamped to 9999.
        private const int HIGH_INTELLIGENCE = 1000;
        private const int MAX_MP_CAP = 9999;

        // Respec floor / increment lookup.
        private const int STRENGTH_INCREMENT = 2;
        private const int DEXTERITY_INCREMENT = 3;
        private const int VITALITY_INCREMENT = 4;
        private const int INTELLIGENCE_INCREMENT = 5;
        private const int LEVEL_1 = 1;
        private const int LEVEL_60 = 60;
        private const int FLOOR_BASE = 10;
        // 10 + (60 - 1) * increment, hand-computed.
        private const int STRENGTH_FLOOR_L60 = 128;
        private const int DEXTERITY_FLOOR_L60 = 187;
        private const int VITALITY_FLOOR_L60 = 246;
        private const int INTELLIGENCE_FLOOR_L60 = 305;

        private static void AssertDerived(
            LevelingDisplayFormulas.DerivedStats actual,
            int maxHp, int maxMp, int attackPower, int defense, int magicDefense,
            float critChance, float attackSpeed)
        {
            Assert.AreEqual(maxHp, actual.MaxHP, "MaxHP");
            Assert.AreEqual(maxMp, actual.MaxMP, "MaxMP");
            Assert.AreEqual(attackPower, actual.AttackPower, "AttackPower");
            Assert.AreEqual(defense, actual.Defense, "Defense");
            Assert.AreEqual(magicDefense, actual.MagicDefense, "MagicDefense");
            Assert.AreEqual(critChance, actual.CritChance, FLOAT_TOLERANCE, "CritChance");
            Assert.AreEqual(attackSpeed, actual.AttackSpeedMultiplier, FLOAT_TOLERANCE, "AttackSpeedMultiplier");
        }

        private static LevelingDisplayFormulas.DerivedStats ComputeAt(float tier)
            => LevelingDisplayFormulas.ComputeDerivedStats(
                PRIMARY_TOTAL, PRIMARY_TOTAL, PRIMARY_TOTAL, PRIMARY_TOTAL, tier);

        [Test]
        public void test_compute_derived_stats_tier_1_matches_pinned_values()
        {
            var actual = ComputeAt(1.0f);

            AssertDerived(actual, T1_MAX_HP, T1_MAX_MP, T1_ATTACK_POWER, T1_DEFENSE,
                T1_MAGIC_DEFENSE, T1_CRIT, T1_ATTACK_SPEED);
        }

        [Test]
        public void test_compute_derived_stats_at_each_tier()
        {
            AssertDerived(ComputeAt(TIER_1_2), T12_MAX_HP, T12_MAX_MP, T12_ATTACK_POWER, T12_DEFENSE,
                T12_MAGIC_DEFENSE, T12_CRIT, T12_ATTACK_SPEED);
            AssertDerived(ComputeAt(TIER_1_5), T15_MAX_HP, T15_MAX_MP, T15_ATTACK_POWER, T15_DEFENSE,
                T15_MAGIC_DEFENSE, T15_CRIT, T15_ATTACK_SPEED);
            AssertDerived(ComputeAt(TIER_2_0), T20_MAX_HP, T20_MAX_MP, T20_ATTACK_POWER, T20_DEFENSE,
                T20_MAGIC_DEFENSE, T20_CRIT, T20_ATTACK_SPEED);
        }

        [Test]
        public void test_compute_derived_stats_max_mp_is_capped_at_9999()
        {
            var actual = LevelingDisplayFormulas.ComputeDerivedStats(
                PRIMARY_TOTAL, PRIMARY_TOTAL, PRIMARY_TOTAL, HIGH_INTELLIGENCE, TIER_2_0);

            Assert.AreEqual(MAX_MP_CAP, actual.MaxMP);
        }

        [Test]
        public void test_get_respec_floor_and_auto_alloc_increment()
        {
            var def = new ClassDefinition(
                strengthAutoAlloc: STRENGTH_INCREMENT,
                dexterityAutoAlloc: DEXTERITY_INCREMENT,
                vitalityAutoAlloc: VITALITY_INCREMENT,
                intelligenceAutoAlloc: INTELLIGENCE_INCREMENT,
                freePointsPerLevel: 1);

            int strength = LevelingDisplayFormulas.GetAutoAllocIncrement(def, StatID.Strength);
            int dexterity = LevelingDisplayFormulas.GetAutoAllocIncrement(def, StatID.Dexterity);
            int vitality = LevelingDisplayFormulas.GetAutoAllocIncrement(def, StatID.Vitality);
            int intelligence = LevelingDisplayFormulas.GetAutoAllocIncrement(def, StatID.Intelligence);

            Assert.AreEqual(STRENGTH_INCREMENT, strength, "Strength increment");
            Assert.AreEqual(DEXTERITY_INCREMENT, dexterity, "Dexterity increment");
            Assert.AreEqual(VITALITY_INCREMENT, vitality, "Vitality increment");
            Assert.AreEqual(INTELLIGENCE_INCREMENT, intelligence, "Intelligence increment");
            Assert.AreEqual(0, LevelingDisplayFormulas.GetAutoAllocIncrement(def, StatID.Level),
                "A non-primary StatID has increment 0.");

            Assert.AreEqual(FLOOR_BASE, LevelingDisplayFormulas.GetRespecFloor(LEVEL_1, strength), "L1 floor is the base 10 for any increment.");
            Assert.AreEqual(FLOOR_BASE, LevelingDisplayFormulas.GetRespecFloor(LEVEL_1, intelligence), "L1 floor is the base 10 for any increment.");
            Assert.AreEqual(STRENGTH_FLOOR_L60, LevelingDisplayFormulas.GetRespecFloor(LEVEL_60, strength), "Strength L60 floor");
            Assert.AreEqual(DEXTERITY_FLOOR_L60, LevelingDisplayFormulas.GetRespecFloor(LEVEL_60, dexterity), "Dexterity L60 floor");
            Assert.AreEqual(VITALITY_FLOOR_L60, LevelingDisplayFormulas.GetRespecFloor(LEVEL_60, vitality), "Vitality L60 floor");
            Assert.AreEqual(INTELLIGENCE_FLOOR_L60, LevelingDisplayFormulas.GetRespecFloor(LEVEL_60, intelligence), "Intelligence L60 floor");
        }
    }
}

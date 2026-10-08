using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.DamageCalculation;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.DamageCalculation
{
    /// <summary>
    /// EditMode integration tests for Damage Calculation Story 005 - the kill sequence run against a real
    /// <see cref="IronGrind.CharacterStats.CharacterStats"/>. No caller exists yet (Auto-Attack Combat and the
    /// Skill System are not built), so this fixture plays the caller and executes the sequence prescribed by
    /// design/gdd/damage-calculation.md Core Rule 4: XP award obtained, AddExperience applied, ApplyDamage
    /// called, then OnEntityDied fires. The caller helper below is test code that follows Rule 4; it is not
    /// the Auto-Attack implementation.
    /// </summary>
    /// <remarks>
    /// Deterministic: no randomness, no I/O, no time. Each test builds its own stats, calculator and recorder.
    /// The event handlers only read from CharacterStats: a write from inside a handler throws in the Editor
    /// and is logged and skipped in release builds.
    /// <para>Naming: inside this namespace (and its sibling <c>Integration.CharacterStats</c>) the bare names
    /// <c>CharacterStats</c> and <c>DamageCalculation</c> bind to namespaces, not types. Write
    /// <c>IronGrind.CharacterStats.CharacterStats</c> in full, as this file does.</para>
    /// </remarks>
    [TestFixture]
    internal sealed class DamageCalculation_KillSequence_Integration_Tests
    {
        private const uint ATTACKER_RAW_ID = 1001u;
        private const uint TARGET_RAW_ID = 2001u;
        private const uint SECOND_TARGET_RAW_ID = 2002u;
        private const int TARGET_MAX_HP = 99999; // StatSchema MaxHP ceiling
        private const int ATTACKER_START_XP = 250;
        private const int XP_AWARD_AMOUNT = 40;
        private const int LEVEL_BELOW_CAP = 1; // AddExperience is a no-op at the level cap
        private const int BASE_DAMAGE_500 = 500;
        private const float HP_100 = 100f;
        private const float HP_1000 = 1000f;
        private const float HP_AFTER_NON_KILL = 500f;
        private const float HP_EXACT_LETHAL = 500f; // equals BASE_DAMAGE_500
        private const string DEAD_ENTITY_ERROR = "dead-entity guard";

        private const string MARKER_XP_AWARD = "xpAward";
        private const string MARKER_ADD_EXPERIENCE = "addExperience";
        private const string MARKER_APPLY_DAMAGE = "applyDamage";
        private const string MARKER_ENTITY_DIED = "entityDied";

        private static readonly EntityID Attacker = new EntityID(ATTACKER_RAW_ID);
        private static readonly EntityID Target = new EntityID(TARGET_RAW_ID);
        private static readonly EntityID SecondTarget = new EntityID(SECOND_TARGET_RAW_ID);

        private IronGrind.CharacterStats.CharacterStats _stats;
        private IronGrind.CharacterStats.CharacterStats.StatChangedHandler _statChangedHandler;
        private IronGrind.CharacterStats.CharacterStats.EntityDiedHandler _diedHandler;
        private FakeEquippedWeaponQuery _weapons;
        private CountingItemDatabase _items;
        private FakeXpSource _xpSource;
        private List<string> _sequence;
        private List<EntityID> _diedIds;
        private List<float> _diedHps;
        private List<int> _diedAttackerXp;

        [SetUp]
        public void SetUp()
        {
            _stats = CharacterStatsFixture.CreateWithLeveling(new AllPlayersLevelingService());
            _weapons = new FakeEquippedWeaponQuery();
            _items = new CountingItemDatabase();
            _sequence = new List<string>();
            _diedIds = new List<EntityID>();
            _diedHps = new List<float>();
            _diedAttackerXp = new List<int>();
            _xpSource = new FakeXpSource(_sequence, XP_AWARD_AMOUNT);
            _statChangedHandler = OnStatChanged;
            _diedHandler = OnEntityDied;
            _stats.Subscribe(_statChangedHandler);
            _stats.Subscribe(_diedHandler);
        }

        [TearDown]
        public void TearDown()
        {
            _stats?.Unsubscribe(_statChangedHandler);
            _stats?.Unsubscribe(_diedHandler);
        }

        // ---------- handlers (read only) ----------

        private void OnStatChanged(EntityID entityId, StatID statId)
        {
            if (entityId == Attacker && statId == StatID.Experience)
                _sequence.Add(MARKER_ADD_EXPERIENCE);
        }

        private void OnEntityDied(EntityID entityId)
        {
            _sequence.Add(MARKER_ENTITY_DIED);
            _diedIds.Add(entityId);
            _diedHps.Add(_stats.GetCurrentHP(entityId));
            _diedAttackerXp.Add(_stats.GetBaseStat(Attacker, StatID.Experience));
        }

        // ---------- helpers ----------

        private DamageCalculator CreateCalculator()
        {
            return new DamageCalculator(
                _stats,
                _weapons,
                _items,
                new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default);
        }

        /// <summary>
        /// Arranges attacker (player, level 1, known Experience) and one target (MaxHP ceiling first, then
        /// current HP, Defense 0), then clears the recorder: the arrange writes raise stat-changed events
        /// that are not part of the observed sequence.
        /// </summary>
        private void GivenKillSetup(float targetHp)
        {
            _stats.SetBaseStat(Attacker, StatID.Level, LEVEL_BELOW_CAP);
            _stats.SetBaseStat(Attacker, StatID.Experience, ATTACKER_START_XP);
            GivenTarget(Target, targetHp);
            _sequence.Clear();
        }

        private void GivenTarget(EntityID target, float hp)
        {
            _stats.SetBaseStat(target, StatID.MaxHP, TARGET_MAX_HP);
            _stats.SetCurrentHP(target, hp);
            _stats.SetBaseStat(target, StatID.Defense, 0);
        }

        /// <summary>Caller step 1: only resolves the hit. Nothing is applied yet.</summary>
        private DamageResult CalculateHit(DamageCalculator calculator, EntityID target)
        {
            return calculator.Calculate(BASE_DAMAGE_500, Attacker, target, DamageContext.PhysicalAuto);
        }

        /// <summary>
        /// Caller step 2 (GDD Core Rule 4): on a kill, obtain the XP award, AddExperience on the attacker,
        /// then ApplyDamage; on a non-kill, just ApplyDamage. The applyDamage marker is appended immediately
        /// before ApplyDamage because OnEntityDied fires inside it.
        /// </summary>
        private void RunCallerFollowUp(DamageResult result, EntityID target)
        {
            if (result.IsKill)
            {
                int xp = _xpSource.GetXpAward();
                _stats.AddExperience(Attacker, xp);
            }

            _sequence.Add(MARKER_APPLY_DAMAGE);
            _stats.ApplyDamage(target, result.FinalDamage);
        }

        /// <summary>Calculate then follow-up, as the real caller would do in one call.</summary>
        private DamageResult RunCaller(DamageCalculator calculator, EntityID target)
        {
            DamageResult result = CalculateHit(calculator, target);
            RunCallerFollowUp(result, target);
            return result;
        }

        // ---------- tests ----------

        [Test]
        public void DamageCalculation_KillSequence_KillOnHp100_EntityDiedHandlerReadsZeroHp() // AC-DC-I-02
        {
            // Arrange
            GivenKillSetup(HP_100);
            DamageCalculator calculator = CreateCalculator();

            // Act
            RunCaller(calculator, Target);

            // Assert
            Assert.AreEqual(1, _diedIds.Count);
            Assert.AreEqual(Target, _diedIds[0]);
            Assert.AreEqual(0f, _diedHps[0], 0f);
        }

        [Test]
        public void DamageCalculation_KillSequence_KillOnHp100_OrderIsXpAddExperienceApplyDamageDied() // AC-DC-I-02b
        {
            // Arrange
            GivenKillSetup(HP_100);
            DamageCalculator calculator = CreateCalculator();

            // Act
            RunCaller(calculator, Target);

            // Assert
            CollectionAssert.AreEqual(
                new[] { MARKER_XP_AWARD, MARKER_ADD_EXPERIENCE, MARKER_APPLY_DAMAGE, MARKER_ENTITY_DIED },
                _sequence);
            // Stored state, not a marker: the XP was already on the attacker when the death fired.
            Assert.AreEqual(1, _diedAttackerXp.Count);
            Assert.AreEqual(ATTACKER_START_XP + XP_AWARD_AMOUNT, _diedAttackerXp[0]);
        }

        [Test]
        public void DamageCalculation_KillSequence_ExactLethalHp500Base500_KillAndDiedWithZeroHp() // Step 10 equality end to end
        {
            // Arrange - damage equal to HP: a kill at equality, and ApplyDamage lands exactly on 0
            GivenKillSetup(HP_EXACT_LETHAL);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = RunCaller(calculator, Target);

            // Assert
            Assert.IsTrue(result.IsKill);
            Assert.AreEqual(BASE_DAMAGE_500, result.FinalDamage);
            Assert.AreEqual(1, _diedIds.Count);
            Assert.AreEqual(0f, _diedHps[0], 0f);
        }

        [Test]
        public void DamageCalculation_KillSequence_CalculateReturned_NothingRecordedAndNoDeathYet() // AC-DC-I-02b
        {
            // Arrange
            GivenKillSetup(HP_100);
            DamageCalculator calculator = CreateCalculator();

            // Act
            CalculateHit(calculator, Target);

            // Assert
            Assert.IsEmpty(_sequence);
            Assert.AreEqual(0, _diedIds.Count);
        }

        [Test]
        public void DamageCalculation_KillSequence_Base500Hp100_KillReportedThenSequenceFiresDiedOnce() // AC-DC-I-06 (caller half)
        {
            // Arrange
            GivenKillSetup(HP_100);
            DamageCalculator calculator = CreateCalculator();

            // Act - resolver only
            DamageResult result = CalculateHit(calculator, Target);

            // Assert - no side effect yet
            Assert.IsTrue(result.IsKill);
            Assert.AreEqual(BASE_DAMAGE_500, result.FinalDamage);
            Assert.AreEqual(0, _diedIds.Count);
            Assert.AreEqual(HP_100, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(0, _xpSource.CallCount);

            // Act - caller follow-up
            RunCallerFollowUp(result, Target);

            // Assert - sequence ran
            Assert.AreEqual(1, _diedIds.Count);
            Assert.AreEqual(ATTACKER_START_XP + XP_AWARD_AMOUNT, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(1, _xpSource.CallCount);
        }

        [Test]
        public void DamageCalculation_KillSequence_SecondHitOnDeadTarget_NoKillNoSecondDeathNoSecondXp() // Second hit on the dead target
        {
            // Arrange
            GivenKillSetup(HP_100);
            DamageCalculator calculator = CreateCalculator();
            RunCaller(calculator, Target);
            int experienceAfterKill = _stats.GetBaseStat(Attacker, StatID.Experience);
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(DEAD_ENTITY_ERROR));

            // Act
            DamageResult secondResult = CalculateHit(calculator, Target);
            RunCallerFollowUp(secondResult, Target);

            // Assert
            Assert.IsFalse(secondResult.IsKill);
            Assert.AreEqual(BASE_DAMAGE_500, secondResult.FinalDamage);
            Assert.AreEqual(1, _diedIds.Count);
            Assert.AreEqual(1, _xpSource.CallCount);
            Assert.AreEqual(experienceAfterKill, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(0f, _stats.GetCurrentHP(Target), 0f);
        }

        [Test]
        public void DamageCalculation_KillSequence_Base500Hp1000_NoKillNoXpHpDropsTo500() // Non-kill
        {
            // Arrange
            GivenKillSetup(HP_1000);
            DamageCalculator calculator = CreateCalculator();

            // Act
            DamageResult result = RunCaller(calculator, Target);

            // Assert
            Assert.IsFalse(result.IsKill);
            Assert.AreEqual(0, _xpSource.CallCount);
            Assert.AreEqual(ATTACKER_START_XP, _stats.GetBaseStat(Attacker, StatID.Experience));
            Assert.AreEqual(HP_AFTER_NON_KILL, _stats.GetCurrentHP(Target), 0f);
            Assert.AreEqual(0, _diedIds.Count);
            CollectionAssert.AreEqual(new[] { MARKER_APPLY_DAMAGE }, _sequence);
        }

        [Test]
        public void DamageCalculation_KillSequence_TwoTargetsKilledInTurn_EachDiedWithZeroHpAndXpTwice() // Extra
        {
            // Arrange
            GivenKillSetup(HP_100);
            GivenTarget(SecondTarget, HP_100);
            _sequence.Clear();
            DamageCalculator calculator = CreateCalculator();

            // Act
            RunCaller(calculator, Target);
            RunCaller(calculator, SecondTarget);

            // Assert
            Assert.AreEqual(2, _diedIds.Count);
            Assert.AreEqual(Target, _diedIds[0]);
            Assert.AreEqual(SecondTarget, _diedIds[1]);
            Assert.AreEqual(0f, _diedHps[0], 0f);
            Assert.AreEqual(0f, _diedHps[1], 0f);
            Assert.AreEqual(2, _xpSource.CallCount);
            Assert.AreEqual(ATTACKER_START_XP + 2 * XP_AWARD_AMOUNT, _stats.GetBaseStat(Attacker, StatID.Experience));
        }

        // ---------- fakes ----------

        /// <summary>Fake of the caller-owned XP lookup (OQ-DC-3): fixed amount, records each call in the sequence.</summary>
        private sealed class FakeXpSource
        {
            private readonly List<string> _sequence;
            private readonly int _amount;

            public FakeXpSource(List<string> sequence, int amount)
            {
                _sequence = sequence;
                _amount = amount;
            }

            public int CallCount { get; private set; }

            public int GetXpAward()
            {
                CallCount++;
                _sequence.Add(MARKER_XP_AWARD);
                return _amount;
            }
        }
    }
}

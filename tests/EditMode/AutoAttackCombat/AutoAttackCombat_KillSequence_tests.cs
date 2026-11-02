using System;
using System.Text.RegularExpressions;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.Events;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 004: the kill sequence (Rule 10 Step 3; TR-aa-004; ADR-010 Decision 3).
    /// One shared call log records the XP award, the experience add, the damage apply and the OnMobDied raise, so their
    /// order is asserted across interfaces. The shared fakes live in AutoAttackCombat_TestFakes.cs.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_KillSequence_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int CYCLE = 20;
        private const int IDLE_CHECK_TICKS = 100;
        private const float TICK_DELTA = 0.05f;
        private const int XP_AMOUNT = 120;
        private const int KILL_DAMAGE = 10;
        private const int SURVIVING_HP = KILL_DAMAGE + 1;
        private const int MOB_HIGH_HP = 1000;
        private const float DEATH_X = 1.5f;   // XZ distance 2.5 from the attacker at the origin: inside RANGE
        private const float DEATH_Y = 1.2f;
        private const float DEATH_Z = -2.0f;
        private const float RANGE = 3.0f;
        private const float POSITION_TOLERANCE = 1e-4f;
        private const string THROW_MESSAGE = "kill-sequence-test-subscriber-failure";
        private const string TYPE_LOOKUP_ERROR_PATTERN = "OnMobDied skipped";
        private const int PASS_THROUGH_XP = 75;
        private const uint ROSTER_MOB_RAW = 5001u;
        private const uint ROSTER_TYPE_RAW = 31u;
        private const uint ROSTER_UNKNOWN_RAW = 5999u;
        private const int KILL_CALLS = 3;              // award, add, apply
        private const int KILL_CALLS_WITH_EVENT = 4;   // award, add, apply, died
        private const int TWO_KILLS = 2;
        private const float SLOW_ATTACK_SPEED = 0.5f;
        private const int XP_THRESHOLD = XP_AMOUNT;     // the kill's XP reaches it exactly
        private const uint OTHER_PLAYER_RAW = 9001u;
        private const float FAR_X = 5.0f;              // XZ distance 5.0 from the attacker at the origin: beyond RANGE

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class AlwaysValid : IAutoAttackTargetValidity
        {
            public bool IsValidTarget(EntityID target)
            {
                return true;
            }
        }

        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float Multiplier = 1.0f;

            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                return Multiplier;
            }
        }

        // Every entity is a player unless listed in NonPlayer; records threshold crossings.
        private sealed class PlayerLeveling : ILevelingService
        {
            public int Threshold = int.MaxValue;
            public EntityID NonPlayer = EntityID.Invalid;
            public int Crossings;
            public int LastExperienceBefore = -1;

            public bool IsPlayerEntity(EntityID entityId) => entityId != NonPlayer;

            public int GetExperienceThreshold(EntityID entityId) => Threshold;

            public void NotifyExperienceCrossedThreshold(EntityID entityId, int experienceBeforeGain)
            {
                Crossings++;
                LastExperienceBefore = experienceBeforeGain;
            }
        }

        private sealed class FakeRoster : IZoneMobRoster
        {
            public EntityID KnownMob;
            public MobTypeID KnownType;

            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
            {
                return EntityID.Invalid;
            }

            public void RemoveMob(ZoneID zone, EntityID id) { }

            public int GetMobCount(ZoneID zone)
            {
                return 0;
            }

            public bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition)
            {
                spawnPosition = Vector3.zero;
                if (id == KnownMob)
                {
                    type = KnownType;
                    return true;
                }

                type = default;
                return false;
            }
        }

        private ZoneSlotPool _pool;
        private FakeZoneState _state;
        private FakeHitEmitter _emitter;
        private FakeStatSource _stats;
        private KillCallLog _log;
        private FakeMobHealth _health;
        private HealthAwareDamageCalc _calc;
        private LoggingDamageApplier _applier;
        private FakeXpAward _xp;
        private FakeExperienceSink _exp;
        private FakeMobTypeSource _mobTypes;
        private FakeAttackSpeed _attackSpeed;
        private AutoAttackCombatSystem _combat;
        private EntityID _reentrantPlayer;
        private uint _nextClient;
        private uint _tick;
        private int _diedCount;
        private int _throwCount;
        private MobDeathContext _lastDeath;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static EntityID MobA => new EntityID(AutoAttackTestFakes.MOB_A_RAW);

        private static EntityID MobB => new EntityID(AutoAttackTestFakes.MOB_B_RAW);

        [SetUp]
        public void SetUp()
        {
            _nextClient = 11u;
            _tick = 0u;
            _diedCount = 0;
            _throwCount = 0;
            _lastDeath = default;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _state = new FakeZoneState();
            _emitter = new FakeHitEmitter();
            _stats = new FakeStatSource { AttackRange = RANGE };
            _log = new KillCallLog();
            _health = new FakeMobHealth { HpA = KILL_DAMAGE, HpB = KILL_DAMAGE };
            _calc = new HealthAwareDamageCalc(_health) { Damage = KILL_DAMAGE };
            _applier = new LoggingDamageApplier(_log, _health);
            _xp = new FakeXpAward(_log) { Award = XP_AMOUNT };
            _exp = new FakeExperienceSink(_log);
            _mobTypes = new FakeMobTypeSource();
            _attackSpeed = new FakeAttackSpeed();
            _reentrantPlayer = EntityID.Invalid;
            _combat = NewCombat(_calc);
        }

        private AutoAttackCombatSystem NewCombat(IAutoAttackDamageCalculator calc, IAutoAttackExperienceSink experience = null)
        {
            return new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), _attackSpeed, _state, new HitDetector(HitDetectionConfig.Default),
                _emitter, _stats, calc, _applier, _xp, experience ?? _exp, _mobTypes, Zone, new FakeFaceOutput());
        }

        private EntityID AddPlayer()
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        private static void Ready(AutoAttackCombatSystem combat, EntityID player, EntityID target)
        {
            combat.SetAutoAttack(player, true);
            combat.SetTarget(player, target);
        }

        private void RunTicks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _tick++;
                _combat.Run(_tick, TICK_DELTA);
            }
        }

        private static void RunKillCycle(AutoAttackCombatSystem combat)
        {
            uint t = 0u;
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);
            }
        }

        // Named-method subscribers (no lambdas).
        private void OnMobDiedRecord(MobDeathContext context)
        {
            _lastDeath = context;
            _diedCount++;
            _log.Record(KillCallKind.Died, context.KillerEntityId, context.MobEntityId, 0);
        }

        private void OnMobDiedThrow(MobDeathContext context)
        {
            _throwCount++;
            throw new InvalidOperationException(THROW_MESSAGE);
        }

        private void OnMobDiedRetargetToMobB(MobDeathContext context)
        {
            _combat.SetTarget(_reentrantPlayer, MobB);
        }

        private void OnMobDiedReleasePlayer(MobDeathContext context)
        {
            _combat.ReleasePlayer(_reentrantPlayer);
        }

        // AC 1: GetXPAward -> AddExperience -> ApplyDamage, with the right ids and amounts.
        [Test]
        public void test_kill_runs_award_then_add_then_apply_with_correct_ids_and_amounts()
        {
            EntityID p = AddPlayer();
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(KILL_CALLS, _log.Count);
            Assert.AreEqual(KillCallKind.Award, _log.Entries[0].Kind);
            Assert.AreEqual(MobA, _log.Entries[0].Target);
            Assert.AreEqual(XP_AMOUNT, _log.Entries[0].Amount);
            Assert.AreEqual(KillCallKind.Add, _log.Entries[1].Kind);
            Assert.AreEqual(p, _log.Entries[1].Attacker);
            Assert.AreEqual(XP_AMOUNT, _log.Entries[1].Amount);
            Assert.AreEqual(KillCallKind.Apply, _log.Entries[2].Kind);
            Assert.AreEqual(p, _log.Entries[2].Attacker);
            Assert.AreEqual(MobA, _log.Entries[2].Target);
            Assert.AreEqual(KILL_DAMAGE, _log.Entries[2].Amount);
        }

        // AC 2: no kill, apply only.
        [Test]
        public void test_non_kill_applies_damage_only_without_award_or_experience()
        {
            EntityID p = AddPlayer();
            _health.HpA = SURVIVING_HP;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _log.Count);
            Assert.AreEqual(KillCallKind.Apply, _log.Entries[0].Kind);
            Assert.AreEqual(0, _xp.Calls);
            Assert.AreEqual(0, _exp.Calls);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p));
        }

        // AC 3: IDLE, timer 0, target cleared, toggle unchanged, nothing fires afterwards.
        [Test]
        public void test_kill_goes_idle_clears_target_keeps_toggle_and_nothing_fires_for_100_ticks()
        {
            EntityID p = AddPlayer();
            Ready(_combat, p, MobA);
            RunTicks(CYCLE);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));

            int logBefore = _log.Count;
            int calcBefore = _calc.Calls;
            int emitBefore = _emitter.Count;
            RunTicks(IDLE_CHECK_TICKS);

            Assert.AreEqual(logBefore, _log.Count);
            Assert.AreEqual(calcBefore, _calc.Calls);
            Assert.AreEqual(emitBefore, _emitter.Count);
            Assert.AreEqual(0, _combat.GetActionTimer(p), "the timer stays frozen at 0");

            _health.HpB = MOB_HIGH_HP;
            _combat.SetTarget(p, MobB);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p), "the toggle is still ON");
        }

        // AC 4: one raise per kill, after apply, with the right context.
        [Test]
        public void test_kill_raises_on_mob_died_once_after_apply_with_correct_context()
        {
            EntityID p = AddPlayer();
            _state.TargetX = AutoAttackTestFakes.Raw(DEATH_X);
            _state.TargetY = AutoAttackTestFakes.Raw(DEATH_Y);
            _state.TargetZ = AutoAttackTestFakes.Raw(DEATH_Z);
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _diedCount);
            Assert.AreEqual(KILL_CALLS_WITH_EVENT, _log.Count);
            Assert.AreEqual(KillCallKind.Apply, _log.Entries[2].Kind);
            Assert.AreEqual(KillCallKind.Died, _log.Entries[3].Kind, "raised after ApplyDamage");
            Assert.AreEqual(Zone, _lastDeath.ZoneId);
            Assert.AreEqual(MobA, _lastDeath.MobEntityId);
            Assert.AreEqual(new MobTypeID(FakeMobTypeSource.MOB_TYPE_RAW), _lastDeath.MobTypeId);
            Assert.AreEqual(p, _lastDeath.KillerEntityId);
            Assert.AreEqual(AutoAttackTestFakes.Raw(DEATH_X) / AutoAttackTestFakes.WIRE_SCALE, _lastDeath.DeathPosition.x, POSITION_TOLERANCE);
            Assert.AreEqual(AutoAttackTestFakes.Raw(DEATH_Y) / AutoAttackTestFakes.WIRE_SCALE, _lastDeath.DeathPosition.y, POSITION_TOLERANCE);
            Assert.AreEqual(AutoAttackTestFakes.Raw(DEATH_Z) / AutoAttackTestFakes.WIRE_SCALE, _lastDeath.DeathPosition.z, POSITION_TOLERANCE);
        }

        [Test]
        public void test_non_kill_does_not_raise_on_mob_died()
        {
            EntityID p = AddPlayer();
            _health.HpA = SURVIVING_HP;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(0, _diedCount);
        }

        // A throwing subscriber is logged; the kill sequence completes and a later slot still resolves in the same Run.
        [Test]
        public void test_throwing_subscriber_does_not_break_tick_and_second_player_still_resolves()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            _combat.OnMobDied += OnMobDiedThrow;
            Ready(_combat, p1, MobA);
            Ready(_combat, p2, MobB);
            LogAssert.Expect(LogType.Exception, new Regex(THROW_MESSAGE));
            LogAssert.Expect(LogType.Exception, new Regex(THROW_MESSAGE));

            RunTicks(CYCLE);

            Assert.AreEqual(TWO_KILLS, _throwCount);
            Assert.AreEqual(TWO_KILLS, _log.CountOf(KillCallKind.Award));
            Assert.AreEqual(TWO_KILLS, _log.CountOf(KillCallKind.Add));
            Assert.AreEqual(TWO_KILLS, _log.CountOf(KillCallKind.Apply));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p1));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p2));
            Assert.AreEqual(0, _combat.GetActionTimer(p1));
        }

        // The mob type cannot be resolved: no raise, one error logged, the rest of the sequence complete.
        [Test]
        public void test_mob_type_lookup_failure_skips_raise_logs_error_and_completes_kill()
        {
            EntityID p = AddPlayer();
            _mobTypes.Known = false;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);
            LogAssert.Expect(LogType.Error, new Regex(TYPE_LOOKUP_ERROR_PATTERN));

            RunTicks(CYCLE);

            Assert.AreEqual(0, _diedCount);
            Assert.AreEqual(KILL_CALLS, _log.Count);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
        }

        // AC 5 and 6: two attackers on one mob in one tick; the second Calculate is reached, sees a dead target and is
        // rejected (FinalDamage 0).
        [Test]
        public void test_two_attackers_one_mob_one_tick_rejected_second_hit_awards_once_and_raises_once()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p1, MobA);
            Ready(_combat, p2, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(TWO_KILLS, _calc.Calls, "the second attacker's Calculate is reached");
            Assert.AreEqual(1, _log.CountOf(KillCallKind.Award));
            Assert.AreEqual(1, _log.CountOf(KillCallKind.Add));
            Assert.AreEqual(1, _log.CountOf(KillCallKind.Apply));
            Assert.AreEqual(1, _diedCount);
            Assert.AreEqual(p1, _lastDeath.KillerEntityId);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p1));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p2), "the rejected second attack is not a kill");
        }

        // AC 5 and 6, real-calculator variant: DamageCalculator's dead-entity guard returns the damage with IsKill false
        // for a 0-HP target, so the second hit is applied to the dead mob but is not a second kill.
        [Test]
        public void test_two_attackers_one_mob_one_tick_dead_target_hit_without_kill_awards_once_and_applies_twice()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            _calc.DeadTargetHitsWithoutKill = true;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p1, MobA);
            Ready(_combat, p2, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _log.CountOf(KillCallKind.Award));
            Assert.AreEqual(1, _log.CountOf(KillCallKind.Add));
            Assert.AreEqual(TWO_KILLS, _log.CountOf(KillCallKind.Apply), "the overkill hit on the dead mob is applied");
            Assert.AreEqual(1, _diedCount);
            Assert.AreEqual(p1, _lastDeath.KillerEntityId);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p1));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p2));
        }

        // Zero XP: the sequence still completes.
        [Test]
        public void test_zero_xp_award_still_adds_applies_and_raises()
        {
            EntityID p = AddPlayer();
            _xp.Award = 0;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(KILL_CALLS_WITH_EVENT, _log.Count);
            Assert.AreEqual(KillCallKind.Add, _log.Entries[1].Kind);
            Assert.AreEqual(0, _log.Entries[1].Amount);
            Assert.AreEqual(KillCallKind.Apply, _log.Entries[2].Kind);
            Assert.AreEqual(1, _diedCount);
        }

        // A rejected result (FinalDamage 0) ignores an IsKill flag.
        [Test]
        public void test_rejected_result_with_kill_flag_does_not_award_apply_or_raise()
        {
            EntityID p = AddPlayer();
            var rejectedKill = new DamageResult(0, 0, 0, false, true, DamageContext.PhysicalAuto, false);
            var combat = NewCombat(new FakeDamageCalc { Result = rejectedKill });
            combat.OnMobDied += OnMobDiedRecord;
            Ready(combat, p, MobA);

            RunKillCycle(combat);

            Assert.AreEqual(0, _log.Count);
            Assert.AreEqual(0, _diedCount);
            Assert.AreEqual(AutoAttackState.CombatActive, combat.GetState(p));
            Assert.AreEqual(MobA, combat.GetTarget(p));
            Assert.AreEqual(0, combat.GetActionTimer(p), "the rejected fire still resets the timer");
        }

        // The mob type is read before Apply: an applier that despawns the dead mob does not suppress the raise.
        [Test]
        public void test_mob_removed_from_roster_by_apply_still_raises_on_mob_died()
        {
            EntityID p = AddPlayer();
            _applier.ForgetTypeOnApply = _mobTypes;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.IsFalse(_mobTypes.Known, "the applier removed the mob");
            Assert.AreEqual(1, _diedCount);
            Assert.AreEqual(new MobTypeID(FakeMobTypeSource.MOB_TYPE_RAW), _lastDeath.MobTypeId);
        }

        // Each subscriber is isolated: the first one throwing does not stop the second.
        [Test]
        public void test_first_subscriber_throws_second_subscriber_still_runs()
        {
            EntityID p = AddPlayer();
            _combat.OnMobDied += OnMobDiedThrow;
            _combat.OnMobDied += OnMobDiedRecord;
            Ready(_combat, p, MobA);
            LogAssert.Expect(LogType.Exception, new Regex(THROW_MESSAGE));

            RunTicks(CYCLE);

            Assert.AreEqual(1, _throwCount);
            Assert.AreEqual(1, _diedCount, "the second subscriber still ran");
            Assert.AreEqual(KillCallKind.Died, _log.Entries[KILL_CALLS].Kind);
        }

        // Custom add/remove keep C# event semantics: each registration is raised, a remove drops one registration.
        [Test]
        public void test_subscribe_twice_remove_once_raises_once_and_remove_all_raises_none()
        {
            EntityID p1 = AddPlayer();
            EntityID p2 = AddPlayer();
            _combat.OnMobDied += OnMobDiedRecord;
            _combat.OnMobDied += OnMobDiedRecord;
            _combat.OnMobDied -= OnMobDiedRecord;
            Ready(_combat, p1, MobA);

            RunTicks(CYCLE);
            Assert.AreEqual(1, _diedCount);

            _combat.OnMobDied -= OnMobDiedRecord;
            _combat.OnMobDied -= OnMobDiedRecord;   // removing an absent handler is a no-op
            Ready(_combat, p2, MobB);
            RunTicks(CYCLE);

            Assert.AreEqual(1, _diedCount, "no subscriber is left");
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p2), "the second kill happened");
        }

        // The slot reaches its final state before the raise, so a subscriber's re-target sticks.
        [Test]
        public void test_subscriber_retarget_during_on_mob_died_keeps_new_target_active()
        {
            EntityID p = AddPlayer();
            _reentrantPlayer = p;
            _health.HpB = MOB_HIGH_HP;
            _combat.OnMobDied += OnMobDiedRetargetToMobB;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(MobB, _combat.GetTarget(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // A subscriber releasing the player leaves the slot released: nothing is written to it after the raise.
        [Test]
        public void test_subscriber_release_during_on_mob_died_leaves_slot_released()
        {
            EntityID p = AddPlayer();
            _reentrantPlayer = p;
            _combat.OnMobDied += OnMobDiedReleasePlayer;
            Ready(_combat, p, MobA);

            RunTicks(CYCLE);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
            Assert.AreEqual(0, _combat.GetCycleTicks(p), "ReleasePlayer's 0 is not overwritten by a later timer reset");
            _health.HpB = MOB_HIGH_HP;
            _combat.SetTarget(p, MobB);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p), "the release turned the toggle OFF");
        }

        // Story note: no kill call runs inside a CharacterStats event. The kill's XP crosses the level threshold through
        // the real CharacterStats; the sequence completes with no re-entrance exception and no error.
        [Test]
        public void test_kill_xp_crossing_threshold_with_real_character_stats_completes_without_error()
        {
            EntityID p = AddPlayer();
            var leveling = new PlayerLeveling { Threshold = XP_THRESHOLD };
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            var combat = NewCombat(_calc, new CharacterStatsExperienceSink(stats));
            combat.OnMobDied += OnMobDiedRecord;
            Ready(combat, p, MobA);

            RunKillCycle(combat);

            Assert.AreEqual(XP_AMOUNT, stats.GetBaseStat(p, StatID.Experience));
            Assert.AreEqual(1, leveling.Crossings);
            Assert.AreEqual(0, leveling.LastExperienceBefore);
            Assert.AreEqual(1, _log.CountOf(KillCallKind.Apply));
            Assert.AreEqual(1, _diedCount);
            Assert.AreEqual(AutoAttackState.Idle, combat.GetState(p));
            LogAssert.NoUnexpectedReceived();
        }

        // Pinned: a kill of a player-resolved target (not a valid target at MVP) still awards and applies; the roster has
        // no type for it, so the raise is skipped with an error.
        [Test]
        public void test_kill_of_player_target_awards_and_applies_but_skips_raise_with_error()
        {
            EntityID p = AddPlayer();
            var other = new EntityID(OTHER_PLAYER_RAW);
            _state.TargetPlayer = other;
            var roster = new FakeRoster { KnownMob = MobA, KnownType = new MobTypeID(FakeMobTypeSource.MOB_TYPE_RAW) };
            var combat = new AutoAttackCombatSystem(
                _pool, new AlwaysValid(), _attackSpeed, _state, new HitDetector(HitDetectionConfig.Default),
                _emitter, _stats, _calc, _applier, _xp, _exp, new ZoneMobRosterMobTypeSource(roster), Zone, new FakeFaceOutput());
            combat.OnMobDied += OnMobDiedRecord;
            Ready(combat, p, other);
            LogAssert.Expect(LogType.Error, new Regex(TYPE_LOOKUP_ERROR_PATTERN));

            RunKillCycle(combat);

            Assert.AreEqual(KILL_CALLS, _log.Count);
            Assert.AreEqual(other, _log.Entries[2].Target);
            Assert.AreEqual(0, _diedCount);
            Assert.AreEqual(AutoAttackState.Idle, combat.GetState(p));
        }

        // A re-target after a kill samples the attack speed afresh and fires after exactly the new cycle.
        [Test]
        public void test_retarget_after_kill_uses_resampled_cycle()
        {
            EntityID p = AddPlayer();
            Ready(_combat, p, MobA);
            RunTicks(CYCLE);
            int expected = AutoAttackCycle.ComputeCycleTicks(SLOW_ATTACK_SPEED);
            Assert.AreNotEqual(CYCLE, expected, "the slower speed must change the cycle");

            _attackSpeed.Multiplier = SLOW_ATTACK_SPEED;
            _health.HpB = MOB_HIGH_HP;
            _combat.SetTarget(p, MobB);
            int appliesBefore = _applier.Count;
            Assert.AreEqual(expected, _combat.GetCycleTicks(p));

            RunTicks(expected - 1);
            Assert.AreEqual(appliesBefore, _applier.Count, "no fire before the new cycle ends");

            RunTicks(1);
            Assert.AreEqual(appliesBefore + 1, _applier.Count);
        }

        // A kill ends the miss streak: the same miss on the next target is emitted again.
        [Test]
        public void test_kill_after_miss_streak_then_same_miss_emits_again()
        {
            EntityID p = AddPlayer();
            _state.TargetX = AutoAttackTestFakes.Raw(FAR_X);
            Ready(_combat, p, MobA);
            RunTicks(CYCLE + 1);   // one ready miss, then a repeat that is not re-sent
            Assert.AreEqual(1, _emitter.Count);

            _state.TargetX = AutoAttackTestFakes.Raw(1f);
            RunTicks(1);           // in range: hit, kill
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(HitResult.Hit, _emitter.Results[1]);

            _state.TargetX = AutoAttackTestFakes.Raw(FAR_X);
            _health.HpB = MOB_HIGH_HP;
            _combat.SetTarget(p, MobB);
            RunTicks(CYCLE);

            Assert.AreEqual(3, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[2]);
        }

        [Test]
        public void test_combat_system_is_an_icombat_service()
        {
            Assert.IsInstanceOf<ICombatService>(_combat);
        }

        [Test]
        public void test_experience_sink_null_stats_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsExperienceSink(null));
        }

        [Test]
        public void test_experience_sink_passes_amount_through_to_character_stats()
        {
            var stats = CharacterStatsFixture.CreateWithLeveling(new PlayerLeveling());
            var sink = new CharacterStatsExperienceSink(stats);
            EntityID player = CharacterStatsFixture.PlayerEntityId;

            sink.AddExperience(player, PASS_THROUGH_XP);
            sink.AddExperience(player, PASS_THROUGH_XP);

            Assert.AreEqual(PASS_THROUGH_XP * 2, stats.GetBaseStat(player, StatID.Experience));
        }

        [Test]
        public void test_experience_sink_zero_amount_adds_nothing()
        {
            var stats = CharacterStatsFixture.CreateWithLeveling(new PlayerLeveling());
            var sink = new CharacterStatsExperienceSink(stats);
            EntityID player = CharacterStatsFixture.PlayerEntityId;

            sink.AddExperience(player, 0);

            Assert.AreEqual(0, stats.GetBaseStat(player, StatID.Experience));
        }

        [Test]
        public void test_experience_sink_non_player_adds_nothing()
        {
            EntityID mob = CharacterStatsFixture.MobEntityId;
            var stats = CharacterStatsFixture.CreateWithLeveling(new PlayerLeveling { NonPlayer = mob });
            var sink = new CharacterStatsExperienceSink(stats);

            sink.AddExperience(mob, PASS_THROUGH_XP);

            Assert.AreEqual(0, stats.GetBaseStat(mob, StatID.Experience));
        }

        [Test]
        public void test_mob_type_source_null_roster_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneMobRosterMobTypeSource(null));
        }

        [Test]
        public void test_mob_type_source_returns_roster_type_for_known_mob_and_false_for_unknown()
        {
            var roster = new FakeRoster
            {
                KnownMob = new EntityID(ROSTER_MOB_RAW),
                KnownType = new MobTypeID(ROSTER_TYPE_RAW)
            };
            var source = new ZoneMobRosterMobTypeSource(roster);

            Assert.IsTrue(source.TryGetMobType(new EntityID(ROSTER_MOB_RAW), out MobTypeID known));
            Assert.AreEqual(new MobTypeID(ROSTER_TYPE_RAW), known);
            Assert.IsFalse(source.TryGetMobType(new EntityID(ROSTER_UNKNOWN_RAW), out _));
        }

        // Forbidden: per-tick allocation. A killing Run with one named-method subscriber, after a warm-up kill.
        [Test]
        public void test_killing_run_with_subscriber_allocates_nothing()
        {
            EntityID p = AddPlayer();
            var warm = NewCombat(_calc);
            warm.OnMobDied += OnMobDiedRecord;
            Ready(warm, p, MobA);
            RunKillCycle(warm);   // JIT warm-up outside the measured block
            Assert.AreEqual(1, _diedCount);

            _health.HpA = KILL_DAMAGE;
            var combat = NewCombat(_calc);
            combat.OnMobDied += OnMobDiedRecord;
            Ready(combat, p, MobA);
            int diedBefore = _diedCount;
            int appliesBefore = _applier.Count;

            Assert.That(() => RunKillCycle(combat), Is.Not.AllocatingGCMemory());

            Assert.AreEqual(1, _diedCount - diedBefore);
            Assert.AreEqual(1, _applier.Count - appliesBefore);
        }
    }
}

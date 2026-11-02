using System.Reflection;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 005: NotifySkillUsed (Rules 12-15; TR-aa-005; AC-04, AC-05;
    /// EC-21, EC-24). The skill request is dispatched before combat's Run in the same tick (ADR-014 Decision 6), so
    /// "fires N ticks after the call" means the Nth Run after the call. The shared fakes live in AutoAttackCombat_TestFakes.cs.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_NotifySkillUsed_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int CYCLE = 20;
        private const int PARTIAL_TICKS = 8;
        private const int EARLY_TICKS = 5;
        private const int TICKS_BETWEEN_CALLS = 2;
        private const int PAST_READY_TICKS = 10;
        private const int FAKE_FINAL_DAMAGE = 10;
        private const int ALLOCATION_CALLS = 10;
        private const float TICK_DELTA = 0.05f;
        private const float OUT_OF_RANGE_DISTANCE = 3.5f;
        private const float FAST_MULTIPLIER = 2.0f;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeValidity : IAutoAttackTargetValidity
        {
            public bool IsValidTarget(EntityID target)
            {
                return true;
            }
        }

        private sealed class CountingAttackSpeed : IAttackSpeedSource
        {
            public float Multiplier = 1.0f;
            public int Reads;

            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                Reads++;
                return Multiplier;
            }
        }

        private ZoneSlotPool _pool;
        private FakeValidity _validity;
        private CountingAttackSpeed _attackSpeed;
        private FakeZoneState _state;
        private HitDetector _detector;
        private FakeHitEmitter _emitter;
        private FakeStatSource _stats;
        private FakeDamageCalc _calc;
        private FakeDamageApplier _applier;
        private AutoAttackCombatSystem _combat;
        private uint _nextClient;
        private uint _tick;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static EntityID MobA => new EntityID(AutoAttackTestFakes.MOB_A_RAW);

        [SetUp]
        public void SetUp()
        {
            _nextClient = 11u;
            _tick = 0u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _validity = new FakeValidity();
            _attackSpeed = new CountingAttackSpeed();
            _state = new FakeZoneState();
            _detector = new HitDetector(HitDetectionConfig.Default);
            _emitter = new FakeHitEmitter();
            _stats = new FakeStatSource();
            _calc = new FakeDamageCalc();
            _applier = new FakeDamageApplier();
            _combat = NewCombat();
        }

        private AutoAttackCombatSystem NewCombat()
        {
            return new AutoAttackCombatSystem(_pool, _validity, _attackSpeed, _state, _detector, _emitter, _stats, _calc, _applier,
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), Zone);
        }

        private EntityID AddPlayer()
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        // Active on MobA with a 20-tick cycle, target in range.
        private EntityID ReadyPlayer()
        {
            EntityID p = AddPlayer();
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            return p;
        }

        private void RunTicks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _tick++;
                _applier.Tick = _tick;
                _combat.Run(_tick, TICK_DELTA);
            }
        }

        // Rule 12: the call changes only the timer.
        [Test]
        public void test_skill_used_active_resets_timer_and_changes_nothing_else()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TICKS);
            Assert.AreEqual(PARTIAL_TICKS, _combat.GetActionTimer(p));
            int cycleBefore = _combat.GetCycleTicks(p);
            AutoAttackState stateBefore = _combat.GetState(p);

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(stateBefore, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p));
            Assert.AreEqual(cycleBefore, _combat.GetCycleTicks(p));
            Assert.AreEqual(0, _emitter.Count, "the call itself emits nothing");
            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _applier.Count, "the call itself applies nothing");

            RunTicks(CYCLE);
            Assert.AreEqual(1, _applier.Count, "toggle and target are still on: the auto resumes");
        }

        // Rule 6: the cycle length is sampled whenever the timer is set to 0.
        [Test]
        public void test_skill_used_active_resamples_cycle_length()
        {
            EntityID p = ReadyPlayer();
            Assert.AreEqual(CYCLE, _combat.GetCycleTicks(p));
            Assert.AreNotEqual(CYCLE, AutoAttackCycle.ComputeCycleTicks(FAST_MULTIPLIER), "the new multiplier must change the cycle");
            _attackSpeed.Multiplier = FAST_MULTIPLIER;

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(AutoAttackCycle.ComputeCycleTicks(FAST_MULTIPLIER), _combat.GetCycleTicks(p));
        }

        // AC-04
        [Test]
        public void test_skill_used_at_timer_8_next_auto_fires_on_20th_run_after_call_not_12th()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TICKS);

            _combat.NotifySkillUsed(p);
            Assert.AreEqual(0, _combat.GetActionTimer(p));

            RunTicks(CYCLE - PARTIAL_TICKS);
            Assert.AreEqual(0, _applier.Count, "12 runs after the call: no auto");
            RunTicks(CYCLE - 1 - (CYCLE - PARTIAL_TICKS));
            Assert.AreEqual(0, _applier.Count, "19 runs after the call: no auto");
            RunTicks(1);
            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(_tick, _applier.Ticks[0]);
        }

        // AC-05: an auto already resolved is kept. The skill's own damage is the Skill System's (out of scope); Combat
        // has no reference to it, so this test pins only Combat's half: one full-value auto, not reversed.
        [Test]
        public void test_skill_used_after_auto_fired_keeps_resolved_auto_at_full_value()
        {
            EntityID p = ReadyPlayer();
            RunTicks(CYCLE);
            Assert.AreEqual(1, _applier.Count);

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(1, _applier.Count, "no extra apply");
            Assert.AreEqual(FAKE_FINAL_DAMAGE, _applier.LastResult.FinalDamage, "the auto damage is full value, not reversed");
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // EC-21
        [Test]
        public void test_skill_used_after_auto_fired_next_auto_is_20_runs_after_call()
        {
            EntityID p = ReadyPlayer();
            RunTicks(CYCLE);
            _combat.NotifySkillUsed(p);

            RunTicks(CYCLE - 1);
            Assert.AreEqual(1, _applier.Count);
            RunTicks(1);

            Assert.AreEqual(2, _applier.Count);
            Assert.AreEqual(_tick, _applier.Ticks[1]);
        }

        // EC-24
        [Test]
        public void test_two_skill_calls_two_runs_apart_next_auto_is_20_runs_after_second_call()
        {
            EntityID p = ReadyPlayer();
            RunTicks(EARLY_TICKS);
            _combat.NotifySkillUsed(p);
            RunTicks(TICKS_BETWEEN_CALLS);
            _combat.NotifySkillUsed(p);

            RunTicks(CYCLE - TICKS_BETWEEN_CALLS);
            Assert.AreEqual(0, _applier.Count, "20 runs after the first call: no auto");
            RunTicks(TICKS_BETWEEN_CALLS - 1);
            Assert.AreEqual(0, _applier.Count, "19 runs after the second call: no auto");
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(_tick, _applier.Ticks[0]);
        }

        // Story 003 interaction: the reset ends the miss streak.
        [Test]
        public void test_skill_used_during_miss_streak_ends_streak_so_miss_emits_again()
        {
            EntityID p = ReadyPlayer();
            _state.TargetX = AutoAttackTestFakes.Raw(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE);
            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));

            _combat.NotifySkillUsed(p);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p), "the state is unchanged");
            RunTicks(CYCLE - 1);
            Assert.AreEqual(1, _emitter.Count, "not ready yet");
            RunTicks(1);

            Assert.AreEqual(2, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[1]);
        }

        [Test]
        public void test_skill_used_while_prohibited_resets_timer_and_fires_one_cycle_after_call()
        {
            EntityID p = ReadyPlayer();
            _combat.SetCombatProhibited(p, true);
            RunTicks(CYCLE + PAST_READY_TICKS);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE + PAST_READY_TICKS, _combat.GetActionTimer(p));

            _combat.NotifySkillUsed(p);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));
            _combat.SetCombatProhibited(p, false);
            RunTicks(CYCLE - 1);
            Assert.AreEqual(0, _applier.Count, "no immediate fire after the clear");
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(_tick, _applier.Ticks[0]);
        }

        // An idle slot only has its frozen timer zeroed; the attack speed is not read.
        [Test]
        public void test_skill_used_while_never_activated_is_idle_timer_zero_and_attack_speed_not_read()
        {
            EntityID p = AddPlayer();

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(0, _attackSpeed.Reads);
        }

        [Test]
        public void test_skill_used_while_idle_after_toggle_off_zeroes_frozen_timer_without_reading_attack_speed()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TICKS);
            _combat.SetAutoAttack(p, false);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(PARTIAL_TICKS, _combat.GetActionTimer(p), "frozen");
            int readsBefore = _attackSpeed.Reads;

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p), "the target is kept");
            Assert.AreEqual(readsBefore, _attackSpeed.Reads);
        }

        [Test]
        public void test_skill_used_for_entity_without_slot_is_ignored()
        {
            EntityID p = ReadyPlayer();
            RunTicks(EARLY_TICKS);
            var unknown = new EntityID(UNKNOWN_ENTITY_RAW);

            Assert.DoesNotThrow(() =>
            {
                _combat.NotifySkillUsed(EntityID.Invalid);
                _combat.NotifySkillUsed(unknown);
            });

            Assert.AreEqual(EARLY_TICKS, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        [Test]
        public void test_skill_used_with_released_id_is_ignored_and_does_not_reset_new_occupant()
        {
            EntityID old = ReadyPlayer();
            Assert.IsTrue(_pool.TryGetPlayerSlot(old, out byte oldSlot));
            _combat.ReleasePlayer(old);
            Assert.IsTrue(_pool.ReleasePlayer(old));

            Assert.DoesNotThrow(() => _combat.NotifySkillUsed(old));

            EntityID fresh = ReadyPlayer();
            Assert.IsTrue(_pool.TryGetPlayerSlot(fresh, out byte freshSlot));
            Assert.AreEqual(oldSlot, freshSlot, "the LIFO free-list hands the same slot back");
            RunTicks(PARTIAL_TICKS);

            _combat.NotifySkillUsed(old);

            Assert.AreEqual(PARTIAL_TICKS, _combat.GetActionTimer(fresh), "the stale id must not reach the new occupant");
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(fresh));
        }

        // After a kill the player is idle with no target and the toggle still ON (Story 004).
        [Test]
        public void test_skill_used_after_kill_keeps_idle_and_next_target_starts_fresh_cycle()
        {
            EntityID p = ReadyPlayer();
            _calc.Result = AutoAttackTestFakes.Damage(FAKE_FINAL_DAMAGE, true);
            RunTicks(CYCLE);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            int readsBefore = _attackSpeed.Reads;

            _combat.NotifySkillUsed(p);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(EntityID.Invalid, _combat.GetTarget(p));
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(readsBefore, _attackSpeed.Reads);

            _calc.Result = AutoAttackTestFakes.Damage(FAKE_FINAL_DAMAGE, false);
            _combat.SetTarget(p, MobA);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p), "the toggle is still ON");
            RunTicks(CYCLE - 1);
            Assert.AreEqual(1, _applier.Count, "only the kill so far");
            RunTicks(1);
            Assert.AreEqual(2, _applier.Count);
        }

        [Test]
        public void test_system_is_a_skill_use_sink_and_call_through_interface_resets_timer()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TICKS);
            IAutoAttackSkillUseSink sink = _combat;

            sink.NotifySkillUsed(p);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // User decision 2026-10-30: the reactive cooldown model supersedes the OWL grace window.
        [Test]
        public void test_system_has_no_last_beat_tracker_dependency()
        {
            foreach (ConstructorInfo ctor in typeof(AutoAttackCombatSystem).GetConstructors())
            {
                foreach (ParameterInfo parameter in ctor.GetParameters())
                {
                    Assert.AreNotEqual(typeof(LastBeatServerTickTracker), parameter.ParameterType);
                }
            }

            FieldInfo[] fields = typeof(AutoAttackCombatSystem).GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                Assert.AreNotEqual(typeof(LastBeatServerTickTracker), field.FieldType, field.Name);
            }
        }

        // Forbidden: per-call allocation, active and idle.
        [Test]
        public void test_skill_used_active_allocates_nothing()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TICKS);
            _combat.NotifySkillUsed(p);   // JIT warm-up outside the measured block

            Assert.That(() =>
            {
                for (int i = 0; i < ALLOCATION_CALLS; i++)
                {
                    _combat.NotifySkillUsed(p);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void test_skill_used_idle_and_unknown_allocates_nothing()
        {
            EntityID p = AddPlayer();
            var unknown = new EntityID(UNKNOWN_ENTITY_RAW);
            _combat.NotifySkillUsed(p);   // JIT warm-up outside the measured block
            _combat.NotifySkillUsed(unknown);

            Assert.That(() =>
            {
                for (int i = 0; i < ALLOCATION_CALLS; i++)
                {
                    _combat.NotifySkillUsed(p);
                    _combat.NotifySkillUsed(unknown);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}

using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.EnhancementSystem;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.DamageCalculation;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 003: Rule 10 resolution order (Rules 10, 11; F-2, F-3; AC-09 to
    /// AC-11, AC-20; EC-5 to EC-8, EC-10, EC-17, EC-19; Hit Detection CR-HD-10, AC-HD-11a, AC-HD-12, AC-HD-13).
    /// The shared fakes live in AutoAttackCombat_TestFakes.cs.
    /// </summary>
    [TestFixture]
    internal sealed class AutoAttackCombat_Resolution_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int CYCLE = 20;
        private const int LONG_WAIT = 60;
        private const int STREAK_TICKS = 30;
        private const int PARTIAL_TIMER_TICKS = 5;
        private const int MISS_TICKS_AFTER_READY = 5;
        private const int BRIEF_MISS_TICKS = 3;
        private const int RELEASE_MISS_TICKS = 2;
        private const int PROHIBITED_TICKS = 10;
        private const int FAILED_ATTEMPTS = 2;
        private const float TICK_DELTA = 0.05f;
        private const float RANGE = 3.0f;
        private const float OUT_OF_RANGE_DISTANCE = 3.5f;
        private const float IN_RANGE_DISTANCE = 2.0f;
        private const float NEAR_DISTANCE = 1.0f;
        private const float FAR_DISTANCE = 5.0f;
        private const float XZ_DISTANCE_WITH_Y_DIFFERENCE = 2.5f;
        private const float Y_DIFFERENCE = 5.0f;
        private const float POINT_BLANK_Y = 2.0f;
        private const float DIAGONAL_OFFSET = 1.5f;
        private const float SHORT_RANGE = 2.0f;
        private const float BEYOND_SHORT_RANGE_DISTANCE = 2.5f;
        private const float NEGATIVE_RANGE = -3.0f;
        private const int BASE_DAMAGE_A = 10;
        private const int BASE_DAMAGE_B = 25;
        private const int FAKE_FINAL_DAMAGE = 10;
        private const int STAT_ATTACK_POWER = 42;
        private const float STAT_ATTACK_RANGE = 2.5f;
        private const int ADAPTER_BASE_DAMAGE = 100;
        private const int ADAPTER_TARGET_MAX_HP = 99999;
        private const float ADAPTER_CRIT_CHANCE = 0.75f;
        private const float ADAPTER_CRIT_MULTIPLIER = 1.5f;
        private const float ROLL_GUARANTEED_CRIT = 0.0f;

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
            public bool MobAValid = true;

            public bool IsValidTarget(EntityID target)
            {
                return target != MobA || MobAValid;
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

        private ZoneSlotPool _pool;
        private FakeValidity _validity;
        private FakeAttackSpeed _attackSpeed;
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

        private static EntityID MobB => new EntityID(AutoAttackTestFakes.MOB_B_RAW);

        [SetUp]
        public void SetUp()
        {
            _nextClient = 11u;
            _tick = 0u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), MAX_PLAYERS, MAX_MOBS);
            _validity = new FakeValidity();
            _attackSpeed = new FakeAttackSpeed();
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
            return new AutoAttackCombatSystem(_pool, _validity, _attackSpeed, _state, _detector, _emitter, _stats, _calc, _applier);
        }

        private EntityID AddPlayer()
        {
            uint client = _nextClient++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        // Active on MobA with a 20-tick cycle, target 1.0 unit away (in range).
        private EntityID ReadyPlayer()
        {
            EntityID p = AddPlayer();
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            return p;
        }

        private void SetTargetDistance(float xz)
        {
            _state.TargetX = AutoAttackTestFakes.Raw(xz);
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

        private static void RunWarmSequence(AutoAttackCombatSystem combat, FakeZoneState state)
        {
            uint t = 0u;
            state.TargetX = AutoAttackTestFakes.Raw(NEAR_DISTANCE);
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);   // fire
            }

            state.TargetX = AutoAttackTestFakes.Raw(FAR_DISTANCE);
            for (int i = 0; i < CYCLE + STREAK_TICKS; i++)
            {
                combat.Run(++t, TICK_DELTA);   // ready, miss, repeated miss
            }

            state.TargetX = AutoAttackTestFakes.Raw(NEAR_DISTANCE);
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);   // fire again
            }
        }

        // A prohibited ready tick, a fire at a player target, then a fire whose calculation is rejected.
        private static void RunProhibitedPlayerRejectedSequence(
            AutoAttackCombatSystem combat, EntityID player, FakeDamageCalc calc, DamageResult accepted, DamageResult rejected)
        {
            uint t = 0u;
            calc.Result = accepted;
            combat.SetCombatProhibited(player, true);
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);   // ready tick is prohibited
            }

            combat.SetCombatProhibited(player, false);
            combat.Run(++t, TICK_DELTA);       // fires at the player target
            calc.Result = rejected;
            for (int i = 0; i < CYCLE; i++)
            {
                combat.Run(++t, TICK_DELTA);   // fires, calculation rejected
            }
        }

        // Rule 10 order: hit check, then damage, then reset.
        [Test]
        public void test_ready_in_range_emits_hit_calculates_applies_and_resets_timer()
        {
            EntityID p = ReadyPlayer();

            RunTicks(CYCLE);

            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(HitResult.Hit, _emitter.Results[0]);
            Assert.AreEqual(p, _emitter.Attackers[0]);
            Assert.AreEqual(MobA, _emitter.Targets[0]);
            Assert.AreEqual(1, _calc.Calls);
            Assert.AreEqual(BASE_DAMAGE_A, _calc.LastBaseDamage);
            Assert.AreEqual(p, _calc.LastAttacker);
            Assert.AreEqual(MobA, _calc.LastTarget);
            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual((uint)CYCLE, _applier.Ticks[0]);
            Assert.AreEqual(p, _applier.LastAttacker);
            Assert.AreEqual(MobA, _applier.LastTarget);
            Assert.AreEqual(FAKE_FINAL_DAMAGE, _applier.LastResult.FinalDamage);
            Assert.IsFalse(_applier.LastResult.IsKill);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        // Positions are decoded only for ready players.
        [Test]
        public void test_before_ready_no_position_lookup_stat_read_or_emit()
        {
            ReadyPlayer();

            RunTicks(CYCLE - 1);

            Assert.AreEqual(0, _state.PlayerLookups);
            Assert.AreEqual(0, _state.MobLookups);
            Assert.AreEqual(0, _stats.RangeReads);
            Assert.AreEqual(0, _emitter.Count);
        }

        // AC-09, AC-20, EC-6, EC-17: out of range, no damage, the timer keeps running, the first in-range tick fires.
        [Test]
        public void test_out_of_range_60_ticks_past_ready_no_damage_then_fires_on_first_in_range_tick()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);

            RunTicks(CYCLE + LONG_WAIT);

            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE + LONG_WAIT, _combat.GetActionTimer(p), "the timer keeps running");
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));

            SetTargetDistance(IN_RANGE_DISTANCE);
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual((uint)(CYCLE + LONG_WAIT + 1), _applier.Ticks[0]);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));

            RunTicks(CYCLE - 1);
            Assert.AreEqual(1, _applier.Count, "one auto fires, then a full new cycle");
        }

        // The out-of-range state is set only by a miss on a ready tick, never earlier in the cycle.
        [Test]
        public void test_out_of_range_state_is_set_only_by_a_ready_tick_miss()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);

            RunTicks(CYCLE - 1);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));

            RunTicks(1);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));
        }

        // AC-10: Y is ignored.
        [Test]
        public void test_y_difference_of_5_with_xz_2_5_hits()
        {
            ReadyPlayer();
            _state.TargetX = AutoAttackTestFakes.Raw(XZ_DISTANCE_WITH_Y_DIFFERENCE);
            _state.TargetY = AutoAttackTestFakes.Raw(Y_DIFFERENCE);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
        }

        // F-2: the range is inclusive; 3.0 hits, a hair beyond misses.
        [TestCase(300, true)]
        [TestCase(301, false)]
        public void test_range_boundary_exactly_3_0_hits_and_beyond_misses(short targetRaw, bool expectHit)
        {
            ReadyPlayer();
            _stats.AttackRange = RANGE;
            _state.TargetX = targetRaw;

            RunTicks(CYCLE);

            Assert.AreEqual(expectHit ? 1 : 0, _applier.Count);
            Assert.AreEqual(expectHit ? HitResult.Hit : HitResult.MissOutOfRange, _emitter.Results[0]);
        }

        // HitDetector would treat NaN as "skip the range check", +Infinity as unlimited and a negative range as its
        // absolute value (a hit at distance 1); the system sanitises all three to 0, so each is an out-of-range miss.
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(NEGATIVE_RANGE)]
        public void test_invalid_attack_range_is_a_fail_safe_out_of_range_miss(float badRange)
        {
            EntityID p = ReadyPlayer();
            _stats.AttackRange = badRange;

            Assert.DoesNotThrow(() => RunTicks(CYCLE));

            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[0]);
            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));
        }

        // Point-blank: same XZ never divides by ~0, and still hits and applies damage.
        [TestCase(0)]
        [TestCase(1)]
        public void test_same_xz_or_below_point_blank_epsilon_hits_and_applies_damage(short targetRaw)
        {
            ReadyPlayer();
            _state.TargetX = targetRaw;
            _state.TargetY = AutoAttackTestFakes.Raw(POINT_BLANK_Y);

            RunTicks(CYCLE);

            Assert.AreEqual(HitResult.Hit, _emitter.Results[0]);
            Assert.AreEqual(1, _applier.Count);
        }

        // Targets on every axis combination still hit when in range (forward direction is XZ only).
        [Test]
        public void test_diagonal_target_in_range_hits()
        {
            ReadyPlayer();
            _state.TargetX = AutoAttackTestFakes.Raw(-DIAGONAL_OFFSET);
            _state.TargetZ = AutoAttackTestFakes.Raw(DIAGONAL_OFFSET);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
        }

        // AC-11, EC-7
        [Test]
        public void test_prohibited_60_ticks_no_damage_timer_runs_and_fires_on_first_tick_after_clear()
        {
            EntityID p = ReadyPlayer();
            _combat.SetCombatProhibited(p, true);

            RunTicks(LONG_WAIT);

            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _emitter.Count);
            Assert.AreEqual(LONG_WAIT, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));

            _combat.SetCombatProhibited(p, false);
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual((uint)(LONG_WAIT + 1), _applier.Ticks[0]);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // EC-8: a clear in the request step of the ready tick lets that tick's check fire.
        [Test]
        public void test_prohibition_cleared_in_request_step_of_ready_tick_fires_that_tick()
        {
            EntityID p = ReadyPlayer();
            _combat.SetCombatProhibited(p, true);
            RunTicks(CYCLE - 1);
            Assert.AreEqual(0, _applier.Count);

            _combat.SetCombatProhibited(p, false);
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual((uint)CYCLE, _applier.Ticks[0]);
        }

        // Control for EC-8: a prohibition still set on the ready tick blocks it.
        [Test]
        public void test_prohibition_set_on_ready_tick_blocks_that_tick()
        {
            EntityID p = ReadyPlayer();
            RunTicks(CYCLE - 1);

            _combat.SetCombatProhibited(p, true);
            RunTicks(1);

            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE, _combat.GetActionTimer(p));
        }

        // Rule 10 Step 1: not a deactivation; the timer is not reset on exit.
        [Test]
        public void test_prohibition_keeps_target_toggle_and_timer_and_clear_does_not_reset_timer()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TIMER_TICKS);

            _combat.SetCombatProhibited(p, true);
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));
            Assert.AreEqual(MobA, _combat.GetTarget(p));
            Assert.AreEqual(PARTIAL_TIMER_TICKS, _combat.GetActionTimer(p));

            _combat.SetCombatProhibited(p, false);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(PARTIAL_TIMER_TICKS, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_prohibition_before_activation_shows_prohibited_on_activation_and_idle_stays_idle()
        {
            EntityID p = AddPlayer();
            _combat.SetCombatProhibited(p, true);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));

            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);

            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));
        }

        // The flag outlives a toggle cycle and a deselect/reselect: 0 = toggle OFF then ON, 1 = deselect then reselect.
        [TestCase(0)]
        [TestCase(1)]
        public void test_prohibition_survives_toggle_cycle_and_reselect(int transition)
        {
            EntityID p = ReadyPlayer();
            _combat.SetCombatProhibited(p, true);

            if (transition == 0)
            {
                _combat.SetAutoAttack(p, false);
                _combat.SetAutoAttack(p, true);
            }
            else
            {
                _combat.SetTarget(p, EntityID.Invalid);
                _combat.SetTarget(p, MobA);
            }

            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));
            RunTicks(CYCLE);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(0, _calc.Calls);
        }

        // Set and cleared while idle: the later activation is not prohibited and fires at ready.
        [Test]
        public void test_prohibition_cleared_while_idle_then_activation_fires_at_ready()
        {
            EntityID p = AddPlayer();
            _combat.SetCombatProhibited(p, true);
            _combat.SetCombatProhibited(p, false);

            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual((uint)CYCLE, _applier.Ticks[0]);
        }

        [Test]
        public void test_prohibition_clear_from_out_of_range_returns_to_combat_active()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));

            _combat.SetCombatProhibited(p, true);
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(p));
            _combat.SetCombatProhibited(p, false);

            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
        }

        // A miss streak survives a prohibition set/clear: the same miss is not re-sent after the clear.
        [Test]
        public void test_miss_streak_survives_prohibition_so_same_miss_is_not_resent_after_clear()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE);
            Assert.AreEqual(1, _emitter.Count);

            _combat.SetCombatProhibited(p, true);
            RunTicks(PROHIBITED_TICKS);
            _combat.SetCombatProhibited(p, false);
            RunTicks(PROHIBITED_TICKS);

            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));
        }

        [Test]
        public void test_prohibition_for_entity_without_slot_is_ignored()
        {
            EntityID p = ReadyPlayer();
            RunTicks(PARTIAL_TIMER_TICKS);
            var unknown = new EntityID(UNKNOWN_ENTITY_RAW);

            Assert.DoesNotThrow(() =>
            {
                _combat.SetCombatProhibited(EntityID.Invalid, true);
                _combat.SetCombatProhibited(unknown, true);
            });

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(EntityID.Invalid));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(unknown));
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            Assert.AreEqual(PARTIAL_TIMER_TICKS, _combat.GetActionTimer(p));
        }

        // AC-HD-12, AC-HD-13: a miss reaches neither damage calculation nor health.
        [Test]
        public void test_miss_never_calls_calculator_or_applier_and_keeps_timer()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);

            RunTicks(CYCLE + MISS_TICKS_AFTER_READY);

            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _stats.DamageReads, "base damage is not read for a miss");
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE + MISS_TICKS_AFTER_READY, _combat.GetActionTimer(p));
        }

        // AC-HD-11a: one Emit for 31 consecutive misses.
        [Test]
        public void test_30_out_of_range_ticks_emit_one_miss()
        {
            ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);

            RunTicks(CYCLE + STREAK_TICKS);

            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[0]);
        }

        // A hit ends the streak: miss, hit, then the same miss again emits again.
        [Test]
        public void test_hit_ends_miss_streak_so_same_miss_emits_again()
        {
            ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE + BRIEF_MISS_TICKS);
            SetTargetDistance(IN_RANGE_DISTANCE);
            RunTicks(1);
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE + BRIEF_MISS_TICKS);

            Assert.AreEqual(3, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[0]);
            Assert.AreEqual(HitResult.Hit, _emitter.Results[1]);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[2]);
        }

        // 0 = timer reset by a selection, 1 = new target, 2 = toggle OFF then ON.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void test_timer_reset_new_target_or_toggle_cycle_ends_streak_so_same_miss_emits_again(int cause)
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE + MISS_TICKS_AFTER_READY);
            Assert.AreEqual(1, _emitter.Count);

            if (cause == 0)
            {
                _combat.SetTarget(p, MobA);
            }
            else if (cause == 1)
            {
                _combat.SetTarget(p, MobB);
            }
            else
            {
                _combat.SetAutoAttack(p, false);
                _combat.SetAutoAttack(p, true);
            }

            RunTicks(CYCLE + MISS_TICKS_AFTER_READY);

            Assert.AreEqual(2, _emitter.Count);
            Assert.AreEqual(HitResult.MissOutOfRange, _emitter.Results[1]);
        }

        // Deactivation ends the streak: deselect, reselect the same target out of range.
        [Test]
        public void test_deactivation_ends_streak_and_idle_clears_out_of_range_state()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE + MISS_TICKS_AFTER_READY);
            Assert.AreEqual(AutoAttackState.OutOfRange, _combat.GetState(p));

            _combat.SetAutoAttack(p, false);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            _combat.SetAutoAttack(p, true);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p));
            RunTicks(CYCLE);

            Assert.AreEqual(2, _emitter.Count);
        }

        [Test]
        public void test_deselect_from_out_of_range_goes_idle()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE);

            _combat.SetTarget(p, EntityID.Invalid);

            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
        }

        // Every hit emits, including two in a row.
        [Test]
        public void test_every_hit_emits_including_two_in_a_row()
        {
            ReadyPlayer();

            RunTicks(CYCLE * 2);

            Assert.AreEqual(2, _emitter.Count);
            Assert.AreEqual(HitResult.Hit, _emitter.Results[0]);
            Assert.AreEqual(HitResult.Hit, _emitter.Results[1]);
            Assert.AreEqual(2, _applier.Count);
            Assert.AreEqual((uint)(CYCLE * 2), _applier.Ticks[1]);
        }

        // EC-10: the target is invalid on the ready tick.
        [Test]
        public void test_target_invalid_on_ready_tick_does_not_fire_emit_or_reset_timer()
        {
            EntityID p = ReadyPlayer();
            RunTicks(CYCLE - 1);
            _validity.MobAValid = false;

            RunTicks(1);

            Assert.AreEqual(0, _emitter.Count);
            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE - 1, _combat.GetActionTimer(p));
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
        }

        // EC-19: a second evaluation in the same tick does nothing.
        [Test]
        public void test_run_twice_in_ready_tick_fires_once()
        {
            EntityID p = ReadyPlayer();
            RunTicks(CYCLE - 1);

            _tick++;
            _applier.Tick = _tick;
            _combat.Run(_tick, TICK_DELTA);
            _combat.Run(_tick, TICK_DELTA);

            Assert.AreEqual(1, _calc.Calls);
            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_run_twice_in_same_tick_before_ready_advances_timer_once()
        {
            EntityID p = ReadyPlayer();

            _combat.Run(0u, TICK_DELTA);
            _combat.Run(0u, TICK_DELTA);

            Assert.AreEqual(1, _combat.GetActionTimer(p));
        }

        // Rule 10 Step 4 still resets for a rejected calculation; nothing is applied.
        [Test]
        public void test_rejected_damage_applies_nothing_but_timer_still_resets()
        {
            EntityID p = ReadyPlayer();
            _calc.Result = DamageResult.Rejected(DamageContext.PhysicalAuto);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _calc.Calls);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(1, _emitter.Count);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // The kill path is Story 004's; here a kill result still reaches the applier once, unchanged.
        [Test]
        public void test_kill_result_with_damage_reaches_applier_once()
        {
            ReadyPlayer();
            _calc.Result = AutoAttackTestFakes.Damage(FAKE_FINAL_DAMAGE, true);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
            Assert.IsTrue(_applier.LastResult.IsKill);
            Assert.AreEqual(FAKE_FINAL_DAMAGE, _applier.LastResult.FinalDamage);
        }

        // Retry: a failed position lookup does not fire, emit, reset or change state; the next tick tries again.
        // 0 = attacker position unknown, 1 = target position unknown.
        [TestCase(0)]
        [TestCase(1)]
        public void test_position_lookup_failure_keeps_timer_without_emit_and_retries_next_tick(int missing)
        {
            EntityID p = ReadyPlayer();
            AutoAttackState stateBefore = _combat.GetState(p);
            _state.AttackerKnown = missing != 0;
            _state.TargetKnown = missing != 1;

            RunTicks(CYCLE + MISS_TICKS_AFTER_READY);

            Assert.AreEqual(0, _emitter.Count);
            Assert.AreEqual(0, _calc.Calls);
            Assert.AreEqual(0, _applier.Count);
            Assert.AreEqual(CYCLE + MISS_TICKS_AFTER_READY, _combat.GetActionTimer(p));
            Assert.AreEqual(stateBefore, _combat.GetState(p), "the state is unchanged during the failure");

            _state.AttackerKnown = true;
            _state.TargetKnown = true;
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        // A player target is resolved when it is not a mob.
        [Test]
        public void test_player_target_position_is_used_when_target_is_not_a_mob()
        {
            EntityID p = AddPlayer();
            EntityID other = AddPlayer();
            _state.TargetPlayer = other;
            _state.TargetX = AutoAttackTestFakes.Raw(IN_RANGE_DISTANCE);
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, other);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(other, _applier.Targets[0]);
        }

        // BaseDamage and AttackRange are read per attempt, never cached.
        [Test]
        public void test_range_and_base_damage_are_read_on_every_attempt()
        {
            ReadyPlayer();
            SetTargetDistance(BEYOND_SHORT_RANGE_DISTANCE);
            _stats.AttackRange = SHORT_RANGE;
            RunTicks(CYCLE + FAILED_ATTEMPTS - 1);
            Assert.AreEqual(0, _applier.Count, "the target is beyond the short range");
            Assert.AreEqual(FAILED_ATTEMPTS, _stats.RangeReads);

            _stats.AttackRange = RANGE;
            _stats.BaseDamage = BASE_DAMAGE_B;
            RunTicks(1);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(BASE_DAMAGE_B, _calc.LastBaseDamage);
            Assert.AreEqual(FAILED_ATTEMPTS + 1, _stats.RangeReads, "one range read per attempt made");
        }

        [Test]
        public void test_two_players_resolve_independently()
        {
            EntityID a = AddPlayer();
            EntityID b = AddPlayer();
            _combat.SetAutoAttack(a, true);
            _combat.SetTarget(a, MobA);
            _combat.SetAutoAttack(b, true);
            _combat.SetTarget(b, MobB);
            _combat.SetCombatProhibited(b, true);

            RunTicks(CYCLE);

            Assert.AreEqual(1, _applier.Count);
            Assert.AreEqual(a, _applier.Attackers[0]);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(a));
            Assert.AreEqual(AutoAttackState.Prohibited, _combat.GetState(b));
            Assert.AreEqual(CYCLE, _combat.GetActionTimer(b));
        }

        // ReleasePlayer clears the streak, the lock and the last-run tick.
        [Test]
        public void test_release_clears_streak_lock_and_last_run_tick()
        {
            EntityID p = ReadyPlayer();
            SetTargetDistance(OUT_OF_RANGE_DISTANCE);
            RunTicks(CYCLE + RELEASE_MISS_TICKS);
            Assert.AreEqual(1, _emitter.Count);
            _combat.SetCombatProhibited(p, true);

            _combat.ReleasePlayer(p);
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            Assert.AreEqual(AutoAttackState.CombatActive, _combat.GetState(p), "the lock was cleared");

            _combat.Run(_tick, TICK_DELTA);   // the tick the old occupant last ran
            Assert.AreEqual(1, _combat.GetActionTimer(p), "the last-run tick was cleared");

            RunTicks(CYCLE);
            Assert.AreEqual(2, _emitter.Count, "the streak was cleared");
        }

        // The sentinel cannot collide with tick 0.
        [Test]
        public void test_first_run_at_tick_zero_advances_timer()
        {
            EntityID p = ReadyPlayer();

            _combat.Run(0u, TICK_DELTA);

            Assert.AreEqual(1, _combat.GetActionTimer(p));
        }

        // The constructor rejects a null at each position.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void test_ctor_null_argument_throws(int nullIndex)
        {
            Assert.Throws<ArgumentNullException>(() => new AutoAttackCombatSystem(
                nullIndex == 0 ? null : _pool,
                nullIndex == 1 ? null : _validity,
                nullIndex == 2 ? null : _attackSpeed,
                nullIndex == 3 ? null : _state,
                nullIndex == 4 ? null : _detector,
                nullIndex == 5 ? null : _emitter,
                nullIndex == 6 ? null : _stats,
                nullIndex == 7 ? null : _calc,
                nullIndex == 8 ? null : _applier));
        }

        [Test]
        public void test_character_stats_stat_source_null_stats_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsAutoAttackStatSource(null));
        }

        [Test]
        public void test_character_stats_stat_source_reads_attack_power_and_range()
        {
            var stats = CharacterStatsFixture.Create();
            stats.SetBaseStat(CharacterStatsFixture.PlayerEntityId, StatID.AttackPower, STAT_ATTACK_POWER);
            stats.SetBaseStatFloat(CharacterStatsFixture.PlayerEntityId, StatID.AttackRange, STAT_ATTACK_RANGE);
            var source = new CharacterStatsAutoAttackStatSource(stats);

            Assert.AreEqual(STAT_ATTACK_POWER, source.GetBaseDamage(CharacterStatsFixture.PlayerEntityId));
            Assert.AreEqual(STAT_ATTACK_RANGE, source.GetAttackRange(CharacterStatsFixture.PlayerEntityId), 0f);
        }

        [Test]
        public void test_damage_calculator_adapter_null_calculator_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new DamageCalculatorAutoAttackAdapter(null));
        }

        // The real DamageCalculator behind the adapter: one crit draw per call, PhysicalAuto, same result as a direct call.
        // The DamageCalculator construction is copied from DamageCalculation_CriticalStrike_tests.cs.
        [Test]
        public void test_damage_calculator_adapter_draws_once_and_matches_direct_physical_auto_calculate()
        {
            var stats = CharacterStatsFixture.CreateWithLeveling(new AllPlayersLevelingService());
            EntityID attacker = CharacterStatsFixture.PlayerEntityId;
            EntityID target = CharacterStatsFixture.MobEntityId;
            stats.SetBaseStat(target, StatID.MaxHP, ADAPTER_TARGET_MAX_HP);
            stats.SetCurrentHP(target, ADAPTER_TARGET_MAX_HP);
            stats.SetBaseStatFloat(attacker, StatID.CritChance, ADAPTER_CRIT_CHANCE);
            stats.SetBaseStatFloat(attacker, StatID.CritMultiplier, ADAPTER_CRIT_MULTIPLIER);
            var random = new ScriptedRandomProvider();
            var calculator = new DamageCalculator(
                stats,
                new FakeEquippedWeaponQuery(),
                new CountingItemDatabase(),
                new EnhancementBonusProvider(EnhancementConfig.Default),
                DamageCalculationConfig.Default,
                random);
            var adapter = new DamageCalculatorAutoAttackAdapter(calculator);

            random.EnqueueFloat(ROLL_GUARANTEED_CRIT);
            DamageResult viaAdapter = adapter.Calculate(ADAPTER_BASE_DAMAGE, attacker, target);
            Assert.AreEqual(1, random.DrawCount, "exactly one draw per Calculate");

            random.EnqueueFloat(ROLL_GUARANTEED_CRIT);
            DamageResult direct = calculator.Calculate(ADAPTER_BASE_DAMAGE, attacker, target, DamageContext.PhysicalAuto);

            Assert.AreEqual(DamageContext.PhysicalAuto, viaAdapter.DamageContext);
            Assert.IsTrue(viaAdapter.IsCrit, "the scripted roll is a guaranteed crit");
            Assert.Greater(viaAdapter.FinalDamage, 0);
            Assert.AreEqual(direct.FinalDamage, viaAdapter.FinalDamage);
            Assert.AreEqual(direct.PhysicalDamage, viaAdapter.PhysicalDamage);
            Assert.AreEqual(direct.ElementalDamage, viaAdapter.ElementalDamage);
            Assert.AreEqual(direct.IsCrit, viaAdapter.IsCrit);
            Assert.AreEqual(direct.IsKill, viaAdapter.IsKill);
            Assert.AreEqual(direct.DamageContext, viaAdapter.DamageContext);
        }

        // Forbidden: per-tick allocation. A fire, a miss with a repeated miss, and a second fire.
        [Test]
        public void test_run_across_fire_and_miss_allocates_nothing()
        {
            EntityID p = AddPlayer();
            var warm = NewCombat();
            warm.SetAutoAttack(p, true);
            warm.SetTarget(p, MobA);
            RunWarmSequence(warm, _state);   // JIT warm-up outside the measured block

            var combat = NewCombat();
            combat.SetAutoAttack(p, true);
            combat.SetTarget(p, MobA);
            int emitsBefore = _emitter.Count;
            int appliesBefore = _applier.Count;

            Assert.That(() => RunWarmSequence(combat, _state), Is.Not.AllocatingGCMemory());

            Assert.AreEqual(3, _emitter.Count - emitsBefore, "hit, one miss, hit");
            Assert.AreEqual(2, _applier.Count - appliesBefore);
        }

        // Forbidden: per-tick allocation on the prohibited ready tick, the player-target fallback lookup and a rejected result.
        [Test]
        public void test_run_across_prohibited_player_target_and_rejected_result_allocates_nothing()
        {
            EntityID p = AddPlayer();
            EntityID other = AddPlayer();
            _state.TargetPlayer = other;
            DamageResult accepted = AutoAttackTestFakes.Damage(FAKE_FINAL_DAMAGE, false);
            DamageResult rejected = DamageResult.Rejected(DamageContext.PhysicalAuto);
            var warm = NewCombat();
            warm.SetAutoAttack(p, true);
            warm.SetTarget(p, other);
            RunProhibitedPlayerRejectedSequence(warm, p, _calc, accepted, rejected);   // JIT warm-up outside the measured block

            var combat = NewCombat();
            combat.SetAutoAttack(p, true);
            combat.SetTarget(p, other);
            int emitsBefore = _emitter.Count;
            int appliesBefore = _applier.Count;
            int callsBefore = _calc.Calls;
            int playerLookupsBefore = _state.PlayerLookups;

            Assert.That(() => RunProhibitedPlayerRejectedSequence(combat, p, _calc, accepted, rejected), Is.Not.AllocatingGCMemory());

            Assert.AreEqual(2, _emitter.Count - emitsBefore, "two hits; the prohibited ready tick emits nothing");
            Assert.AreEqual(2, _calc.Calls - callsBefore, "an accepted and a rejected calculation");
            Assert.AreEqual(1, _applier.Count - appliesBefore, "the rejected result is not applied");
            Assert.Greater(_state.PlayerLookups - playerLookupsBefore, 0, "the player-target fallback ran");
        }
    }
}

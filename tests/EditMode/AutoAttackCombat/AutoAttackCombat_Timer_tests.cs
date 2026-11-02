using System;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.HitDetection;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.AutoAttackCombat
{
    /// <summary>
    /// EditMode tests for Auto-Attack Combat Story 002: tick timer and cycle ticks (Rules 5-7, F-1, EC-11, EC-15,
    /// EC-23; AC-01 to AC-03; TR-aa-002).
    /// </summary>
    /// <remarks>
    /// Since Story 003 the timer resets only on an actual fire; the default fakes put the target in range, so a ready
    /// player fires and resets as before.
    /// </remarks>
    [TestFixture]
    internal sealed class AutoAttackCombat_Timer_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint UNKNOWN_ENTITY_RAW = 900000u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const int ALLOC_PLAYERS = 50;
        private const int EVENT_CAPACITY = 1024;
        private const int AC01_TICKS = 200;
        private const int AC01_EVENTS = 10;
        private const int AC01_CYCLE = 20;
        private const int AC02_TICKS = 50;
        private const int AC02_EVENTS = 5;
        private const int AC02_CYCLE = 10;
        private const int CLAMPED_CYCLE = 40;
        private const int IDLE_TICKS = 100;
        private const int CHANGE_AT_TICK = 5;
        private const int FRACTIONAL_CYCLE = 16;
        private const int ALLOC_TICKS = 25;
        private const float DOUBLE_SPEED = 2.0f;
        private const float FRACTIONAL_SPEED = 1.3f;

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
                return MobAValid;
            }
        }

        // One multiplier for everyone, except OverrideEntity, which reads OverrideMultiplier.
        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float Multiplier = 1.0f;
            public EntityID OverrideEntity = EntityID.Invalid;
            public float OverrideMultiplier = 1.0f;

            public float GetAttackSpeedMultiplier(EntityID entity)
            {
                return entity == OverrideEntity ? OverrideMultiplier : Multiplier;
            }
        }

        private ZoneSlotPool _pool;
        private FakeValidity _validity;
        private FakeAttackSpeed _attackSpeed;
        private FakeZoneState _positions;
        private FakeHitEmitter _emitter;
        private FakeStatSource _stats;
        private FakeDamageCalc _calc;
        private FakeDamageApplier _sink;
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
            _attackSpeed = new FakeAttackSpeed();
            _positions = new FakeZoneState();
            _emitter = new FakeHitEmitter();
            _stats = new FakeStatSource();
            _calc = new FakeDamageCalc();
            _sink = new FakeDamageApplier();
            _combat = new AutoAttackCombatSystem(
                _pool, _validity, _attackSpeed, _positions, new HitDetector(HitDetectionConfig.Default), _emitter, _stats, _calc, _sink,
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), default(ZoneID), new FakeFaceOutput());
        }

        private EntityID AddPlayer(ZoneSlotPool pool)
        {
            uint client = _nextClient++;
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(client), new AccountID(client + 1000u), client, out _, out EntityID id));
            return id;
        }

        private EntityID ActivePlayer(float multiplier)
        {
            _attackSpeed.Multiplier = multiplier;
            EntityID p = AddPlayer(_pool);
            _combat.SetAutoAttack(p, true);
            _combat.SetTarget(p, MobA);
            return p;
        }

        // Runs n ticks with a strictly increasing tick number starting at 1.
        private void RunTicks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _tick++;
                _sink.Tick = _tick;
                _combat.Run(_tick, 0.05f);
            }
        }

        private void AssertDeltas(int expectedCount, int expectedDelta)
        {
            Assert.AreEqual(expectedCount, _sink.Count);
            for (int i = 0; i < expectedCount; i++)
            {
                uint previous = i == 0 ? 0u : _sink.Ticks[i - 1];
                Assert.AreEqual((uint)expectedDelta, _sink.Ticks[i] - previous, "event " + i);
            }
        }

        // AC-01.
        [Test]
        public void test_multiplier_1_over_200_ticks_gives_10_ready_events_20_ticks_apart()
        {
            ActivePlayer(1.0f);

            RunTicks(AC01_TICKS);

            AssertDeltas(AC01_EVENTS, AC01_CYCLE);
        }

        // AC-02.
        [Test]
        public void test_multiplier_2_over_50_ticks_gives_5_ready_events_10_ticks_apart()
        {
            ActivePlayer(DOUBLE_SPEED);

            RunTicks(AC02_TICKS);

            AssertDeltas(AC02_EVENTS, AC02_CYCLE);
        }

        // AC-03: below-minimum, zero, negative and exactly-minimum multipliers all give a 40-tick cycle.
        [TestCase(0.0f)]
        [TestCase(-1.0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        public void test_low_multiplier_clamps_to_40_tick_cycle_without_log(float multiplier)
        {
            EntityID p = ActivePlayer(multiplier);
            Assert.AreEqual(CLAMPED_CYCLE, _combat.GetCycleTicks(p));

            RunTicks(CLAMPED_CYCLE - 1);
            Assert.AreEqual(0, _sink.Count);
            RunTicks(1);

            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual((uint)CLAMPED_CYCLE, _sink.Ticks[0]);
            LogAssert.NoUnexpectedReceived();
        }

        // F-1 rounding: ceil(TICK_RATE_HZ / max(m, 0.5)); the boundary values are the point.
        [TestCase(1.3f, 16)]
        [TestCase(1.0f, 20)]
        [TestCase(2.0f, 10)]
        [TestCase(0.5f, 40)]
        [TestCase(1.25f, 16)]
        [TestCase(float.NaN, 40)]
        [TestCase(float.PositiveInfinity, 1)]
        public void test_compute_cycle_ticks_rounds_up_and_clamps(float multiplier, int expected)
        {
            Assert.AreEqual(expected, AutoAttackCycle.ComputeCycleTicks(multiplier));
        }

        [Test]
        public void test_fractional_cycle_never_fires_early()
        {
            ActivePlayer(FRACTIONAL_SPEED);

            RunTicks(FRACTIONAL_CYCLE - 1);
            Assert.AreEqual(0, _sink.Count);
            RunTicks(1);

            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual((uint)FRACTIONAL_CYCLE, _sink.Ticks[0]);
        }

        // Rule 7, EC-23: the timer counts Run calls only; deltaTime and currentTick are irrelevant.
        [Test]
        public void test_timer_advances_exactly_one_per_run_regardless_of_delta_time_and_tick_number()
        {
            EntityID p = ActivePlayer(1.0f);

            _combat.Run(0u, 0f);
            Assert.AreEqual(1, _combat.GetActionTimer(p));
            _combat.Run(5u, 10f);
            Assert.AreEqual(2, _combat.GetActionTimer(p));
            _combat.Run(1u, -3f);
            Assert.AreEqual(3, _combat.GetActionTimer(p));
            _combat.Run(uint.MaxValue, 1000f);
            Assert.AreEqual(4, _combat.GetActionTimer(p));
        }

        // EC-15: a change mid-cycle applies from the next cycle.
        [Test]
        public void test_multiplier_change_mid_cycle_applies_from_next_cycle()
        {
            EntityID p = ActivePlayer(1.0f);

            RunTicks(CHANGE_AT_TICK);
            _attackSpeed.Multiplier = DOUBLE_SPEED;
            Assert.AreEqual(AC01_CYCLE, _combat.GetCycleTicks(p));
            RunTicks(AC01_CYCLE - CHANGE_AT_TICK);

            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual((uint)AC01_CYCLE, _sink.Ticks[0]);
            Assert.AreEqual(AC02_CYCLE, _combat.GetCycleTicks(p));

            RunTicks(AC02_CYCLE);
            Assert.AreEqual(2, _sink.Count);
            Assert.AreEqual((uint)(AC01_CYCLE + AC02_CYCLE), _sink.Ticks[1]);
        }

        // EC-11: a drop to exactly 0.5 mid-cycle leaves the current 20-tick cycle; the next is 40.
        [Test]
        public void test_multiplier_change_to_minimum_mid_cycle_keeps_current_cycle_then_40()
        {
            ActivePlayer(1.0f);

            RunTicks(CHANGE_AT_TICK);
            _attackSpeed.Multiplier = AutoAttackCycle.MIN_ATTACK_SPEED_MULTIPLIER;
            RunTicks(AC01_CYCLE - CHANGE_AT_TICK);
            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual((uint)AC01_CYCLE, _sink.Ticks[0]);

            RunTicks(CLAMPED_CYCLE - 1);
            Assert.AreEqual(1, _sink.Count);
            RunTicks(1);
            Assert.AreEqual(2, _sink.Count);
            Assert.AreEqual((uint)(AC01_CYCLE + CLAMPED_CYCLE), _sink.Ticks[1]);
        }

        // Story 003: a ready auto that cannot fire keeps counting past the cycle and resets only on the fire.
        [Test]
        public void test_ready_but_out_of_range_keeps_counting_and_resets_only_on_fire()
        {
            EntityID p = ActivePlayer(1.0f);
            _positions.TargetX = AutoAttackTestFakes.Raw(9f);

            RunTicks(AC01_CYCLE + AC02_TICKS);
            Assert.AreEqual(0, _sink.Count);
            Assert.AreEqual(AC01_CYCLE + AC02_TICKS, _combat.GetActionTimer(p));

            _positions.TargetX = AutoAttackTestFakes.Raw(1f);
            RunTicks(1);

            Assert.AreEqual(1, _sink.Count);
            Assert.AreEqual((uint)(AC01_CYCLE + AC02_TICKS + 1), _sink.Ticks[0]);
            Assert.AreEqual(0, _combat.GetActionTimer(p));
        }

        [Test]
        public void test_idle_player_never_readies()
        {
            EntityID p = AddPlayer(_pool);
            _combat.SetTarget(p, MobA);   // toggle OFF: stays idle

            RunTicks(IDLE_TICKS);

            Assert.AreEqual(0, _sink.Count);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
        }

        [Test]
        public void test_target_lost_by_polling_never_readies()
        {
            EntityID p = ActivePlayer(1.0f);
            RunTicks(AC01_CYCLE - 1);
            _validity.MobAValid = false;

            RunTicks(IDLE_TICKS);

            Assert.AreEqual(0, _sink.Count);
            Assert.AreEqual(AutoAttackState.Idle, _combat.GetState(p));
            Assert.AreEqual(AC01_CYCLE - 1, _combat.GetActionTimer(p), "the timer stays frozen");
        }

        // Rule 3, EC-15: a new selection mid-cycle restarts the cycle with a fresh sample.
        [Test]
        public void test_set_target_mid_cycle_resamples_cycle_and_resets_timer()
        {
            EntityID p = ActivePlayer(1.0f);
            RunTicks(CHANGE_AT_TICK);
            _attackSpeed.Multiplier = DOUBLE_SPEED;

            _combat.SetTarget(p, MobA);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AC02_CYCLE, _combat.GetCycleTicks(p));
        }

        // Rule 2, EC-15: toggle OFF then ON restarts the cycle with a fresh sample.
        [Test]
        public void test_toggle_off_then_on_resamples_cycle_and_resets_timer()
        {
            EntityID p = ActivePlayer(1.0f);
            RunTicks(CHANGE_AT_TICK);
            _combat.SetAutoAttack(p, false);
            _attackSpeed.Multiplier = DOUBLE_SPEED;
            Assert.AreEqual(AC01_CYCLE, _combat.GetCycleTicks(p), "no sample while OFF");

            _combat.SetAutoAttack(p, true);

            Assert.AreEqual(0, _combat.GetActionTimer(p));
            Assert.AreEqual(AC02_CYCLE, _combat.GetCycleTicks(p));
        }

        // Each player's multiplier is sampled for that player, and the ready event carries the right attacker.
        [Test]
        public void test_two_players_with_different_multipliers_get_their_own_cycles_and_ready_events()
        {
            EntityID slow = AddPlayer(_pool);
            EntityID fast = AddPlayer(_pool);
            _attackSpeed.OverrideEntity = fast;
            _attackSpeed.OverrideMultiplier = DOUBLE_SPEED;
            foreach (EntityID p in new[] { slow, fast })
            {
                _combat.SetAutoAttack(p, true);
                _combat.SetTarget(p, MobA);
            }

            RunTicks(AC01_CYCLE);

            Assert.AreEqual(AC01_CYCLE, _combat.GetCycleTicks(slow));
            Assert.AreEqual(AC02_CYCLE, _combat.GetCycleTicks(fast));
            Assert.AreEqual(3, _sink.Count, "fast readies at 10 and 20, slow at 20");
            Assert.AreEqual(fast, _sink.Attackers[0]);
            Assert.AreEqual((uint)AC02_CYCLE, _sink.Ticks[0]);
            Assert.AreEqual((uint)AC01_CYCLE, _sink.Ticks[1]);
            Assert.AreEqual((uint)AC01_CYCLE, _sink.Ticks[2]);
            Assert.That(new[] { _sink.Attackers[1], _sink.Attackers[2] }, Is.EquivalentTo(new[] { slow, fast }));
        }

        [Test]
        public void test_get_cycle_ticks_is_zero_before_activation_and_for_unknown_entity()
        {
            EntityID p = AddPlayer(_pool);

            Assert.AreEqual(0, _combat.GetCycleTicks(p));
            Assert.AreEqual(0, _combat.GetCycleTicks(new EntityID(UNKNOWN_ENTITY_RAW)));
        }

        [Test]
        public void test_character_stats_source_null_stats_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CharacterStatsAttackSpeedSource(null));
        }

        // The float read, not the flooring int read; the raw value passes through unclamped.
        [Test]
        public void test_character_stats_source_reads_fractional_multiplier_unfloored()
        {
            var stats = CharacterStatsFixture.Create();
            stats.SetBaseStatFloat(CharacterStatsFixture.PlayerEntityId, StatID.AttackSpeedMultiplier, FRACTIONAL_SPEED);
            var source = new CharacterStatsAttackSpeedSource(stats);

            Assert.AreEqual(FRACTIONAL_SPEED, source.GetAttackSpeedMultiplier(CharacterStatsFixture.PlayerEntityId), 0f);
        }

        // An entity with no stats reads 0, which the cycle clamps to the slowest value.
        [Test]
        public void test_character_stats_source_unknown_entity_reads_zero_and_slowest_cycle()
        {
            var source = new CharacterStatsAttackSpeedSource(CharacterStatsFixture.Create());

            float m = source.GetAttackSpeedMultiplier(CharacterStatsFixture.MobEntityId);

            Assert.AreEqual(0f, m);
            Assert.AreEqual(CLAMPED_CYCLE, AutoAttackCycle.ComputeCycleTicks(m));
        }

        [Test]
        public void test_release_clears_cycle_ticks()
        {
            EntityID p = ActivePlayer(1.0f);
            Assert.AreEqual(AC01_CYCLE, _combat.GetCycleTicks(p));

            _combat.ReleasePlayer(p);

            Assert.AreEqual(0, _combat.GetCycleTicks(p));
        }

        [Test]
        public void test_run_with_50_active_players_across_a_fire_allocates_nothing()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakePoolListener(), ALLOC_PLAYERS, MAX_MOBS);
            var validity = new FakeValidity();
            var attackSpeed = new FakeAttackSpeed();
            var sink = new FakeDamageApplier();
            var combat = new AutoAttackCombatSystem(
                pool, validity, attackSpeed, new FakeZoneState(), new HitDetector(HitDetectionConfig.Default),
                new FakeHitEmitter(), new FakeStatSource(), new FakeDamageCalc(), sink,
                new FakeXpAward(), new FakeExperienceSink(), new FakeMobTypeSource(), default(ZoneID), new FakeFaceOutput());
            for (int i = 0; i < ALLOC_PLAYERS; i++)
            {
                EntityID p = AddPlayer(pool);
                combat.SetAutoAttack(p, true);
                combat.SetTarget(p, MobA);
            }

            // Warm-up outside the measured block, so first-call JIT cannot count as an allocation.
            combat.Run(1u, 0.05f);

            Assert.That(() =>
            {
                for (uint t = 2u; t <= ALLOC_TICKS; t++)
                {
                    combat.Run(t, 0.05f);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(ALLOC_PLAYERS, sink.Count, "every player crossed exactly one ready");
        }
    }
}

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode unit tests for Enemy AI Story 005: attack cycle (CR-AI-4, CR-AI-6, CR-AI-8, CR-AI-11, F-AI-1, EC-AI-3,
    /// EC-AI-8, AC-AI-3 to AC-AI-6, AC-AI-18, AC-AI-20b, AC-AI-25). AC-AI-20a (F-AI-1 = 22 / 18) is covered by the
    /// Story 001 MobDefinition test of EffectiveAttackIntervalTicks and is not duplicated here.
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_AttackCycle_Tests
    {
        private const float ATTACK_RANGE = 2f;
        private static readonly EntityID PLAYER_A = new EntityID(101u);
        private static readonly Vector3 FAR = new Vector3(5f, 0f, 0f);
        private static readonly Vector3 NEAR_X = new Vector3(1.5f, 0f, 0f);
        private static readonly Vector3 NEAR_Z = new Vector3(0f, 0f, 1.5f);

        private sealed class LoggingNav : INavigationProvider
        {
            public readonly List<string> Log = new List<string>();
            public bool Stale;

            public void SetDestination(EntityID agent, Vector3 target) { Log.Add("SetDestination"); LastDestination = target; }
            public void SetSpeed(EntityID agent, float speed) { Log.Add("SetSpeed"); }
            public void Stop(EntityID agent) { Log.Add("Stop"); }
            public void Resume(EntityID agent) { Log.Add("Resume"); }
            public bool IsPathStale(EntityID agent) { Log.Add("IsPathStale"); return Stale; }
            public Vector3 GetCurrentVelocity(EntityID agent) { Log.Add("GetCurrentVelocity"); return Vector3.zero; }

            public Vector3 LastDestination;

            public int Count(string call)
            {
                int n = 0;
                for (int i = 0; i < Log.Count; i++)
                {
                    if (Log[i] == call)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private sealed class RecordingResolver : IMobAttackResolver
        {
            public int Calls;
            public long Clock;
            public readonly List<long> Ticks = new List<long>();
            public EntityID LastMob;
            public EntityID LastTarget;
            public Vector3 LastMobPos;
            public Vector3 LastTargetPos;
            public float LastRange;
            public Vector3 LastForward;

            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
            {
                Calls++;
                Ticks.Add(Clock);
                LastMob = mob;
                LastTarget = target;
                LastMobPos = mobPos;
                LastTargetPos = targetPos;
                LastRange = attackRange;
                LastForward = capturedForward;
            }
        }

        private sealed class ThrowingResolver : IMobAttackResolver
        {
            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
            {
                throw new InvalidOperationException("resolver failure");
            }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public float GetCurrentHP(EntityID entityId) => 100f;
            public float GetCurrentMP(EntityID entityId) => 0f;
            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class FakeCandidates : IAggroCandidateSource
        {
            public Vector3 Position;
            public bool PositionAvailable = true;

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)
            {
                into.Add((PLAYER_A, Position));
            }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                position = Position;
                return PositionAvailable && id.Equals(PLAYER_A);
            }
        }

        private LoggingNav _nav;
        private RecordingResolver _resolver;
        private FakeCandidates _candidates;
        private MobController _mob;
        private uint _tick;

        private static int N => EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT;
        private static float Threshold => EnemyAIConfigValues.PURSUIT_REPOSITION_THRESHOLD_DEFAULT;

        private static MobDefinition Definition(int windUp, int recovery, int cooldown)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: 10,
                baseDefense: 5, attackRange: ATTACK_RANGE, moveSpeed: 3.5f, aggroRange: 8f, leashRange: 20f,
                attackWindUpTicks: windUp, attackRecoveryTicks: recovery, attackCooldownTicks: cooldown, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
        }

        [SetUp]
        public void SetUp()
        {
            _nav = new LoggingNav();
            _resolver = new RecordingResolver();
            _candidates = new FakeCandidates { Position = FAR };
            _tick = 0;
            Build(12, 6, 10);
        }

        private void Build(int windUp, int recovery, int cooldown)
        {
            _mob = new MobController(new EntityID(7u), Definition(windUp, recovery, cooldown), Vector3.zero, _nav,
                new FakeStats(), _resolver, EnemyAIConfigValues.Defaults, NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, _candidates);
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _tick++;
                _resolver.Clock = _tick;
                _mob.Tick(_tick);
            }
        }

        private void TickTo(uint tick)
        {
            while (_tick < tick)
            {
                Tick(1);
            }
        }

        private void AggroAtFar()
        {
            _candidates.Position = FAR;
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "precondition: aggro, out of attack range");
        }

        /// <summary>Aggros out of range, then moves the target to <paramref name="inRange"/> and ticks once. Returns the entry tick E.</summary>
        private uint StartWindUp(Vector3 inRange)
        {
            AggroAtFar();
            _candidates.Position = inRange;
            Tick(1);
            Assert.AreEqual(MobState.WindingUp, _mob.State, "precondition: WindingUp entered");
            return _tick;
        }

        /// <summary>Runs a full wind-up from E and returns the attack tick X (state AttackExecution after it).</summary>
        private uint RunToAttackTick(Vector3 inRange, int windUp)
        {
            uint e = StartWindUp(inRange);
            uint x = e + (uint)windUp;
            TickTo(x);
            Assert.AreEqual(MobState.AttackExecution, _mob.State, "precondition: attack tick reached");
            return x;
        }

        private static void AssertVec(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 0.001f);
            Assert.AreEqual(expected.y, actual.y, 0.001f);
            Assert.AreEqual(expected.z, actual.z, 0.001f);
        }

        // 1
        [Test]
        public void test_attack_first_attack_in_range_enters_winding_up_with_last_tick_minus_one()
        {
            StartWindUp(NEAR_X);

            Assert.IsFalse(_mob.HasAttackedOnce);
            Assert.AreEqual(-1L, _mob.LastAttackExecutionTick);
            Tick(11);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(-1L, _mob.LastAttackExecutionTick);
        }

        // 2
        [Test]
        public void test_attack_cooldown_gate_blocks_until_cooldown_ticks_after_last_execution()
        {
            _candidates.Position = NEAR_X;
            _mob.ForceStateForTest(MobState.Pursuing);
            _mob.TargetEntityID = PLAYER_A;
            _mob.HasAttackedOnce = true;
            _mob.LastAttackExecutionTick = 100;
            _tick = 108;

            Tick(1);
            Assert.AreEqual(109u, _tick);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "T + C - 1 stays Pursuing");

            Tick(1);
            Assert.AreEqual(110u, _tick);
            Assert.AreEqual(MobState.WindingUp, _mob.State, "T + C enters WindingUp");
        }

        // 3
        [TestCase(2f, 0f, 0f, true)]
        [TestCase(2.01f, 0f, 0f, false)]
        [TestCase(1f, 50f, 0f, true)]
        [TestCase(1f, -50f, 1f, true)]
        [TestCase(0f, 0f, 2f, true)]
        [TestCase(0f, 0f, 2.01f, false)]
        public void test_attack_range_boundary_inclusive_xz_only(float x, float y, float z, bool expectWindUp)
        {
            _candidates.Position = new Vector3(x, y, z);
            _mob.ForceStateForTest(MobState.Pursuing);
            _mob.TargetEntityID = PLAYER_A;

            Tick(1);

            Assert.AreEqual(expectWindUp ? MobState.WindingUp : MobState.Pursuing, _mob.State);
        }

        // 4
        [Test]
        public void test_attack_pursuing_to_winding_up_calls_stop_once_and_no_set_destination()
        {
            AggroAtFar();
            _nav.Log.Clear();
            _candidates.Position = NEAR_X;

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            CollectionAssert.AreEqual(new[] { "Stop" }, _nav.Log);
        }

        // 5
        [Test]
        public void test_attack_captured_forward_uses_facing_written_on_entry_tick()
        {
            AggroAtFar();
            Assert.AreEqual(900, _mob.FacingAngle, "facing toward +X from the aggro tick");
            _candidates.Position = NEAR_Z;

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(0, _mob.FacingAngle, "facing refreshed on the entry tick, before capture");
            AssertVec(MobController.FacingToForward((short)_mob.FacingAngle), _mob.CapturedForward);
            AssertVec(new Vector3(0f, 0f, 1f), _mob.CapturedForward);
        }

        // 6
        [Test]
        public void test_attack_captured_forward_survives_target_moving_ninety_degrees_after_entry()
        {
            uint e = StartWindUp(NEAR_X);
            _candidates.Position = NEAR_Z;
            TickTo(e + 12);

            Assert.AreEqual(1, _resolver.Calls);
            AssertVec(new Vector3(1f, 0f, 0f), _resolver.LastForward);
        }

        // 7
        [Test]
        public void test_attack_facing_unchanged_through_wind_up_execution_and_recovering()
        {
            uint e = StartWindUp(NEAR_X);
            _nav.Log.Clear();
            int captured = _mob.FacingAngle;
            uint lastRecoveringTick = e + 12 + 5;

            for (uint t = e + 1; t <= lastRecoveringTick; t++)
            {
                _candidates.Position = (t % 2u == 0u) ? NEAR_Z : NEAR_X;
                TickTo(t);
                Assert.AreEqual(captured, _mob.FacingAngle, "tick " + t);
            }

            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(0, _nav.Log.Count, "no nav calls during wind-up, execution and Recovering countdown");
        }

        // 8
        [Test]
        public void test_attack_wind_up_of_one_tick_gives_one_execution_tick_then_recovering()
        {
            Build(1, 6, 10);
            uint e = StartWindUp(NEAR_X);

            Tick(1);
            Assert.AreEqual(e + 1, _tick);
            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(1, _resolver.Calls);

            Tick(1);
            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(1, _resolver.Calls, "no second resolve");
        }

        // 9
        [Test]
        public void test_attack_resolves_on_entry_tick_plus_wind_up_with_expected_arguments()
        {
            uint e = StartWindUp(NEAR_X);
            TickTo(e + 11);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(0, _resolver.Calls);

            Tick(1);

            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(1, _resolver.Calls);
            Assert.AreEqual(e + 12, (uint)_resolver.Ticks[0]);
            Assert.AreEqual(new EntityID(7u), _resolver.LastMob);
            Assert.AreEqual(PLAYER_A, _resolver.LastTarget);
            AssertVec(_mob.Position, _resolver.LastMobPos);
            AssertVec(NEAR_X, _resolver.LastTargetPos);
            Assert.AreEqual(ATTACK_RANGE, _resolver.LastRange);
            AssertVec(_mob.CapturedForward, _resolver.LastForward);
            Assert.IsTrue(_mob.HasAttackedOnce);
            Assert.AreEqual((long)(e + 12), _mob.LastAttackExecutionTick);
        }

        // 10
        [TestCase(false)]
        [TestCase(true)]
        public void test_attack_invalid_target_at_attack_tick_skips_resolver_but_marks_attacked(bool nonFinite)
        {
            uint e = StartWindUp(NEAR_X);
            TickTo(e + 11);
            if (nonFinite)
            {
                _candidates.Position = new Vector3(float.NaN, 0f, 0f);
            }
            else
            {
                _candidates.PositionAvailable = false;
            }

            Tick(1);

            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(0, _resolver.Calls);
            Assert.IsTrue(_mob.HasAttackedOnce);
            Assert.AreEqual((long)(e + 12), _mob.LastAttackExecutionTick);
        }

        // 11
        [Test]
        public void test_attack_recovery_zero_leaves_recovering_on_first_tick_after_execution()
        {
            Build(12, 0, 10);
            uint x = RunToAttackTick(NEAR_X, 12);

            Tick(1);

            Assert.AreEqual(x + 1, _tick);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "cooldown 1 < 10: Pursuing");
        }

        // 12
        [Test]
        public void test_attack_recovery_six_leaves_recovering_on_sixth_tick()
        {
            uint x = RunToAttackTick(NEAR_X, 12);

            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State);

            Tick(1);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 13
        [Test]
        public void test_attack_recovery_exit_in_range_cooldown_pending_resumes_pursuit_then_winds_up_at_x_plus_ten()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            // The target (1.5) is 3.5 from the stored destination (FAR, 5): beyond the threshold, so the exit re-paths.
            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale", "SetDestination" }, _nav.Log);

            TickTo(x + 9);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Tick(1);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
        }

        // 14
        [Test]
        public void test_attack_recovery_exit_in_range_cooldown_elapsed_refreshes_facing_and_winds_up_with_stop()
        {
            Build(12, 6, 4);
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _candidates.Position = NEAR_Z;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(0, _mob.FacingAngle, "facing refreshed toward the target");
            AssertVec(new Vector3(0f, 0f, 1f), _mob.CapturedForward);
            CollectionAssert.AreEqual(new[] { "Stop" }, _nav.Log);
        }

        // 15
        [TestCase(false)]
        [TestCase(true)]
        public void test_attack_recovery_exit_out_of_range_resumes_then_polls_stale_once_and_repaths_only_if_stale(bool stale)
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _candidates.Position = FAR;
            _nav.Stale = stale;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            string[] expected = stale
                ? new[] { "Resume", "IsPathStale", "SetDestination" }
                : new[] { "Resume", "IsPathStale" };
            CollectionAssert.AreEqual(expected, _nav.Log);
        }

        // 16
        [Test]
        public void test_attack_recovery_exit_target_beyond_threshold_from_stored_destination_repaths_and_updates_basis()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            Vector3 moved = new Vector3(FAR.x + Threshold + 1f, 0f, 0f);
            _candidates.Position = moved;
            _nav.Log.Clear();

            Tick(1);

            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale", "SetDestination" }, _nav.Log);
            AssertVec(moved, _nav.LastDestination);

            _nav.Log.Clear();
            Tick(1);
            Assert.AreEqual(0, _nav.Count("SetDestination"), "basis updated: same target does not re-path");
        }

        // 17
        [Test]
        public void test_attack_recovery_exit_with_invalid_target_enters_returning_with_speed_cap_then_spawn_destination()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _candidates.PositionAvailable = false;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        // 18 (AC-AI-20b)
        [TestCase(12, 6, 10, 22)]
        [TestCase(12, 6, 4, 18)]
        public void test_attack_target_held_in_range_executions_are_effective_interval_apart(int w, int r, int c, int interval)
        {
            // AC-AI-20a (MobDefinition.EffectiveAttackIntervalTicks = 22 / 18) is covered by the Story 001 test.
            Build(w, r, c);
            _candidates.Position = NEAR_X;

            Tick(N + 4 * interval);

            Assert.GreaterOrEqual(_resolver.Ticks.Count, 3);
            Assert.AreEqual(interval, _resolver.Ticks[1] - _resolver.Ticks[0]);
            Assert.AreEqual(interval, _resolver.Ticks[2] - _resolver.Ticks[1]);
        }

        // 19 (EC-AI-3)
        [Test]
        public void test_attack_aggro_with_target_in_attack_range_winds_up_on_same_tick_after_set_destination()
        {
            _candidates.Position = NEAR_X;

            Tick(N);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            CollectionAssert.AreEqual(new[] { "SetDestination", "Stop" }, _nav.Log);
            AssertVec(new Vector3(1f, 0f, 0f), _mob.CapturedForward);
        }

        [Test]
        public void test_attack_aggro_with_target_outside_attack_range_stays_pursuing()
        {
            _candidates.Position = FAR;

            Tick(N);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            CollectionAssert.AreEqual(new[] { "SetDestination" }, _nav.Log);
        }

        // EC-AI-3 discarded request: aggro tick SetDestination + Stop on the same tick -> exit must re-path.
        [Test]
        public void test_attack_aggro_tick_discarded_destination_forces_repath_on_recovering_exit()
        {
            _candidates.Position = NEAR_X;
            Tick(N);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
            uint x = _tick + 12;
            TickTo(x + 5);
            Vector3 moved = new Vector3(2.3f, 0f, 0f);
            Assert.Less((moved - NEAR_X).magnitude, Threshold, "precondition: within threshold of the original destination");
            _candidates.Position = moved;
            _nav.Stale = false;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale", "SetDestination" }, _nav.Log);
            AssertVec(moved, _nav.LastDestination);
        }

        [Test]
        public void test_attack_pursuing_entry_with_earlier_destination_does_not_repath_on_recovering_exit()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _candidates.Position = new Vector3(4.9f, 0f, 0f);
            _nav.Stale = false;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Assert.AreEqual(0, _nav.Count("SetDestination"));
            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale" }, _nav.Log);
        }

        [Test]
        public void test_attack_throwing_resolver_still_records_attack_before_exception_propagates()
        {
            _mob = new MobController(new EntityID(7u), Definition(12, 6, 10), Vector3.zero, _nav,
                new FakeStats(), new ThrowingResolver(), EnemyAIConfigValues.Defaults, NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, _candidates);
            uint e = StartWindUp(NEAR_X);
            TickTo(e + 11);
            Assert.IsFalse(_mob.HasAttackedOnce);

            Assert.Throws<InvalidOperationException>(() => Tick(1));

            Assert.AreEqual(e + 12, _tick);
            Assert.IsTrue(_mob.HasAttackedOnce);
            Assert.AreEqual((long)(e + 12), _mob.LastAttackExecutionTick);
        }

        [Test]
        public void test_attack_recovery_zero_cooldown_one_winds_up_on_first_tick_and_next_attack_at_x_plus_thirteen()
        {
            Build(12, 0, 1);
            uint x = RunToAttackTick(NEAR_X, 12);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(x + 1, _tick);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(1, _nav.Count("Stop"));
            Assert.AreEqual(0, _nav.Count("Resume"));

            TickTo(x + 13);
            Assert.AreEqual(2, _resolver.Calls);
            Assert.AreEqual(x + 13, (uint)_resolver.Ticks[1]);
        }

        [Test]
        public void test_attack_recovering_invalid_target_mid_countdown_enters_returning_without_waiting_for_counter()
        {
            Build(12, 6, 4);
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 3);
            _candidates.PositionAvailable = false;
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State, "x + 4: counter still above 0");
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        [Test]
        public void test_attack_recovering_invalid_then_valid_again_stays_returning_without_resume()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            _candidates.PositionAvailable = false;
            _nav.Log.Clear();
            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);

            _mob.Position = new Vector3(10f, 0f, 0f);
            _candidates.PositionAvailable = true;
            _candidates.Position = FAR;
            Tick(3);

            Assert.AreEqual(MobState.Returning, _mob.State);
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        [TestCase(6, true)]
        [TestCase(7, false)]
        public void test_attack_recovering_exit_cooldown_boundary(int cooldown, bool windsUpAtExit)
        {
            Build(12, 6, cooldown);
            uint x = RunToAttackTick(NEAR_X, 12);

            TickTo(x + 6);
            Assert.AreEqual(windsUpAtExit ? MobState.WindingUp : MobState.Pursuing, _mob.State, "tick X+6");

            if (!windsUpAtExit)
            {
                Tick(1);
                Assert.AreEqual(x + 7, _tick);
                Assert.AreEqual(MobState.WindingUp, _mob.State, "tick X+7");
            }
        }

        [Test]
        public void test_attack_recovering_to_pursuing_refreshes_facing_toward_moved_target()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 5);
            Assert.AreEqual(900, _mob.FacingAngle);
            Vector3 moved = new Vector3(3f, 0f, 3f);
            _candidates.Position = moved;

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Assert.AreEqual(450, _mob.FacingAngle);
            Assert.AreEqual(MobController.BearingToFacing(_mob.Position, moved, 0), _mob.FacingAngle);
        }

        [Test]
        public void test_attack_target_leaves_range_during_wind_up_still_resolves_once_with_new_position()
        {
            uint e = StartWindUp(NEAR_X);
            _candidates.Position = FAR;

            TickTo(e + 12);

            Assert.AreEqual(1, _resolver.Calls);
            AssertVec(FAR, _resolver.LastTargetPos);
        }

        [Test]
        public void test_attack_resolver_called_exactly_once_from_attack_tick_through_last_recovering_tick()
        {
            uint x = RunToAttackTick(NEAR_X, 12);

            for (uint t = x; t <= x + 5; t++)
            {
                TickTo(t);
                Assert.AreEqual(1, _resolver.Calls, "tick " + t);
            }
        }

        [Test]
        public void test_attack_pursuing_in_range_cooldown_pending_polls_stale_without_stop()
        {
            uint x = RunToAttackTick(NEAR_X, 12);
            TickTo(x + 6);
            Assert.AreEqual(MobState.Pursuing, _mob.State);

            for (uint t = x + 7; t <= x + 9; t++)
            {
                _nav.Log.Clear();
                TickTo(t);
                Assert.AreEqual(MobState.Pursuing, _mob.State, "tick " + t);
                Assert.AreEqual(0, _nav.Count("Stop"), "tick " + t);
                Assert.AreEqual(1, _nav.Count("IsPathStale"), "tick " + t);
            }
        }

        [Test]
        public void test_attack_pursuing_unreadable_target_enters_returning_without_stop()
        {
            AggroAtFar();
            _candidates.PositionAvailable = false;
            _mob.Position = new Vector3(10f, 0f, 0f);
            _nav.Log.Clear();

            Tick(3);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.AreEqual(0, _nav.Count("Stop"));
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        [Test]
        public void test_attack_controller_ctor_null_resolver_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MobController(new EntityID(7u), Definition(12, 6, 10),
                Vector3.zero, _nav, new FakeStats(), null, EnemyAIConfigValues.Defaults, NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, _candidates));
        }
    }
}

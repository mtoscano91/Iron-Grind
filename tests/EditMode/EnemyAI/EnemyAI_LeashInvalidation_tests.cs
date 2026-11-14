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
    /// EditMode unit tests for Enemy AI Story 006: leash, target invalidation, re-scan and Returning (CR-AI-9, CR-AI-10,
    /// CR-AI-11 Returning part, CR-AI-14, F-AI-4, EC-AI-2, EC-AI-5, AC-AI-9 to AC-AI-12b, AC-AI-23, AC-AI-26).
    /// Tick Model note: Recovering runs the leash / target checks on every tick, so a persisting breach or invalidation that
    /// began in the cycle enters Returning on the first Recovering tick (X+1), not at the counter exit. A target death noted
    /// during the cycle is the exception: its re-scan waits for the Recovering exit (CR-AI-10, "not immediately").
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_LeashInvalidation_Tests
    {
        private const float LEASH = 20f;
        private static readonly EntityID PLAYER_A = new EntityID(101u);
        private static readonly EntityID PLAYER_B = new EntityID(102u);
        private static readonly Vector3 FAR = new Vector3(5f, 0f, 0f);
        private static readonly Vector3 NEAR_X = new Vector3(1.5f, 0f, 0f);
        private static readonly Vector3 B_OUT_OF_AGGRO = new Vector3(50f, 0f, 0f);
        private static readonly Vector3 B_IN_ATTACK_RANGE = new Vector3(1.2f, 0f, 0f);

        private sealed class LoggingNav : INavigationProvider
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<float> Speeds = new List<float>();
            public Vector3 LastDestination;

            public void SetDestination(EntityID agent, Vector3 target) { Log.Add("SetDestination"); LastDestination = target; }
            public void SetSpeed(EntityID agent, float speed) { Log.Add("SetSpeed"); Speeds.Add(speed); }
            public void Stop(EntityID agent) { Log.Add("Stop"); }
            public void Resume(EntityID agent) { Log.Add("Resume"); }
            public bool IsPathStale(EntityID agent) { Log.Add("IsPathStale"); return false; }
            public Vector3 GetCurrentVelocity(EntityID agent) { return Vector3.zero; }

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

        private sealed class CountingResolver : IMobAttackResolver
        {
            public int Calls;

            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward)
            {
                Calls++;
            }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public readonly Dictionary<uint, float> HP = new Dictionary<uint, float>();

            public float GetCurrentHP(EntityID entityId)
            {
                return HP.TryGetValue(entityId.RawValue, out float hp) ? hp : 100f;
            }

            public float GetCurrentMP(EntityID entityId) => 0f;
            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class FakeCandidates : IAggroCandidateSource
        {
            public readonly List<(EntityID Id, Vector3 Position)> Players = new List<(EntityID Id, Vector3 Position)>();
            public int CopyCalls;

            public void Set(EntityID id, Vector3 position)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        Players[i] = (id, position);
                        return;
                    }
                }

                Players.Add((id, position));
            }

            public void Remove(EntityID id)
            {
                Players.RemoveAll(p => p.Id.Equals(id));
            }

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)
            {
                CopyCalls++;
                into.AddRange(Players);
            }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        position = Players[i].Position;
                        return true;
                    }
                }

                position = Vector3.zero;
                return false;
            }
        }

        private LoggingNav _nav;
        private CountingResolver _resolver;
        private FakeStats _stats;
        private FakeCandidates _candidates;
        private MobController _mob;
        private uint _tick;

        private static int N => EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT;
        private static float CAP => NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT;

        private static MobDefinition Definition(int windUp, int recovery, int cooldown)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: 10,
                baseDefense: 5, attackRange: 2f, moveSpeed: 3.5f, aggroRange: 8f, leashRange: LEASH,
                attackWindUpTicks: windUp, attackRecoveryTicks: recovery, attackCooldownTicks: cooldown, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
        }

        [SetUp]
        public void SetUp()
        {
            _nav = new LoggingNav();
            _resolver = new CountingResolver();
            _stats = new FakeStats();
            _candidates = new FakeCandidates();
            _candidates.Set(PLAYER_A, FAR);
            _candidates.Set(PLAYER_B, B_OUT_OF_AGGRO);
            _tick = 0;
            Build(12, 6, 10);
        }

        private void Build(int windUp, int recovery, int cooldown)
        {
            _mob = new MobController(new EntityID(7u), Definition(windUp, recovery, cooldown), Vector3.zero, _nav,
                _stats, _resolver, EnemyAIConfigValues.Defaults, CAP, _candidates);
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _tick++;
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
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "precondition: aggro on A, out of attack range");
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);
        }

        /// <summary>Aggro, then A moves into attack range; returns the wind-up entry tick E.</summary>
        private uint StartWindUp()
        {
            AggroAtFar();
            _candidates.Set(PLAYER_A, NEAR_X);
            Tick(1);
            Assert.AreEqual(MobState.WindingUp, _mob.State, "precondition: WindingUp entered");
            return _tick;
        }

        private uint RunToAttackTick(int windUp)
        {
            uint x = StartWindUp() + (uint)windUp;
            TickTo(x);
            Assert.AreEqual(MobState.AttackExecution, _mob.State, "precondition: attack tick reached");
            return x;
        }

        private void KillA()
        {
            _stats.HP[PLAYER_A.RawValue] = 0f;
        }

        // 1
        [TestCase(20f, 0f, 0f, false)]
        [TestCase(20f, 50f, 0f, false)]
        [TestCase(12f, 0f, 16f, false)]
        [TestCase(20.01f, 0f, 0f, true)]
        [TestCase(0f, 0f, 20.01f, true)]
        public void test_leash_pursuing_boundary_is_strict_and_ignores_y(float x, float y, float z, bool expectReturning)
        {
            AggroAtFar();
            _candidates.Set(PLAYER_A, FAR);
            _mob.Position = new Vector3(x, y, z);

            Tick(1);

            Assert.AreEqual(expectReturning ? MobState.Returning : MobState.Pursuing, _mob.State);
            Assert.AreEqual(expectReturning, !_mob.TargetEntityID.HasValue);
        }

        // 2
        [Test]
        public void test_leash_recovering_breach_with_counter_above_zero_enters_returning_on_that_tick()
        {
            uint x = RunToAttackTick(12);
            TickTo(x + 2);
            Assert.AreEqual(MobState.Recovering, _mob.State);
            _mob.Position = new Vector3(20.5f, 0f, 0f);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            Assert.IsFalse(_mob.PendingReturn, "breach first seen in Recovering: no cycle cause");
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        // 3 (AC-AI-9)
        [Test]
        public void test_leash_winding_up_breach_sets_pending_still_attacks_then_returns_never_pursuing()
        {
            uint e = StartWindUp();
            TickTo(e + 9);
            _mob.Position = new Vector3(20.1f, 0f, 0f);

            for (uint t = e + 10; t <= e + 11; t++)
            {
                TickTo(t);
                Assert.AreEqual(MobState.WindingUp, _mob.State, "tick " + t);
                Assert.IsTrue(_mob.PendingReturn, "tick " + t);
            }

            Tick(1);
            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(1, _resolver.Calls, "target valid: the resolver is called on tick X");

            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State, "Recovering is entered and left on X+1 (breach persists)");
            Assert.AreEqual(1, _resolver.Calls);
        }

        // 4
        [Test]
        public void test_leash_breach_during_attack_execution_with_zero_recovery_returns_on_x_plus_one_keeping_pending()
        {
            Build(12, 0, 10);
            RunToAttackTick(12);
            _mob.Position = new Vector3(20.1f, 0f, 0f);

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsTrue(_mob.PendingReturn, "PendingReturn is cleared on Dormant entry only");
        }

        // 5 (AC-AI-10, AC-AI-12b, none found)
        [Test]
        public void test_leash_target_dies_in_wind_up_skips_resolver_marks_attacked_then_returns_when_nobody_else()
        {
            uint e = StartWindUp();
            TickTo(e + 9);
            KillA();

            TickTo(e + 11);
            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.IsTrue(_mob.PendingReturn);

            Tick(1);
            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(0, _resolver.Calls);
            Assert.IsTrue(_mob.HasAttackedOnce);
            Assert.AreEqual((long)(e + 12), _mob.LastAttackExecutionTick);

            uint x = e + 12;
            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State, "CR-AI-10: a death noted in the cycle is re-scanned on Recovering exit, not immediately");
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);

            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
        }

        // 6 (AC-AI-12b, found, in range)
        [Test]
        public void test_leash_target_dies_in_wind_up_rescan_finds_b_in_range_clears_pending_and_winds_up_at_exit()
        {
            uint e = StartWindUp();
            TickTo(e + 9);
            KillA();
            _candidates.Set(PLAYER_B, B_IN_ATTACK_RANGE);
            uint x = e + 12;
            TickTo(x);
            _nav.Log.Clear();

            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID, "re-scan deferred to Recovering exit (CR-AI-10)");
            Assert.AreEqual(0, _nav.Log.Count, "no nav calls while counting down");
            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            Assert.IsFalse(_mob.HasAttackedOnce, "cooldown reset on target change");
            Assert.IsFalse(_mob.PendingReturn, "documented CR-AI-14 deviation: successful re-scan clears pendingReturn");
            CollectionAssert.AreEqual(new[] { "Stop" }, _nav.Log);
        }

        // 7 (AC-AI-12b, found, out of attack range). A's last issued destination is FAR (5, 0, 0); both B rows are within
        // PURSUIT_REPOSITION_THRESHOLD of it, so only the target-change re-path makes the SetDestination toward B.
        [TestCase(5f, 0f)]
        [TestCase(5f, 0.5f)]
        public void test_leash_target_dies_in_wind_up_rescan_finds_b_out_of_attack_range_resumes_pursuit_toward_b(float bx, float bz)
        {
            Vector3 b = new Vector3(bx, 0f, bz);
            uint e = StartWindUp();
            TickTo(e + 9);
            KillA();
            _candidates.Set(PLAYER_B, b);
            uint x = e + 12;
            TickTo(x + 5);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            Assert.IsFalse(_mob.PendingReturn);
            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale", "SetDestination" }, _nav.Log);
            Assert.AreEqual(b, _nav.LastDestination);
        }

        [Test]
        public void test_leash_recovering_swap_mid_countdown_then_pursuing_exit_repaths_toward_b_within_threshold()
        {
            Vector3 b = new Vector3(5f, 0f, 0.5f);
            _candidates.Set(PLAYER_B, b);
            uint x = RunToAttackTick(12);
            TickTo(x + 2);
            KillA();
            Tick(1);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID, "precondition: swapped mid-countdown");
            TickTo(x + 5);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            CollectionAssert.AreEqual(new[] { "Resume", "IsPathStale", "SetDestination" }, _nav.Log);
            Assert.AreEqual(b, _nav.LastDestination);
        }

        [Test]
        public void test_leash_target_dies_in_wind_up_then_revives_before_exit_continues_against_a_keeping_cooldown()
        {
            uint e = StartWindUp();
            TickTo(e + 9);
            KillA();
            uint x = e + 12;
            TickTo(x + 3);
            Assert.IsTrue(_mob.PendingReturn, "precondition: death noted in the cycle");
            _stats.HP[PLAYER_A.RawValue] = 100f;
            TickTo(x + 5);

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State, "cooldown 10 not elapsed at X+6: Pursuing, not WindingUp");
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);
            Assert.IsFalse(_mob.PendingReturn);
            Assert.IsTrue(_mob.HasAttackedOnce, "same target: cooldown state kept");
            Assert.AreEqual((long)x, _mob.LastAttackExecutionTick);
        }

        // 8
        [Test]
        public void test_leash_mixed_causes_death_then_transient_breach_returns_at_exit_even_with_b_in_range()
        {
            uint e = StartWindUp();
            TickTo(e + 3);
            KillA();
            _candidates.Set(PLAYER_B, B_IN_ATTACK_RANGE);
            Tick(1);
            _mob.Position = new Vector3(20.5f, 0f, 0f);
            Tick(1);
            _mob.Position = Vector3.zero;
            uint x = e + 12;
            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID, "the dead target is kept until Recovering exit (CR-AI-10)");
            Assert.AreEqual(0, _resolver.Calls, "dead target: resolver skipped on tick X");
            Assert.IsTrue(_mob.HasAttackedOnce);
            Assert.AreEqual((long)x, _mob.LastAttackExecutionTick);
            int copies = _candidates.CopyCalls;

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State, "pending cause included a leash breach: no re-scan continuation");
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            Assert.AreEqual(copies, _candidates.CopyCalls, "no wasted re-scan when the exit is already decided");
        }

        [Test]
        public void test_leash_cycle_breach_then_death_first_seen_in_recovering_returns_at_exit_without_rescan()
        {
            uint e = StartWindUp();
            TickTo(e + 4);
            _mob.Position = new Vector3(20.5f, 0f, 0f);
            Tick(1);
            _mob.Position = Vector3.zero;
            uint x = e + 12;
            TickTo(x + 2);
            Assert.AreEqual(MobState.Recovering, _mob.State, "precondition: breach was transient");
            Assert.AreEqual(1, _resolver.Calls, "target was valid on tick X");
            KillA();
            _candidates.Set(PLAYER_B, B_IN_ATTACK_RANGE);
            int copies = _candidates.CopyCalls;

            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID, "pending return held: no immediate re-scan");
            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            Assert.AreEqual(copies, _candidates.CopyCalls, "no re-scan at all");
        }

        // 9
        [Test]
        public void test_leash_death_only_cause_with_mob_inside_leash_applies_only_rescan_logic()
        {
            uint e = StartWindUp();
            _mob.Position = new Vector3(10f, 0f, 0f);
            TickTo(e + 3);
            KillA();
            _candidates.Set(PLAYER_B, new Vector3(10.5f, 0f, 0f));
            uint x = e + 12;
            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State);

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            Assert.IsFalse(_mob.PendingReturn);
        }

        // 10 (AC-AI-26)
        [Test]
        public void test_leash_pursuing_target_dies_rescan_b_within_threshold_forces_set_destination_toward_b()
        {
            Vector3 b = new Vector3(5f, 0f, 0.5f);
            _candidates.Set(PLAYER_B, b);
            AggroAtFar();
            KillA();
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            Assert.AreEqual("SetDestination", _nav.Log[_nav.Log.Count - 1]);
            Assert.AreEqual(b.x, _nav.LastDestination.x, 0.001f);
            Assert.AreEqual(b.z, _nav.LastDestination.z, 0.001f);
            Assert.AreEqual(MobController.BearingToFacing(_mob.Position, b, 0), _mob.FacingAngle);
        }

        // 11
        [Test]
        public void test_leash_pursuing_death_rescan_b_in_attack_range_winds_up_same_tick_with_stop_and_no_destination()
        {
            _candidates.Set(PLAYER_B, B_IN_ATTACK_RANGE);
            AggroAtFar();
            KillA();
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            CollectionAssert.AreEqual(new[] { "Stop" }, _nav.Log);
        }

        // 12 and 14 (NaN HP)
        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(float.NaN)]
        public void test_leash_pursuing_target_hp_not_positive_and_nobody_in_range_enters_returning(float hp)
        {
            AggroAtFar();
            _stats.HP[PLAYER_A.RawValue] = hp;

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
        }

        // 13
        [Test]
        public void test_leash_recovering_death_with_counter_above_zero_swaps_target_without_nav_calls_or_counter_change()
        {
            _candidates.Set(PLAYER_B, new Vector3(6f, 0f, 0f));
            uint x = RunToAttackTick(12);
            TickTo(x + 2);
            KillA();
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Recovering, _mob.State);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
            Assert.AreEqual(0, _nav.Log.Count);

            TickTo(x + 5);
            Assert.AreEqual(MobState.Recovering, _mob.State, "counter unchanged: still Recovering at X+5");
            Tick(1);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "exits at X+6 as before");
        }

        // 15 (AC-AI-23)
        [Test]
        public void test_leash_pursuing_target_removed_enters_returning_with_cap_then_spawn_destination()
        {
            AggroAtFar();
            _candidates.Remove(PLAYER_A);
            _nav.Log.Clear();
            _nav.Speeds.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
            Assert.AreEqual(CAP, _nav.Speeds[0]);
            Assert.AreEqual(_mob.SpawnPoint, _nav.LastDestination);
        }

        // 16
        [TestCase(20f, false)]
        [TestCase(20.01f, true)]
        public void test_leash_pursuing_target_beyond_leash_from_spawn_is_standard_invalidation(float targetX, bool expectReturning)
        {
            AggroAtFar();
            _candidates.Set(PLAYER_A, new Vector3(targetX, 0f, 0f));

            Tick(1);

            Assert.AreEqual(expectReturning ? MobState.Returning : MobState.Pursuing, _mob.State);
        }

        // 17
        [Test]
        public void test_leash_winding_up_target_removed_skips_resolver_sets_pending_and_returns()
        {
            uint e = StartWindUp();
            TickTo(e + 3);
            _candidates.Remove(PLAYER_A);

            TickTo(e + 12);
            Assert.AreEqual(MobState.AttackExecution, _mob.State);
            Assert.AreEqual(0, _resolver.Calls);
            Assert.IsTrue(_mob.PendingReturn);
            Assert.IsTrue(_mob.HasAttackedOnce, "Story 005 order: bookkeeping written even when the resolver is skipped");
            Assert.AreEqual((long)(e + 12), _mob.LastAttackExecutionTick);

            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);
        }

        // 18, also standard invalidation first seen on tick X+1 (service-level regression:
        // Navigation_TickDrain_tests.test_tick_drain_stop_then_set_destination_before_drain_issues_new_target)
        [Test]
        public void test_leash_returning_entry_from_stopped_agent_logs_speed_cap_then_destination_without_resume()
        {
            RunToAttackTick(12);
            _candidates.Remove(PLAYER_A);
            _nav.Log.Clear();

            Tick(1);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsTrue(_mob.PendingReturn, "the X+1 cycle hook noted the invalidation before Recovering");
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            CollectionAssert.AreEqual(new[] { "SetSpeed", "SetDestination" }, _nav.Log);
        }

        // 19
        [Test]
        public void test_leash_returning_makes_no_nav_calls_ignores_leash_breach_and_does_not_aggro()
        {
            AggroAtFar();
            _candidates.Remove(PLAYER_A);
            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);
            _mob.Position = new Vector3(25f, 0f, 0f);
            _candidates.Set(PLAYER_B, new Vector3(25.5f, 0f, 0f));
            _nav.Log.Clear();

            Tick(3 * N);

            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsFalse(_mob.TargetEntityID.HasValue);
            Assert.AreEqual(0, _nav.Log.Count);
        }

        // 20 (AC-AI-11)
        [TestCase(0.3f, true)]
        [TestCase(0.5f, true)]
        [TestCase(0.51f, false)]
        public void test_leash_returning_arrival_threshold_is_inclusive_and_runs_stop_then_set_speed(float distance, bool arrives)
        {
            AggroAtFar();
            _candidates.Remove(PLAYER_A);
            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);
            _mob.Position = new Vector3(distance, 0f, 0f);
            _nav.Log.Clear();
            _nav.Speeds.Clear();

            Tick(1);

            if (arrives)
            {
                Assert.AreEqual(MobState.Dormant, _mob.State);
                CollectionAssert.AreEqual(new[] { "Stop", "SetSpeed" }, _nav.Log);
                Assert.AreEqual(3.5f, _nav.Speeds[0]);
                Assert.IsFalse(_mob.TargetEntityID.HasValue);
                Assert.IsFalse(_mob.PendingReturn);
                Assert.IsFalse(_mob.HasAttackedOnce);
                Assert.AreEqual(-1L, _mob.LastAttackExecutionTick);
            }
            else
            {
                Assert.AreEqual(MobState.Returning, _mob.State);
                Assert.AreEqual(0, _nav.Log.Count);
            }
        }

        [Test]
        public void test_leash_returning_arrival_after_pending_return_clears_pending_and_cooldown_state()
        {
            Build(12, 0, 10);
            RunToAttackTick(12);
            _mob.Position = new Vector3(20.1f, 0f, 0f);
            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State);
            Assert.IsTrue(_mob.PendingReturn);
            _mob.Position = Vector3.zero;

            Tick(1);

            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.IsFalse(_mob.PendingReturn);
            Assert.IsFalse(_mob.HasAttackedOnce);
            Assert.AreEqual(-1L, _mob.LastAttackExecutionTick);
        }

        // 21 (EC-AI-5)
        [Test]
        public void test_leash_returning_at_spawn_arrives_on_first_returning_tick_not_entry_tick()
        {
            AggroAtFar();
            _candidates.Remove(PLAYER_A);

            Tick(1);
            Assert.AreEqual(MobState.Returning, _mob.State, "entry tick");

            Tick(1);
            Assert.AreEqual(MobState.Dormant, _mob.State, "first Returning tick");
        }

        // 22
        [Test]
        public void test_leash_new_target_after_rescan_does_not_inherit_old_cooldown()
        {
            uint x = RunToAttackTick(12);
            TickTo(x + 6);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "cooldown 10 not elapsed: Pursuing");
            Assert.AreEqual(1, _resolver.Calls);
            KillA();
            _candidates.Set(PLAYER_B, B_IN_ATTACK_RANGE);

            Tick(1);

            Assert.AreEqual(MobState.WindingUp, _mob.State, "no cooldown wait for the new target");
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
        }

        // 23
        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_leash_controller_ctor_invalid_return_speed_cap_throws(float cap)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MobController(new EntityID(7u), Definition(12, 6, 10),
                Vector3.zero, _nav, _stats, _resolver, EnemyAIConfigValues.Defaults, cap, _candidates));
        }
    }
}

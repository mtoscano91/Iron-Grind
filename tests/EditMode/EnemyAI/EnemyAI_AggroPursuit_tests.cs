using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using System;
using UnityEngine;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode unit tests for Enemy AI Story 004: aggro scan and pursuit (CR-AI-3, F-AI-2, CR-AI-5, CR-AI-11,
    /// AC-AI-1, AC-AI-2, AC-AI-17, AC-AI-22, AC-AI-24, EC-AI-7).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_AggroPursuit_Tests
    {
        private sealed class NullResolver : IMobAttackResolver
        {
            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward) { }
        }

        private const float AGGRO_RANGE = 8f;
        private static readonly EntityID PLAYER_A = new EntityID(101u);
        private static readonly EntityID PLAYER_B = new EntityID(102u);

        private sealed class RecordingNav : INavigationProvider
        {
            public readonly List<Vector3> Destinations = new List<Vector3>();
            public int ResumeCalls;
            public int StaleCalls;
            public int OtherCalls;
            public bool Stale;
            public bool Unregistered;

            public int TotalCalls => Destinations.Count + ResumeCalls + StaleCalls + OtherCalls;

            public void SetDestination(EntityID agent, Vector3 target)
            {
                // Counted even when Unregistered: the real provider's SetDestination is a no-op there.
                Destinations.Add(target);
            }

            public void SetSpeed(EntityID agent, float speed) { OtherCalls++; }
            public void Stop(EntityID agent) { OtherCalls++; }
            public void Resume(EntityID agent) { ResumeCalls++; }
            public bool IsPathStale(EntityID agent) { StaleCalls++; return Unregistered || Stale; }
            public Vector3 GetCurrentVelocity(EntityID agent) { OtherCalls++; return Vector3.zero; }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public readonly Dictionary<EntityID, float> HP = new Dictionary<EntityID, float>();
            public float GetCurrentHP(EntityID entityId) => HP.TryGetValue(entityId, out float hp) ? hp : 100f;
            public float GetCurrentMP(EntityID entityId) => 0f;
            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class FakeCandidates : IAggroCandidateSource
        {
            public readonly List<(EntityID Id, Vector3 Position)> Players = new List<(EntityID Id, Vector3 Position)>();
            public int CopyCalls;
            public int TryGetCalls;
            public bool PositionAvailable = true;
            public int LastIncomingCount = -1;

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into)
            {
                CopyCalls++;
                LastIncomingCount = into.Count;
                into.AddRange(Players);
            }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                TryGetCalls++;
                position = Vector3.zero;
                if (!PositionAvailable)
                {
                    return false;
                }

                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        position = Players[i].Position;
                        return true;
                    }
                }

                return false;
            }

            public void SetPosition(EntityID id, Vector3 position)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        Players[i] = (id, position);
                    }
                }
            }
        }

        private RecordingNav _nav;
        private FakeStats _stats;
        private FakeCandidates _candidates;
        private EnemyAIConfigValues _config;
        private MobController _mob;
        private uint _tick;

        private static int N => EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT;
        private static float Threshold => EnemyAIConfigValues.PURSUIT_REPOSITION_THRESHOLD_DEFAULT;

        private static MobDefinition Definition()
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(1u), displayName: "Test Mob", baseMaxHP: 100, baseAttackPower: 10,
                baseDefense: 5, attackRange: 2f, moveSpeed: 3.5f, aggroRange: AGGRO_RANGE, leashRange: 20f,
                attackWindUpTicks: 12, attackRecoveryTicks: 6, attackCooldownTicks: 10, killXP: 10,
                mobTier: MobTier.Common, enragedChance: 0.07f, enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f, enragedXPMultiplier: 1.5f, enragedLootQualityBonusTier: 1);
        }

        [SetUp]
        public void SetUp()
        {
            _nav = new RecordingNav();
            _stats = new FakeStats();
            _candidates = new FakeCandidates();
            _config = EnemyAIConfigValues.Defaults;
            _tick = 0;
            _mob = NewMob(Vector3.zero);
        }

        private MobController NewMob(Vector3 spawn)
        {
            return new MobController(new EntityID(7u), Definition(), spawn, _nav, _stats,
                new NullResolver(), _config, _candidates);
        }

        private void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _mob.Tick(++_tick);
            }
        }

        private static Vector3 PlayerAt(float x, float y, float z) => new Vector3(x, y, z);

        private void AggroPlayerA(Vector3 position)
        {
            _candidates.Players.Add((PLAYER_A, position));
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "precondition: aggro");
        }

        // 1
        [Test]
        public void test_aggro_scan_first_scan_fires_exactly_on_interval_tick()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(3f, 0f, 0f)));

            Tick(N - 1);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.AreEqual(0, _candidates.CopyCalls);

            Tick(1);
            Assert.AreEqual(1, _candidates.CopyCalls);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 2
        [Test]
        public void test_aggro_scan_boundary_outside_stays_dormant_then_inside_aggros()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(AGGRO_RANGE + 0.1f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.IsNull(_mob.TargetEntityID);

            _candidates.SetPosition(PLAYER_A, PlayerAt(AGGRO_RANGE - 0.1f, 0f, 0f));
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);
        }

        [Test]
        public void test_aggro_scan_exactly_at_range_aggros_inclusive()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(AGGRO_RANGE, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 3
        [Test]
        public void test_aggro_scan_y_offset_ignored()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(AGGRO_RANGE - 0.1f, 50f, 0f)));
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 4
        [Test]
        public void test_aggro_scan_two_in_range_first_in_list_wins()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(6f, 0f, 0f)));
            _candidates.Players.Add((PLAYER_B, PlayerAt(2f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);
        }

        // 5
        [TestCase(0f)]
        [TestCase(-3f)]
        [TestCase(float.NaN)]
        public void test_aggro_scan_dead_first_player_skipped_next_wins(float hpOfA)
        {
            _stats.HP[PLAYER_A] = hpOfA;
            _candidates.Players.Add((PLAYER_A, PlayerAt(2f, 0f, 0f)));
            _candidates.Players.Add((PLAYER_B, PlayerAt(3f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
        }

        [Test]
        public void test_aggro_scan_all_in_range_dead_stays_dormant()
        {
            _stats.HP[PLAYER_A] = 0f;
            _stats.HP[PLAYER_B] = float.NaN;
            _candidates.Players.Add((PLAYER_A, PlayerAt(2f, 0f, 0f)));
            _candidates.Players.Add((PLAYER_B, PlayerAt(3f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.IsNull(_mob.TargetEntityID);
            Assert.AreEqual(0, _nav.TotalCalls);
        }

        // 6
        [Test]
        public void test_aggro_scan_nobody_in_range_for_three_intervals_stays_dormant_without_nav()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(50f, 0f, 0f)));
            Tick(3 * N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.IsNull(_mob.TargetEntityID);
            Assert.AreEqual(0, _nav.TotalCalls);
            Assert.AreEqual(3, _candidates.CopyCalls);
        }

        // 7
        [Test]
        public void test_aggro_entry_issues_one_set_destination_and_no_resume()
        {
            Vector3 pos = PlayerAt(4f, 0f, 3f);
            AggroPlayerA(pos);
            Assert.AreEqual(1, _nav.Destinations.Count);
            Assert.AreEqual(pos, _nav.Destinations[0]);
            Assert.AreEqual(0, _nav.ResumeCalls);
        }

        // 8
        [Test]
        public void test_pursuit_facing_written_every_tick()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            Tick(1);
            Assert.AreEqual(900, _mob.FacingAngle);

            _candidates.SetPosition(PLAYER_A, PlayerAt(0f, 0f, 5f));
            Tick(1);
            Assert.AreEqual(0, _mob.FacingAngle);
        }

        // 9
        [Test]
        public void test_pursuit_reposition_threshold_boundaries()
        {
            Assert.Greater(Threshold, 0.01f, "guard: Threshold - 0.01 must stay positive");
            Vector3 start = PlayerAt(5f, 0f, 0f);
            AggroPlayerA(start);
            int baseline = _nav.Destinations.Count;

            _candidates.SetPosition(PLAYER_A, start + new Vector3(Threshold - 0.01f, 0f, 0f));
            Tick(1);
            Assert.AreEqual(baseline, _nav.Destinations.Count);

            Vector3 moved = start + new Vector3(Threshold + 0.01f, 0f, 0f);
            _candidates.SetPosition(PLAYER_A, moved);
            Tick(1);
            Assert.AreEqual(baseline + 1, _nav.Destinations.Count);
            Assert.AreEqual(moved, _nav.Destinations[baseline]);

            _candidates.SetPosition(PLAYER_A, moved + new Vector3(0f, 0f, Threshold - 0.01f));
            Tick(1);
            Assert.AreEqual(baseline + 1, _nav.Destinations.Count);
        }

        // 10
        [Test]
        public void test_pursuit_stale_path_reissues_destination_regardless_of_distance()
        {
            Vector3 start = PlayerAt(5f, 0f, 0f);
            AggroPlayerA(start);
            int baseline = _nav.Destinations.Count;
            _nav.Stale = true;
            Vector3 moved = start + new Vector3(0.1f, 0f, 0f);
            _candidates.SetPosition(PLAYER_A, moved);

            Tick(1);

            Assert.AreEqual(baseline + 1, _nav.Destinations.Count);
            Assert.AreEqual(moved, _nav.Destinations[baseline], "issued destination is the new target position");

            _nav.Stale = false;
            _candidates.SetPosition(PLAYER_A, moved + new Vector3(Threshold - 0.01f, 0f, 0f));
            Tick(1);
            Assert.AreEqual(baseline + 1, _nav.Destinations.Count, "basis moved to the reissued destination");
        }

        [Test]
        public void test_pursuit_polls_path_stale_exactly_once_per_tick()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            int before = _nav.StaleCalls;

            Tick(1);
            Assert.AreEqual(before + 1, _nav.StaleCalls);

            int destinationsBefore = _nav.Destinations.Count;
            _nav.Stale = true;
            Tick(1);
            Assert.AreEqual(before + 2, _nav.StaleCalls);
            Assert.AreEqual(destinationsBefore + 1, _nav.Destinations.Count, "exactly one SetDestination on the stale tick");
        }

        // 11
        [Test]
        public void test_pursuit_target_position_unavailable_is_noop()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            Tick(1);
            int facing = _mob.FacingAngle;
            int navBefore = _nav.TotalCalls;
            _candidates.PositionAvailable = false;
            _candidates.SetPosition(PLAYER_A, PlayerAt(0f, 0f, 5f));

            Tick(1);

            Assert.AreEqual(navBefore, _nav.TotalCalls);
            Assert.AreEqual(facing, _mob.FacingAngle);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 12
        [Test]
        public void test_pursuit_unregistered_provider_does_not_throw()
        {
            _nav.Unregistered = true;
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            Assert.DoesNotThrow(() => Tick(5));
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 13
        [Test]
        public void test_pursuit_nearer_player_does_not_steal_aggro_and_no_list_reads()
        {
            AggroPlayerA(PlayerAt(6f, 0f, 0f));
            int copyBefore = _candidates.CopyCalls;
            _candidates.Players.Add((PLAYER_B, PlayerAt(3f, 0f, 0f)));

            Tick(3 * N);

            Assert.AreEqual(PLAYER_A, _mob.TargetEntityID);
            Assert.AreEqual(copyBefore, _candidates.CopyCalls);
        }

        // 14
        [Test]
        public void test_dormant_reentry_resets_scan_countdown()
        {
            Assert.Greater(N, 2, "guard: N - 2 must be positive");
            Tick(N - 2);
            _mob.ForceStateForTest(MobState.Pursuing, _tick);
            _mob.ForceStateForTest(MobState.Dormant, _tick);
            _candidates.Players.Add((PLAYER_A, PlayerAt(3f, 0f, 0f)));

            Tick(N - 1);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.AreEqual(0, _candidates.CopyCalls);

            Tick(1);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }

        // 15
        [Test]
        public void test_controller_ctor_null_candidates_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MobController(new EntityID(7u), Definition(), Vector3.zero,
                _nav, _stats, new NullResolver(), _config, null));
        }

        [Test]
        public void test_controller_ctor_default_config_throws_out_of_range()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MobController(new EntityID(7u), Definition(),
                Vector3.zero, _nav, _stats, new NullResolver(),
                default(EnemyAIConfigValues), _candidates));
        }

        [Test]
        public void test_aggro_custom_config_honoured_interval_and_threshold()
        {
            _config = EnemyAIConfigValues.Create(3, 0.5f,
                EnemyAIConfigValues.RETURN_ARRIVAL_THRESHOLD_DEFAULT, EnemyAIConfigValues.DEATH_LINGER_TICKS_DEFAULT);
            _mob = NewMob(Vector3.zero);
            Vector3 start = PlayerAt(5f, 0f, 0f);
            _candidates.Players.Add((PLAYER_A, start));

            Tick(2);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Tick(1);
            Assert.AreEqual(MobState.Pursuing, _mob.State, "first scan on Dormant tick 3");
            int baseline = _nav.Destinations.Count;

            _candidates.SetPosition(PLAYER_A, start + new Vector3(0.5f - 0.01f, 0f, 0f));
            Tick(1);
            Assert.AreEqual(baseline, _nav.Destinations.Count);

            _candidates.SetPosition(PLAYER_A, start + new Vector3(0.5f + 0.01f, 0f, 0f));
            Tick(1);
            Assert.AreEqual(baseline + 1, _nav.Destinations.Count);
        }

        [Test]
        public void test_aggro_scan_buffer_cleared_before_each_copy()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(50f, 0f, 0f)));
            Tick(3 * N);
            Assert.AreEqual(3, _candidates.CopyCalls);
            Assert.AreEqual(0, _candidates.LastIncomingCount);
        }

        // Accepted edge: with an unregistered provider IsPathStale reports true, so every Pursuing tick re-issues
        // SetDestination (a no-op in the real provider). It must not throw.
        [Test]
        public void test_pursuit_unregistered_provider_stale_reissues_every_tick()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            int baseline = _nav.Destinations.Count;
            _nav.Unregistered = true;
            _nav.Stale = true;

            Assert.DoesNotThrow(() => Tick(5));

            Assert.AreEqual(baseline + 5, _nav.Destinations.Count);
        }

        [Test]
        public void test_pursuit_exact_threshold_move_and_y_move_do_not_reissue()
        {
            Vector3 start = PlayerAt(5f, 0f, 0f);
            AggroPlayerA(start);
            int baseline = _nav.Destinations.Count;

            _candidates.SetPosition(PLAYER_A, start + new Vector3(Threshold, 0f, 0f));
            Tick(1);
            Assert.AreEqual(baseline, _nav.Destinations.Count, "exactly at threshold does not reissue");

            _candidates.SetPosition(PLAYER_A, start + new Vector3(0f, 50f, 0f));
            Tick(1);
            Assert.AreEqual(baseline, _nav.Destinations.Count, "Y movement ignored");
        }

        [Test]
        public void test_aggro_entry_tick_writes_facing_and_only_set_destination()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));

            Assert.AreEqual(1, _nav.TotalCalls);
            Assert.AreEqual(1, _nav.Destinations.Count);
            Assert.AreEqual(900, _mob.FacingAngle, "facing toward +X written on the aggro entry tick");
        }

        [Test]
        public void test_pursuit_non_finite_target_is_noop()
        {
            AggroPlayerA(PlayerAt(5f, 0f, 0f));
            Tick(1);
            int facing = _mob.FacingAngle;
            int destinations = _nav.Destinations.Count;
            int staleCalls = _nav.StaleCalls;
            _nav.Stale = true;

            _candidates.SetPosition(PLAYER_A, PlayerAt(float.NaN, 0f, 0f));
            Tick(1);
            _candidates.SetPosition(PLAYER_A, PlayerAt(0f, 0f, float.PositiveInfinity));
            Tick(1);

            Assert.AreEqual(destinations, _nav.Destinations.Count);
            Assert.AreEqual(staleCalls, _nav.StaleCalls);
            Assert.AreEqual(facing, _mob.FacingAngle);
        }

        [Test]
        public void test_aggro_scan_non_finite_player_position_skipped_next_valid_wins()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(float.NaN, 0f, 0f)));
            _candidates.Players.Add((PLAYER_B, PlayerAt(3f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(PLAYER_B, _mob.TargetEntityID);
        }

        [Test]
        public void test_aggro_scan_non_finite_player_only_stays_dormant()
        {
            _candidates.Players.Add((PLAYER_A, PlayerAt(0f, 0f, float.NegativeInfinity)));
            Tick(N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
        }

        [Test]
        public void test_aggro_scan_nan_mob_position_never_aggroes()
        {
            _mob.Position = new Vector3(float.NaN, 0f, 0f);
            _candidates.Players.Add((PLAYER_A, PlayerAt(3f, 0f, 0f)));
            Tick(3 * N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.AreEqual(0, _nav.TotalCalls);
        }

        [Test]
        public void test_aggro_scan_empty_player_list_stays_dormant()
        {
            Tick(N);
            Assert.AreEqual(MobState.Dormant, _mob.State);
            Assert.AreEqual(1, _candidates.CopyCalls);
            Assert.AreEqual(0, _nav.TotalCalls);
        }

        [Test]
        public void test_pursuit_null_target_id_is_noop()
        {
            _mob.ForceStateForTest(MobState.Pursuing, _tick);
            Assert.IsNull(_mob.TargetEntityID);

            Assert.DoesNotThrow(() => Tick(3));

            Assert.AreEqual(0, _nav.TotalCalls);
        }

        [Test]
        public void test_controller_ctor_position_equals_spawn_point()
        {
            MobController mob = NewMob(new Vector3(3f, 1f, -4f));
            Assert.AreEqual(mob.SpawnPoint, mob.Position);
        }

        [Test]
        public void test_aggro_scan_uses_mob_position_not_spawn()
        {
            _mob.Position = new Vector3(100f, 0f, 0f);
            _candidates.Players.Add((PLAYER_A, PlayerAt(104f, 0f, 0f)));
            Tick(N);
            Assert.AreEqual(MobState.Pursuing, _mob.State);
        }
    }
}

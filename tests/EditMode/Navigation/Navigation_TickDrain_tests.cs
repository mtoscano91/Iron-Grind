using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>
    /// EditMode unit tests for Navigation Story 005: SyncAgentPositions, the Tick drain and NavigationPhaseStep
    /// (CR-NAV-12, CR-NAV-14, EC-NAV-7, EC-NAV-9, F-NAV-1, AC-NAV-14 to 16, 18).
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_TickDrain_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);
        private static readonly Vector3 POS = new Vector3(1f, 2f, 3f);
        private const int POOL_SIZE = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
        private const int R = NavigationConfigValues.MAX_PATH_UPDATES_PER_TICK_DEFAULT;

        private sealed class EmptyPositions : IMobCommittedPositionSource
        {
            public void CopyCommittedPositions(Dictionary<EntityID, Vector3> into) { }
        }

        private sealed class FakePositionSource : IMobCommittedPositionSource
        {
            public readonly Dictionary<EntityID, Vector3> Positions = new Dictionary<EntityID, Vector3>();

            public void CopyCommittedPositions(Dictionary<EntityID, Vector3> into)
            {
                foreach (KeyValuePair<EntityID, Vector3> pair in Positions)
                {
                    into[pair.Key] = pair.Value;
                }
            }
        }

        private NavigationConfig _config;
        private List<string> _recorder;
        private FakeNavAgentFactory _factory;
        private FakeNavMeshInstanceOps _ops;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private NavMeshDataInstance _instanceA;
        private HashSet<FakeNavAgent> _claimed;

        private static EntityID Id(int i) => new EntityID(100u + (uint)i);

        private static Vector3 Target(int i) => new Vector3(10f + i, 0f, -i);

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _recorder = new List<string>();
            _ops = new FakeNavMeshInstanceOps();
            _instanceA = NavigationTestSupport.MakeInstance(1);
            _bus = new MobEventBus();
            _factory = new FakeNavAgentFactory(_recorder);
            _claimed = new HashSet<FakeNavAgent>();
            BuildService(R);
        }

        [TearDown]
        public void TearDown()
        {
            if (_service != null)
            {
                _service.Teardown();
                _service = null;
            }

            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
                _config = null;
            }
        }

        private void BuildService(int maxPathUpdates)
        {
            _config.SetForTesting(new NavigationConfigValues(
                POOL_SIZE, maxPathUpdates, NavigationConfigValues.PARTIAL_PATH_TIMEOUT_TICKS_DEFAULT,
                NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, NavigationConfigValues.MIN_VALID_SPAWN_FRACTION_DEFAULT,
                NavigationConfigValues.NAV_STUCK_REROUTE_DETECTION_TICKS_DEFAULT,
                NavigationConfigValues.NAV_OFF_MESH_RECOVERY_TICKS_DEFAULT,
                NavigationConfigValues.SPAWN_SAMPLE_RADIUS_DEFAULT));
            var speeds = new FakeSpeedSource();
            speeds.Speeds[TYPE_A] = 3.5f;
            _service = new ZoneNavigationService(_config, _factory, speeds, new FakeMeshQuery(), _ops, _bus);
        }

        private void InitializeService()
        {
            _ops.Add(_instanceA);
            _service.Initialize(ZONE, _instanceA);
        }

        // Spawns entity i and returns the agent that was claimed for it.
        private FakeNavAgent Spawn(int i)
        {
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(i), TYPE_A, POS));
            FakeNavAgent found = null;
            foreach (FakeNavAgent a in _factory.Created)
            {
                if (a.Enabled && !_claimed.Contains(a))
                {
                    found = a;
                }
            }

            Assert.That(found, Is.Not.Null);
            _claimed.Add(found);
            return found;
        }

        private FakeNavAgent[] InitAndSpawn(int count)
        {
            InitializeService();
            var agents = new FakeNavAgent[count + 1];
            for (int i = 1; i <= count; i++)
            {
                agents[i] = Spawn(i);
            }

            return agents;
        }

        private int TotalSetDestinationCalls()
        {
            int total = 0;
            foreach (FakeNavAgent a in _factory.Created)
            {
                total += a.SetDestinationCalls;
            }

            return total;
        }

        // ---- Sync ----

        [TestCase(false)]
        [TestCase(true)]
        public void test_tick_drain_sync_on_mesh_off_mesh_unknown_sets_next_position_on_mesh_only_never_warps(bool viaInterfaceOverload)
        {
            FakeNavAgent[] agents = InitAndSpawn(3);
            agents[3].IsOnNavMesh = false;
            int warpsBefore = agents[1].WarpCalls + agents[2].WarpCalls + agents[3].WarpCalls;
            var positions = new Dictionary<EntityID, Vector3>
            {
                { Id(1), new Vector3(5f, 0f, 5f) },
                { Id(2), new Vector3(6f, 0f, 6f) },
                { Id(3), new Vector3(7f, 0f, 7f) },
                { Id(99), new Vector3(8f, 0f, 8f) },
            };

            if (viaInterfaceOverload)
            {
                _service.SyncAgentPositions((IReadOnlyDictionary<EntityID, Vector3>)positions);
            }
            else
            {
                _service.SyncAgentPositions(positions);
            }

            Assert.That(agents[1].NextPosition, Is.EqualTo(new Vector3(5f, 0f, 5f)));
            Assert.That(agents[2].NextPosition, Is.EqualTo(new Vector3(6f, 0f, 6f)));
            Assert.That(agents[3].NextPositionSets, Is.EqualTo(0));
            Assert.That(agents[1].WarpCalls + agents[2].WarpCalls + agents[3].WarpCalls, Is.EqualTo(warpsBefore));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_sync_released_entity_and_null_input_make_no_writes()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(2)));
            var positions = new Dictionary<EntityID, Vector3>
            {
                { Id(1), new Vector3(5f, 0f, 5f) },
                { Id(2), new Vector3(6f, 0f, 6f) },
            };

            _service.SyncAgentPositions(positions);

            Assert.That(agents[1].NextPositionSets, Is.EqualTo(1));
            Assert.That(agents[2].NextPositionSets, Is.EqualTo(0));

            Assert.DoesNotThrow(() => _service.SyncAgentPositions((Dictionary<EntityID, Vector3>)null));
            Assert.DoesNotThrow(() => _service.SyncAgentPositions((IReadOnlyDictionary<EntityID, Vector3>)null));

            Assert.That(agents[1].NextPositionSets, Is.EqualTo(1));
            Assert.That(agents[2].NextPositionSets, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Drain ----

        [Test]
        public void test_tick_drain_150_queued_issues_r_latest_destinations_rest_stay_queued()
        {
            FakeNavAgent[] agents = InitAndSpawn(POOL_SIZE);
            for (int i = 1; i <= POOL_SIZE; i++)
            {
                _service.SetDestination(Id(i), Target(i));
            }

            _service.Tick();

            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(R));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(POOL_SIZE - R));
            for (int i = 1; i <= POOL_SIZE; i++)
            {
                // agents[i] is the agent Spawn(i) claimed for entity i; entities are enqueued in order 1..POOL_SIZE.
                if (i <= R)
                {
                    Assert.That(agents[i].SetDestinationCalls, Is.EqualTo(1), "entity " + i);
                    Assert.That(agents[i].LastDestination, Is.EqualTo(Target(i)), "entity " + i);
                    Assert.That(_service.IsQueued(Id(i)), Is.False, "entity " + i);
                    Assert.That(_service.HasLatestRequest(Id(i), out _), Is.False, "entity " + i);
                    Assert.That(agents[i].IsStopped, Is.False, "entity " + i);
                }
                else
                {
                    Assert.That(agents[i].SetDestinationCalls, Is.EqualTo(0), "entity " + i);
                    Assert.That(_service.IsQueued(Id(i)), Is.True, "entity " + i);
                    Assert.That(_service.HasLatestRequest(Id(i), out _), Is.True, "entity " + i);
                }
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_latest_wins_across_queue_issues_second_destination_once()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            _service.SetDestination(Id(1), Target(11));

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(agents[1].LastDestination, Is.EqualTo(Target(11)));
            Assert.That(agents[2].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(_service.HasLatestRequest(Id(1), out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_skips_stopped_released_off_mesh_issues_next_r_valid()
        {
            // AC-NAV-11 and AC-NAV-22 post-drain halves: stopped e1 and released e2 are dequeued without a call.
            int total = 3 + R;
            FakeNavAgent[] agents = InitAndSpawn(total);
            for (int i = 1; i <= total; i++)
            {
                _service.SetDestination(Id(i), Target(i));
            }

            _service.Stop(Id(1));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(2)));
            agents[3].IsOnNavMesh = false;

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(0));
            Assert.That(agents[1].IsStopped, Is.True);
            Assert.That(agents[2].SetDestinationCalls, Is.EqualTo(0));
            Assert.That(agents[3].SetDestinationCalls, Is.EqualTo(0));
            for (int i = 4; i <= total; i++)
            {
                Assert.That(agents[i].SetDestinationCalls, Is.EqualTo(1), "entity " + i);
            }

            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(R));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            for (int i = 1; i <= total; i++)
            {
                Assert.That(_service.IsQueued(Id(i)), Is.False, "entity " + i);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_f_nav_1_150_queued_non_empty_after_7_empty_after_8_150_calls()
        {
            // F-NAV-1 / AC-NAV-18: 8 = ceil(150 / 20) with the default R of 20.
            FakeNavAgent[] agents = InitAndSpawn(POOL_SIZE);
            for (int i = 1; i <= POOL_SIZE; i++)
            {
                _service.SetDestination(Id(i), Target(i));
            }

            for (int t = 0; t < 7; t++)
            {
                _service.Tick();
            }

            Assert.That(_service.PendingRequestCount, Is.EqualTo(POOL_SIZE - 7 * R));
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(7 * R));

            _service.Tick();

            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(POOL_SIZE));
            for (int i = 1; i <= POOL_SIZE; i++)
            {
                Assert.That(_service.HasLatestRequest(Id(i), out _), Is.False, "entity " + i);
                Assert.That(agents[i].SetDestinationCalls, Is.EqualTo(1), "entity " + i);
            }

            LogAssert.NoUnexpectedReceived();
        }

        // ---- Order ----

        [Test]
        public void test_tick_drain_order_phase_step_next_position_writes_precede_first_set_destination()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            var source = new FakePositionSource();
            source.Positions[Id(1)] = new Vector3(5f, 0f, 5f);
            source.Positions[Id(2)] = new Vector3(6f, 0f, 6f);
            var step = new NavigationPhaseStep(_service, source);
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            _recorder.Clear();

            step.Run(1u, 0.05f);

            int firstSetDestination = _recorder.FindIndex(s => s.EndsWith(".SetDestination"));
            int lastNextPosition = _recorder.FindLastIndex(s => s.EndsWith(".NextPosition"));
            Assert.That(firstSetDestination, Is.GreaterThanOrEqualTo(0));
            Assert.That(lastNextPosition, Is.GreaterThanOrEqualTo(0));
            Assert.That(lastNextPosition, Is.LessThan(firstSetDestination));
            Assert.That(agents[1].NextPosition, Is.EqualTo(new Vector3(5f, 0f, 5f)));
            Assert.That(agents[2].NextPosition, Is.EqualTo(new Vector3(6f, 0f, 6f)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_order_is_stopped_false_immediately_before_each_set_destination()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            _recorder.Clear();

            _service.Tick();

            int seen = 0;
            for (int i = 0; i < _recorder.Count; i++)
            {
                if (_recorder[i].EndsWith(".SetDestination"))
                {
                    seen++;
                    Assert.That(i, Is.GreaterThan(0));
                    string agentName = _recorder[i].Substring(0, _recorder[i].IndexOf('.'));
                    Assert.That(_recorder[i - 1], Is.EqualTo(agentName + ".IsStopped=False"));
                }
            }

            Assert.That(seen, Is.EqualTo(2));
            Assert.That(agents[1].IsStopped, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_order_tick_never_reads_desired_velocity()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            var source = new FakePositionSource();
            source.Positions[Id(1)] = POS;
            var step = new NavigationPhaseStep(_service, source);

            step.Run(1u, 0.05f);

            Assert.That(agents[1].DesiredVelocityReads, Is.EqualTo(0));
            Assert.That(agents[2].DesiredVelocityReads, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_request_after_drain_issued_on_next_tick()
        {
            FakeNavAgent[] agents = InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();
            _service.SetDestination(Id(1), Target(2));

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(_service.IsQueued(Id(1)), Is.True);

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(2));
            Assert.That(agents[1].LastDestination, Is.EqualTo(Target(2)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_respawn_with_stale_slot_issues_new_destination_once()
        {
            InitializeService();
            Spawn(1);
            _service.SetDestination(Id(1), Target(1));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(1), TYPE_A, POS));
            _service.SetDestination(Id(1), Target(2));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.HasLatestRequest(Id(1), out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(Target(2)));
            FakeNavAgent live = null;
            foreach (FakeNavAgent a in _factory.Created)
            {
                if (a.Enabled)
                {
                    live = a;
                }
            }

            Assert.That(live, Is.Not.Null);

            _service.Tick();

            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(1));
            Assert.That(live.SetDestinationCalls, Is.EqualTo(1));
            Assert.That(live.LastDestination, Is.EqualTo(Target(2)));
            Assert.That(_service.IsQueued(Id(1)), Is.False);

            _service.SetDestination(Id(1), Target(3));

            Assert.That(_service.IsQueued(Id(1)), Is.True);
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_stale_slot_without_new_request_makes_no_call()
        {
            InitializeService();
            Spawn(1);
            _service.SetDestination(Id(1), Target(1));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(1), TYPE_A, POS));

            _service.Tick();

            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(0));
            Assert.That(_service.IsQueued(Id(1)), Is.False);
            Assert.That(_service.HasLatestRequest(Id(1), out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_stop_then_set_destination_before_drain_issues_new_target()
        {
            FakeNavAgent[] agents = InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _service.Stop(Id(1));
            _service.SetDestination(Id(1), Target(2));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(agents[1].LastDestination, Is.EqualTo(Target(2)));
            Assert.That(agents[1].IsStopped, Is.False);
            Assert.That(_service.IsQueued(Id(1)), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_stop_then_resume_before_drain_makes_no_call()
        {
            FakeNavAgent[] agents = InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _service.Stop(Id(1));
            _service.Resume(Id(1));

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_drain_then_release_leaves_no_tracked_state()
        {
            InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_release_then_drain_empties_queue_and_tracked_state()
        {
            InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));

            _service.Tick();

            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_trailing_skip_after_r_stays_queued()
        {
            BuildService(1);
            FakeNavAgent[] agents = InitAndSpawn(2);
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            _service.Stop(Id(2));

            _service.Tick();

            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.IsQueued(Id(2)), Is.True);
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(1));

            _service.Tick();

            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(_service.IsQueued(Id(2)), Is.False);
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(1));
            Assert.That(agents[2].SetDestinationCalls, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_step_clears_buffer_between_runs()
        {
            FakeNavAgent[] agents = InitAndSpawn(1);
            var source = new FakePositionSource();
            source.Positions[Id(1)] = new Vector3(5f, 0f, 5f);
            source.Positions[Id(99)] = new Vector3(8f, 0f, 8f);
            var step = new NavigationPhaseStep(_service, source);

            Assert.DoesNotThrow(() => step.Run(1u, 0.05f));
            Assert.That(agents[1].NextPositionSets, Is.EqualTo(1));

            source.Positions.Clear();
            step.Run(2u, 0.05f);

            Assert.That(agents[1].NextPositionSets, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_step_empty_source_nothing_queued_no_agent_calls()
        {
            FakeNavAgent[] agents = InitAndSpawn(2);
            var step = new NavigationPhaseStep(_service, new EmptyPositions());
            _recorder.Clear();

            step.Run(1u, 0.05f);

            Assert.That(_recorder, Is.Empty);
            Assert.That(agents[1].NextPositionSets + agents[2].NextPositionSets, Is.EqualTo(0));
            Assert.That(TotalSetDestinationCalls(), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Failure paths ----

        [Test]
        public void test_tick_drain_set_destination_false_warns_removes_request_counts_toward_limit()
        {
            BuildService(1);
            FakeNavAgent[] agents = InitAndSpawn(2);
            agents[1].SetDestinationResult = false;
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            LogAssert.Expect(LogType.Warning, new Regex(@"SetDestination rejected"));

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(agents[1].IsStopped, Is.False);
            Assert.That(_service.HasLatestRequest(Id(1), out _), Is.False);
            Assert.That(_service.IsQueued(Id(1)), Is.False);
            Assert.That(agents[2].SetDestinationCalls, Is.EqualTo(0));
            Assert.That(_service.IsQueued(Id(2)), Is.True);
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_set_destination_throws_propagates_state_consistent_next_tick_issues_rest()
        {
            BuildService(1);
            FakeNavAgent[] agents = InitAndSpawn(2);
            agents[1].ThrowOnSetDestination = true;
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));

            Assert.Throws<InvalidOperationException>(() => _service.Tick());

            Assert.That(_service.IsQueued(Id(1)), Is.False);
            Assert.That(_service.HasLatestRequest(Id(1), out _), Is.False);
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));

            _service.Tick();

            Assert.That(agents[2].SetDestinationCalls, Is.EqualTo(1));
            Assert.That(agents[2].LastDestination, Is.EqualTo(Target(2)));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_off_mesh_skip_keeps_latest_request_later_request_re_enqueues()
        {
            FakeNavAgent[] agents = InitAndSpawn(1);
            agents[1].IsOnNavMesh = false;
            _service.SetDestination(Id(1), Target(1));

            _service.Tick();

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(0));
            Assert.That(_service.HasLatestRequest(Id(1), out Vector3 kept), Is.True);
            Assert.That(kept, Is.EqualTo(Target(1)));
            Assert.That(_service.IsQueued(Id(1)), Is.False);

            _service.SetDestination(Id(1), Target(2));

            Assert.That(_service.IsQueued(Id(1)), Is.True);
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Teardown and construction ----

        [Test]
        public void test_tick_drain_after_teardown_tick_and_sync_do_not_throw_or_call_agents()
        {
            // Teardown also clears the registry, so this is a no-throw smoke test.
            FakeNavAgent[] agents = InitAndSpawn(1);
            _service.SetDestination(Id(1), Target(1));
            _service.Teardown();
            var positions = new Dictionary<EntityID, Vector3> { { Id(1), POS } };

            _service.Tick();
            _service.SyncAgentPositions(positions);
            _service.SyncAgentPositions((IReadOnlyDictionary<EntityID, Vector3>)positions);

            Assert.That(agents[1].SetDestinationCalls, Is.EqualTo(0));
            Assert.That(agents[1].NextPositionSets, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_drain_step_null_service_or_source_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(
                () => new NavigationPhaseStep(null, new EmptyPositions()));
            Assert.Throws<ArgumentNullException>(() => new NavigationPhaseStep(_service, null));
        }
    }
}

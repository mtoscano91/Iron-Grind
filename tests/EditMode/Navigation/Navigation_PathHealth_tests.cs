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
    /// EditMode unit tests for Navigation Story 006: path health (partial timeout, off-mesh recovery, stuck re-route,
    /// rejected-SetDestination rate limit; CR-NAV-15, F-NAV-2, EC-NAV-8, EC-NAV-10, AC-NAV-17, 19, 23).
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_PathHealth_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);
        private static readonly Vector3 POS = new Vector3(1f, 2f, 3f);
        private static readonly Vector3 COMMITTED = new Vector3(5f, 0f, 5f);
        private const int POOL_SIZE = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
        private const int T = NavigationConfigValues.PARTIAL_PATH_TIMEOUT_TICKS_DEFAULT;
        private const int STUCK_N = NavigationConfigValues.NAV_STUCK_REROUTE_DETECTION_TICKS_DEFAULT;
        private const int OFF_N = NavigationConfigValues.NAV_OFF_MESH_RECOVERY_TICKS_DEFAULT;

        // A mid-range tick count: must stay below T (partial timeout) and below STUCK_N (stuck error), so a test that
        // uses it never trips either threshold by accident. Both defaults are well above it.
        private const int MID_TICKS = 4;

        // Tracked state after ClaimAndIssue: active agent + requested destination + health entry, nothing queued.
        private const int TRACKED_AFTER_CLAIM_AND_ISSUE = 3;

        private NavigationConfig _config;
        private FakeNavAgentFactory _factory;
        private FakeNavMeshInstanceOps _ops;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private NavMeshDataInstance _instanceA;
        private readonly HashSet<FakeNavAgent> _claimed = new HashSet<FakeNavAgent>();

        private static EntityID Id(int i) => new EntityID(100u + (uint)i);

        private static Vector3 Target(int i) => new Vector3(10f + i, 0f, -i);

        [SetUp]
        public void SetUp()
        {
            _claimed.Clear();
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _ops = new FakeNavMeshInstanceOps();
            _instanceA = NavigationTestSupport.MakeInstance(1);
            _bus = new MobEventBus();
            _factory = new FakeNavAgentFactory();
            BuildService(T);
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

        private void BuildService(int partialTimeout)
        {
            _config.SetForTesting(new NavigationConfigValues(
                POOL_SIZE, NavigationConfigValues.MAX_PATH_UPDATES_PER_TICK_DEFAULT, partialTimeout,
                NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT, NavigationConfigValues.MIN_VALID_SPAWN_FRACTION_DEFAULT,
                STUCK_N, OFF_N, NavigationConfigValues.SPAWN_SAMPLE_RADIUS_DEFAULT));
            var speeds = new FakeSpeedSource();
            speeds.Speeds[TYPE_A] = 3.5f;
            _service = new ZoneNavigationService(_config, _factory, speeds, new FakeMeshQuery(), _ops, _bus);
        }

        private void InitializeService()
        {
            _ops.Add(_instanceA);
            _service.Initialize(ZONE, _instanceA);
        }

        // Claims entity i and returns the agent created last that is enabled and not yet used by an earlier entity.
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

        // One claimed agent for entity 1 with a drained request: the next ticks only run the health pass.
        private FakeNavAgent ClaimAndIssue(int i = 1)
        {
            InitializeService();
            FakeNavAgent agent = Spawn(i);
            _service.SetDestination(Id(i), Target(i));
            _service.Tick();
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(1));
            return agent;
        }

        // Claims entity 1 whose first drained call is rejected (one rejection warning expected and registered here).
        private FakeNavAgent ClaimAndIssueRejected()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.SetDestinationResult = false;
            LogAssert.Expect(LogType.Warning, Pattern("SetDestination rejected", Id(1).ToString()));
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(1));
            return agent;
        }

        private void Ticks(int n)
        {
            for (int k = 0; k < n; k++)
            {
                _service.Tick();
            }
        }

        private void Sync(EntityID entity, Vector3 position)
        {
            var positions = new Dictionary<EntityID, Vector3> { [entity] = position };
            _service.SyncAgentPositions(positions);
        }

        private static Regex Pattern(params string[] parts)
        {
            var escaped = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                escaped[i] = Regex.Escape(parts[i]);
            }

            return new Regex(string.Join(".*", escaped));
        }

        private void ExpectOffMeshWarning(int i, Vector3 warpTo)
        {
            LogAssert.Expect(LogType.Warning, Pattern(ZONE.ToString(), Id(i).ToString(), "off the NavMesh", warpTo.ToString()));
        }

        // ---- Partial counter ----

        [Test]
        public void test_path_health_partial_counts_per_tick_and_goes_stale_at_timeout()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;

            Ticks(T - 1);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(T - 1));
            Assert.That(_service.IsPathStale(Id(1)), Is.False);

            Ticks(1);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(T));
            Assert.That(_service.IsPathStale(Id(1)), Is.True);
        }

        [Test]
        public void test_path_health_partial_timeout_from_config_19_goes_stale_at_19()
        {
            _service.Teardown();
            BuildService(19);
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;

            Ticks(18);
            Assert.That(_service.IsPathStale(Id(1)), Is.False);

            Ticks(1);
            Assert.That(_service.IsPathStale(Id(1)), Is.True);
        }

        [Test]
        public void test_path_health_partial_stopped_does_not_count()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            agent.IsStoppedValue = true;

            Ticks(MID_TICKS);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_partial_pending_does_not_count()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            agent.PathPending = true;

            Ticks(MID_TICKS + 1);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));
        }

        [Test]
        public void test_path_health_partial_resets_when_status_becomes_complete()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));

            agent.PathStatus = NavMeshPathStatus.PathComplete;
            Ticks(1);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_partial_resets_on_fresh_set_destination_same_tick()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));

            _service.SetDestination(Id(1), Target(2));
            Ticks(1);

            Assert.That(agent.SetDestinationCalls, Is.EqualTo(2));
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_partial_resets_on_rejected_set_destination()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            agent.SetDestinationResult = false;
            LogAssert.Expect(LogType.Warning, Pattern("SetDestination rejected", Id(1).ToString()));

            _service.SetDestination(Id(1), Target(2));
            Ticks(1);

            Assert.That(agent.SetDestinationCalls, Is.EqualTo(2));
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_partial_path_invalid_leaves_count_unchanged()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);

            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(2); // far below STUCK_N, so no stuck error

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));
            Assert.That(_service.IsPathStale(Id(1)), Is.True); // stale through the PathInvalid row
        }

        [Test]
        public void test_path_health_partial_count_is_frozen_while_off_mesh()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);

            agent.IsOnNavMesh = false;
            Ticks(OFF_N - 1); // below the recovery threshold: no Warp, no warning

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- IsPathStale precedence ----

        [Test]
        public void test_path_health_is_path_stale_pending_wins_over_partial_timeout()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(T);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.GreaterThanOrEqualTo(T));
            Assert.That(_service.IsPathStale(Id(1)), Is.True);

            agent.PathPending = true;

            Assert.That(_service.IsPathStale(Id(1)), Is.False);
        }

        [Test]
        public void test_path_health_is_path_stale_off_mesh_is_true_even_when_pending()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathPending = true;
            Assert.That(_service.IsPathStale(Id(1)), Is.False);

            agent.IsOnNavMesh = false;

            Assert.That(_service.IsPathStale(Id(1)), Is.True);
        }

        [Test]
        public void test_path_health_is_path_stale_never_requested_is_true()
        {
            InitializeService();
            Spawn(1);

            Assert.That(_service.IsPathStale(Id(1)), Is.True);
        }

        [Test]
        public void test_path_health_is_path_stale_partial_below_timeout_is_false()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;

            Ticks(T - 1);

            Assert.That(_service.IsPathStale(Id(1)), Is.False);
        }

        // ---- Off-mesh recovery ----

        [Test]
        public void test_path_health_off_mesh_warps_to_committed_position_at_third_tick_with_one_warning()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            Sync(Id(1), COMMITTED);
            int baseline = agent.WarpCalls;
            agent.IsOnNavMesh = false;

            Ticks(OFF_N - 1);

            Assert.That(agent.WarpCalls, Is.EqualTo(baseline));
            Assert.That(_service.OffMeshTickCount(Id(1)), Is.EqualTo(OFF_N - 1));

            ExpectOffMeshWarning(1, COMMITTED);
            Ticks(1);

            Assert.That(agent.WarpCalls, Is.EqualTo(baseline + 1));
            Assert.That(agent.LastWarpPosition, Is.EqualTo(COMMITTED));
            Assert.That(_service.OffMeshTickCount(Id(1)), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_on_mesh_tick_resets_count_no_warp()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            int baseline = agent.WarpCalls;

            agent.IsOnNavMesh = false;
            Ticks(OFF_N - 1);
            agent.IsOnNavMesh = true;
            Ticks(1);
            agent.IsOnNavMesh = false;
            Ticks(OFF_N - 1);

            Assert.That(agent.WarpCalls, Is.EqualTo(baseline));
            Assert.That(_service.OffMeshTickCount(Id(1)), Is.EqualTo(OFF_N - 1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_retries_every_n_ticks_with_one_warning()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            int baseline = agent.WarpCalls;
            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);

            Ticks(2 * OFF_N);

            Assert.That(agent.WarpCalls, Is.EqualTo(baseline + 2));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_new_streak_after_recovery_warns_again()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);
            Ticks(OFF_N);
            agent.IsOnNavMesh = true;
            Ticks(1);
            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);

            Ticks(OFF_N);

            Assert.That(agent.WarpCalls, Is.EqualTo(1 + 2)); // claim + one Warp per streak (two streaks)
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_warp_false_logs_error_with_zone_entity_position()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            Sync(Id(1), COMMITTED);
            agent.IsOnNavMesh = false;
            agent.WarpResult = false;
            ExpectOffMeshWarning(1, COMMITTED);
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), COMMITTED.ToString()));

            Ticks(OFF_N);

            Assert.That(agent.WarpCalls, Is.EqualTo(1 + 1)); // claim + the failed recovery Warp
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_warp_false_retries_with_one_error_and_one_warning_per_streak()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            Sync(Id(1), COMMITTED);
            agent.IsOnNavMesh = false;
            agent.WarpResult = false;
            ExpectOffMeshWarning(1, COMMITTED);
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), COMMITTED.ToString()));

            Ticks(OFF_N);
            Ticks(OFF_N); // second Warp: no second warning, no second error

            Assert.That(agent.WarpCalls, Is.EqualTo(1 + 2)); // claim + two recovery Warps
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_warp_false_new_streak_logs_the_error_again()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.IsOnNavMesh = false;
            agent.WarpResult = false;
            ExpectOffMeshWarning(1, POS);
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), POS.ToString()));
            Ticks(OFF_N);
            agent.IsOnNavMesh = true;
            Ticks(1);
            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), POS.ToString()));

            Ticks(OFF_N);

            Assert.That(agent.WarpCalls, Is.EqualTo(1 + 2)); // claim + one failed Warp per streak
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_warp_throws_propagates_out_of_tick()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.IsOnNavMesh = false;
            agent.ThrowOnWarp = true;
            Ticks(OFF_N - 1);
            ExpectOffMeshWarning(1, POS);

            Assert.Throws<System.InvalidOperationException>(() => _service.Tick());
        }

        [Test]
        public void test_path_health_off_mesh_is_per_entity()
        {
            InitializeService();
            FakeNavAgent a1 = Spawn(1);
            FakeNavAgent a2 = Spawn(2);
            int b1 = a1.WarpCalls;
            int b2 = a2.WarpCalls;
            a1.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);

            Ticks(OFF_N);

            Assert.That(a1.WarpCalls, Is.EqualTo(b1 + 1));
            Assert.That(a2.WarpCalls, Is.EqualTo(b2));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_never_committed_warps_to_spawn_position()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            Assert.That(_service.LastKnownCommittedPosition(Id(1)), Is.EqualTo(POS));
            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);

            Ticks(OFF_N);

            Assert.That(agent.LastWarpPosition, Is.EqualTo(POS));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_off_mesh_successful_warp_clears_request_and_counters_and_allows_a_new_issue()
        {
            // Rejected first call and a partial path give non-zero partial and stuck counts before the recovery.
            FakeNavAgent agent = ClaimAndIssueRejected();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS));
            Assert.That(_service.HasRequestedDestination(Id(1)), Is.True);

            agent.IsOnNavMesh = false;
            ExpectOffMeshWarning(1, POS);
            Ticks(OFF_N); // the recovery Warp succeeds (WarpResult stays true)
            agent.IsOnNavMesh = true; // the fake does not flip it; the real agent is back on the mesh after Warp

            Assert.That(_service.IsPathStale(Id(1)), Is.True); // "never set": a real Warp clears the path
            Assert.That(_service.HasRequestedDestination(Id(1)), Is.False);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));

            agent.SetDestinationResult = true;
            _service.SetDestination(Id(1), Target(1));
            Ticks(1);

            Assert.That(agent.SetDestinationCalls, Is.EqualTo(2));
            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.HasRequestedDestination(Id(1)), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_sync_never_warps_and_keeps_committed_position_when_off_mesh()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            Sync(Id(1), COMMITTED);
            int baseline = agent.WarpCalls;
            agent.IsOnNavMesh = false;

            Sync(Id(1), new Vector3(99f, 0f, 99f));

            Assert.That(agent.WarpCalls, Is.EqualTo(baseline));
            Assert.That(_service.LastKnownCommittedPosition(Id(1)), Is.EqualTo(COMMITTED));
        }

        [Test]
        public void test_path_health_sync_for_unknown_entity_does_not_throw()
        {
            InitializeService();

            Assert.DoesNotThrow(() => Sync(Id(9), COMMITTED));

            Assert.That(_service.LastKnownCommittedPosition(Id(9)), Is.EqualTo(Vector3.zero));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Stuck re-route ----

        [Test]
        public void test_path_health_stuck_invalid_path_logs_one_error_at_threshold_per_streak()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;

            Ticks(STUCK_N - 1);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(STUCK_N - 1));

            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), "stuck", Target(1).ToString()));
            Ticks(1);
            Ticks(STUCK_N); // no second error in the same streak

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(2 * STUCK_N));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_target_change_resets_streak_and_allows_a_new_error()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            LogAssert.Expect(LogType.Error, Pattern("stuck"));
            Ticks(STUCK_N);

            _service.SetDestination(Id(1), Target(2));
            _service.Tick(); // health +1, then drain resets on the new target

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));

            LogAssert.Expect(LogType.Error, Pattern("stuck", Target(2).ToString()));
            Ticks(STUCK_N);
        }

        [Test]
        public void test_path_health_stuck_same_target_reissued_does_not_reset_streak()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(MID_TICKS);

            _service.SetDestination(Id(1), Target(1));
            _service.Tick();

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS + 1));
        }

        [Test]
        public void test_path_health_stuck_nearly_equal_target_counts_as_a_different_target()
        {
            // Vector3 operator == treats 1e-6 as equal; the stuck rule needs exact equality.
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(MID_TICKS);
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS));

            _service.SetDestination(Id(1), Target(1) + new Vector3(1e-6f, 0f, 0f));
            _service.Tick();

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_stuck_valid_path_resets_streak()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(MID_TICKS);

            agent.PathStatus = NavMeshPathStatus.PathComplete;
            Ticks(1);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_stuck_error_fires_again_after_a_valid_path_breaks_the_streak()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), "stuck"));
            Ticks(STUCK_N);

            agent.PathStatus = NavMeshPathStatus.PathComplete; // valid path with an accepted last call
            Ticks(1);
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));

            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), "stuck"));
            Ticks(STUCK_N);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(STUCK_N));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_pending_does_not_count()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(MID_TICKS);
            agent.PathPending = true;

            Ticks(STUCK_N);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_rejected_calls_log_one_error_at_threshold()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.SetDestinationResult = false;
            LogAssert.Expect(LogType.Warning, Pattern("SetDestination rejected", Id(1).ToString()));

            // First call issues; every following tick re-requests the same target and is rejected again.
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();
            for (int k = 0; k < STUCK_N - 1; k++)
            {
                _service.SetDestination(Id(1), Target(1));
                _service.Tick();
            }

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(STUCK_N - 1));

            LogAssert.Expect(LogType.Error, Pattern(ZONE.ToString(), Id(1).ToString(), "stuck", Target(1).ToString()));
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();

            Assert.That(agent.SetDestinationCalls, Is.EqualTo(STUCK_N + 1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_stopped_agent_with_path_invalid_logs_no_error_and_keeps_streak_zero()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            agent.IsStoppedValue = true;

            Ticks(STUCK_N + 5);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_stopped_agent_with_rejected_last_call_logs_no_error_and_keeps_streak_zero()
        {
            FakeNavAgent agent = ClaimAndIssueRejected();
            agent.IsStoppedValue = true;

            Ticks(STUCK_N + 5);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_stuck_stopped_agent_resets_a_running_streak()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathInvalid;
            Ticks(MID_TICKS);
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS));

            agent.IsStoppedValue = true;
            Ticks(1);

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_stuck_stop_clears_streak_and_target_so_a_later_resume_logs_no_error()
        {
            FakeNavAgent agent = ClaimAndIssueRejected();
            Ticks(MID_TICKS);
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(MID_TICKS));

            _service.Stop(Id(1));

            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));

            // Resume without a new request: nothing is issued, so the rejected last call must not keep counting.
            _service.Resume(Id(1));
            Ticks(STUCK_N + 5);

            Assert.That(agent.IsStoppedValue, Is.False);
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Rejected SetDestination rate limit ----

        [Test]
        public void test_path_health_rejected_warning_only_on_first_consecutive_rejection()
        {
            InitializeService();
            FakeNavAgent agent = Spawn(1);
            agent.SetDestinationResult = false;
            LogAssert.Expect(LogType.Warning, Pattern("SetDestination rejected", Id(1).ToString()));

            for (int k = 0; k < 5; k++) // 5 consecutive rejections, well below STUCK_N
            {
                _service.SetDestination(Id(1), Target(1));
                _service.Tick();
            }

            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(5));
            LogAssert.NoUnexpectedReceived();

            agent.SetDestinationResult = true;
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();
            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(0));

            agent.SetDestinationResult = false;
            LogAssert.Expect(LogType.Warning, Pattern("SetDestination rejected", Id(1).ToString()));
            _service.SetDestination(Id(1), Target(1));
            _service.Tick();

            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Bookkeeping ----

        [Test]
        public void test_path_health_release_clears_counters_and_reclaim_starts_at_zero()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(1), TYPE_A, POS));

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.OffMeshTickCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.LastKnownCommittedPosition(Id(1)), Is.EqualTo(POS));
        }

        [Test]
        public void test_path_health_release_with_a_rejected_call_clears_the_failure_count()
        {
            ClaimAndIssueRejected();
            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(1));

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, Id(1)));

            Assert.That(_service.FailureCount(Id(1)), Is.EqualTo(0));
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
        }

        [Test]
        public void test_path_health_claim_with_throwing_warp_tracks_no_state()
        {
            InitializeService();
            _factory.Created[POOL_SIZE - 1].ThrowOnWarp = true; // the stack pops the last pushed agent first
            LogAssert.Expect(LogType.Exception, new Regex(@"warp exploded"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(1), TYPE_A, POS));

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_claim_with_warp_returning_false_tracks_no_state()
        {
            InitializeService();
            _factory.Created[POOL_SIZE - 1].WarpResult = false;
            LogAssert.Expect(LogType.Error, new Regex(@"Warp failed for"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, Id(1), TYPE_A, POS));

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_two_agents_keep_independent_partial_and_stuck_counts()
        {
            InitializeService();
            FakeNavAgent partialAgent = Spawn(1);
            FakeNavAgent invalidAgent = Spawn(2);
            partialAgent.PathStatus = NavMeshPathStatus.PathPartial;
            invalidAgent.PathStatus = NavMeshPathStatus.PathInvalid;
            _service.SetDestination(Id(1), Target(1));
            _service.SetDestination(Id(2), Target(2));
            _service.Tick(); // both issued; the counters start after this tick

            Ticks(MID_TICKS);

            Assert.That(_service.PartialPathTickCount(Id(1)), Is.EqualTo(MID_TICKS));
            Assert.That(_service.StuckStreak(Id(1)), Is.EqualTo(0));
            Assert.That(_service.PartialPathTickCount(Id(2)), Is.EqualTo(0));
            Assert.That(_service.StuckStreak(Id(2)), Is.EqualTo(MID_TICKS));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_path_health_teardown_clears_all_tracked_state()
        {
            FakeNavAgent agent = ClaimAndIssue();
            agent.PathStatus = NavMeshPathStatus.PathPartial;
            Ticks(MID_TICKS);
            Assert.That(_service.TrackedStateCount, Is.EqualTo(TRACKED_AFTER_CLAIM_AND_ISSUE));

            _service.Teardown();

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
        }
    }
}

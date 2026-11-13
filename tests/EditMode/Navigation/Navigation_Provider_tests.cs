using System.Collections.Generic;
using System.Reflection;
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
    /// EditMode unit tests for Navigation Story 004: the INavigationProvider calls (CR-NAV-7 to 11, CR-NAV-16,
    /// EC-NAV-1, EC-NAV-3, AC-NAV-08 to 13, 22, 24).
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_Provider_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);
        private static readonly EntityID E1 = new EntityID(100u);
        private static readonly Vector3 POS = new Vector3(1f, 2f, 3f);
        private static readonly Vector3 TARGET_A = new Vector3(10f, 0f, 10f);
        private static readonly Vector3 TARGET_B = new Vector3(-4f, 0f, 8f);
        private const int POOL_SIZE = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
        private const float START_SPEED = 3.7f;

        // ---- Fixture state ----

        private NavigationConfig _config;
        private FakeNavAgentFactory _factory;
        private FakeNavMeshInstanceOps _ops;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private NavMeshDataInstance _instanceA;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _ops = new FakeNavMeshInstanceOps();
            _instanceA = NavigationTestSupport.MakeInstance(1);
            _bus = new MobEventBus();
            _factory = new FakeNavAgentFactory();
            var speeds = new FakeSpeedSource();
            speeds.Speeds[TYPE_A] = START_SPEED;
            _service = new ZoneNavigationService(
                _config, _factory, speeds, new FakeMeshQuery(), _ops, _bus);
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

        private void InitializeService()
        {
            _ops.Add(_instanceA);
            _service.Initialize(ZONE, _instanceA);
        }

        // Initialises, spawns E1 and returns its agent (the only enabled one). Single-claim tests only.
        private FakeNavAgent Claim(EntityID entity)
        {
            InitializeService();
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, entity, TYPE_A, POS));
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            FakeNavAgent found = null;
            foreach (FakeNavAgent a in _factory.Created)
            {
                if (a.Enabled)
                {
                    found = a;
                }
            }

            Assert.That(found, Is.Not.Null);
            return found;
        }

        private static void ExpectUnregisteredWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"unregistered EntityID\(100\)"));
        }

        // ---- SetDestination ----

        [Test]
        public void test_provider_set_destination_twice_keeps_latest_and_one_queue_entry()
        {
            FakeNavAgent agent = Claim(E1);

            _service.SetDestination(E1, TARGET_A);
            _service.SetDestination(E1, TARGET_B);

            Assert.That(_service.HasLatestRequest(E1, out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(TARGET_B));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.IsQueued(E1), Is.True);
            Assert.That(_service.HasRequestedDestination(E1), Is.True);
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_after_stop_keeps_one_queue_entry_and_new_latest()
        {
            Claim(E1);

            _service.SetDestination(E1, TARGET_A);
            _service.Stop(E1);
            _service.SetDestination(E1, TARGET_B);

            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.HasLatestRequest(E1, out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(TARGET_B));
            Assert.That(_service.IsQueued(E1), Is.True);
            Assert.That(_service.HasRequestedDestination(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_after_respawn_with_stale_slot_queues_new_request()
        {
            Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, E1, TYPE_A, POS));

            _service.SetDestination(E1, TARGET_B);

            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.HasLatestRequest(E1, out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(TARGET_B));
            Assert.That(_service.HasRequestedDestination(E1), Is.True);
            Assert.That(_service.IsQueued(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_after_death_and_reclaim_before_any_request_is_true()
        {
            Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, E1, TYPE_A, POS));

            Assert.That(_service.HasRequestedDestination(E1), Is.False);
            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_unregistered_warns_once_and_queues_nothing()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            _service.SetDestination(E1, TARGET_A);

            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(0f, float.NaN, 0f)]
        [TestCase(0f, 0f, float.NaN)]
        [TestCase(float.PositiveInfinity, 0f, 0f)]
        [TestCase(0f, float.NegativeInfinity, 0f)]
        [TestCase(0f, 0f, float.PositiveInfinity)]
        public void test_provider_set_destination_non_finite_target_warns_once_and_changes_nothing(float x, float y, float z)
        {
            Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            LogAssert.Expect(LogType.Warning, new Regex(@"non-finite target"));

            _service.SetDestination(E1, new Vector3(x, y, z));

            Assert.That(_service.HasLatestRequest(E1, out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(TARGET_A));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_non_finite_first_call_leaves_entity_out_of_every_set()
        {
            Claim(E1);
            LogAssert.Expect(LogType.Warning, new Regex(@"non-finite target"));

            _service.SetDestination(E1, new Vector3(float.NaN, 0f, 0f));

            Assert.That(_service.HasRequestedDestination(E1), Is.False);
            Assert.That(_service.HasLatestRequest(E1, out _), Is.False);
            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            Assert.That(_service.IsQueued(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- SetSpeed ----

        [TestCase(0f, 0.1f, true)]
        [TestCase(25f, 20f, true)]
        [TestCase(5f, 5f, false)]
        [TestCase(0.1f, 0.1f, false)]
        [TestCase(20f, 20f, false)]
        public void test_provider_set_speed_clamps_to_range_and_warns_only_when_clamped(float input, float expected, bool warns)
        {
            FakeNavAgent agent = Claim(E1);
            if (warns)
            {
                LogAssert.Expect(LogType.Warning, new Regex(@"clamped"));
            }

            _service.SetSpeed(E1, input);

            Assert.That(agent.Speed, Is.EqualTo(expected));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_provider_set_speed_non_finite_warns_once_and_leaves_speed_unchanged(float input)
        {
            FakeNavAgent agent = Claim(E1);
            LogAssert.Expect(LogType.Warning, new Regex(@"non-finite speed"));

            _service.SetSpeed(E1, input);

            Assert.That(agent.Speed, Is.EqualTo(START_SPEED));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_speed_unregistered_warns_once()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            _service.SetSpeed(E1, 5f);

            LogAssert.NoUnexpectedReceived();
        }

        // ---- Stop / Resume ----

        [Test]
        public void test_provider_stop_sets_is_stopped_keeps_path_and_drops_latest_request()
        {
            FakeNavAgent agent = Claim(E1);
            agent.IsStopped = false;
            _service.SetDestination(E1, TARGET_A);

            _service.Stop(E1);

            Assert.That(agent.IsStopped, Is.True);
            Assert.That(agent.ResetPathCalls, Is.EqualTo(0));
            Assert.That(_service.HasLatestRequest(E1, out _), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_stop_after_queued_set_destination_makes_no_agent_call_on_stub_tick()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);

            _service.Stop(E1);
            _service.Tick();

            Assert.That(_service.HasLatestRequest(E1, out _), Is.False);
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(0));
            Assert.That(agent.IsStopped, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_stop_twice_and_resume_twice_are_idempotent_without_warnings()
        {
            FakeNavAgent agent = Claim(E1);

            _service.Stop(E1);
            _service.Stop(E1);
            Assert.That(agent.IsStopped, Is.True);

            _service.Resume(E1);
            _service.Resume(E1);
            Assert.That(agent.IsStopped, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_speed_after_stop_applies_speed_and_stays_stopped()
        {
            FakeNavAgent agent = Claim(E1);
            _service.Stop(E1);

            _service.SetSpeed(E1, 5f);

            Assert.That(agent.Speed, Is.EqualTo(5f));
            Assert.That(agent.IsStopped, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_after_stop_without_prior_request_queues_and_stays_stopped()
        {
            FakeNavAgent agent = Claim(E1);
            _service.Stop(E1);

            _service.SetDestination(E1, TARGET_A);

            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.HasLatestRequest(E1, out Vector3 latest), Is.True);
            Assert.That(latest, Is.EqualTo(TARGET_A));
            Assert.That(agent.IsStopped, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_after_stop_follows_request_history()
        {
            Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            _service.Stop(E1);
            Assert.That(_service.IsPathStale(E1), Is.False);

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, E1, TYPE_A, POS));
            _service.Stop(E1);

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_set_destination_non_finite_on_unregistered_warns_only_unregistered()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            _service.SetDestination(E1, new Vector3(float.NaN, 0f, 0f));

            Assert.That(_service.PendingRequestCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_stop_unregistered_warns_once()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            _service.Stop(E1);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_resume_clears_is_stopped_only_and_velocity_follows_preserved_path()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            _service.Stop(E1);
            agent.DesiredVelocity = new Vector3(2f, 1f, -1f);
            int queueBefore = _service.PendingRequestCount;

            _service.Resume(E1);

            Assert.That(agent.IsStopped, Is.False);
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(0));
            Assert.That(agent.ResetPathCalls, Is.EqualTo(0));
            Assert.That(_service.PendingRequestCount, Is.EqualTo(queueBefore));
            Assert.That(_service.IsQueued(E1), Is.True);
            Assert.That(_service.HasLatestRequest(E1, out _), Is.False);
            Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(new Vector3(2f, 0f, -1f)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_resume_unregistered_warns_once_and_does_not_throw()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            Assert.DoesNotThrow(() => _service.Resume(E1));

            LogAssert.NoUnexpectedReceived();
        }

        // ---- IsPathStale ----

        [Test]
        public void test_provider_is_path_stale_unregistered_is_true_with_one_warning()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            Assert.That(_service.IsPathStale(E1), Is.True);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_off_mesh_is_true()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.IsOnNavMesh = false;

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_path_invalid_is_true()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.PathStatus = NavMeshPathStatus.PathInvalid;

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_agent_flag_is_true()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.IsPathStale = true;

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_never_requested_is_true()
        {
            Claim(E1);

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_path_pending_is_false()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.PathPending = true;

            Assert.That(_service.IsPathStale(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_path_complete_is_false()
        {
            Claim(E1);
            _service.SetDestination(E1, TARGET_A);

            Assert.That(_service.IsPathStale(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_partial_below_timeout_is_false()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.PathStatus = NavMeshPathStatus.PathPartial;

            Assert.That(_service.IsPathStale(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_off_mesh_beats_path_pending()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.IsOnNavMesh = false;
            agent.PathPending = true;

            Assert.That(_service.IsPathStale(E1), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_path_pending_beats_path_invalid()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            agent.PathPending = true;
            agent.PathStatus = NavMeshPathStatus.PathInvalid;

            Assert.That(_service.IsPathStale(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_is_path_stale_path_pending_beats_never_requested()
        {
            FakeNavAgent agent = Claim(E1);
            agent.PathPending = true;

            Assert.That(_service.IsPathStale(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- GetCurrentVelocity ----

        [Test]
        public void test_provider_get_current_velocity_returns_desired_velocity_xz_with_y_zero()
        {
            FakeNavAgent agent = Claim(E1);
            agent.IsStoppedValue = false; // the claim stops the agent; the drain clears it on the first SetDestination
            agent.DesiredVelocity = new Vector3(3f, 5f, -2f);

            Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(new Vector3(3f, 0f, -2f)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_get_current_velocity_stopped_agent_is_zero_even_with_stale_desired_velocity()
        {
            // A real agent still reports its old desiredVelocity in the frame isStopped is set (Story 009).
            FakeNavAgent agent = Claim(E1);
            agent.DesiredVelocity = new Vector3(3f, 0f, -2f);

            _service.Stop(E1);

            Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(Vector3.zero));
            Assert.That(agent.DesiredVelocityReads, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_get_current_velocity_after_resume_returns_desired_velocity()
        {
            FakeNavAgent agent = Claim(E1);
            agent.DesiredVelocity = new Vector3(3f, 0f, -2f);
            _service.Stop(E1);

            _service.Resume(E1);

            Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(new Vector3(3f, 0f, -2f)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_provider_get_current_velocity_unregistered_is_zero_with_one_warning()
        {
            InitializeService();
            ExpectUnregisteredWarning();

            Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(Vector3.zero));

            LogAssert.NoUnexpectedReceived();
        }

        // ---- Pool exhausted (AC-NAV-04 follow-up) ----

        [Test]
        public void test_provider_pool_exhausted_entity_gets_safe_defaults_with_one_warning_each()
        {
            InitializeService();
            for (int i = 0; i < POOL_SIZE; i++)
            {
                _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, new EntityID(1000u + (uint)i), TYPE_A, POS));
            }

            Assert.That(_service.ActiveCount, Is.EqualTo(POOL_SIZE));
            LogAssert.Expect(LogType.Error, new Regex(@"pool exhausted"));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, E1, TYPE_A, POS));
            LogAssert.Expect(LogType.Warning, new Regex(@"IsPathStale for unregistered EntityID\(100\)"));
            LogAssert.Expect(LogType.Warning, new Regex(@"GetCurrentVelocity for unregistered EntityID\(100\)"));

            bool stale = false;
            Vector3 velocity = Vector3.one;
            Assert.DoesNotThrow(() =>
            {
                stale = _service.IsPathStale(E1);
                velocity = _service.GetCurrentVelocity(E1);
            });

            Assert.That(stale, Is.True);
            Assert.That(velocity, Is.EqualTo(Vector3.zero));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- MobDied with a queued request (AC-NAV-22 state part) ----

        [Test]
        public void test_provider_mob_died_with_queued_request_clears_state_and_pools_agent()
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));

            Assert.That(_service.HasLatestRequest(E1, out _), Is.False);
            Assert.That(_service.HasRequestedDestination(E1), Is.False);
            Assert.That(_service.IsQueued(E1), Is.True); // stale slot stays by design
            Assert.That(_service.PendingRequestCount, Is.EqualTo(1));
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- After Teardown ----

        [TestCase("SetDestination")]
        [TestCase("SetSpeed")]
        [TestCase("Stop")]
        [TestCase("Resume")]
        [TestCase("IsPathStale")]
        [TestCase("GetCurrentVelocity")]
        public void test_provider_after_teardown_method_returns_safe_default_silently(string method)
        {
            FakeNavAgent agent = Claim(E1);
            _service.SetDestination(E1, TARGET_A);
            _service.Teardown();
            float speedBefore = agent.Speed;
            bool stoppedBefore = agent.IsStopped;
            int callsBefore = agent.SetDestinationCalls;

            switch (method)
            {
                case "SetDestination": _service.SetDestination(E1, TARGET_B); break;
                case "SetSpeed": _service.SetSpeed(E1, 5f); break;
                case "Stop": _service.Stop(E1); break;
                case "Resume": _service.Resume(E1); break;
                case "IsPathStale": Assert.That(_service.IsPathStale(E1), Is.True); break;
                case "GetCurrentVelocity": Assert.That(_service.GetCurrentVelocity(E1), Is.EqualTo(Vector3.zero)); break;
                default: Assert.Fail("unknown method " + method); break;
            }

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            Assert.That(agent.Speed, Is.EqualTo(speedBefore));
            Assert.That(agent.IsStopped, Is.EqualTo(stoppedBefore));
            Assert.That(agent.SetDestinationCalls, Is.EqualTo(callsBefore));
            Assert.That(agent.Enabled, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

using System;
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
    /// EditMode unit tests for Navigation Story 003: agent claim and release on the MobEventBus (CR-NAV-5, CR-NAV-6,
    /// EC-NAV-2 to EC-NAV-4, AC-NAV-04 to 07).
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_AgentClaim_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly ZoneID OTHER_ZONE = new ZoneID(9u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);
        private static readonly MobTypeID TYPE_UNKNOWN = new MobTypeID(99u);
        private static readonly EntityID E1 = new EntityID(100u);
        private static readonly Vector3 POS = new Vector3(1f, 2f, 3f);
        private const int POOL_SIZE = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
        private const float TYPE_A_SPEED = 3.7f;

        // ---- Fixture state ----

        private NavigationConfig _config;
        private List<string> _recorder;
        private FakeNavAgentFactory _factory;
        private FakeNavMeshInstanceOps _ops;
        private FakeSpeedSource _speeds;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private NavMeshDataInstance _instanceA;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _recorder = new List<string>();
            _ops = new FakeNavMeshInstanceOps(_recorder);
            _instanceA = NavigationTestSupport.MakeInstance(1);
            _speeds = new FakeSpeedSource();
            _speeds.Speeds[TYPE_A] = TYPE_A_SPEED;
            _bus = new MobEventBus();
            _factory = new FakeNavAgentFactory(_recorder);
            _service = new ZoneNavigationService(
                _config, _factory, _speeds, new FakeMeshQuery(), _ops, _bus);
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
            {
                UnityEngine.Object.DestroyImmediate(_config);
                _config = null;
            }
        }

        // Initialises with a valid instance and clears the recorder so only the test's own calls remain.
        private void InitializeService()
        {
            _ops.Add(_instanceA);
            _service.Initialize(ZONE, _instanceA);
            _recorder.Clear();
        }

        private void Spawn(EntityID entity, MobTypeID type, Vector3 position)
        {
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, entity, type, position));
        }

        private void Die(EntityID entity)
        {
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, entity));
        }

        private T Field<T>(string name)
        {
            FieldInfo f = typeof(ZoneNavigationService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, "field " + name + " not found");
            return (T)f.GetValue(_service);
        }

        private Dictionary<EntityID, INavAgent> ActiveAgents() => Field<Dictionary<EntityID, INavAgent>>("_activeAgents");

        private FakeNavAgent EnabledAgent()
        {
            FakeNavAgent found = null;
            foreach (FakeNavAgent a in _factory.Created)
            {
                if (a.EnabledValue)
                {
                    Assert.That(found, Is.Null, "more than one enabled agent");
                    found = a;
                }
            }

            Assert.That(found, Is.Not.Null, "no enabled agent");
            return found;
        }

        // ---- Subscription ----

        [Test]
        public void test_agent_claim_initialize_failure_subscribes_nothing()
        {
            // Instance not added, so Initialize throws.
            LogAssert.Expect(LogType.Error, new Regex(@"invalid NavMesh instance"));
            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));

            Spawn(E1, TYPE_A, POS);
            Die(E1);

            LogAssert.NoUnexpectedReceived();
            Assert.That(_factory.CreateCalls, Is.EqualTo(0));
            Assert.That(_recorder, Is.Empty);
        }

        [Test]
        public void test_agent_claim_after_teardown_outside_raise_events_reach_no_handler()
        {
            InitializeService();
            _service.Teardown();
            _recorder.Clear();

            Spawn(E1, TYPE_A, POS);
            Die(E1);

            LogAssert.NoUnexpectedReceived();
            Assert.That(_recorder, Is.Empty);
        }

        // ---- Claim ----

        [Test]
        public void test_agent_claim_spawn_claims_one_agent_in_adr_order_with_exact_speed_and_stopped()
        {
            InitializeService();

            Spawn(E1, TYPE_A, POS);

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE - 1));
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            FakeNavAgent agent = EnabledAgent();
            string n = agent.Name;
            Assert.That(_recorder, Is.EqualTo(new[]
            {
                n + ".Enabled=True", n + ".Warp", n + ".Speed", n + ".IsStopped=True",
            }));
            Assert.That(agent.LastWarpPosition, Is.EqualTo(POS));
            Assert.That(agent.SpeedValue, Is.EqualTo(TYPE_A_SPEED));
            Assert.That(agent.IsStoppedValue, Is.True);
            Assert.That(ActiveAgents()[E1], Is.SameAs(agent));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_warp_false_disables_agent_returns_it_and_logs_error_with_ids_and_position()
        {
            InitializeService();
            _factory.Created[POOL_SIZE - 1].WarpResult = false; // the stack pops the last pushed agent first
            LogAssert.Expect(LogType.Error, new Regex(
                @"Warp failed for ZoneID\(7\) EntityID\(100\) at " + Regex.Escape(POS.ToString())));

            Spawn(E1, TYPE_A, POS);

            FakeNavAgent agent = _factory.Created[POOL_SIZE - 1];
            Assert.That(agent.WarpCalls, Is.EqualTo(1));
            Assert.That(agent.EnabledValue, Is.False);
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_recorder, Is.EqualTo(new[]
            {
                agent.Name + ".Enabled=True", agent.Name + ".Warp", agent.Name + ".Enabled=False",
            }));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_agent_returned_after_warp_false_is_claimed_cleanly_by_the_next_spawn()
        {
            InitializeService();
            FakeNavAgent agent = _factory.Created[POOL_SIZE - 1];
            agent.WarpResult = false;
            LogAssert.Expect(LogType.Error, new Regex(@"Warp failed for"));
            Spawn(E1, TYPE_A, POS);
            agent.WarpResult = true;
            var e2 = new EntityID(200u);

            Spawn(e2, TYPE_A, POS);

            Assert.That(agent.EnabledValue, Is.True);
            Assert.That(agent.IsStoppedValue, Is.True);
            Assert.That(agent.SpeedValue, Is.EqualTo(TYPE_A_SPEED));
            Assert.That(ActiveAgents()[e2], Is.SameAs(agent));
            Assert.That(ActiveAgents().ContainsKey(E1), Is.False);
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE - 1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_unknown_mob_type_logs_error_and_touches_no_agent()
        {
            InitializeService();
            LogAssert.Expect(LogType.Error, new Regex(
                @"No valid move speed for EntityID\(100\) of type MobTypeID\(99\)"));

            Spawn(E1, TYPE_UNKNOWN, POS);

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void test_agent_claim_non_finite_speed_logs_error_and_touches_no_agent(float speed)
        {
            InitializeService();
            _speeds.Speeds[TYPE_A] = speed;
            LogAssert.Expect(LogType.Error, new Regex(
                @"No valid move speed for EntityID\(100\) of type MobTypeID\(5\)"));

            Spawn(E1, TYPE_A, POS);

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Speed clamp ----

        [TestCase(25f, 20f)]
        [TestCase(0f, 0.1f)]
        [TestCase(-3f, 0.1f)]
        public void test_agent_claim_out_of_range_speed_is_clamped_with_one_warning(float source, float expected)
        {
            InitializeService();
            _speeds.Speeds[TYPE_A] = source;
            LogAssert.Expect(LogType.Warning, new Regex(@"clamped"));

            Spawn(E1, TYPE_A, POS);

            Assert.That(EnabledAgent().SpeedValue, Is.EqualTo(expected));
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0.1f)]
        [TestCase(20f)]
        [TestCase(TYPE_A_SPEED)]
        public void test_agent_claim_in_range_speed_is_set_exactly_with_no_log(float source)
        {
            InitializeService();
            _speeds.Speeds[TYPE_A] = source;

            Spawn(E1, TYPE_A, POS);

            Assert.That(EnabledAgent().SpeedValue, Is.EqualTo(source));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_speed_bounds_constants_are_the_cr_nav_8_range()
        {
            Assert.That(NavigationConfigValues.AGENT_SPEED_MIN, Is.EqualTo(0.1f));
            Assert.That(NavigationConfigValues.AGENT_SPEED_MAX, Is.EqualTo(20.0f));
        }

        // ---- Double spawn (EC-NAV-2) ----

        [Test]
        public void test_agent_claim_double_spawn_logs_one_error_and_keeps_the_first_agent()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            INavAgent first = ActiveAgents()[E1];
            _recorder.Clear();
            LogAssert.Expect(LogType.Error, new Regex(@"Duplicate MobSpawned for EntityID\(100\)"));

            Spawn(E1, TYPE_A, new Vector3(9f, 9f, 9f));

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE - 1));
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            Assert.That(ActiveAgents()[E1], Is.SameAs(first));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Pool exhaustion (AC-NAV-04) ----

        [Test]
        public void test_agent_claim_151st_spawn_logs_one_pool_exhausted_error_and_does_not_throw()
        {
            InitializeService();
            var last = new EntityID(1000u + POOL_SIZE);
            LogAssert.Expect(LogType.Error, new Regex(@"pool exhausted.*" + Regex.Escape(last.ToString())));

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i <= POOL_SIZE; i++)
                {
                    Spawn(new EntityID(1000u + (uint)i), TYPE_A, POS);
                }
            });

            Assert.That(_service.ActiveCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            Assert.That(_factory.CreateCalls, Is.EqualTo(POOL_SIZE));
            Assert.That(ActiveAgents().ContainsKey(last), Is.False);

            // Fakes do not override Equals, so HashSet uses reference equality: 150 distinct agents.
            var distinct = new HashSet<INavAgent>(ActiveAgents().Values);
            Assert.That(distinct.Count, Is.EqualTo(POOL_SIZE));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Zone mismatch ----

        [Test]
        public void test_agent_claim_spawn_for_other_zone_warns_and_is_ignored()
        {
            InitializeService();
            LogAssert.Expect(LogType.Warning, new Regex(@"MobSpawned for ZoneID\(9\) ignored"));

            _bus.RaiseMobSpawned(new MobSpawnedArgs(OTHER_ZONE, E1, TYPE_A, POS));

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_died_for_other_zone_warns_and_leaves_claimed_mob_alone()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            _recorder.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(@"MobDied for ZoneID\(9\) ignored"));

            _bus.RaiseMobDied(new MobDiedArgs(OTHER_ZONE, E1));

            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Post-teardown spawn via bus snapshot (EC-NAV-4) ----

        [Test]
        public void test_agent_claim_spawn_reaching_handler_after_teardown_in_same_raise_logs_and_claims_nothing()
        {
            // Subscribed before Initialize, so it runs before the service handler and tears the service down mid-raise.
            _bus.OnMobSpawned += OnSpawnedTeardownService;
            InitializeService();
            LogAssert.Expect(LogType.Error, new Regex(@"post-teardown MobSpawned"));

            Spawn(E1, TYPE_A, POS);

            Assert.That(_service.IsAcceptingSpawns, Is.False);
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            foreach (FakeNavAgent a in _factory.Created)
            {
                Assert.That(a.WarpCalls, Is.EqualTo(0));
                Assert.That(a.SpeedValue, Is.EqualTo(0f));
            }

            Assert.That(_recorder.FindAll(s => s.EndsWith(".Warp") || s.EndsWith(".Speed") || s.EndsWith(".Enabled=True")),
                Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        private void OnSpawnedTeardownService(MobSpawnedArgs args)
        {
            _service.Teardown();
        }

        private void OnDiedTeardownService(MobDiedArgs args)
        {
            _service.Teardown();
        }

        [Test]
        public void test_agent_claim_died_reaching_handler_after_teardown_in_same_raise_warns_and_is_a_no_op()
        {
            _bus.OnMobDied += OnDiedTeardownService;
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            FakeNavAgent agent = EnabledAgent();
            _recorder.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(@"MobDied for unknown or already released EntityID\(100\)"));

            Assert.DoesNotThrow(() => Die(E1));

            Assert.That(agent.ResetPathCalls, Is.EqualTo(0));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
        }

        // ---- Teardown with a claimed agent ----

        [Test]
        public void test_agent_claim_teardown_with_claimed_agent_disables_it_then_clears_then_removes()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            FakeNavAgent claimed = EnabledAgent();
            _ops.OnRemoveSnapshot = () =>
                "Remove:available=" + _service.AvailableCount + ":active=" + _service.ActiveCount
                + ":state=" + _service.TrackedStateCount;
            _recorder.Clear();

            _service.Teardown();

            Assert.That(_recorder.Count, Is.EqualTo(POOL_SIZE + 1));
            Assert.That(_recorder, Does.Contain(claimed.Name + ".Enabled=False"));
            for (int i = 0; i < POOL_SIZE; i++)
            {
                Assert.That(_recorder[i], Does.EndWith(".Enabled=False"), "entry " + i);
            }

            Assert.That(_recorder[POOL_SIZE], Is.EqualTo("Remove:available=0:active=0:state=0"));
            Assert.That(claimed.EnabledValue, Is.False);
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
        }

        // ---- Release (CR-NAV-6) ----

        [Test]
        public void test_agent_claim_release_stops_resets_path_once_disables_and_returns_agent_to_pool()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            FakeNavAgent agent = EnabledAgent();
            _recorder.Clear();

            Die(E1);

            string n = agent.Name;
            Assert.That(_recorder, Is.EqualTo(new[] { n + ".IsStopped=True", n + ".ResetPath", n + ".Enabled=False" }));
            Assert.That(agent.ResetPathCalls, Is.EqualTo(1));
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(ActiveAgents().ContainsKey(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_release_removes_entity_from_latest_requests_and_failure_counts()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            var other = new EntityID(200u);
            Spawn(other, TYPE_A, POS);
            var latest = Field<Dictionary<EntityID, Vector3>>("_latestRequests");
            var failures = Field<Dictionary<EntityID, int>>("_failureCounts");
            latest[E1] = Vector3.one;
            latest[other] = Vector3.one;
            failures[E1] = 2;
            failures[other] = 1;
            var requested = Field<HashSet<EntityID>>("_destinationRequested");
            requested.Add(E1);
            requested.Add(other);
            Assert.That(_service.TrackedStateCount, Is.EqualTo(10)); // + one path-health entry per agent (Story 006)

            Die(E1);

            Assert.That(latest.ContainsKey(E1), Is.False);
            Assert.That(failures.ContainsKey(E1), Is.False);
            Assert.That(latest.ContainsKey(other), Is.True);
            Assert.That(failures.ContainsKey(other), Is.True);
            Assert.That(requested.Contains(E1), Is.False);
            Assert.That(requested.Contains(other), Is.True);
            Assert.That(_service.TrackedStateCount, Is.EqualTo(5));

            Die(other);

            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
        }

        [Test]
        public void test_agent_claim_release_unknown_entity_warns_once_and_changes_nothing()
        {
            InitializeService();
            LogAssert.Expect(LogType.Warning, new Regex(@"MobDied for unknown or already released EntityID\(100\)"));

            Assert.DoesNotThrow(() => Die(E1));

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_second_release_warns_and_does_not_double_return_the_agent()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            Die(E1);
            _recorder.Clear();
            LogAssert.Expect(LogType.Warning, new Regex(@"MobDied for unknown or already released EntityID\(100\)"));

            Assert.DoesNotThrow(() => Die(E1));

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_recorder, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_spawn_after_release_of_same_entity_claims_without_duplicate_error()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            Die(E1);

            Spawn(E1, TYPE_A, POS);

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE - 1));
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            Assert.That(Field<Dictionary<EntityID, Vector3>>("_latestRequests"), Is.Empty);
            Assert.That(Field<Dictionary<EntityID, int>>("_failureCounts"), Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_throwing_warp_logs_exception_and_returns_disabled_agent_to_pool()
        {
            InitializeService();
            FakeNavAgent agent = _factory.Created[POOL_SIZE - 1];
            agent.ThrowOnWarp = true;
            LogAssert.Expect(LogType.Exception, new Regex(@"agent150 warp exploded"));

            Assert.DoesNotThrow(() => Spawn(E1, TYPE_A, POS)); // the bus isolates and logs the throw

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
            Assert.That(agent.EnabledValue, Is.False);
            Assert.That(ActiveAgents().ContainsKey(E1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_throwing_reset_path_on_release_still_pools_disabled_agent_and_clears_state()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            FakeNavAgent agent = EnabledAgent();
            var latest = Field<Dictionary<EntityID, Vector3>>("_latestRequests");
            var failures = Field<Dictionary<EntityID, int>>("_failureCounts");
            latest[E1] = Vector3.one;
            failures[E1] = 1;
            agent.ThrowOnResetPath = true;
            LogAssert.Expect(LogType.Exception, new Regex(@"agent150 resetpath exploded"));

            Assert.DoesNotThrow(() => Die(E1));

            Assert.That(agent.EnabledValue, Is.False);
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(ActiveAgents().ContainsKey(E1), Is.False);
            Assert.That(latest.ContainsKey(E1), Is.False);
            Assert.That(failures.ContainsKey(E1), Is.False);
            Assert.That(_service.TrackedStateCount, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_agent_claim_released_agent_can_be_claimed_again_with_the_same_order()
        {
            InitializeService();
            Spawn(E1, TYPE_A, POS);
            FakeNavAgent agent = EnabledAgent();
            Die(E1);
            _recorder.Clear();

            Spawn(new EntityID(300u), TYPE_A, POS);

            string n = agent.Name;
            Assert.That(_recorder, Is.EqualTo(new[]
            {
                n + ".Enabled=True", n + ".Warp", n + ".Speed", n + ".IsStopped=True",
            }));
        }
    }
}

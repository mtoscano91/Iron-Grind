using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
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
    /// EditMode unit tests for Navigation Story 002: service lifecycle, agent pool fill, teardown order and the
    /// torn-down guards (AC-NAV-01 to 04, EC-NAV-4 state, EC-NAV-6).
    /// </summary>
    [TestFixture]
    internal sealed class Navigation_Lifecycle_Tests
    {
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private const int POOL_SIZE = 150;

        // ---- Fakes ----

        private sealed class FakeNavAgent : INavAgent
        {
            private readonly List<string> _recorder;
            private readonly int _id;

            public FakeNavAgent(int id, List<string> recorder)
            {
                _id = id;
                _recorder = recorder;
            }

            // Defaults mirror a fresh NavMeshAgent: everything on.
            public bool EnabledValue = true;
            public bool UpdatePositionValue = true;
            public bool UpdateRotationValue = true;
            public bool AutoBrakingValue = true;
            public bool ObstacleAvoidanceDisabled;

            // When true, setting Enabled = false throws (e.g. a destroyed agent) and records nothing.
            public bool ThrowOnDisable;

            public bool Enabled
            {
                get => EnabledValue;
                set
                {
                    if (!value && ThrowOnDisable)
                    {
                        throw new InvalidOperationException("agent" + _id + " cannot be disabled");
                    }

                    EnabledValue = value;
                    _recorder.Add("agent" + _id + ".Enabled=" + value);
                }
            }

            public bool UpdatePosition
            {
                get => UpdatePositionValue;
                set { UpdatePositionValue = value; _recorder.Add("agent" + _id + ".UpdatePosition=" + value); }
            }

            public bool UpdateRotation
            {
                get => UpdateRotationValue;
                set { UpdateRotationValue = value; _recorder.Add("agent" + _id + ".UpdateRotation=" + value); }
            }

            public bool AutoBraking
            {
                get => AutoBrakingValue;
                set { AutoBrakingValue = value; _recorder.Add("agent" + _id + ".AutoBraking=" + value); }
            }

            public void DisableObstacleAvoidance()
            {
                ObstacleAvoidanceDisabled = true;
                _recorder.Add("agent" + _id + ".ObstacleAvoidance=off");
            }

            public bool IsStopped { get; set; }
            public float Speed { get; set; }
            public bool IsOnNavMesh => true;
            public bool PathPending => false;
            public NavMeshPathStatus PathStatus => NavMeshPathStatus.PathComplete;
            public bool IsPathStale => false;
            public Vector3 DesiredVelocity => Vector3.zero;
            public Vector3 NextPosition { get; set; }
            public bool Warp(Vector3 position) => true;
            public bool SetDestination(Vector3 target) => true;
            public void ResetPath() { }
        }

        private sealed class FakeNavAgentFactory : INavAgentFactory
        {
            private readonly List<string> _recorder;
            private readonly int _throwOnCall;

            public readonly List<FakeNavAgent> Created = new List<FakeNavAgent>();
            public readonly List<string> RootNames = new List<string>();
            public int CreateCalls;

            // throwOnCall is 1-based; 0 means never throw.
            public FakeNavAgentFactory(List<string> recorder, int throwOnCall = 0)
            {
                _recorder = recorder;
                _throwOnCall = throwOnCall;
            }

            public INavAgent Create(string poolRootName)
            {
                CreateCalls++;
                RootNames.Add(poolRootName);
                if (CreateCalls == _throwOnCall)
                {
                    throw new InvalidOperationException("factory failed on call " + CreateCalls);
                }

                var agent = new FakeNavAgent(CreateCalls, _recorder);
                Created.Add(agent);
                return agent;
            }
        }

        // Models the process-global NavMesh: an instance is valid while it is added. Add() stands in for the scene
        // loader's AddNavMeshData; Remove() takes the instance out, as NavMeshDataInstance.Remove() does.
        private sealed class FakeNavMeshInstanceOps : INavMeshInstanceOps
        {
            private readonly List<string> _recorder;
            private readonly HashSet<NavMeshDataInstance> _added = new HashSet<NavMeshDataInstance>();

            public readonly List<NavMeshDataInstance> Removed = new List<NavMeshDataInstance>();
            public int IsValidCalls;

            // Hook invoked at Remove time so a test can record the service state at that moment.
            public Func<string> OnRemoveSnapshot;

            public FakeNavMeshInstanceOps(List<string> recorder)
            {
                _recorder = recorder;
            }

            public int RemoveCalls => Removed.Count;

            public void Add(NavMeshDataInstance instance)
            {
                _added.Add(instance);
            }

            public bool IsAdded(NavMeshDataInstance instance) => _added.Contains(instance);

            public bool IsValid(NavMeshDataInstance instance)
            {
                IsValidCalls++;
                return _added.Contains(instance);
            }

            public void Remove(NavMeshDataInstance instance)
            {
                _added.Remove(instance);
                Removed.Add(instance);
                _recorder.Add(OnRemoveSnapshot != null ? OnRemoveSnapshot() : "Remove");
            }
        }

        private sealed class FakeSpeedSource : INavMobSpeedSource
        {
            public bool TryGetMoveSpeed(MobTypeID mobType, out float speed)
            {
                speed = 1f;
                return true;
            }
        }

        private sealed class FakeMeshQuery : INavMeshQuery
        {
            public bool SamplePosition(Vector3 position, float maxDistance, int areaMask, out Vector3 hitPosition)
            {
                hitPosition = position;
                return true;
            }
        }

        // Builds a non-default instance by setting the struct's int backing field, so tests can tell instances apart
        // (every real instance needs added NavMesh data, which EditMode does not have).
        private static NavMeshDataInstance MakeInstance(int id)
        {
            FieldInfo field = null;
            foreach (FieldInfo f in typeof(NavMeshDataInstance).GetFields(BindingFlags.Instance | BindingFlags.NonPublic
                         | BindingFlags.Public))
            {
                if (f.FieldType == typeof(int))
                {
                    field = f;
                    break;
                }
            }

            Assert.That(field, Is.Not.Null, "NavMeshDataInstance has no int field to set; update MakeInstance");
            object boxed = default(NavMeshDataInstance);
            field.SetValue(boxed, id);
            return (NavMeshDataInstance)boxed;
        }

        // ---- Fixture state ----

        private NavigationConfig _config;
        private List<string> _recorder;
        private FakeNavAgentFactory _factory;
        private FakeNavMeshInstanceOps _ops;
        private ZoneNavigationService _service;
        private NavMeshDataInstance _instanceA;
        private NavMeshDataInstance _instanceB;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _recorder = new List<string>();
            _ops = new FakeNavMeshInstanceOps(_recorder);
            _instanceA = MakeInstance(1);
            _instanceB = MakeInstance(2);
            BuildService(0);
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

        private void BuildService(int factoryThrowOnCall)
        {
            _factory = new FakeNavAgentFactory(_recorder, factoryThrowOnCall);
            _service = new ZoneNavigationService(
                _config, _factory, new FakeSpeedSource(), new FakeMeshQuery(), _ops, new MobEventBus());
        }

        // Adds instance A (as the scene loader would) and initialises the service with it.
        private void InitializeWithA()
        {
            _ops.Add(_instanceA);
            _service.Initialize(ZONE, _instanceA);
        }

        // Puts every created agent back to enabled without recording, so Teardown's writes are observable.
        private void ReEnableAll()
        {
            foreach (FakeNavAgent a in _factory.Created)
            {
                a.EnabledValue = true;
            }

            _recorder.Clear();
        }

        private static int CountEnabledFalseWrites(List<string> recorder)
        {
            int n = 0;
            foreach (string s in recorder)
            {
                if (s.EndsWith(".Enabled=False"))
                {
                    n++;
                }
            }

            return n;
        }

        [Test]
        public void test_lifecycle_make_instance_builds_distinct_non_default_instances()
        {
            Assert.That(_instanceA, Is.Not.EqualTo(default(NavMeshDataInstance)));
            Assert.That(_instanceA, Is.Not.EqualTo(_instanceB));
        }

        // ---- Invalid instance (AC-NAV-01) ----

        [Test]
        public void test_lifecycle_invalid_instance_throws_logs_zone_and_never_creates_agents()
        {
            // Instance A is not added, so it is invalid.
            LogAssert.Expect(LogType.Error, new Regex(@"invalid NavMesh instance for ZoneID\(7\)"));

            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));

            Assert.That(_factory.CreateCalls, Is.EqualTo(0));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
        }

        [Test]
        public void test_lifecycle_invalid_instance_then_teardown_does_not_remove_and_does_not_throw()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"ZoneID\(7\)"));
            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));

            Assert.DoesNotThrow(() => _service.Teardown());

            Assert.That(_ops.RemoveCalls, Is.EqualTo(0));
        }

        // ---- Pool fill (AC-NAV-04 first half) ----

        [Test]
        public void test_lifecycle_valid_instance_fills_exactly_150_agents_with_flags_set()
        {
            InitializeWithA();

            Assert.That(_factory.CreateCalls, Is.EqualTo(POOL_SIZE));
            Assert.That(_factory.Created.Count, Is.EqualTo(POOL_SIZE));
            foreach (FakeNavAgent a in _factory.Created)
            {
                Assert.That(a.UpdatePositionValue, Is.False);
                Assert.That(a.UpdateRotationValue, Is.False);
                Assert.That(a.AutoBrakingValue, Is.False);
                Assert.That(a.ObstacleAvoidanceDisabled, Is.True);
                Assert.That(a.EnabledValue, Is.False);
            }
        }

        [Test]
        public void test_lifecycle_valid_instance_reports_150_available_and_0_active()
        {
            InitializeWithA();

            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void test_lifecycle_valid_instance_creates_every_agent_under_the_zone_pool_root()
        {
            InitializeWithA();

            Assert.That(_factory.RootNames.Count, Is.EqualTo(POOL_SIZE));
            foreach (string root in _factory.RootNames)
            {
                Assert.That(root, Is.EqualTo("NavigationPool_" + ZONE.RawValue));
            }
        }

        [Test]
        public void test_lifecycle_valid_instance_is_stored()
        {
            InitializeWithA();

            Assert.That(_service.StoredInstance, Is.EqualTo(_instanceA));
        }

        // ---- Factory throws partway ----

        [Test]
        public void test_lifecycle_factory_throws_on_agent_37_teardown_removes_once_and_disables_36_agents()
        {
            BuildService(37);
            _ops.Add(_instanceA);

            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));
            Assert.That(_factory.Created.Count, Is.EqualTo(36));
            Assert.That(_service.IsAcceptingSpawns, Is.False);
            ReEnableAll();

            Assert.DoesNotThrow(() => _service.Teardown());

            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }));
            Assert.That(CountEnabledFalseWrites(_recorder), Is.EqualTo(36));
            foreach (FakeNavAgent a in _factory.Created)
            {
                Assert.That(a.EnabledValue, Is.False);
            }
        }

        // ---- Only the first Initialize is accepted (EC-NAV-6) ----

        [Test]
        public void test_lifecycle_second_initialize_logs_one_error_and_leaves_pool_and_instance_untouched()
        {
            InitializeWithA();
            _ops.Add(_instanceB);
            LogAssert.Expect(LogType.Error, new Regex(@"Initialize called twice for ZoneID\(7\)"));

            _service.Initialize(ZONE, _instanceB);

            Assert.That(_factory.CreateCalls, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.AvailableCount, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.StoredInstance, Is.EqualTo(_instanceA));
            Assert.That(_ops.RemoveCalls, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();

            _service.Teardown();
            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }), "first instance removed once");
            Assert.That(_ops.IsAdded(_instanceB), Is.True, "the ignored instance stays with the caller");
        }

        [Test]
        public void test_lifecycle_retry_after_partial_fill_throw_is_refused_and_removes_first_instance_once()
        {
            BuildService(37);
            _ops.Add(_instanceA);
            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));
            _ops.Add(_instanceB);
            LogAssert.Expect(LogType.Error, new Regex(@"Initialize called twice for ZoneID\(7\)"));

            Assert.DoesNotThrow(() => _service.Initialize(ZONE, _instanceB));

            Assert.That(_factory.CreateCalls, Is.EqualTo(37), "no second fill");
            Assert.That(_service.AvailableCount, Is.LessThanOrEqualTo(POOL_SIZE));
            Assert.That(_service.StoredInstance, Is.EqualTo(_instanceA));
            Assert.That(_service.IsAcceptingSpawns, Is.False);

            _service.Teardown();
            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }));
            Assert.That(_ops.IsAdded(_instanceB), Is.True);
        }

        [Test]
        public void test_lifecycle_retry_after_invalid_instance_is_refused_and_creates_nothing()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"invalid NavMesh instance for ZoneID\(7\)"));
            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));
            _ops.Add(_instanceB);
            LogAssert.Expect(LogType.Error, new Regex(@"Initialize called twice for ZoneID\(7\)"));

            Assert.DoesNotThrow(() => _service.Initialize(ZONE, _instanceB));

            Assert.That(_factory.CreateCalls, Is.EqualTo(0));
            Assert.That(_service.IsAcceptingSpawns, Is.False);

            _service.Teardown();
            Assert.That(_ops.RemoveCalls, Is.EqualTo(0), "the stored instance A was never valid");
            Assert.That(_ops.IsAdded(_instanceB), Is.True);
        }

        [Test]
        public void test_lifecycle_initialize_after_teardown_logs_error_and_creates_nothing()
        {
            InitializeWithA();
            _service.Teardown();
            _ops.Add(_instanceB);
            LogAssert.Expect(LogType.Error, new Regex(@"Initialize called after Teardown for ZoneID\(7\)"));

            Assert.DoesNotThrow(() => _service.Initialize(ZONE, _instanceB));

            Assert.That(_factory.CreateCalls, Is.EqualTo(POOL_SIZE));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            Assert.That(_service.IsAcceptingSpawns, Is.False);
            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }));
            Assert.That(_ops.IsAdded(_instanceB), Is.True, "the ignored instance stays with the caller");
        }

        // ---- Teardown order (AC-NAV-02) ----

        [Test]
        public void test_lifecycle_teardown_disables_agents_then_clears_collections_then_removes_once()
        {
            InitializeWithA();
            _ops.OnRemoveSnapshot = () =>
                "Remove:available=" + _service.AvailableCount + ":active=" + _service.ActiveCount
                + ":state=" + _service.TrackedStateCount;
            ReEnableAll();

            _service.Teardown();

            Assert.That(_recorder.Count, Is.EqualTo(POOL_SIZE + 1));
            for (int i = 0; i < POOL_SIZE; i++)
            {
                Assert.That(_recorder[i], Does.EndWith(".Enabled=False"), "entry " + i);
            }

            Assert.That(_recorder[POOL_SIZE], Is.EqualTo("Remove:available=0:active=0:state=0"));
            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
        }

        [Test]
        public void test_lifecycle_teardown_resets_stored_instance_to_default()
        {
            InitializeWithA();

            _service.Teardown();

            Assert.That(_service.StoredInstance, Is.EqualTo(default(NavMeshDataInstance)));
        }

        [Test]
        public void test_lifecycle_teardown_with_throwing_agent_still_disables_others_and_removes_instance()
        {
            InitializeWithA();
            _factory.Created[10].ThrowOnDisable = true;
            ReEnableAll();
            LogAssert.Expect(LogType.Exception, new Regex("agent11 cannot be disabled"));

            Assert.DoesNotThrow(() => _service.Teardown());

            Assert.That(CountEnabledFalseWrites(_recorder), Is.EqualTo(POOL_SIZE - 1));
            Assert.That(_ops.Removed, Is.EqualTo(new[] { _instanceA }));
            Assert.That(_service.StoredInstance, Is.EqualTo(default(NavMeshDataInstance)));
            Assert.That(_service.IsAcceptingSpawns, Is.False);
        }

        // ---- Teardown twice ----

        [Test]
        public void test_lifecycle_second_teardown_does_nothing()
        {
            InitializeWithA();
            _service.Teardown();
            int validCallsAfterFirst = _ops.IsValidCalls;
            _recorder.Clear();

            Assert.DoesNotThrow(() => _service.Teardown());

            Assert.That(_ops.RemoveCalls, Is.EqualTo(1));
            Assert.That(_ops.IsValidCalls, Is.EqualTo(validCallsAfterFirst));
            Assert.That(_recorder, Is.Empty);
        }

        [Test]
        public void test_lifecycle_teardown_before_initialize_does_not_throw_and_does_not_remove()
        {
            Assert.DoesNotThrow(() => _service.Teardown());

            Assert.That(_ops.RemoveCalls, Is.EqualTo(0));
        }

        // ---- Guards ----

        [Test]
        public void test_lifecycle_after_teardown_tick_and_sync_touch_no_agent_and_velocity_is_zero()
        {
            InitializeWithA();
            _service.Teardown();
            var snapshot = new List<string>(_recorder);
            var positions = new Dictionary<EntityID, Vector3> { { new EntityID(42u), new Vector3(1f, 2f, 3f) } };

            Assert.DoesNotThrow(() => _service.Tick());
            Assert.DoesNotThrow(() => _service.SyncAgentPositions(positions));
            Vector3 velocity = _service.GetCurrentVelocity(new EntityID(42u));

            Assert.That(_recorder, Is.EqualTo(snapshot));
            Assert.That(velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void test_lifecycle_before_initialize_tick_sync_and_velocity_do_not_throw()
        {
            var positions = new Dictionary<EntityID, Vector3> { { new EntityID(42u), Vector3.one } };

            Assert.DoesNotThrow(() => _service.Tick());
            Assert.DoesNotThrow(() => _service.SyncAgentPositions(positions));
            Vector3 velocity = _service.GetCurrentVelocity(new EntityID(42u));

            Assert.That(velocity, Is.EqualTo(Vector3.zero));
            Assert.That(_recorder, Is.Empty);
        }

        // ---- IsAcceptingSpawns (EC-NAV-4 state) ----

        [Test]
        public void test_lifecycle_is_accepting_spawns_false_before_initialize()
        {
            Assert.That(_service.IsAcceptingSpawns, Is.False);
        }

        [Test]
        public void test_lifecycle_is_accepting_spawns_false_after_invalid_instance_initialize()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"ZoneID\(7\)"));
            Assert.Throws<InvalidOperationException>(() => _service.Initialize(ZONE, _instanceA));

            Assert.That(_service.IsAcceptingSpawns, Is.False);
        }

        [Test]
        public void test_lifecycle_is_accepting_spawns_true_after_valid_initialize()
        {
            InitializeWithA();

            Assert.That(_service.IsAcceptingSpawns, Is.True);
        }

        [Test]
        public void test_lifecycle_is_accepting_spawns_false_after_teardown()
        {
            InitializeWithA();

            _service.Teardown();

            Assert.That(_service.IsAcceptingSpawns, Is.False);
        }

        // ---- Constructor argument checks ----

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void test_lifecycle_constructor_null_argument_throws(int nullIndex)
        {
            NavigationConfig config = nullIndex == 0 ? null : _config;
            INavAgentFactory factory = nullIndex == 1 ? null : new FakeNavAgentFactory(_recorder);
            INavMobSpeedSource speeds = nullIndex == 2 ? null : new FakeSpeedSource();
            INavMeshQuery query = nullIndex == 3 ? null : new FakeMeshQuery();
            INavMeshInstanceOps ops = nullIndex == 4 ? null : _ops;
            MobEventBus bus = nullIndex == 5 ? null : new MobEventBus();

            Assert.Throws<ArgumentNullException>(() => new ZoneNavigationService(config, factory, speeds, query, ops, bus));
        }

        // ---- No async (AC-NAV-03) ----

        private const string NAV_DIR = "src/ServerLogic/Navigation";

        private static string FindNavigationDirectory()
        {
            string[] starts = { Directory.GetCurrentDirectory(), Application.dataPath };
            foreach (string start in starts)
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, NAV_DIR);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }

            return null;
        }

        private static readonly Regex COMMENTS = new Regex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline);
        private static readonly Regex FORBIDDEN = new Regex(@"\bTask\b|ThreadPool|ConfigureAwait|\basync\b");

        /// <summary>True when the code (comments stripped) uses a forbidden asynchronous construct; pure.</summary>
        internal static bool ContainsAsyncConstruct(string source)
        {
            return source != null && FORBIDDEN.IsMatch(COMMENTS.Replace(source, string.Empty));
        }

        [Test]
        public void test_lifecycle_async_scanner_detects_constructs_and_ignores_comments()
        {
            Assert.That(ContainsAsyncConstruct("public async void Run() { }"), Is.True, "positive control async");
            Assert.That(ContainsAsyncConstruct("Task t = null;"), Is.True, "positive control Task");
            Assert.That(ContainsAsyncConstruct("ThreadPool.QueueUserWorkItem(null);"), Is.True);
            Assert.That(ContainsAsyncConstruct("x.ConfigureAwait(false);"), Is.True);
            Assert.That(ContainsAsyncConstruct("// async note\nint a = 1; /* Task */"), Is.False);
            Assert.That(ContainsAsyncConstruct(null), Is.False);
        }

        [Test]
        public void test_lifecycle_navigation_sources_contain_no_async_constructs()
        {
            string dir = FindNavigationDirectory();
            Assert.That(dir, Is.Not.Null, "could not locate " + NAV_DIR);
            string[] files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0));

            var offenders = new List<string>();
            foreach (string file in files)
            {
                if (ContainsAsyncConstruct(File.ReadAllText(file)))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }

            Assert.That(offenders, Is.Empty, string.Join(", ", offenders));
        }
    }
}

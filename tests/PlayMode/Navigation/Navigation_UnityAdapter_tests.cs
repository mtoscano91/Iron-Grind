#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Is = NUnit.Framework.Is;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Navigation Story 009 (ADR-002 Decision 5, ADR-003 Decision 1): the Unity adapters against the real Story 008
    /// NavMesh bake. Covers pool fill off the mesh, instance lifecycle, the claim / move / stop / resume / release
    /// cycle on a real <see cref="NavMeshAgent"/>, Warp, SamplePosition, the main-thread rule (AC-NAV-03) and the
    /// engine verifications the service code relies on (observed values are logged with the "[Nav009]" prefix).
    /// </summary>
    public sealed class Navigation_UnityAdapter_tests
    {
        private const string MESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";
        private const string RAW_ROOT = "NavTestRaw";
        private const string AGENT_CREATION_FAILURE = "Failed to create agent";
        private const int MAX_RESOLVE_TICKS = 5;
        private const int RAW_RESOLVE_FRAMES = 10;
        private const int RESUME_FRAMES = 3;
        private const float SPEED_TOLERANCE = 0.10f;
        private const float ZERO_EPSILON = 0.01f;
        private const float SAMPLE_RADIUS = 0.1f;
        private const float MESH_SURFACE_Y = 0.083f;   // Story 008 bake: mesh surface over the y = 0 floor
        private const float MESH_Y_TOLERANCE = 0.01f;
        private const float DESTINATION_X = 10f;
        private const float EDGE_INSIDE_X = 49.3f;     // mesh edge is at 49.333
        private const float EDGE_OUTSIDE_X = 49.5f;
        private const float BEYOND_EDGE_X = 60f;       // past the ±49.333 edge, still near the floor
        private const float FAR_COORD = 500f;          // well off the 100 x 100 floor
        private const float FAR_SAMPLE_COORD = 100f;
        private const float ON_MESH_WARP = 5f;
        private const float OUT_OF_PARK_RANGE_COORD = 5000f; // beyond UnityNavAgentFactory.PARK_SAMPLE_RADIUS
        private static readonly Vector3 OFF_MESH = new Vector3(FAR_COORD, 0f, FAR_COORD);
        private static readonly Vector3 NO_MESH_IN_RANGE = new Vector3(OUT_OF_PARK_RANGE_COORD, 0f, OUT_OF_PARK_RANGE_COORD);
        private static readonly Vector3 ON_MESH_SPAWN = new Vector3(ON_MESH_WARP, 0f, ON_MESH_WARP);

        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);
        private static readonly EntityID E1 = new EntityID(100u);
        private static readonly EntityID E2 = new EntityID(101u);

        // ---- Recording decorators (thread ids, per-decorator call counts, SetDestination count) ----

        private sealed class RecordingAgent : INavAgent
        {
            private readonly List<int> _ids;
            public readonly UnityNavAgent Inner;
            public int Calls;
            public int SetDestinationCalls;

            public RecordingAgent(UnityNavAgent inner, List<int> ids)
            {
                Inner = inner;
                _ids = ids;
            }

            private void Rec() { Calls++; _ids.Add(Thread.CurrentThread.ManagedThreadId); }

            public bool Enabled { get { Rec(); return Inner.Enabled; } set { Rec(); Inner.Enabled = value; } }
            public bool IsStopped { get { Rec(); return Inner.IsStopped; } set { Rec(); Inner.IsStopped = value; } }
            public float Speed { get { Rec(); return Inner.Speed; } set { Rec(); Inner.Speed = value; } }
            public bool UpdatePosition { get { Rec(); return Inner.UpdatePosition; } set { Rec(); Inner.UpdatePosition = value; } }
            public bool UpdateRotation { get { Rec(); return Inner.UpdateRotation; } set { Rec(); Inner.UpdateRotation = value; } }
            public bool AutoBraking { get { Rec(); return Inner.AutoBraking; } set { Rec(); Inner.AutoBraking = value; } }
            public void DisableObstacleAvoidance() { Rec(); Inner.DisableObstacleAvoidance(); }
            public bool IsOnNavMesh { get { Rec(); return Inner.IsOnNavMesh; } }
            public bool PathPending { get { Rec(); return Inner.PathPending; } }
            public NavMeshPathStatus PathStatus { get { Rec(); return Inner.PathStatus; } }
            public bool IsPathStale { get { Rec(); return Inner.IsPathStale; } }
            public Vector3 DesiredVelocity { get { Rec(); return Inner.DesiredVelocity; } }
            public Vector3 NextPosition { get { Rec(); return Inner.NextPosition; } set { Rec(); Inner.NextPosition = value; } }
            public bool Warp(Vector3 position) { Rec(); return Inner.Warp(position); }
            public bool SetDestination(Vector3 target) { Rec(); SetDestinationCalls++; return Inner.SetDestination(target); }
            public void ResetPath() { Rec(); Inner.ResetPath(); }
        }

        private sealed class RecordingFactory : INavAgentFactory
        {
            private readonly UnityNavAgentFactory _inner;
            private readonly List<int> _ids;
            public readonly List<RecordingAgent> Created = new List<RecordingAgent>();
            public int Calls;

            public RecordingFactory(UnityNavAgentFactory inner, List<int> ids)
            {
                _inner = inner;
                _ids = ids;
            }

            public INavAgent Create(string poolRootName)
            {
                Calls++;
                _ids.Add(Thread.CurrentThread.ManagedThreadId);
                var agent = new RecordingAgent((UnityNavAgent)_inner.Create(poolRootName), _ids);
                Created.Add(agent);
                return agent;
            }
        }

        private sealed class RecordingQuery : INavMeshQuery
        {
            private readonly UnityNavMeshQuery _inner = new UnityNavMeshQuery();
            private readonly List<int> _ids;
            public int Calls;

            public RecordingQuery(List<int> ids) { _ids = ids; }

            public bool SamplePosition(Vector3 position, float maxDistance, int areaMask, out Vector3 hitPosition)
            {
                Calls++;
                _ids.Add(Thread.CurrentThread.ManagedThreadId);
                return _inner.SamplePosition(position, maxDistance, areaMask, out hitPosition);
            }
        }

        private sealed class RecordingOps : INavMeshInstanceOps
        {
            private readonly UnityNavMeshInstanceOps _inner = new UnityNavMeshInstanceOps();
            private readonly List<int> _ids;
            public int Calls;

            public RecordingOps(List<int> ids) { _ids = ids; }

            public bool IsValid(NavMeshDataInstance instance)
            {
                Calls++;
                _ids.Add(Thread.CurrentThread.ManagedThreadId);
                return _inner.IsValid(instance);
            }

            public void Remove(NavMeshDataInstance instance)
            {
                Calls++;
                _ids.Add(Thread.CurrentThread.ManagedThreadId);
                _inner.Remove(instance);
            }
        }

        // ---- Fixture state ----

        private readonly List<int> _threadIds = new List<int>();
        private readonly List<UnityNavAgentFactory> _rawFactories = new List<UnityNavAgentFactory>();
        private readonly List<string> _warningsAndErrors = new List<string>();
        private NavigationConfig _config;
        private NavMeshDataInstance _instance;
        private UnityNavAgentFactory _unityFactory;
        private RecordingFactory _factory;
        private RecordingQuery _query;
        private RecordingOps _ops;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private Dictionary<EntityID, Vector3> _committed;
        private RecordingAgent _claimed;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _threadIds.Clear();
            _warningsAndErrors.Clear();
            _committed = new Dictionary<EntityID, Vector3>();
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(MESH_ASSET_PATH);
            Assert.That(data, Is.Not.Null, "NavMeshData not found at " + MESH_ASSET_PATH);
            _instance = NavMesh.AddNavMeshData(data);
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _bus = new MobEventBus();
            Application.logMessageReceived += RecordWarningOrError;
            yield return null; // let the added mesh settle
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= RecordWarningOrError;
            if (_service != null)
            {
                _service.Teardown();
                _service = null;
            }

            // A test may already have removed the instance (SamplePosition without a mesh).
            if (_instance.valid)
            {
                NavMesh.RemoveNavMeshData(_instance);
            }

            if (_unityFactory != null)
            {
                _unityFactory.DestroyPoolRoots();
                _unityFactory = null;
            }

            foreach (UnityNavAgentFactory f in _rawFactories)
            {
                f.DestroyPoolRoots();
            }

            _rawFactories.Clear();
            if (_config != null)
            {
                Object.Destroy(_config);
                _config = null;
            }

            yield return null;
        }

        // Unity logs "Failed to create agent ..." as a warning, which LogAssert does not catch: listen directly.
        private void RecordWarningOrError(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
            {
                _warningsAndErrors.Add(type + ": " + message);
            }
        }

        // Builds the service with the real adapters wrapped in the recording decorators.
        private void BuildService(Vector3 parkPosition)
        {
            _unityFactory = new UnityNavAgentFactory(parkPosition);
            _factory = new RecordingFactory(_unityFactory, _threadIds);
            _query = new RecordingQuery(_threadIds);
            _ops = new RecordingOps(_threadIds);
            _service = new ZoneNavigationService(
                _config, _factory, new FixedNavSpeedSource(3.5f), _query, _ops, _bus);
        }

        private static float FixedSpeed => 3.5f;

        private RecordingAgent EnabledAgentFor(EntityID entity)
        {
            RecordingAgent found = null;
            foreach (RecordingAgent a in _factory.Created)
            {
                if (a.Inner.Agent.enabled)
                {
                    Assert.That(found, Is.Null, "more than one enabled agent for " + entity);
                    found = a;
                }
            }

            return found;
        }

        private void Spawn(EntityID entity, Vector3 position)
        {
            _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, entity, TYPE_A, position));
            _committed[entity] = position;
            _claimed = EnabledAgentFor(entity);
        }

        // One server tick: phase 1 (commit the agent's own next position, as the integrating server would), phase 2.
        private void TickOnce(EntityID entity)
        {
            if (_claimed != null && _committed.ContainsKey(entity))
            {
                _committed[entity] = _claimed.Inner.Agent.nextPosition;
            }

            _service.SyncAgentPositions((IReadOnlyDictionary<EntityID, Vector3>)_committed);
            _service.Tick();
        }

        // Requests (10, 0, 0) for the entity and ticks until the path resolves and the agent moves toward +x.
        private IEnumerator MoveToDestination(EntityID entity)
        {
            _service.SetDestination(entity, new Vector3(DESTINATION_X, 0f, 0f));
            for (int i = 0; i < MAX_RESOLVE_TICKS; i++)
            {
                TickOnce(entity);
                yield return null;
                if (!_claimed.Inner.Agent.pathPending && _service.GetCurrentVelocity(entity).x > 0f)
                {
                    yield break;
                }
            }

            Assert.Fail("path to (" + DESTINATION_X + ", 0, 0) did not resolve within " + MAX_RESOLVE_TICKS + " ticks");
        }

        // Initialize, claim at the origin, request (10, 0, 0) and tick until the path resolves.
        private IEnumerator StartMove()
        {
            BuildService(Vector3.zero);
            _service.Initialize(ZONE, _instance);
            Spawn(E1, Vector3.zero);
            Assert.That(_claimed, Is.Not.Null, "claim failed");
            yield return MoveToDestination(E1);
        }

        private UnityNavAgent MakeRawAgent()
        {
            var factory = new UnityNavAgentFactory(Vector3.zero);
            _rawFactories.Add(factory);
            var agent = (UnityNavAgent)factory.Create(RAW_ROOT);
            agent.UpdatePosition = false;
            agent.UpdateRotation = false;
            agent.AutoBraking = false;
            agent.DisableObstacleAvoidance();
            agent.Enabled = true;
            return agent;
        }

        // Raw agent at the origin with a resolved path to (10, 0, 0).
        private IEnumerator MakePathedRawAgent(System.Action<NavMeshAgent> result)
        {
            NavMeshAgent a = MakeRawAgent().Agent;
            a.Warp(Vector3.zero);
            a.SetDestination(new Vector3(DESTINATION_X, 0f, 0f));
            for (int i = 0; i < RAW_RESOLVE_FRAMES && (a.pathPending || !a.hasPath); i++)
            {
                yield return null;
            }

            Assert.That(!a.pathPending && a.hasPath, Is.True, "raw agent path did not resolve within " + RAW_RESOLVE_FRAMES + " frames");
            result(a);
        }

        private static void AssertMovingTowardDestination(Vector3 v)
        {
            Assert.That(v.x, Is.GreaterThan(0f));
            Assert.That(v.magnitude, Is.EqualTo(FixedSpeed).Within(FixedSpeed * SPEED_TOLERANCE));
            Assert.That(Mathf.Abs(v.y), Is.LessThan(ZERO_EPSILON));
            Assert.That(Mathf.Abs(v.z), Is.LessThan(FixedSpeed * SPEED_TOLERANCE));
        }

        // ---- Pool, factory, instance ----

        [UnityTest]
        public IEnumerator test_pool_fill_parked_off_mesh_logs_nothing_and_all_agents_disabled_without_avoidance()
        {
            // No mesh within the park search radius, so the agents really are created off the mesh.
            BuildService(NO_MESH_IN_RANGE);

            _service.Initialize(ZONE, _instance);
            yield return null;

            Assert.That(_warningsAndErrors, Is.Empty);
            LogAssert.NoUnexpectedReceived();
            Assert.That(_service.AvailableCount, Is.EqualTo(NavigationConfigValues.FromAsset(_config).MaxAgentPoolSize));
            Assert.That(_factory.Created.Count, Is.EqualTo(_service.AvailableCount));
            foreach (RecordingAgent a in _factory.Created)
            {
                Assert.That(a.Inner.Agent.enabled, Is.False);
                Assert.That(a.Inner.Agent.obstacleAvoidanceType, Is.EqualTo(ObstacleAvoidanceType.NoObstacleAvoidance));
                Assert.That(a.Inner.Agent.updatePosition, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator test_claim_with_off_mesh_park_preference_parks_on_mesh_and_logs_nothing()
        {
            // Story 003 carry: the claim enables the pooled agent before Warp (ADR-003 order); the factory moves the
            // park position onto the mesh, so the enable happens on the mesh.
            BuildService(OFF_MESH);
            _service.Initialize(ZONE, _instance);
            yield return null;
            Vector3 parked = _factory.Created[0].Inner.Agent.transform.position;
            Assert.That(NavMesh.SamplePosition(parked, out _, SAMPLE_RADIUS, NavMesh.AllAreas), Is.True,
                "park position " + parked + " is not on the mesh");

            Spawn(E1, ON_MESH_SPAWN);
            yield return null;

            Debug.Log("[Nav009] claim with park preference " + OFF_MESH + " parked at " + parked
                + ": warnings/errors=" + _warningsAndErrors.Count);
            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            Assert.That(_claimed, Is.Not.Null);
            Assert.That(_claimed.Inner.Agent.isOnNavMesh, Is.True);
            Assert.That(_warningsAndErrors, Is.Empty);
        }

        [UnityTest]
        public IEnumerator test_claim_with_no_mesh_in_park_range_logs_agent_warning_but_claim_succeeds()
        {
            // Fallback documented on UnityNavAgentFactory: the real agent warns when enabled off the mesh, then Warp
            // places it.
            BuildService(NO_MESH_IN_RANGE);
            _service.Initialize(ZONE, _instance);
            yield return null;

            Spawn(E1, ON_MESH_SPAWN);
            yield return null;

            Assert.That(_service.ActiveCount, Is.EqualTo(1));
            Assert.That(_claimed.Inner.Agent.isOnNavMesh, Is.True);
            Assert.That(_warningsAndErrors.Count, Is.EqualTo(1));
            StringAssert.Contains(AGENT_CREATION_FAILURE, _warningsAndErrors[0]);
        }

        [UnityTest]
        public IEnumerator test_claim_release_reclaim_real_agent_starts_stopped_then_moves_without_logs()
        {
            yield return StartMove();
            RecordingAgent first = _claimed;

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));
            yield return null;
            Assert.That(first.Inner.Agent.enabled, Is.False);

            Spawn(E2, Vector3.zero);
            Assert.That(_claimed, Is.Not.Null, "re-claim failed");
            Assert.That(_claimed.Inner.Agent.isStopped, Is.True);
            Assert.That(_service.GetCurrentVelocity(E2), Is.EqualTo(Vector3.zero));
            yield return MoveToDestination(E2);

            AssertMovingTowardDestination(_service.GetCurrentVelocity(E2));
            Assert.That(_warningsAndErrors, Is.Empty);
        }

        [UnityTest]
        public IEnumerator test_factory_same_root_name_reuses_root_and_recreates_after_destroy()
        {
            var factory = new UnityNavAgentFactory(Vector3.zero);
            _rawFactories.Add(factory);

            Transform rootA = ((UnityNavAgent)factory.Create(RAW_ROOT)).Agent.transform.parent;
            Transform rootB = ((UnityNavAgent)factory.Create(RAW_ROOT)).Agent.transform.parent;
            Assert.That(rootA, Is.SameAs(rootB));
            Assert.That(rootA.childCount, Is.EqualTo(2));

            factory.DestroyPoolRoots();
            yield return null;

            Transform rootC = ((UnityNavAgent)factory.Create(RAW_ROOT)).Agent.transform.parent;
            Assert.That(rootA == null, Is.True, "first root destroyed");
            Assert.That(rootC != null, Is.True);
            Assert.That(rootC.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator test_instance_ops_real_instance_valid_before_teardown_invalid_after()
        {
            BuildService(Vector3.zero);
            var ops = new UnityNavMeshInstanceOps();
            _service.Initialize(ZONE, _instance);
            Assert.That(ops.IsValid(_instance), Is.True);

            _service.Teardown();
            yield return null;

            Assert.That(ops.IsValid(_instance), Is.False);
        }

        [UnityTest]
        public IEnumerator test_instance_ops_remove_twice_second_call_is_harmless()
        {
            var ops = new UnityNavMeshInstanceOps();
            ops.Remove(_instance);
            yield return null;

            Assert.DoesNotThrow(() => ops.Remove(_instance));
            Debug.Log("[Nav009] second Remove on an invalid instance: warnings/errors=" + _warningsAndErrors.Count);
            Assert.That(ops.IsValid(_instance), Is.False);
            Assert.That(_warningsAndErrors, Is.Empty);
        }

        // ---- Round trip ----

        [UnityTest]
        public IEnumerator test_adapter_round_trip_claim_and_move_velocity_points_to_destination_at_speed()
        {
            yield return StartMove();

            Assert.That(_claimed.Inner.Agent.pathPending, Is.False);
            AssertMovingTowardDestination(_service.GetCurrentVelocity(E1));
        }

        [UnityTest]
        public IEnumerator test_stop_resume_real_agent_velocity_zero_then_restored_without_new_set_destination()
        {
            yield return StartMove();
            int callsBefore = _claimed.SetDestinationCalls;
            Assert.That(_service.GetCurrentVelocity(E1).magnitude, Is.GreaterThan(ZERO_EPSILON));

            _service.Stop(E1);
            // Same frame: the real agent still reports the old desiredVelocity here; the service must not.
            Assert.That(_service.GetCurrentVelocity(E1).magnitude, Is.LessThan(ZERO_EPSILON), "same frame as Stop");
            yield return null;
            Assert.That(_service.GetCurrentVelocity(E1).magnitude, Is.LessThan(ZERO_EPSILON));

            _service.Resume(E1);
            for (int i = 0; i < RESUME_FRAMES && _service.GetCurrentVelocity(E1).magnitude < ZERO_EPSILON; i++)
            {
                yield return null;
            }

            AssertMovingTowardDestination(_service.GetCurrentVelocity(E1));
            Assert.That(_claimed.SetDestinationCalls, Is.EqualTo(callsBefore));
        }

        // ---- Warp, sample ----

        [UnityTest]
        public IEnumerator test_warp_real_agent_off_mesh_false_on_mesh_true()
        {
            UnityNavAgent agent = MakeRawAgent();
            yield return null;

            bool off = agent.Warp(OFF_MESH);
            bool on = agent.Warp(ON_MESH_SPAWN);

            Assert.That(off, Is.False);
            Assert.That(on, Is.True);
        }

        [UnityTest]
        public IEnumerator test_sample_position_real_mesh_hits_floor_misses_outside_and_mask_agnostic()
        {
            var query = new UnityNavMeshQuery();
            int walkable = 1 << NavMesh.GetAreaFromName("Walkable");

            // Story 007 carry 1: a spawn point at floor height (y = 0) is within the 0.1 radius of the y 0.083 mesh.
            Assert.That(query.SamplePosition(Vector3.zero, SAMPLE_RADIUS, NavMesh.AllAreas, out Vector3 hit), Is.True);
            Assert.That(hit.y, Is.EqualTo(MESH_SURFACE_Y).Within(MESH_Y_TOLERANCE));
            Assert.That(query.SamplePosition(new Vector3(FAR_SAMPLE_COORD, 0f, FAR_SAMPLE_COORD), SAMPLE_RADIUS, NavMesh.AllAreas, out _), Is.False);
            Assert.That(query.SamplePosition(Vector3.zero, SAMPLE_RADIUS, walkable, out _), Is.True);
            Assert.That(query.SamplePosition(new Vector3(FAR_SAMPLE_COORD, 0f, FAR_SAMPLE_COORD), SAMPLE_RADIUS, walkable, out _), Is.False);
            Assert.That(query.SamplePosition(new Vector3(EDGE_INSIDE_X, 0f, 0f), SAMPLE_RADIUS, NavMesh.AllAreas, out _), Is.True);
            Assert.That(query.SamplePosition(new Vector3(EDGE_OUTSIDE_X, 0f, 0f), SAMPLE_RADIUS, NavMesh.AllAreas, out _), Is.False);

            // Remove the mesh mid-test to check the no-mesh path; TearDown skips the removal when already invalid.
            NavMesh.RemoveNavMeshData(_instance);
            yield return null;

            bool hitWithoutMesh = query.SamplePosition(Vector3.zero, SAMPLE_RADIUS, NavMesh.AllAreas, out Vector3 none);
            Assert.That(hitWithoutMesh, Is.False);
            Assert.That(none, Is.EqualTo(default(Vector3)));
        }

        // ---- Main thread ----

        [UnityTest]
        public IEnumerator test_main_thread_every_adapter_call_runs_on_test_thread()
        {
            int mainId = Thread.CurrentThread.ManagedThreadId;
            yield return StartMove();
            _service.SetSpeed(E1, FixedSpeed);
            _service.IsPathStale(E1);
            _service.Stop(E1);
            _service.Resume(E1);
            Assert.That(_query.SamplePosition(Vector3.zero, SAMPLE_RADIUS, NavMesh.AllAreas, out _), Is.True);
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, E1));
            _service.Teardown();

            Assert.That(_factory.Calls, Is.GreaterThan(0), "factory");
            Assert.That(_claimed.Calls, Is.GreaterThan(0), "agent");
            Assert.That(_query.Calls, Is.GreaterThan(0), "query");
            Assert.That(_ops.Calls, Is.GreaterThan(0), "instance ops");
            foreach (int id in _threadIds)
            {
                Assert.That(id, Is.EqualTo(mainId));
            }
        }

        // ---- Engine verifications (Story 009 Engine Notes, Story 006 carries) ----

        [UnityTest]
        public IEnumerator test_engine_is_stopped_true_zeroes_desired_velocity_next_frame()
        {
            NavMeshAgent a = null;
            yield return MakePathedRawAgent(x => a = x);
            yield return null;
            Vector3 before = a.desiredVelocity;

            a.isStopped = true;
            Vector3 sameFrame = a.desiredVelocity;
            yield return null;
            Vector3 nextFrame = a.desiredVelocity;

            Debug.Log("[Nav009] desiredVelocity before=" + before + " stopped same frame=" + sameFrame + " next frame=" + nextFrame);
            Assert.That(before.magnitude, Is.GreaterThan(ZERO_EPSILON));
            Assert.That(nextFrame.magnitude, Is.LessThan(ZERO_EPSILON));
        }

        [UnityTest]
        public IEnumerator test_engine_path_kept_across_is_stopped_toggle()
        {
            NavMeshAgent a = null;
            yield return MakePathedRawAgent(x => a = x);

            a.isStopped = true;
            yield return null;
            bool hasPathStopped = a.hasPath;
            a.isStopped = false;
            yield return null;

            Debug.Log("[Nav009] hasPath while stopped=" + hasPathStopped + " after resume=" + a.hasPath
                + " pathStatus=" + a.pathStatus + " desiredVelocity=" + a.desiredVelocity);
            Assert.That(hasPathStopped, Is.True);
            Assert.That(a.hasPath, Is.True);
        }

        [UnityTest]
        public IEnumerator test_engine_warp_on_pathed_agent_update_position_false_clears_path_keeps_is_stopped()
        {
            NavMeshAgent a = null;
            yield return MakePathedRawAgent(x => a = x);
            bool stoppedBefore = a.isStopped;
            Vector3 warpTo = new Vector3(ON_MESH_WARP, 0f, 0f);

            bool warped = a.Warp(warpTo);

            Debug.Log("[Nav009] after Warp: warped=" + warped + " hasPath=" + a.hasPath + " pathStatus=" + a.pathStatus
                + " isStopped=" + a.isStopped + " nextPosition=" + a.nextPosition);
            Assert.That(warped, Is.True);
            Assert.That(a.hasPath, Is.False);
            Assert.That(a.pathStatus, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(a.isStopped, Is.EqualTo(stoppedBefore));
            Assert.That(a.nextPosition.x, Is.EqualTo(warpTo.x).Within(ZERO_EPSILON));
            Assert.That(a.nextPosition.z, Is.EqualTo(warpTo.z).Within(ZERO_EPSILON));
        }

        [UnityTest]
        public IEnumerator test_engine_set_destination_off_mesh_returns_false_and_path_status_unchanged()
        {
            NavMeshAgent a = null;
            yield return MakePathedRawAgent(x => a = x);
            NavMeshPathStatus before = a.pathStatus;

            bool accepted = a.SetDestination(OFF_MESH);
            NavMeshPathStatus after = a.pathStatus;

            Debug.Log("[Nav009] SetDestination off-mesh accepted=" + accepted + " pathStatus before=" + before + " after=" + after);
            Assert.That(accepted, Is.False);
            Assert.That(after, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator test_engine_path_pending_clears_next_frame_and_next_position_holds()
        {
            NavMeshAgent a = MakeRawAgent().Agent;
            a.Warp(Vector3.zero);
            Vector3 start = a.nextPosition;

            a.SetDestination(new Vector3(DESTINATION_X, 0f, 0f));
            bool pendingSameFrame = a.pathPending;
            Vector3 nextSameFrame = a.nextPosition;
            yield return null;
            bool pendingNextFrame = a.pathPending;
            Vector3 nextNextFrame = a.nextPosition;

            Debug.Log("[Nav009] nextPosition start=" + start + " pending(same frame)=" + pendingSameFrame + " next=" + nextSameFrame
                + " pending(next frame)=" + pendingNextFrame + " next=" + nextNextFrame);
            // Story 006 carry 2: the service expects the path on the tick after the drain.
            Assert.That(pendingNextFrame, Is.False);
            // EC-NAV-9: nextPosition does not move while the path is pending.
            Assert.That(Vector3.Distance(nextSameFrame, start), Is.LessThan(ZERO_EPSILON));
        }

        [UnityTest]
        public IEnumerator test_engine_reset_path_clears_has_path()
        {
            NavMeshAgent a = null;
            yield return MakePathedRawAgent(x => a = x);

            a.ResetPath();

            Debug.Log("[Nav009] after ResetPath: hasPath=" + a.hasPath + " pathStatus=" + a.pathStatus);
            Assert.That(a.hasPath, Is.False);
        }

        [UnityTest]
        public IEnumerator test_engine_next_position_beyond_edge_stays_on_mesh_and_warp_back_succeeds()
        {
            // Story 006 carry 4: isOnNavMesh while the server applies nextPosition, and the recovery Warp.
            NavMeshAgent a = MakeRawAgent().Agent;
            a.Warp(Vector3.zero);
            yield return null;

            a.nextPosition = new Vector3(BEYOND_EDGE_X, 0f, 0f);
            yield return null;
            Vector3 clamped = a.nextPosition;
            bool onMeshAfterPush = a.isOnNavMesh;
            bool warpedBack = a.Warp(Vector3.zero);

            Debug.Log("[Nav009] nextPosition set beyond edge -> " + clamped + " isOnNavMesh=" + onMeshAfterPush
                + " warp back=" + warpedBack + " isOnNavMesh after=" + a.isOnNavMesh);
            Assert.That(warpedBack, Is.True);
            Assert.That(a.isOnNavMesh, Is.True);
        }
    }
}
#endif

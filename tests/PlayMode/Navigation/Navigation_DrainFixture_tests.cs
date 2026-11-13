#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
using Stopwatch = System.Diagnostics.Stopwatch;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Navigation Story 011 (AC-NAV-20, F-NAV-3, EC-NAV-9, ADR-002 Decisions 1 and 2): the 150-agent drain fixture on the
    /// real Story 008 NavMesh and the real Unity adapters. 150 agents are claimed on a 15 x 10 grid, every destination
    /// is queued before tick 0, and ticks run only through <see cref="NavigationPhaseStep.Run"/> with one frame stepped
    /// per tick at 20 Hz. Observation uses public API only: a recording <see cref="INavAgentFactory"/> decorator over
    /// <see cref="UnityNavAgentFactory"/> records the tick of each <c>SetDestination</c> (the drain tick) and exposes
    /// <c>PathPending</c> / <c>PathStatus</c> for the per-tick poll (serviced tick). While a path is pending, the
    /// position source nudges <c>nextPosition</c> every tick to provoke EC-NAV-9. Results are logged with "[Nav011]".
    /// </summary>
    public sealed class Navigation_DrainFixture_tests
    {
        private const string MESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";
        private const int AGENT_COUNT = 150;
        private const int GRID_COLUMNS = 15;
        private const float GRID_SPACING = 5f;
        private const float GRID_ORIGIN_X = -35f;   // 15 columns, 5 m apart, centred on the origin
        private const float GRID_ORIGIN_Z = -22.5f; // 10 rows, 5 m apart, centred on the origin
        private const float SPAWN_Y = 0f;           // within the 0.1 sample radius of the y 0.083 mesh surface
        private const float TICK_SECONDS = 0.05f;   // 20 Hz
        private const int PATH_UPDATES_PER_TICK = 20;
        private const int LAST_DRAIN_TICK = 7;
        private const int MAX_SERVICED_TICK = 9;
        private const int FAST_SERVICED_TICK = 7;
        private const int FAST_REQUIRED_COUNT = 140;
        private const int MAX_RUN_TICKS = 12;       // a few ticks past the limit so a late entity is measured, not missing
        private const int PATHFINDING_ITERATIONS_PER_FRAME = 100; // Unity default, pinned so a settings change can't flip the result
        private const float PENDING_NUDGE_METRES = 0.05f;         // EC-NAV-9 provocation, well inside the mesh
        private const int NOT_SET = -1;
        private const uint FIRST_ENTITY_ID = 1000u;

        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly MobTypeID TYPE_A = new MobTypeID(5u);

        // ---- Recording decorators ----

        /// <summary>Shared tick index the agents stamp onto their <c>SetDestination</c> call.</summary>
        private sealed class TickClock
        {
            public int Current = NOT_SET;
        }

        /// <summary>Forwards every member to the real agent; records the drain tick and the call result.</summary>
        private sealed class RecordingAgent : INavAgent
        {
            private readonly TickClock _clock;
            public readonly UnityNavAgent Inner;
            public int DrainTick = NOT_SET;
            public int SetDestinationCalls;
            public bool LastAccepted;

            public RecordingAgent(UnityNavAgent inner, TickClock clock)
            {
                Inner = inner;
                _clock = clock;
            }

            public bool Enabled { get => Inner.Enabled; set => Inner.Enabled = value; }
            public bool IsStopped { get => Inner.IsStopped; set => Inner.IsStopped = value; }
            public float Speed { get => Inner.Speed; set => Inner.Speed = value; }
            public bool UpdatePosition { get => Inner.UpdatePosition; set => Inner.UpdatePosition = value; }
            public bool UpdateRotation { get => Inner.UpdateRotation; set => Inner.UpdateRotation = value; }
            public bool AutoBraking { get => Inner.AutoBraking; set => Inner.AutoBraking = value; }
            public void DisableObstacleAvoidance() { Inner.DisableObstacleAvoidance(); }
            public bool IsOnNavMesh => Inner.IsOnNavMesh;
            public bool PathPending => Inner.PathPending;
            public NavMeshPathStatus PathStatus => Inner.PathStatus;
            public bool IsPathStale => Inner.IsPathStale;
            public Vector3 DesiredVelocity => Inner.DesiredVelocity;
            public Vector3 NextPosition { get => Inner.NextPosition; set => Inner.NextPosition = value; }
            public bool Warp(Vector3 position) { return Inner.Warp(position); }
            public void ResetPath() { Inner.ResetPath(); }

            public bool SetDestination(Vector3 target)
            {
                SetDestinationCalls++;
                if (DrainTick == NOT_SET)
                {
                    DrainTick = _clock.Current;
                }

                LastAccepted = Inner.SetDestination(target);
                return LastAccepted;
            }
        }

        private sealed class RecordingFactory : INavAgentFactory
        {
            private readonly UnityNavAgentFactory _inner;
            private readonly TickClock _clock;
            public readonly List<RecordingAgent> Created = new List<RecordingAgent>();

            public RecordingFactory(UnityNavAgentFactory inner, TickClock clock)
            {
                _inner = inner;
                _clock = clock;
            }

            public INavAgent Create(string poolRootName)
            {
                var agent = new RecordingAgent((UnityNavAgent)_inner.Create(poolRootName), _clock);
                Created.Add(agent);
                return agent;
            }
        }

        /// <summary>
        /// Test position source: reports each claimed agent's own <c>NextPosition</c> as its committed position. While the
        /// agent's path is pending, the position is nudged by ±<see cref="PENDING_NUDGE_METRES"/> on x, alternating per
        /// tick, so the phase 1 sync writes a changed <c>nextPosition</c> during <c>pathPending</c> (provokes EC-NAV-9).
        /// The alternation keeps the agent within one nudge of where it was.
        /// </summary>
        private sealed class AgentPositionSource : IMobCommittedPositionSource
        {
            private readonly TickClock _clock;
            public readonly List<EntityID> Entities = new List<EntityID>();
            public readonly List<RecordingAgent> Agents = new List<RecordingAgent>();
            public int NudgesWritten;

            public AgentPositionSource(TickClock clock)
            {
                _clock = clock;
            }

            public void CopyCommittedPositions(Dictionary<EntityID, Vector3> into)
            {
                float nudge = (_clock.Current % 2 == 0) ? PENDING_NUDGE_METRES : -PENDING_NUDGE_METRES;
                for (int i = 0; i < Entities.Count; i++)
                {
                    RecordingAgent agent = Agents[i];
                    Vector3 position = agent.NextPosition;
                    if (agent.PathPending)
                    {
                        position.x += nudge;
                        NudgesWritten++;
                    }

                    into[Entities[i]] = position;
                }
            }
        }

        /// <summary>Per-entity and per-tick results of one fixture run.</summary>
        private sealed class FixtureRun
        {
            public readonly int[] ServicedTick = new int[AGENT_COUNT];
            public readonly NavMeshPathStatus[] ServicedStatus = new NavMeshPathStatus[AGENT_COUNT];
            public readonly double[] RunMs = new double[MAX_RUN_TICKS];
            public int TicksRun;

            public FixtureRun()
            {
                for (int i = 0; i < AGENT_COUNT; i++)
                {
                    ServicedTick[i] = NOT_SET;
                }
            }
        }

        /// <summary>Counts derived from a <see cref="FixtureRun"/>.</summary>
        private struct FixtureSummary
        {
            public int Late;            // not serviced by MAX_SERVICED_TICK (includes never serviced)
            public int Unserviced;
            public int WithinFast;
            public int MaxServiced;
            public int NotPathComplete;
            public int DrainMismatches;
            public int Rejected;
            public int NotExactlyOnce;
        }

        // ---- Fixture state ----

        private readonly List<string> _warningsAndErrors = new List<string>();
        private readonly TickClock _clock = new TickClock();
        private NavigationConfig _config;
        private NavMeshDataInstance _instance;
        private UnityNavAgentFactory _unityFactory;
        private RecordingFactory _factory;
        private MobEventBus _bus;
        private ZoneNavigationService _service;
        private NavigationPhaseStep _step;
        private AgentPositionSource _source;
        private int _savedIterationsPerFrame;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _warningsAndErrors.Clear();
            _clock.Current = NOT_SET;
            _savedIterationsPerFrame = NavMesh.pathfindingIterationsPerFrame;
            NavMesh.pathfindingIterationsPerFrame = PATHFINDING_ITERATIONS_PER_FRAME;
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
            Time.captureDeltaTime = 0f;
            NavMesh.pathfindingIterationsPerFrame = _savedIterationsPerFrame;
            try
            {
                if (_service != null)
                {
                    _service.Teardown();
                    _service = null;
                }
            }
            finally
            {
                if (_unityFactory != null)
                {
                    _unityFactory.DestroyPoolRoots();
                    _unityFactory = null;
                }

                if (_instance.valid)
                {
                    NavMesh.RemoveNavMeshData(_instance);
                }

                if (_config != null)
                {
                    Object.DestroyImmediate(_config);
                    _config = null;
                }
            }

            yield return null;
        }

        // Unity logs agent problems as warnings, which LogAssert does not catch: listen directly.
        private void RecordWarningOrError(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Log)
            {
                _warningsAndErrors.Add(type + ": " + message);
            }
        }

        private static Vector3 GridPosition(int index)
        {
            int column = index % GRID_COLUMNS;
            int row = index / GRID_COLUMNS;
            return new Vector3(GRID_ORIGIN_X + column * GRID_SPACING, SPAWN_Y, GRID_ORIGIN_Z + row * GRID_SPACING);
        }

        // Mirrored across the origin; y is irrelevant to the pathfinder (it samples the mesh).
        private static Vector3 MirroredDestination(Vector3 spawn)
        {
            return new Vector3(-spawn.x, spawn.y, -spawn.z);
        }

        // Builds the service, initializes it, claims 150 agents on the grid and wires the phase step.
        private void BuildAndClaimAll()
        {
            _unityFactory = new UnityNavAgentFactory(Vector3.zero);
            _factory = new RecordingFactory(_unityFactory, _clock);
            _service = new ZoneNavigationService(
                _config, _factory, new StandInNavMobSpeedSource(NavigationConfigValues.Defaults),
                new UnityNavMeshQuery(), new UnityNavMeshInstanceOps(), _bus);
            _service.Initialize(ZONE, _instance);
            _source = new AgentPositionSource(_clock);

            // The pool is a stack, so claim order is not creation order: map each entity to the agent it just enabled.
            var mapped = new HashSet<RecordingAgent>();
            for (int i = 0; i < AGENT_COUNT; i++)
            {
                var entity = new EntityID(FIRST_ENTITY_ID + (uint)i);
                _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, entity, TYPE_A, GridPosition(i)));

                RecordingAgent claimed = null;
                foreach (RecordingAgent a in _factory.Created)
                {
                    if (a.Inner.Agent.enabled && !mapped.Contains(a))
                    {
                        claimed = a;
                        break;
                    }
                }

                Assert.That(claimed, Is.Not.Null, "claim failed for entity index " + i);
                mapped.Add(claimed);
                _source.Entities.Add(entity);
                _source.Agents.Add(claimed);
            }

            _step = new NavigationPhaseStep(_service, _source, AGENT_COUNT);
        }

        // Queues every destination before tick 0. The drain-schedule assertion (drain tick == i / 20) relies on this
        // loop queuing in entity index order: the service's request queue is FIFO.
        private void QueueAllDestinations()
        {
            for (int i = 0; i < AGENT_COUNT; i++)
            {
                _service.SetDestination(_source.Entities[i], MirroredDestination(GridPosition(i)));
            }
        }

        // Tick t = Run(t) -> one frame at 20 Hz -> poll. Serviced = drained, PathPending false; the path status is
        // recorded at that moment. Stops after the last drain tick once every entity is serviced.
        private IEnumerator RunTicks(FixtureRun run)
        {
            for (int t = 0; t < MAX_RUN_TICKS; t++)
            {
                _clock.Current = t;
                var watch = Stopwatch.StartNew();
                _step.Run((uint)t, TICK_SECONDS);
                watch.Stop();
                run.RunMs[t] = watch.Elapsed.TotalMilliseconds;

                yield return null;
                run.TicksRun = t + 1;

                int open = 0;
                for (int i = 0; i < AGENT_COUNT; i++)
                {
                    if (run.ServicedTick[i] != NOT_SET)
                    {
                        continue;
                    }

                    RecordingAgent a = _source.Agents[i];
                    if (a.DrainTick != NOT_SET && !a.PathPending)
                    {
                        run.ServicedTick[i] = t;
                        run.ServicedStatus[i] = a.PathStatus;
                    }
                    else
                    {
                        open++;
                    }
                }

                if (open == 0 && t >= LAST_DRAIN_TICK)
                {
                    yield break;
                }
            }
        }

        private FixtureSummary Summarize(FixtureRun run)
        {
            var s = new FixtureSummary { MaxServiced = NOT_SET };
            for (int i = 0; i < AGENT_COUNT; i++)
            {
                RecordingAgent a = _source.Agents[i];
                int serviced = run.ServicedTick[i];
                if (serviced == NOT_SET)
                {
                    s.Unserviced++;
                }
                else
                {
                    s.MaxServiced = Mathf.Max(s.MaxServiced, serviced);
                    if (serviced <= FAST_SERVICED_TICK)
                    {
                        s.WithinFast++;
                    }

                    if (run.ServicedStatus[i] != NavMeshPathStatus.PathComplete)
                    {
                        s.NotPathComplete++;
                    }
                }

                if (serviced == NOT_SET || serviced > MAX_SERVICED_TICK)
                {
                    s.Late++;
                }

                if (a.DrainTick != i / PATH_UPDATES_PER_TICK)
                {
                    s.DrainMismatches++;
                }

                if (!a.LastAccepted)
                {
                    s.Rejected++;
                }

                if (a.SetDestinationCalls != 1)
                {
                    s.NotExactlyOnce++;
                }
            }

            return s;
        }

        private void LogResults(FixtureRun run, FixtureSummary s)
        {
            var distribution = new StringBuilder();
            var perTickMs = new StringBuilder();
            double maxSteadyMs = 0.0;
            for (int t = 0; t < run.TicksRun; t++)
            {
                int count = 0;
                foreach (int serviced in run.ServicedTick)
                {
                    if (serviced == t)
                    {
                        count++;
                    }
                }

                distribution.Append(" t").Append(t).Append('=').Append(count);
                perTickMs.Append(" t").Append(t).Append('=').Append(run.RunMs[t].ToString("F3"));
                if (t >= 1 && t <= LAST_DRAIN_TICK && run.RunMs[t] > maxSteadyMs)
                {
                    maxSteadyMs = run.RunMs[t];
                }
            }

            Debug.Log("[Nav011] serviced-tick distribution:" + distribution + " unserviced=" + s.Unserviced
                + " | max serviced tick=" + s.MaxServiced + " | within tick " + FAST_SERVICED_TICK + "=" + s.WithinFast
                + "/" + AGENT_COUNT + " | not PathComplete=" + s.NotPathComplete + " | pending nudges written="
                + _source.NudgesWritten + " | ticks run=" + run.TicksRun);
            Debug.Log("[Nav011] Run() main-thread cost (excludes pathfinding, which runs in the frame): t0 (first call)="
                + run.RunMs[0].ToString("F3") + " ms, max t1-" + LAST_DRAIN_TICK + "=" + maxSteadyMs.ToString("F3")
                + " ms | per tick ms:" + perTickMs + " | NavMesh.pathfindingIterationsPerFrame="
                + NavMesh.pathfindingIterationsPerFrame);
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator test_navigation_drain_150_agents_claimed_on_mesh_without_warnings()
        {
            // Arrange / Act
            BuildAndClaimAll();
            yield return null;

            // Assert
            Assert.That(_service.ActiveCount, Is.EqualTo(AGENT_COUNT));
            Assert.That(_service.AvailableCount, Is.EqualTo(0));
            Assert.That(_factory.Created.Count, Is.EqualTo(AGENT_COUNT));
            int offMesh = 0;
            foreach (RecordingAgent a in _source.Agents)
            {
                Assert.That(a.Inner.Agent.enabled, Is.True);
                if (!a.IsOnNavMesh)
                {
                    offMesh++;
                }
            }

            Assert.That(offMesh, Is.EqualTo(0), "agents off the mesh after claim");
            Assert.That(_warningsAndErrors, Is.Empty);
        }

        [UnityTest]
        public IEnumerator test_navigation_drain_150_agents_serviced_within_10_ticks_at_20_per_tick()
        {
            // Arrange: 150 agents claimed, every destination queued before tick 0, in entity index order.
            BuildAndClaimAll();
            QueueAllDestinations();
            Time.captureDeltaTime = TICK_SECONDS;
            var run = new FixtureRun();

            // Act
            yield return RunTicks(run);
            FixtureSummary s = Summarize(run);
            LogResults(run, s);

            // Assert
            Assert.That(_service.ActiveCount, Is.EqualTo(AGENT_COUNT), "all 150 claimed");
            Assert.That(s.NotExactlyOnce, Is.EqualTo(0), "entities whose agent got SetDestination != once");
            Assert.That(s.Rejected, Is.EqualTo(0), "SetDestination rejected by the agent");
            Assert.That(s.DrainMismatches, Is.EqualTo(0), "entities whose drain tick != index / 20");
            Assert.That(s.NotPathComplete, Is.EqualTo(0), "serviced entities whose path is not PathComplete");
            Assert.That(s.Late, Is.EqualTo(0), "entities not serviced by tick " + MAX_SERVICED_TICK);
            Assert.That(s.WithinFast, Is.GreaterThanOrEqualTo(FAST_REQUIRED_COUNT),
                "entities serviced by tick " + FAST_SERVICED_TICK);
            Assert.That(_warningsAndErrors, Is.Empty);
        }
    }
}
#endif

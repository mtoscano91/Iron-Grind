#if UNITY_EDITOR
#pragma warning disable CS0067 // the fake combat service never raises its event
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.Randomness;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
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
    /// Enemy AI Story 014 (OQ-AI-4, TR-ai-010): the 150-mob tick budget fixture on the real Story 008 NavMesh. 150 mobs are
    /// spawned through <see cref="MobEventBus"/> into a real <see cref="MobRuntimeStore"/> and a real
    /// <see cref="MobDefinitionRegistry"/> (one test definition), and ticked in production order (navigation phase step, then
    /// <see cref="EnemyAISystem.Run"/>, then the integration step, then one frame) for 20 warm-up and 200 measured ticks.
    /// Only <see cref="EnemyAISystem.Run"/> is measured (Stopwatch time, <see cref="GC.GetAllocatedBytesForCurrentThread"/>).
    /// The scenario runs once and its results are cached for the assertions; results are logged with "[EnemyAI014]".
    /// </summary>
    /// <remarks>
    /// Placement (one test definition: AggroRange 10, AttackRange 2, LeashRange 20, MoveSpeed 0.4, WindUp 12, Recovery 6,
    /// Cooldown 10). MoveSpeed 0.4 m/s means a pursuer covers at most 4.4 m in the 220 ticks of the run, so a group B
    /// player 8.2–9.8 m away is never reached (the closest a B mob can get is about 3.8 m, outside AttackRange 2) and
    /// group B never calls the attack resolver, with the default Enemy AI config. Group D uses the same offset and is
    /// leashed at measured tick 100. Sites are 25 m or more apart so a mob only ever sees its own group's player.
    /// Players never die and never move mid-pursuit (except D's leash break), so target-lost and pursuit-reposition
    /// branches are not part of the measured cost.
    /// </remarks>
    public sealed class EnemyAI_TickBudget_tests
    {
        private const string MESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";
        private const int MOB_COUNT = 150;
        private const int GROUP_A_COUNT = 38;
        private const int GROUP_B_COUNT = 37;
        private const int GROUP_C_COUNT = 38;
        private const int GROUP_D_COUNT = 37;
        private const int GROUP_A = 0;
        private const int GROUP_B = 1;
        private const int GROUP_C = 2;
        private const int GROUP_D = 3;
        private const int D_START = GROUP_A_COUNT + GROUP_B_COUNT + GROUP_C_COUNT;
        private const int WARMUP_TICKS = 20;
        private const int MEASURED_TICKS = 200;
        private const int LEASH_BREAK_MEASURED_TICK = 100;
        private const float TICK_SECONDS = 0.05f;
        private const int PATHFINDING_ITERATIONS_PER_FRAME = 100;
        private const double MAX_RUN_MS = 2.0;
        private const float P95 = 0.95f;
        private const float ARRIVAL_SLACK_METRES = 0.6f;
        private const int NOT_SET = -1;
        private const long BYTES_NOT_RECORDED = long.MinValue;
        private const uint FIRST_MOB_ID = 1000u;
        private const uint ZONE_RAW = 7u;
        private const uint MOB_TYPE_RAW = 5u;
        private const int STATE_SLOTS = 16;
        private const float GOLDEN_ANGLE = 2.39996f;

        private const float AGGRO_RANGE = 10f;
        private const float ATTACK_RANGE = 2f;
        private const float LEASH_RANGE = 20f;
        private const float MOVE_SPEED = 0.4f;
        private const int BASE_MAX_HP = 100;
        private const int BASE_ATTACK_POWER = 10;
        private const int BASE_DEFENSE = 5;
        private const int WIND_UP_TICKS = 12;
        private const int RECOVERY_TICKS = 6;
        private const int COOLDOWN_TICKS = 10;
        private const int KILL_XP = 10;
        private const float SPAWN_JITTER_METRES = 0.8f;
        private const float C_RING_METRES = 1.0f;
        private const float A_SPREAD_METRES = 3f;
        private const float PURSUER_PLAYER_OFFSET_METRES = 9f; // groups B and D

        private static readonly Vector3 SITE_A = new Vector3(-30f, 0f, -17.5f);
        private static readonly Vector3 SITE_B = new Vector3(30f, 0f, -12.5f);
        private static readonly Vector3 SITE_C = new Vector3(-25f, 0f, 12.5f);
        private static readonly Vector3 SITE_D = new Vector3(20f, 0f, 17.5f);
        private static readonly Vector3 D_PLAYER_FAR = new Vector3(200f, 0f, 17.5f);

        private static readonly ZoneID ZONE = new ZoneID(ZONE_RAW);
        private static readonly MobTypeID MOB_TYPE = new MobTypeID(MOB_TYPE_RAW);

        private static readonly MobState[] ATTACK_STATES =
        {
            MobState.WindingUp, MobState.AttackExecution, MobState.Recovering
        };

        // ---- Fakes ----

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakePositions : IZonePlayerPositionReader
        {
            public readonly Dictionary<EntityID, Vector3> Map = new Dictionary<EntityID, Vector3>();

            public bool TryGetPosition(EntityID id, out Vector3 position) => Map.TryGetValue(id, out position);
        }

        /// <summary>Every entity has full HP; nothing is ever damaged in this fixture.</summary>
        private sealed class FakeStatReader : IZoneEntityStatReader
        {
            public float GetCurrentHP(EntityID entityId) => 100f;

            public float GetCurrentMP(EntityID entityId) => 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class FakeStatWriter : IMobStatWriter
        {
            public void SetBaseStat(EntityID entityId, StatID statId, int value) { }

            public void SetCurrentHP(EntityID entityId, float value) { }
        }

        private sealed class FakeRandom : IRandomProvider
        {
            public float NextFloat() => 0.5f;

            public double NextDouble() => 0.5;

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class FakeCombat : ICombatService
        {
            public event Action<MobDeathContext> OnMobDied;
        }

        private sealed class FakeLoot : ILootTableService
        {
            public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage) { }

            public void ClearMob(EntityID mobEntityId) { }

            public void ResolveMobDrop(EntityID mobEntityID, int tierShift) { }

            public void Clear() { }
        }

        /// <summary>Counts <c>ResolveAttack</c> calls per mob in a pre-allocated array (no allocation on the call).</summary>
        private sealed class CountingResolver : IMobAttackResolver
        {
            public readonly int[] Calls = new int[MOB_COUNT];

            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange,
                Vector3 capturedForward)
            {
                Calls[(int)(mob.RawValue - FIRST_MOB_ID)]++;
            }
        }

        /// <summary>Results of the one scenario run, cached for the assertions.</summary>
        private sealed class ScenarioResult
        {
            public readonly double[] RunMs = new double[MEASURED_TICKS];
            public readonly long[] Bytes = new long[MEASURED_TICKS];
            public readonly long[] StateTicks = new long[STATE_SLOTS];
            public readonly int[,] MobStateTicks = new int[MOB_COUNT, STATE_SLOTS];
            public readonly int[] PursuingBeforeLeash = new int[MOB_COUNT];
            public readonly int[] FirstReturningTick = new int[GROUP_D_COUNT];
            public readonly List<string> Errors = new List<string>();
            public double FirstTickMs;
            public int WarningCount;
            public int MissedLookups;
            public int[] ResolverCalls;
            public int DormantAtSpawnAtEnd;
            public float MaxDistanceFromSpawnAtEnd;
            public int ActiveAfterTeardown = NOT_SET;
            public int EnabledAgentsAfterTeardown = NOT_SET;
            public float CaptureDeltaBeforeScenario = NOT_SET;
            public float CaptureDeltaAfterTeardown = NOT_SET;
            public double MaxMs;
            public int MaxMsTick = NOT_SET;
            public double P95Ms;
            public long MaxBytes;
        }

        // ---- Fixture state ----

        private static ScenarioResult _cached;

        private readonly List<string> _errors = new List<string>();
        private int _warningCount;
        private NavigationConfig _config;
        private NavMeshDataInstance _instance;
        private UnityNavAgentFactory _unityFactory;
        private MobEventBus _bus;
        private MobRuntimeStore _store;
        private MobDefinitionRegistry _registry;
        private ZoneNavigationService _service;
        private NavigationPhaseStep _navStep;
        private EnemyAISystem _ai;
        private ZoneSlotPool _pool;
        private FakePositions _positions;
        private CountingResolver _resolver;
        private EntityID _playerB;
        private EntityID _playerC;
        private EntityID _playerD;
        private int _savedIterationsPerFrame;
        private float _savedCaptureDeltaTime;
        private bool _tornDown;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _cached = null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _errors.Clear();
            _warningCount = 0;
            _tornDown = false;
            _savedIterationsPerFrame = NavMesh.pathfindingIterationsPerFrame;
            _savedCaptureDeltaTime = Time.captureDeltaTime;
            NavMesh.pathfindingIterationsPerFrame = PATHFINDING_ITERATIONS_PER_FRAME;
            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(MESH_ASSET_PATH);
            Assert.That(data, Is.Not.Null, "NavMeshData not found at " + MESH_ASSET_PATH);
            _instance = NavMesh.AddNavMeshData(data);
            _config = ScriptableObject.CreateInstance<NavigationConfig>();
            _bus = new MobEventBus();
            Application.logMessageReceived += RecordLog;
            yield return null; // let the added mesh settle
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TeardownScenario(null);
            yield return null;
        }

        private void RecordLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors.Add(type + ": " + message);
            }
            else if (type == LogType.Warning)
            {
                _warningCount++;
            }
        }

        // Teardown order as in the Navigation drain fixture: Enemy AI and store unsubscribe, navigation service teardown
        // (disables the agents), enabled agents counted, pool roots destroyed, mesh data removed, config destroyed.
        // Idempotent.
        private void TeardownScenario(ScenarioResult result)
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
            Application.logMessageReceived -= RecordLog;
            Time.captureDeltaTime = _savedCaptureDeltaTime;
            NavMesh.pathfindingIterationsPerFrame = _savedIterationsPerFrame;
            try
            {
                _ai?.Dispose();
                _store?.Dispose();
                if (_service != null)
                {
                    _service.Teardown();
                    if (result != null)
                    {
                        result.ActiveAfterTeardown = _service.ActiveCount;
                        result.EnabledAgentsAfterTeardown = CountEnabledAgents();
                    }

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
                    UnityEngine.Object.DestroyImmediate(_config);
                    _config = null;
                }

                if (result != null)
                {
                    result.CaptureDeltaAfterTeardown = Time.captureDeltaTime;
                }
            }
        }

        private static int CountEnabledAgents()
        {
            int enabled = 0;
            foreach (NavMeshAgent agent in UnityEngine.Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (agent.enabled)
                {
                    enabled++;
                }
            }

            return enabled;
        }

        // ---- Scenario ----

        private static int GroupOf(int index)
        {
            if (index < GROUP_A_COUNT)
            {
                return GROUP_A;
            }

            if (index < GROUP_A_COUNT + GROUP_B_COUNT)
            {
                return GROUP_B;
            }

            return index < D_START ? GROUP_C : GROUP_D;
        }

        private static EntityID MobId(int index) => new EntityID(FIRST_MOB_ID + (uint)index);

        private static Vector3 Spiral(Vector3 center, float radius, int i, int n)
        {
            float r = radius * Mathf.Sqrt((i + 0.5f) / n);
            float a = i * GOLDEN_ANGLE;
            return new Vector3(center.x + Mathf.Cos(a) * r, 0f, center.z + Mathf.Sin(a) * r);
        }

        private static Vector3 SpawnPosition(int index)
        {
            switch (GroupOf(index))
            {
                case GROUP_A:
                    return Spiral(SITE_A, A_SPREAD_METRES, index, GROUP_A_COUNT);
                case GROUP_B:
                    return Spiral(SITE_B, SPAWN_JITTER_METRES, index - GROUP_A_COUNT, GROUP_B_COUNT);
                case GROUP_C:
                {
                    int i = index - GROUP_A_COUNT - GROUP_B_COUNT;
                    float a = i * (2f * Mathf.PI / GROUP_C_COUNT);
                    return new Vector3(SITE_C.x + Mathf.Cos(a) * C_RING_METRES, 0f, SITE_C.z + Mathf.Sin(a) * C_RING_METRES);
                }

                default:
                    return Spiral(SITE_D, SPAWN_JITTER_METRES, index - D_START, GROUP_D_COUNT);
            }
        }

        private EntityID AddPlayer(uint n, Vector3 position)
        {
            bool ok = _pool.TryAllocatePlayer(ZONE, new CharacterID(n), new AccountID(n + 1000u), n, out _, out EntityID id);
            Assert.That(ok, Is.True, "player allocation " + n);
            _positions.Map[id] = position;
            return id;
        }

        // Builds the object graph (the composition's construction order: store, Enemy AI, navigation Initialize), spawns
        // the 150 mobs through the bus and places the three players.
        private void BuildScenario(out int[] spawnedCheck)
        {
            var def = new MobDefinition(
                mobTypeID: MOB_TYPE, displayName: "TickBudgetMob", baseMaxHP: BASE_MAX_HP,
                baseAttackPower: BASE_ATTACK_POWER, baseDefense: BASE_DEFENSE, attackRange: ATTACK_RANGE,
                moveSpeed: MOVE_SPEED, aggroRange: AGGRO_RANGE, leashRange: LEASH_RANGE, attackWindUpTicks: WIND_UP_TICKS,
                attackRecoveryTicks: RECOVERY_TICKS, attackCooldownTicks: COOLDOWN_TICKS, killXP: KILL_XP,
                mobTier: MobTier.Common, enragedChance: 0f, enragedMaxHPMultiplier: 1f, enragedAttackPowerMultiplier: 1f,
                enragedXPMultiplier: 1f, enragedLootQualityBonusTier: 0);
            _registry = MobDefinitionRegistry.Build(new List<MobDefinition> { def });
            Assert.That(_registry.Count, Is.EqualTo(1));

            _pool = new ZoneSlotPool(ZONE, new FakeTicks(), new FakePoolListener());
            Assert.That(_pool.MaxMobs, Is.GreaterThanOrEqualTo(MOB_COUNT));
            _positions = new FakePositions();
            _playerB = AddPlayer(1u, SITE_B + new Vector3(-PURSUER_PLAYER_OFFSET_METRES, 0f, 0f));
            _playerC = AddPlayer(2u, SITE_C);
            _playerD = AddPlayer(3u, SITE_D + new Vector3(-PURSUER_PLAYER_OFFSET_METRES, 0f, 0f));

            _store = new MobRuntimeStore(ZONE, _bus, _registry, new FakeRandom(), new FakeStatWriter(), _pool.MaxMobs);
            _resolver = new CountingResolver();
            var candidates = new ZoneAggroCandidateSource(ZONE, _pool, _positions);
            float returnSpeedCap = NavigationConfigValues.FromAsset(_config).ReturnSpeedCap;
            _unityFactory = new UnityNavAgentFactory(Vector3.zero);
            _service = new ZoneNavigationService(_config, _unityFactory, new MobDefinitionRegistryNavSpeedSource(_registry),
                new UnityNavMeshQuery(), new UnityNavMeshInstanceOps(), _bus);
            _ai = new EnemyAISystem(ZONE, _bus, _store, _registry, _service, new FakeStatReader(), _resolver,
                EnemyAIConfigValues.Defaults, returnSpeedCap, candidates, new FakeCombat(), new FakeLoot(), _pool.MaxMobs);
            _service.Initialize(ZONE, _instance);
            _navStep = new NavigationPhaseStep(_service, _store, MOB_COUNT);

            for (int i = 0; i < MOB_COUNT; i++)
            {
                _bus.RaiseMobSpawned(new MobSpawnedArgs(ZONE, MobId(i), MOB_TYPE, SpawnPosition(i)));
            }

            spawnedCheck = new int[] { _store.Count, _ai.Count, _service.ActiveCount };
        }

        private IEnumerator EnsureRun()
        {
            if (_cached != null)
            {
                yield break;
            }

            var result = new ScenarioResult { CaptureDeltaBeforeScenario = _savedCaptureDeltaTime };
            for (int i = 0; i < GROUP_D_COUNT; i++)
            {
                result.FirstReturningTick[i] = NOT_SET;
            }

            for (int m = 0; m < MEASURED_TICKS; m++)
            {
                result.Bytes[m] = BYTES_NOT_RECORDED;
            }

            BuildScenario(out int[] counts);
            Assert.That(counts[0], Is.EqualTo(MOB_COUNT), "store count");
            Assert.That(counts[1], Is.EqualTo(MOB_COUNT), "Enemy AI controller count");
            Assert.That(counts[2], Is.EqualTo(MOB_COUNT), "navigation agents claimed");
            yield return null;

            yield return RunTicks(result);

            CollectGroupDResults(result);
            result.ResolverCalls = (int[])_resolver.Calls.Clone();
            Summarize(result);
            Log(result);

            result.Errors.AddRange(_errors);
            result.WarningCount = _warningCount;
            TeardownScenario(result);
            _cached = result;
        }

        // The tick loop: navigation step, measured EnemyAISystem.Run, integration step, one frame.
        private IEnumerator RunTicks(ScenarioResult result)
        {
            Time.captureDeltaTime = TICK_SECONDS;
            double ticksToMs = 1000.0 / Stopwatch.Frequency;
            int total = WARMUP_TICKS + MEASURED_TICKS;
            for (int t = 0; t < total; t++)
            {
                int m = t - WARMUP_TICKS;
                if (m == LEASH_BREAK_MEASURED_TICK)
                {
                    _positions.Map[_playerD] = D_PLAYER_FAR;
                }

                _navStep.Run((uint)t, TICK_SECONDS);

                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                _ai.Run((uint)t, TICK_SECONDS);
                long end = Stopwatch.GetTimestamp();
                long bytesAfter = GC.GetAllocatedBytesForCurrentThread();

                _ai.IntegrationStep.Run((uint)t, TICK_SECONDS);

                double ms = (end - start) * ticksToMs;
                if (t == 0)
                {
                    result.FirstTickMs = ms;
                }

                if (m >= 0)
                {
                    result.RunMs[m] = ms;
                    result.Bytes[m] = bytesAfter - bytesBefore;
                    RecordStates(result, m);
                }

                yield return null;
            }
        }

        private void RecordStates(ScenarioResult result, int m)
        {
            for (int i = 0; i < MOB_COUNT; i++)
            {
                if (!_ai.TryGetMobState(MobId(i), out MobState state))
                {
                    result.MissedLookups++;
                    continue;
                }

                result.StateTicks[(int)state]++;
                result.MobStateTicks[i, (int)state]++;
                if (i < D_START)
                {
                    continue;
                }

                if (state == MobState.Pursuing && m < LEASH_BREAK_MEASURED_TICK)
                {
                    result.PursuingBeforeLeash[i]++;
                }

                if (state == MobState.Returning && result.FirstReturningTick[i - D_START] == NOT_SET)
                {
                    result.FirstReturningTick[i - D_START] = m;
                }
            }
        }

        private void CollectGroupDResults(ScenarioResult result)
        {
            for (int i = D_START; i < MOB_COUNT; i++)
            {
                EntityID id = MobId(i);
                if (!_ai.TryGetMobState(id, out MobState state) || !_store.TryGet(id, out MobRuntimeEntry entry))
                {
                    result.MissedLookups++;
                    continue;
                }

                float dx = entry.CommittedPosition.x - entry.SpawnPoint.x;
                float dz = entry.CommittedPosition.z - entry.SpawnPoint.z;
                if (state == MobState.Dormant)
                {
                    result.DormantAtSpawnAtEnd++;
                }

                result.MaxDistanceFromSpawnAtEnd = Mathf.Max(result.MaxDistanceFromSpawnAtEnd, Mathf.Sqrt(dx * dx + dz * dz));
            }
        }

        private static void Summarize(ScenarioResult r)
        {
            r.MaxMs = double.MinValue;
            for (int m = 0; m < MEASURED_TICKS; m++)
            {
                if (r.RunMs[m] > r.MaxMs)
                {
                    r.MaxMs = r.RunMs[m];
                    r.MaxMsTick = m;
                }

                r.MaxBytes = Math.Max(r.MaxBytes, r.Bytes[m]);
            }

            var sorted = (double[])r.RunMs.Clone();
            Array.Sort(sorted);
            r.P95Ms = sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * P95))];
        }

        private static void Log(ScenarioResult r)
        {
            var states = new StringBuilder();
            for (int s = 0; s < STATE_SLOTS; s++)
            {
                if (r.StateTicks[s] > 0 || Enum.IsDefined(typeof(MobState), s))
                {
                    states.Append(' ').Append((MobState)s).Append('=').Append(r.StateTicks[s]);
                }
            }

            var calls = new int[4];
            for (int i = 0; i < MOB_COUNT; i++)
            {
                calls[GroupOf(i)] += r.ResolverCalls[i];
            }

            Debug.Log("[EnemyAI014] EnemyAISystem.Run first tick=" + r.FirstTickMs.ToString("F3") + " ms | measured max="
                + r.MaxMs.ToString("F3") + " ms (tick " + r.MaxMsTick + "), p95=" + r.P95Ms.ToString("F3") + " ms over "
                + MEASURED_TICKS + " ticks");
            Debug.Log("[EnemyAI014] bytes/tick max=" + r.MaxBytes + " (0 required) | per-state mob-ticks over " + MEASURED_TICKS
                + " measured ticks:" + states + " | missed lookups=" + r.MissedLookups);
            Debug.Log("[EnemyAI014] resolver calls: A=" + calls[GROUP_A] + " B=" + calls[GROUP_B] + " C=" + calls[GROUP_C]
                + " D=" + calls[GROUP_D] + " | group D: Dormant at end=" + r.DormantAtSpawnAtEnd + "/" + GROUP_D_COUNT
                + ", max distance from spawn=" + r.MaxDistanceFromSpawnAtEnd.ToString("F3")
                + " m | pathfindingIterationsPerFrame=" + NavMesh.pathfindingIterationsPerFrame);
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator test_tickBudget_150mobs_runsWithoutExceptionOrLogError()
        {
            // Arrange + Act: the cached scenario run
            yield return EnsureRun();

            // Assert
            Assert.That(_cached.Errors, Is.Empty, string.Join(" | ", _cached.Errors));
            Assert.That(_cached.MissedLookups, Is.EqualTo(0), "mobs missing from Enemy AI or the store during the run");
        }

        [UnityTest]
        public IEnumerator test_tickBudget_allSixStatesObservedOnMeasuredTicks()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert
            foreach (MobState s in new[]
            {
                MobState.Dormant, MobState.Pursuing, MobState.WindingUp, MobState.AttackExecution, MobState.Recovering,
                MobState.Returning
            })
            {
                Assert.That(_cached.StateTicks[(int)s], Is.GreaterThan(0L), "state never observed: " + s);
            }
        }

        [UnityTest]
        public IEnumerator test_tickBudget_eachGroup_showsItsIntendedStatesPerMob()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert: A always Dormant; B pursues and never attacks; C goes through the full attack cycle; D pursues
            // before the leash break.
            int[,] st = _cached.MobStateTicks;
            for (int i = 0; i < MOB_COUNT; i++)
            {
                switch (GroupOf(i))
                {
                    case GROUP_A:
                        Assert.That(st[i, (int)MobState.Dormant], Is.EqualTo(MEASURED_TICKS), "group A mob " + i + " left Dormant");
                        break;
                    case GROUP_B:
                        Assert.That(st[i, (int)MobState.Pursuing], Is.GreaterThan(0), "group B mob " + i + " never pursued");
                        foreach (MobState s in ATTACK_STATES)
                        {
                            Assert.That(st[i, (int)s], Is.EqualTo(0), "group B mob " + i + " entered " + s);
                        }

                        break;
                    case GROUP_C:
                        foreach (MobState s in ATTACK_STATES)
                        {
                            Assert.That(st[i, (int)s], Is.GreaterThan(0), "group C mob " + i + " never in " + s);
                        }

                        break;
                    default:
                        Assert.That(_cached.PursuingBeforeLeash[i], Is.GreaterThan(0),
                            "group D mob " + i + " never pursued before the leash break");
                        break;
                }
            }
        }

        [UnityTest]
        public IEnumerator test_tickBudget_enemyAiRun_allocatesZeroBytesOnEveryMeasuredTick()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert (an unrecorded tick keeps BYTES_NOT_RECORDED and fails here)
            for (int m = 0; m < MEASURED_TICKS; m++)
            {
                Assert.That(_cached.Bytes[m], Is.EqualTo(0L), "bytes allocated by Run on measured tick " + m);
            }
        }

        [UnityTest]
        public IEnumerator test_tickBudget_enemyAiRun_maxWithinTwoMilliseconds()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert
            Assert.That(_cached.MaxMs, Is.LessThanOrEqualTo(MAX_RUN_MS),
                "max EnemyAISystem.Run ms over measured ticks (slowest: measured tick " + _cached.MaxMsTick + ")");
        }

        [UnityTest]
        public IEnumerator test_tickBudget_attackResolver_calledForGroupCOnly()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert
            for (int i = 0; i < MOB_COUNT; i++)
            {
                int group = GroupOf(i);
                if (group == GROUP_C)
                {
                    Assert.That(_cached.ResolverCalls[i], Is.GreaterThan(0), "group C mob " + i + " never attacked");
                }
                else
                {
                    Assert.That(_cached.ResolverCalls[i], Is.EqualTo(0), "group " + "ABCD"[group] + " mob " + i);
                }
            }
        }

        [UnityTest]
        public IEnumerator test_tickBudget_groupD_returnsAfterLeashBreakAndEndsDormantAtSpawn()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert (NOT_SET = never Returning, also fails the first check)
            for (int i = 0; i < GROUP_D_COUNT; i++)
            {
                Assert.That(_cached.FirstReturningTick[i], Is.GreaterThanOrEqualTo(LEASH_BREAK_MEASURED_TICK),
                    "group D mob " + i + " first Returning tick (must be at or after " + LEASH_BREAK_MEASURED_TICK + ")");
            }

            Assert.That(_cached.DormantAtSpawnAtEnd, Is.EqualTo(GROUP_D_COUNT), "group D mobs Dormant on the last tick");
            Assert.That(_cached.MaxDistanceFromSpawnAtEnd,
                Is.LessThanOrEqualTo(EnemyAIConfigValues.RETURN_ARRIVAL_THRESHOLD_DEFAULT + ARRIVAL_SLACK_METRES),
                "group D mob distance from its spawn point on the last tick");
        }

        [UnityTest]
        public IEnumerator test_tickBudget_teardown_leavesNoEnabledAgentsAndRestoresCaptureDeltaTime()
        {
            // Arrange + Act
            yield return EnsureRun();

            // Assert
            Assert.That(_cached.ActiveAfterTeardown, Is.EqualTo(0), "navigation service ActiveCount after teardown");
            Assert.That(_cached.EnabledAgentsAfterTeardown, Is.EqualTo(0), "enabled NavMeshAgents after teardown");
            Assert.That(_cached.CaptureDeltaAfterTeardown, Is.EqualTo(_cached.CaptureDeltaBeforeScenario),
                "Time.captureDeltaTime restored after teardown");
        }
    }
}
#endif

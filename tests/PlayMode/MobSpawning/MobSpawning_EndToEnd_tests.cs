#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.Randomness;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
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
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Mob Spawning Story 008 (TR-ms-008, end-to-end part): kill a spawned mob and see it respawn, in a hand-built rig on the
    /// real starting-zone NavMesh. Real: <see cref="ZoneNavigationService"/> with the Unity agent factory,
    /// <see cref="ZoneSlotPool"/>, <see cref="ZoneMobRoster"/>, <see cref="MobEventBus"/>, <see cref="MobRuntimeStore"/>,
    /// <see cref="EnemyAISystem"/>, <see cref="AutoAttackCombatSystem"/>, Character Stats, the shipped mob roster registry and the
    /// spawner built by <see cref="MobSpawningZoneLoader"/> from the shipped starting-zone definition. Per tick, in the Story 006
    /// order: navigation step, spawner Tick, Enemy AI Run, combat Run, Enemy AI integration step, then one frame.
    /// </summary>
    /// <remarks>
    /// Player survival (AC-2): the rig has no aggro candidates and Enemy AI is built with a no-op attack resolver, so mobs never
    /// attack. The damage calculator is a small stand-in (base damage minus nothing, kill when it empties the mob's HP);
    /// the real Combat kill sequence, death bridge, loot stand-in and bus event are all exercised. Each test builds and tears
    /// down its own rig.
    /// </remarks>
    public sealed class MobSpawning_EndToEnd_tests
    {
        private const string MESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";
        private const string DEFINITION_PATH = "Assets/data/Zones/ZoneDefinition_StartingZone.asset";
        private const string ROSTER_PATH = "Assets/data/Mobs/MobRoster.asset";
        private const string LOOT_LOG = "[ZoneServer] loot table not implemented";
        private const int POINT_COUNT = 48;
        private const float TICK_SECONDS = 0.05f;
        private const int PATHFINDING_ITERATIONS_PER_FRAME = 100;
        private const uint ZONE_RAW = 1u;
        private const uint PLAYER_RAW = 11u;
        private const int ONE_SHOT_DAMAGE = 100000;
        private const float KILL_DAMAGE = 100000f;
        private const float NO_ENRAGE_ROLL = 0.99f;
        private const float RETURN_SPEED_FALLBACK = 5f;
        private const float POSITION_TOLERANCE = 0.01f;
        private const int MAX_COMBAT_TICKS = 300;
        private const int BULK_COMMON = 7;
        private const int BULK_UNCOMMON = 5;
        private const int MIN_KILLS = 10;
        private const int KILL_SPACING_TICKS = 3;
        private const int SOAK_MARGIN_TICKS = 10;
        private const int GROUP_SIZE = 3;
        private const int SETTLE_TICKS = 5;
        private const int DEFAULT_LINGER_TICKS = 30;
        private const int DEFAULT_COMMON_DELAY_TICKS = 60;
        private const float WIRE_SCALE = 100f;
        private const float ATTACK_RANGE_METRES = 3f;
        private const float P95 = 0.95f;
        private const double MS_PER_SECOND = 1000.0;

        private static readonly ZoneID ZONE = new ZoneID(ZONE_RAW);

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

        private sealed class FakeRandom : IRandomProvider
        {
            public float NextFloat() => NO_ENRAGE_ROLL;

            public double NextDouble() => NO_ENRAGE_ROLL;

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class NullLeveling : ILevelingService
        {
            public bool IsPlayerEntity(EntityID entityId) => false;

            public int GetExperienceThreshold(EntityID entityId) => int.MaxValue;

            public void NotifyExperienceCrossedThreshold(EntityID entityId, int experienceBeforeGain) { }
        }

        private sealed class NoCandidates : IAggroCandidateSource
        {
            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into) { }

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                position = Vector3.zero;
                return false;
            }
        }

        private sealed class NoopResolver : IMobAttackResolver
        {
            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange,
                Vector3 capturedForward) { }
        }

        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity) => 1.0f;
        }

        private sealed class FakeHitEmitter : IHitResultEmitter
        {
            public void Emit(EntityID attacker, EntityID target, HitResult result) { }
        }

        private sealed class FakeFaceOutput : IAutoAttackFaceOutput
        {
            public void Face(EntityID attacker, Vector3 direction) { }
        }

        private sealed class FakeStatSource : IAutoAttackStatSource
        {
            public int GetBaseDamage(EntityID attacker) => ONE_SHOT_DAMAGE;

            public float GetAttackRange(EntityID attacker) => ATTACK_RANGE_METRES;
        }

        private sealed class CountingSink : IAutoAttackExperienceSink
        {
            public int Calls;
            public int Amount;
            public EntityID LastAttacker;

            public void AddExperience(EntityID attacker, int amount)
            {
                Calls++;
                Amount += amount;
                LastAttacker = attacker;
            }
        }

        /// <summary>Kill when the damage empties the mob's HP; a dead target is rejected.</summary>
        private sealed class HpAwareCalculator : IAutoAttackDamageCalculator
        {
            private readonly ZoneStats _stats;

            public HpAwareCalculator(ZoneStats stats)
            {
                _stats = stats;
            }

            public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
            {
                float hp = _stats.GetCurrentHP(target);
                if (hp <= 0f)
                {
                    return DamageResult.Rejected(DamageContext.PhysicalAuto);
                }

                return new DamageResult(baseDamage, 0, baseDamage, false, hp <= baseDamage, DamageContext.PhysicalAuto, false);
            }
        }

        /// <summary>The player stands 1 m behind (-x of) the target mob, as in the Combat fixtures' geometry.</summary>
        private sealed class FakeZoneState : IZoneEntityStateSource
        {
            public EntityID Player;
            public Vector3 TargetPosition;

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                if (id != Player)
                {
                    view = default;
                    return false;
                }

                view = new ZonePlayerStateView(Wire(TargetPosition.x - 1f), Wire(TargetPosition.y), Wire(TargetPosition.z),
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, null, 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = new ZoneMobStateView(Wire(TargetPosition.x), Wire(TargetPosition.y), Wire(TargetPosition.z),
                    0, 0, 0, 0, 0, 0, 0, false, 0);
                return true;
            }

            private static short Wire(float world) => (short)Mathf.RoundToInt(world * WIRE_SCALE);
        }

        // ---- Rig ----

        private readonly List<string> _errors = new List<string>();
        private readonly List<double> _spawnerMs = new List<double>();
        private NavMeshDataInstance _instance;
        private NavigationConfig _navConfig;
        private UnityNavAgentFactory _factory;
        private ZoneNavigationService _nav;
        private NavigationPhaseStep _navStep;
        private MobEventBus _bus;
        private ZoneSlotPool _pool;
        private ZoneMobRoster _roster;
        private ZoneStats _stats;
        private MobRuntimeStore _store;
        private IMobDefinitionRegistry _registry;
        private EnemyAISystem _ai;
        private AutoAttackCombatSystem _combat;
        private MobSpawningService _spawner;
        private ZoneDefinition _definition;
        private FakeZoneState _zoneState;
        private CountingSink _xp;
        private EntityID _player;
        private int _linger;
        private uint _tick;
        private int _diedCount;
        private int _spawnedCount;
        private int _savedIterations;
        private float _savedCaptureDelta;
        private bool _tornDown;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _errors.Clear();
            _spawnerMs.Clear();
            _tick = 0;
            _diedCount = 0;
            _spawnedCount = 0;
            _tornDown = false;
            _savedIterations = NavMesh.pathfindingIterationsPerFrame;
            _savedCaptureDelta = Time.captureDeltaTime;
            NavMesh.pathfindingIterationsPerFrame = PATHFINDING_ITERATIONS_PER_FRAME;
            Application.logMessageReceived += RecordLog;

            NavMeshData data = AssetDatabase.LoadAssetAtPath<NavMeshData>(MESH_ASSET_PATH);
            Assert.That(data, Is.Not.Null, "NavMeshData not found at " + MESH_ASSET_PATH);
            _instance = NavMesh.AddNavMeshData(data);
            _navConfig = ScriptableObject.CreateInstance<NavigationConfig>();
            _definition = AssetDatabase.LoadAssetAtPath<ZoneDefinition>(DEFINITION_PATH);
            Assert.That(_definition, Is.Not.Null, "zone definition missing at " + DEFINITION_PATH);
            var rosterAsset = AssetDatabase.LoadAssetAtPath<MobRoster>(ROSTER_PATH);
            Assert.That(rosterAsset, Is.Not.Null, "mob roster missing at " + ROSTER_PATH);
            _registry = MobDefinitionRegistry.Build(rosterAsset.ToDefinitions());
            yield return null; // let the added mesh settle

            Time.captureDeltaTime = TICK_SECONDS;
            BuildRig();
            Assert.That(_spawner, Is.Not.Null, "spawner failed to load");
            _spawner.PopulateOnActive();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Teardown();
            yield return null;
        }

        private void RecordLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors.Add(type + ": " + message);
            }
        }

        private void BuildRig()
        {
            var ticks = new FakeTicks();
            _bus = new MobEventBus();
            _pool = new ZoneSlotPool(ZONE, ticks, new FakePoolListener());
            _roster = new ZoneMobRoster(_pool, ticks);
            _stats = new ZoneStats(new NullLeveling());
            _store = new MobRuntimeStore(ZONE, _bus, _registry, new FakeRandom(), new CharacterStatsMobStatWriter(_stats),
                _pool.MaxMobs);

            _zoneState = new FakeZoneState();
            _xp = new CountingSink();
            _combat = new AutoAttackCombatSystem(_pool, new MobTargetValidity(_pool, _store, _stats), new FakeAttackSpeed(),
                _zoneState, new HitDetector(HitDetectionConfig.Default), new FakeHitEmitter(), new FakeStatSource(),
                new HpAwareCalculator(_stats), new CharacterStatsAutoAttackDamageApplier(_stats),
                new MobRuntimeStoreXpAward(_store), _xp, new ZoneMobRosterMobTypeSource(_roster), ZONE, new FakeFaceOutput());

            EnemyAIConfigValues aiConfig = EnemyAIConfigValues.Defaults;
            _linger = aiConfig.DeathLingerTicks;
            float returnSpeedCap = NavigationConfigValues.FromAsset(_navConfig).ReturnSpeedCap;
            _factory = new UnityNavAgentFactory(Vector3.zero);
            _nav = new ZoneNavigationService(_navConfig, _factory, new MobDefinitionRegistryNavSpeedSource(_registry),
                new UnityNavMeshQuery(), new UnityNavMeshInstanceOps(), _bus);
            _ai = new EnemyAISystem(ZONE, _bus, _store, _registry, _nav, new CharacterStatsZoneEntityStatReader(_stats),
                new NoopResolver(), aiConfig, returnSpeedCap > 0f ? returnSpeedCap : RETURN_SPEED_FALLBACK,
                new NoCandidates(), _combat, new StandInLootTableService(), _pool.MaxMobs);
            _nav.Initialize(ZONE, _instance);
            _navStep = new NavigationPhaseStep(_nav, _store, _pool.MaxMobs);

            var validator = new SpawnPointValidator(new UnityNavMeshQuery(), _navConfig);
            MobSpawningZoneLoader.TryLoad(ZONE, _definition, _registry, validator, _roster, _bus,
                MobSpawningConfigValues.Defaults, _linger, out _spawner);

            _bus.OnMobDied += args => _diedCount++;
            _bus.OnMobSpawned += args => _spawnedCount++;
            Assert.IsTrue(_pool.TryAllocatePlayer(ZONE, new CharacterID(PLAYER_RAW), new AccountID(PLAYER_RAW + 1000u),
                PLAYER_RAW, out _, out _player));
            _zoneState.Player = _player;
        }

        private void Teardown()
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;
            Application.logMessageReceived -= RecordLog;
            Time.captureDeltaTime = _savedCaptureDelta;
            NavMesh.pathfindingIterationsPerFrame = _savedIterations;
            try
            {
                _spawner?.CancelAllSpawnTimers(ZONE);
                _ai?.Dispose();
                _store?.Dispose();
                _nav?.Teardown();
            }
            finally
            {
                if (_factory != null)
                {
                    _factory.DestroyPoolRoots();
                    _factory = null;
                }

                if (_instance.valid)
                {
                    NavMesh.RemoveNavMeshData(_instance);
                }

                if (_navConfig != null)
                {
                    UnityEngine.Object.DestroyImmediate(_navConfig);
                    _navConfig = null;
                }
            }
        }

        // One server tick in the Story 006 order, then one frame.
        private IEnumerator Tick()
        {
            _tick++;
            _navStep.Run(_tick, TICK_SECONDS);
            long start = Stopwatch.GetTimestamp();
            _spawner.Tick();
            long end = Stopwatch.GetTimestamp();
            _spawnerMs.Add((end - start) * MS_PER_SECOND / Stopwatch.Frequency);
            _ai.Run(_tick, TICK_SECONDS);
            _combat.Run(_tick, TICK_SECONDS);
            _ai.IntegrationStep.Run(_tick, TICK_SECONDS);
            yield return null;
        }

        private IEnumerator Ticks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return Tick();
            }
        }

        // ---- Helpers ----

        private SpawnPointEntry Entry(int pointIndex) => _definition.SpawnTable.Entries[_spawner.GetPoint(pointIndex).EntryIndex];

        private MobTier TierOf(int pointIndex) => _registry.GetDefinition(Entry(pointIndex).MobTypeID).MobTier;

        private int FindPoint(MobTier tier, bool withoutOverride, int skip = 0)
        {
            for (int i = 0; i < _spawner.PointCount; i++)
            {
                if (TierOf(i) == tier && (!withoutOverride || Entry(i).RespawnDelayOverrideTicks <= 0) && skip-- <= 0)
                {
                    return i;
                }
            }

            Assert.Fail("no point of tier " + tier);
            return -1;
        }

        private int ExpectedDelay(int pointIndex)
        {
            SpawnPointEntry entry = Entry(pointIndex);
            return entry.RespawnDelayOverrideTicks > 0
                ? entry.RespawnDelayOverrideTicks
                : MobSpawningConfigValues.Defaults.RespawnDelayForTier(TierOf(pointIndex));
        }

        private void KillViaStats(int pointIndex)
        {
            _stats.ApplyDamage(_spawner.GetPoint(pointIndex).CurrentEntityID, KILL_DAMAGE);
        }

        private void AssertAllPointsLive()
        {
            Assert.That(_spawner.LiveCount, Is.EqualTo(POINT_COUNT), "points Live");
            Assert.That(_roster.GetMobCount(ZONE), Is.EqualTo(POINT_COUNT), "roster mob count");
            Assert.That(_nav.ActiveCount, Is.EqualTo(POINT_COUNT), "claimed nav agents");
        }

        private void LogSpawnerCost()
        {
            if (_spawnerMs.Count == 0)
            {
                return;
            }

            var sorted = _spawnerMs.ToArray();
            Array.Sort(sorted);
            Debug.Log("[MobSpawning008] spawner Tick() max=" + sorted[sorted.Length - 1].ToString("F4") + " ms, p95="
                + sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * P95))].ToString("F4") + " ms over "
                + sorted.Length + " ticks");
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator test_endToEnd_populatedOnActive_all48PointsLiveWithAgents()
        {
            // Arrange + Act: SetUp populated the zone
            yield return null;

            // Assert
            Assert.That(_spawner.PointCount, Is.EqualTo(POINT_COUNT));
            AssertAllPointsLive();
            for (int i = 0; i < POINT_COUNT; i++)
            {
                Assert.That(_spawner.GetPoint(i).State, Is.EqualTo(SpawnPointState.Live), "point " + i);
            }

            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
        }

        [UnityTest]
        public IEnumerator test_endToEnd_playerAutoAttackKill_awardsXpOnceRaisesMobDiedOnceAndPointDying()
        {
            // Arrange
            LogAssert.Expect(LogType.Log, LOOT_LOG);
            int index = FindPoint(MobTier.Common, true);
            EntityID mob = _spawner.GetPoint(index).CurrentEntityID;
            Assert.That(_store.TryGet(mob, out MobRuntimeEntry before), Is.True);
            int expectedXp = before.Variant.EffectiveKillXP;
            _zoneState.TargetPosition = Entry(index).Position;
            yield return Tick();

            // Act
            _combat.SetAutoAttack(_player, true);
            _combat.SetTarget(_player, mob);
            for (int i = 0; i < MAX_COMBAT_TICKS && _diedCount == 0; i++)
            {
                yield return Tick();
            }

            // Assert
            Assert.That(_diedCount, Is.EqualTo(1), "MobDied raised");
            Assert.That(_xp.Calls, Is.EqualTo(1), "XP awards");
            Assert.That(_xp.Amount, Is.EqualTo(expectedXp), "XP amount is the mob's kill XP");
            Assert.That(_xp.LastAttacker, Is.EqualTo(_player), "XP goes to the player");
            Assert.That(_stats.GetCurrentHP(mob), Is.LessThanOrEqualTo(0f), "killed by Combat damage");
            Assert.That(_spawner.GetPoint(index).State, Is.EqualTo(SpawnPointState.Dying));
            Assert.That(_spawner.GetPoint(index).CurrentEntityID, Is.EqualTo(mob));
            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
        }

        [UnityTest]
        public IEnumerator test_endToEnd_corpseRemoval_releasesSlotControllerAndStoreAtLingerTick()
        {
            // Arrange
            int index = FindPoint(MobTier.Common, true);
            EntityID mob = _spawner.GetPoint(index).CurrentEntityID;
            LogAssert.Expect(LogType.Log, LOOT_LOG);

            // Act: kill through Character Stats, the death routine runs on the next Enemy AI Run (tick T)
            KillViaStats(index);
            yield return Tick();
            Assert.That(_diedCount, Is.EqualTo(1), "MobDied on the kill tick");
            Assert.That(_spawner.GetPoint(index).State, Is.EqualTo(SpawnPointState.Dying), "Dying on the kill tick");
            Assert.That(_nav.ActiveCount, Is.EqualTo(POINT_COUNT - 1), "agent released on MobDied (ADR-002)");
            yield return Ticks(_linger - 1);

            // Assert: T + linger - 1, still a corpse (the navigation agent was released on MobDied, ADR-002 / CR-NAV-6)
            Assert.That(_pool.TryGetMobSlot(mob, out _), Is.True, "slot present");
            Assert.That(_ai.TryGetMobState(mob, out _), Is.True, "controller present");
            Assert.That(_store.TryGet(mob, out _), Is.True, "store entry present");
            Assert.That(_roster.GetMobCount(ZONE), Is.EqualTo(POINT_COUNT));
            Assert.That(_nav.ActiveCount, Is.EqualTo(POINT_COUNT - 1), "agent released at death");

            yield return Tick();

            // Assert: T + linger, everything released on the same tick
            Assert.That(_pool.TryGetMobSlot(mob, out _), Is.False, "slot released");
            Assert.That(_ai.TryGetMobState(mob, out _), Is.False, "controller released");
            Assert.That(_store.TryGet(mob, out _), Is.False, "store entry released");
            Assert.That(_roster.GetMobCount(ZONE), Is.EqualTo(POINT_COUNT - 1));
            Assert.That(_nav.ActiveCount, Is.EqualTo(POINT_COUNT - 1), "no second agent release at the linger end");
            Assert.That(_spawner.GetPoint(index).State, Is.EqualTo(SpawnPointState.Respawning));
            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
        }

        [UnityTest]
        public IEnumerator test_endToEnd_respawn_newMobAtAuthoredPositionAfterLingerPlusDelay()
        {
            // Arrange
            int index = FindPoint(MobTier.Common, true);
            EntityID oldMob = _spawner.GetPoint(index).CurrentEntityID;
            SpawnPointEntry entry = Entry(index);
            int delay = ExpectedDelay(index);
            Assert.That(_linger, Is.EqualTo(DEFAULT_LINGER_TICKS), "F-MS-2 at defaults: linger 30");
            Assert.That(delay, Is.EqualTo(DEFAULT_COMMON_DELAY_TICKS), "F-MS-2 at defaults: Common delay 60");
            LogAssert.Expect(LogType.Log, LOOT_LOG);

            // Act: kill tick T, then to T + linger + delay - 1
            KillViaStats(index);
            yield return Tick();
            yield return Ticks(_linger + delay - 1);

            // Assert: not yet
            Assert.That(_spawner.GetPoint(index).State, Is.EqualTo(SpawnPointState.Respawning));
            Assert.That(_roster.GetMobCount(ZONE), Is.EqualTo(POINT_COUNT - 1));
            Assert.That(_nav.ActiveCount, Is.EqualTo(POINT_COUNT - 1));

            yield return Tick();

            // Assert: T + linger + delay
            SpawnPoint point = _spawner.GetPoint(index);
            Assert.That(point.State, Is.EqualTo(SpawnPointState.Live));
            Assert.That(point.CurrentEntityID, Is.Not.EqualTo(oldMob), "new EntityID");
            Assert.That(_store.TryGet(point.CurrentEntityID, out MobRuntimeEntry runtime), Is.True);
            Assert.That(Vector3.Distance(runtime.CommittedPosition, entry.Position), Is.LessThan(POSITION_TOLERANCE));
            Assert.That(_ai.TryGetMobState(point.CurrentEntityID, out MobState state), Is.True);
            Assert.That(state, Is.EqualTo(MobState.Dormant));
            Assert.That(_roster.TryGetMob(point.CurrentEntityID, out MobTypeID type, out _), Is.True);
            Assert.That(type, Is.EqualTo(entry.MobTypeID));
            Assert.That(_store.TryGet(oldMob, out _), Is.False, "old store entry gone");
            Assert.That(_pool.TryGetMobSlot(oldMob, out _), Is.False, "old slot gone");
            AssertAllPointsLive();
            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
        }

        [UnityTest]
        public IEnumerator test_endToEnd_manyCycles_commonAndUncommonRestoreAllPointsWithoutLeaksOrErrors()
        {
            // Arrange
            var killed = new List<int>();
            for (int i = 0; i < BULK_COMMON; i++)
            {
                killed.Add(FindPoint(MobTier.Common, false, i));
            }

            for (int i = 0; i < BULK_UNCOMMON; i++)
            {
                killed.Add(FindPoint(MobTier.Uncommon, false, i));
            }

            Assert.That(killed.Count, Is.GreaterThanOrEqualTo(MIN_KILLS));
            Assert.That(ExpectedDelay(killed[killed.Count - 1]), Is.GreaterThan(ExpectedDelay(killed[0])),
                "the Uncommon delay differs from the Common one");
            var oldIds = new List<EntityID>();
            foreach (int i in killed)
            {
                oldIds.Add(_spawner.GetPoint(i).CurrentEntityID);
            }

            LogAssert.Expect(LogType.Log, LOOT_LOG);
            int spawnedBefore = _spawnedCount;

            // Act: a sequence of kills, one every KILL_SPACING_TICKS ticks; then linger + longest delay + margin
            int longest = 0;
            foreach (int i in killed)
            {
                longest = Math.Max(longest, ExpectedDelay(i));
                KillViaStats(i);
                yield return Ticks(KILL_SPACING_TICKS);
            }

            Assert.That(_diedCount, Is.EqualTo(killed.Count), "each kill reported once");
            yield return Ticks(_linger + longest + SOAK_MARGIN_TICKS);

            // Assert
            AssertAllPointsLive();
            Assert.That(_spawnedCount - spawnedBefore, Is.EqualTo(killed.Count), "one MobSpawned per killed point");
            for (int k = 0; k < killed.Count; k++)
            {
                EntityID now = _spawner.GetPoint(killed[k]).CurrentEntityID;
                Assert.That(now, Is.Not.EqualTo(oldIds[k]), "point " + killed[k] + " has a new mob");
                Assert.That(_roster.TryGetMob(oldIds[k], out _, out _), Is.False, "old mob of point " + killed[k] + " gone");
            }

            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
            LogSpawnerCost();
        }

        [UnityTest]
        public IEnumerator test_endToEnd_closeWithDyingAndRespawningPoints_nothingSpawnsAfterCancelAndAgentsCleared()
        {
            // Arrange: first group ends up Respawning, second group Dying
            var first = new List<int>();
            var second = new List<int>();
            for (int i = 0; i < GROUP_SIZE; i++)
            {
                first.Add(FindPoint(MobTier.Common, true, i));
                second.Add(FindPoint(MobTier.Uncommon, false, i));
            }

            LogAssert.Expect(LogType.Log, LOOT_LOG);
            foreach (int i in first)
            {
                KillViaStats(i);
            }

            yield return Tick();
            yield return Ticks(_linger + SETTLE_TICKS);
            foreach (int i in second)
            {
                KillViaStats(i);
            }

            yield return Tick();
            foreach (int i in first)
            {
                Assert.That(_spawner.GetPoint(i).State, Is.EqualTo(SpawnPointState.Respawning));
            }

            foreach (int i in second)
            {
                Assert.That(_spawner.GetPoint(i).State, Is.EqualTo(SpawnPointState.Dying));
            }

            // Act: the close order. The run outlasts every pending timer (the Respawning group has about Common delay - 5
            // ticks left, the Dying group linger + Uncommon delay), so a spawn that escaped the cancel would show up.
            // The rig's TearDown calls _nav.Teardown() again; the second call is a guarded no-op.
            int closeRunTicks = _linger + MobSpawningConfigValues.Defaults.RespawnDelayForTier(MobTier.Uncommon) + SETTLE_TICKS;
            int spawnedBeforeCancel = _spawnedCount;
            _spawner.CancelAllSpawnTimers(ZONE);
            _nav.Teardown();
            int activeAfterTeardown = _nav.ActiveCount;
            yield return Ticks(closeRunTicks);

            // Assert
            Assert.That(_spawnedCount, Is.EqualTo(spawnedBeforeCancel), "no MobSpawned after the cancel");
            for (int i = 0; i < _spawner.PointCount; i++)
            {
                Assert.That(_spawner.GetPoint(i).State, Is.EqualTo(SpawnPointState.Cancelled), "point " + i);
            }

            Assert.That(activeAfterTeardown, Is.EqualTo(0), "ActiveCount after Teardown");
            Assert.That(_nav.ActiveCount, Is.EqualTo(0));
            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
            LogSpawnerCost();
        }
    }
}
#endif

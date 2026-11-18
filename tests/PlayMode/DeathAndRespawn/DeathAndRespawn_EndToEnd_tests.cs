#if UNITY_EDITOR
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.DamageCalculation;
using IronGrind.HitDetection;
using IronGrind.LootTableSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.Randomness;
using IronGrind.ServerLogic.DeathAndRespawn;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.StatusEffects;
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
    /// Death and Respawn Story 009 (TR-dr-008): a mob kills a player and the player respawns, end to end on the real
    /// starting-zone NavMesh. Real: navigation, pool, roster, bus, mob store, Enemy AI with
    /// <see cref="ZoneAggroCandidateSource"/> and <see cref="MobAttackResolver"/>, Auto-Attack Combat, Character Stats,
    /// <see cref="MovementService"/> (intent buffer, speed source, NavMesh sampler, entry listener),
    /// <see cref="StatusEffectsService"/>, <see cref="ZoneDefinitionSlotRespawnPointProvider"/> on the shipped
    /// definition, <see cref="ClientMessageOutbox"/>, <see cref="DeathAndRespawnService"/> with its phase step.
    /// Stand-ins: the damage calculator (no crits), the damage output (records hits), the hit-result emitters (the mob
    /// side records every emit), the ghost-death sink (fails when called), the recording send seam and the zone state
    /// source's inner reader (mob positions from the store; Movement supplies player positions). Tick order:
    /// navigation, spawner, Status Effects, Death and Respawn, Movement, Enemy AI, combat, Enemy AI integration, then
    /// flush.
    /// </summary>
    /// <remarks>
    /// The pool slot-release listener is not wired (no slot is released in these tests) and the Movement entry
    /// listener is called by hand (<c>OnPlayerEntering</c>) as a stand-in for <c>EntryCompletion</c>.
    /// </remarks>
    public sealed class DeathAndRespawn_EndToEnd_tests
    {
        private const string MESH_ASSET_PATH = "Assets/NavMesh/Zone0_StartingZone.asset";
        private const string DEFINITION_PATH = "Assets/data/Zones/ZoneDefinition_StartingZone.asset";
        private const string ROSTER_PATH = "Assets/data/Mobs/MobRoster.asset";
        private const float TICK_SECONDS = 0.05f;
        private const int PATHFINDING_ITERATIONS_PER_FRAME = 100;
        private const uint ZONE_RAW = 1u;
        private const uint PLAYER_RAW = 11u;
        private const uint PLAYER_CLIENT = 77u;
        private const uint OTHER_CLIENT = 78u;
        private const int ONE_SHOT_DAMAGE = 100000;
        private const float NO_ENRAGE_ROLL = 0.99f;
        private const float RETURN_SPEED_FALLBACK = 5f;
        private const float ATTACK_RANGE_METRES = 3f;
        private const float BEHIND_METRES = 1f;
        private const float AGGRO_CLEARANCE_METRES = 1f;
        private const int PLAYER_MAX_HP = 100;
        private const int PLAYER_MAX_MP = 50;
        private const float PLAYER_START_MP = 10f;
        private const float PLAYER_START_HP = 1f;
        private const float POSITION_TOLERANCE = 0.1f;
        private const float WIRE_SCALE = 100f;
        private const int ENVELOPE_SIZE = ServerMessageEnvelope.WireSize;
        private const int PHASE_MARGIN_TICKS = 5;

        // Safety multiplier on the computed "phase takes at most N ticks" bounds (scan alignment, path length and
        // return distance are not exactly predictable); 4x keeps the bounds finite but the test free of flakes.
        private const int PHASE_LIMIT_FACTOR = 4;
        private const int SEED = 20260118;
        private const float P95 = 0.95f;
        private const double MS_PER_SECOND = 1000.0;

        private static readonly Vector3 AUTHORED_TOWN_POINT = new Vector3(-40f, 0f, -40f);
        private static readonly ZoneID ZONE = new ZoneID(ZONE_RAW);

        // ---- Stand-ins ----

        private sealed class RigTicks : IServerTickSource
        {
            public uint Tick;

            public uint ServerTickNumber => Tick;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        /// <summary>
        /// Fixed-seed PRNG that is effectively constant: every roll is at least <c>NO_ENRAGE_ROLL</c> (0.99), so no
        /// spawn enrages. The seed only keeps it deterministic should that floor ever be lowered.
        /// </summary>
        private sealed class SeededRandom : IRandomProvider
        {
            private readonly System.Random _random = new System.Random(SEED);

            public float NextFloat() => Mathf.Max(NO_ENRAGE_ROLL, (float)_random.NextDouble());

            public double NextDouble() => Math.Max(NO_ENRAGE_ROLL, _random.NextDouble());

            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        }

        private sealed class NullLeveling : ILevelingService
        {
            public bool IsPlayerEntity(EntityID entityId) => false;

            public int GetExperienceThreshold(EntityID entityId) => int.MaxValue;

            public void NotifyExperienceCrossedThreshold(EntityID entityId, int experienceBeforeGain) { }
        }

        private sealed class FakeAttackSpeed : IAttackSpeedSource
        {
            public float GetAttackSpeedMultiplier(EntityID entity) => 1.0f;
        }

        /// <summary>The player-side combat hit emitter: nothing is checked on it.</summary>
        private sealed class FakeHitEmitter : IHitResultEmitter
        {
            public void Emit(EntityID attacker, EntityID target, HitResult result) { }
        }

        /// <summary>The mob-side hit emitter: records the tick, attacker and target of every emit.</summary>
        private sealed class RecordingHitEmitter : IHitResultEmitter
        {
            public readonly List<(uint Tick, EntityID Attacker, EntityID Target)> Emits =
                new List<(uint, EntityID, EntityID)>();

            public uint CurrentTick;

            public void Emit(EntityID attacker, EntityID target, HitResult result)
            {
                Emits.Add((CurrentTick, attacker, target));
            }
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

        private sealed class NullSink : IAutoAttackExperienceSink
        {
            public void AddExperience(EntityID attacker, int amount) { }
        }

        /// <summary>Kill when the damage empties the target's HP; a dead target is rejected. No crits.</summary>
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

                return new DamageResult(baseDamage, 0, baseDamage, false, hp <= baseDamage,
                    DamageContext.PhysicalAuto, false);
            }
        }

        /// <summary>Records every applied mob hit: the tick it landed on, the attacker and the target.</summary>
        private sealed class CountingOutput : IAutoAttackDamageOutput
        {
            public readonly List<(uint Tick, EntityID Attacker, EntityID Target)> Hits =
                new List<(uint, EntityID, EntityID)>();

            public uint CurrentTick;

            public void Emit(EntityID attacker, EntityID target, in DamageResult result)
            {
                Hits.Add((CurrentTick, attacker, target));
            }
        }

        /// <summary>Wraps the real player-side damage applier and records every application.</summary>
        private sealed class CountingApplier : IAutoAttackDamageApplier
        {
            private readonly IAutoAttackDamageApplier _inner;

            public readonly List<(uint Tick, EntityID Attacker, EntityID Target)> Applied =
                new List<(uint, EntityID, EntityID)>();

            public uint CurrentTick;

            public CountingApplier(IAutoAttackDamageApplier inner)
            {
                _inner = inner;
            }

            public void Apply(EntityID attacker, EntityID target, in DamageResult result)
            {
                Applied.Add((CurrentTick, attacker, target));
                _inner.Apply(attacker, target, in result);
            }
        }

        private sealed class RecordingSeam : IClientMessageSendSeam
        {
            private readonly List<(uint Client, ushort Type, byte[] Body)> _sent =
                new List<(uint, ushort, byte[])>();

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(message);
                _sent.Add((clientId, type, message.Slice(ENVELOPE_SIZE).ToArray()));
            }

            /// <summary>How many messages of the type went to the client with the given decoded entity id.</summary>
            public int Count(ushort type, uint client, uint entityRaw)
            {
                int n = 0;
                foreach ((uint c, ushort t, byte[] body) in _sent)
                {
                    if (t == type && c == client && TryDecodeEntity(type, body, out uint id) && id == entityRaw)
                    {
                        n++;
                    }
                }

                return n;
            }

            private static bool TryDecodeEntity(ushort type, ReadOnlySpan<byte> body, out uint id)
            {
                id = 0u;
                switch (type)
                {
                    case DeathStateEntered.MessageTypeId:
                        return DeathStateEnteredCodec.TryReadBody(body, out id, out _);
                    case EntityDied.MessageTypeId:
                        return EntityDiedCodec.TryReadBody(body, out id);
                    case EntityRespawned.MessageTypeId:
                        return EntityRespawnedCodec.TryReadBody(body, out id, out _);
                    case RespawnConfirmed.MessageTypeId:
                        return RespawnConfirmedCodec.TryReadBody(body, out id);
                    default:
                        return false;
                }
            }
        }

        private sealed class FailingGhostSink : IGhostDeathSink
        {
            public int Calls;

            public void OnGhostDied(EntityID entity)
            {
                Calls++;
            }
        }

        /// <summary>Inner zone state: mob positions from the store; player entries are blank.</summary>
        private sealed class InnerState : IZoneEntityStateSource
        {
            public MobRuntimeStore Store;

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                view = new ZonePlayerStateView(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 0, null, 0u);
                return true;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                if (Store == null || !Store.TryGet(id, out MobRuntimeEntry entry))
                {
                    view = default;
                    return false;
                }

                Vector3 p = entry.CommittedPosition;
                view = new ZoneMobStateView(Wire(p.x), Wire(p.y), Wire(p.z), 0, 0, 0, 0, 0, 0, 0, false, 0);
                return true;
            }

            private static short Wire(float world) => (short)Mathf.RoundToInt(world * WIRE_SCALE);
        }

        // ---- Rig ----

        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<double> _stepMs = new List<double>();
        private readonly List<EntityID> _diedMobs = new List<EntityID>();
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
        private ZoneDefinitionSlot _slot;
        private RigTicks _ticks;
        private CountingOutput _output;
        private RecordingHitEmitter _mobHitEmitter;
        private CountingApplier _applier;
        private RecordingSeam _seam;
        private ClientMessageOutbox _outbox;
        private FailingGhostSink _ghostSink;
        private MovementService _movement;
        private StatusEffectsService _statusEffects;
        private DeathAndRespawnService _service;
        private DeathAndRespawnPhaseStep _deathStep;
        private ZoneDefinitionSlotRespawnPointProvider _respawn;
        private EntityID _player;
        private int _savedIterations;
        private float _savedCaptureDelta;
        private bool _tornDown;
        private int _diedCount;
        private uint _deathTick;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _errors.Clear();
            _warnings.Clear();
            _stepMs.Clear();
            _diedMobs.Clear();
            _tornDown = false;
            _diedCount = 0;
            _deathTick = 0u;
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
            else if (type == LogType.Warning)
            {
                _warnings.Add(message);
            }
        }

        // The return speed the rig gives Enemy AI; AC-2's Dormant limit uses the same expression.
        private float ReturnSpeed()
        {
            float cap = NavigationConfigValues.FromAsset(_navConfig).ReturnSpeedCap;
            return cap > 0f ? cap : RETURN_SPEED_FALLBACK;
        }

        private void BuildRig()
        {
            _ticks = new RigTicks();
            _bus = new MobEventBus();
            _pool = new ZoneSlotPool(ZONE, _ticks, new FakePoolListener());
            _roster = new ZoneMobRoster(_pool, _ticks);
            _stats = new ZoneStats(new NullLeveling());
            _store = new MobRuntimeStore(ZONE, _bus, _registry, new SeededRandom(),
                new CharacterStatsMobStatWriter(_stats), _pool.MaxMobs);
            _output = new CountingOutput();
            _mobHitEmitter = new RecordingHitEmitter();
            _applier = new CountingApplier(new CharacterStatsAutoAttackDamageApplier(_stats));
            _seam = new RecordingSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(PLAYER_CLIENT);
            _outbox.AddClient(OTHER_CLIENT);
            _ghostSink = new FailingGhostSink();
            _slot = new ZoneDefinitionSlot();
            _slot.Set(_definition);
            _respawn = new ZoneDefinitionSlotRespawnPointProvider(ZONE, _slot);

            var slotIndex = new ZoneSlotPoolEntityIndex(_pool);
            var buffer = new MovementIntentBuffer(_ticks, slotIndex, MovementTuningValues.Defaults);
            _movement = new MovementService(buffer, new CharacterStatsMovementSpeedSource(_stats),
                new UnityNavMeshSampler(), slotIndex, MovementTuningValues.Defaults,
                new LoggingMovementGeometryErrorSink(), ZONE, _respawn);
            _statusEffects = new StatusEffectsService(new CharacterStatsBuffModifierLayer(_stats),
                new CharacterStatsResourceSink(_stats), slotIndex, new CharacterStatsDeathSource(_stats));

            var inner = new InnerState { Store = _store };
            var zoneState = new MovementZoneEntityStateSource(inner);
            zoneState.AttachMovement(_movement);
            _combat = new AutoAttackCombatSystem(_pool, new MobTargetValidity(_pool, _store, _stats),
                new FakeAttackSpeed(), zoneState, new HitDetector(HitDetectionConfig.Default), new FakeHitEmitter(),
                new FakeStatSource(), new HpAwareCalculator(_stats), _applier, new MobRuntimeStoreXpAward(_store),
                new NullSink(), new ZoneMobRosterMobTypeSource(_roster), ZONE, new FakeFaceOutput());

            var deathReader = new ForwardingPlayerDeathStateReader();
            _service = new DeathAndRespawnService(DeathAndRespawnConfigValues.Defaults, _pool,
                new CharacterStatsDeathSource(_stats), new ServerTickDeathTickSource(_ticks), _combat, _movement,
                new NoOpPartyMemberStatusSink(), new ZoneSlotGhostStatusSource(_pool), _ghostSink, _respawn,
                new CharacterStatsRespawnVitalsWriter(_stats), _movement, _statusEffects,
                new DeathMessageEmitter(_pool, _outbox));
            deathReader.Bind(_service);
            _deathStep = new DeathAndRespawnPhaseStep(_service);

            EnemyAIConfigValues aiConfig = EnemyAIConfigValues.Defaults;
            _factory = new UnityNavAgentFactory(Vector3.zero);
            _nav = new ZoneNavigationService(_navConfig, _factory, new MobDefinitionRegistryNavSpeedSource(_registry),
                new UnityNavMeshQuery(), new UnityNavMeshInstanceOps(), _bus);
            var resolver = new MobAttackResolver(new HitDetector(HitDetectionConfig.Default), _mobHitEmitter,
                new HpAwareCalculator(_stats), _stats, _output);
            var candidates = new ZoneAggroCandidateSource(ZONE, _pool, new MovementPlayerPositionReader(_movement),
                deathReader);
            _ai = new EnemyAISystem(ZONE, _bus, _store, _registry, _nav, new CharacterStatsZoneEntityStatReader(_stats),
                resolver, aiConfig, ReturnSpeed(), candidates, _combat, new StandInLootTableService(), _pool.MaxMobs);
            _nav.Initialize(ZONE, _instance);
            _navStep = new NavigationPhaseStep(_nav, _store, _pool.MaxMobs);

            var validator = new SpawnPointValidator(new UnityNavMeshQuery(), _navConfig);
            MobSpawningZoneLoader.TryLoad(ZONE, _definition, _registry, validator, _roster, _bus,
                MobSpawningConfigValues.Defaults, aiConfig.DeathLingerTicks, out _spawner);

            _bus.OnMobDied += args =>
            {
                _diedCount++;
                _diedMobs.Add(args.Entity);
            };
            Assert.IsTrue(_pool.TryAllocatePlayer(ZONE, new CharacterID(PLAYER_RAW), new AccountID(PLAYER_RAW + 1000u),
                PLAYER_CLIENT, out byte slot, out _player));
            _stats.SetBaseStat(_player, StatID.MaxHP, PLAYER_MAX_HP);
            _stats.SetBaseStat(_player, StatID.MaxMP, PLAYER_MAX_MP);
            _stats.SetCurrentHP(_player, PLAYER_START_HP);
            _stats.SetCurrentMP(_player, PLAYER_START_MP);
            _movement.OnPlayerEntering(slot, _player);
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
                _service?.Dispose();
                _statusEffects?.Dispose();
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

        // One server tick in the Story 008 order, then flush and one frame.
        private IEnumerator Tick()
        {
            _ticks.Tick++;
            uint tick = _ticks.Tick;
            _output.CurrentTick = tick;
            _mobHitEmitter.CurrentTick = tick;
            _applier.CurrentTick = tick;
            _navStep.Run(tick, TICK_SECONDS);
            _spawner.Tick();
            _statusEffects.Run(tick, TICK_SECONDS);
            long start = Stopwatch.GetTimestamp();
            _deathStep.Run(tick, TICK_SECONDS);
            _stepMs.Add((Stopwatch.GetTimestamp() - start) * MS_PER_SECOND / Stopwatch.Frequency);
            _movement.Run(tick, TICK_SECONDS);
            _ai.Run(tick, TICK_SECONDS);
            _combat.Run(tick, TICK_SECONDS);
            _ai.IntegrationStep.Run(tick, TICK_SECONDS);
            _outbox.Flush(tick);

            // The killing swing resolves in the Enemy AI phase of tick D (MobAttackResolver, inside _ai.Run) and
            // Death and Respawn enters Dead synchronously from Character Stats' OnEntityDied, so IsDead is already
            // true at the end of that same tick D (the Death phase step only runs at the start of D + 1).
            if (_deathTick == 0u && _service.IsDead(_player))
            {
                _deathTick = tick;
            }

            yield return null;
        }

        // ---- Helpers ----

        private sealed class Scenario
        {
            public int Point;
            public EntityID Mob;
            public MobDefinition Definition;
            public EntityID FarMob;
            public EntityID Victim;
            public int CycleTicks;
            public int KillLimit;
        }

        private Scenario ChooseScenario()
        {
            float maxAggro = 0f;
            for (int i = 0; i < _spawner.PointCount; i++)
            {
                maxAggro = Mathf.Max(maxAggro, _registry.GetDefinition(Entry(i).MobTypeID).AggroRange);
            }

            int best = -1;
            float bestGap = -1f;
            for (int i = 0; i < _spawner.PointCount; i++)
            {
                SpawnPointEntry a = Entry(i);
                if (_registry.GetDefinition(a.MobTypeID).MobTier != MobTier.Common)
                {
                    continue;
                }

                float nearest = float.MaxValue;
                for (int j = 0; j < _spawner.PointCount; j++)
                {
                    if (j != i)
                    {
                        nearest = Mathf.Min(nearest, Vector3.Distance(a.Position, Entry(j).Position));
                    }
                }

                if (nearest > bestGap)
                {
                    bestGap = nearest;
                    best = i;
                }
            }

            Assert.That(best, Is.GreaterThanOrEqualTo(0), "a Common spawn point");
            // The shipped table packs spawn points in 3 m clusters (AggroRange 6-9 m), so no point can be isolated
            // beyond maxAggro + clearance; a hard assert here can never hold. Isolation is instead proven by the
            // per-hit attacker == killer checks (AssertOnlyKillerHitPlayerBeforeDeath) and the no-emit-after-D check.
            Debug.Log("[DeathAndRespawn009] killer isolation: nearest spawn point " + bestGap + " m, largest "
                + "AggroRange " + maxAggro + " m, clearance " + AGGRO_CLEARANCE_METRES + " m");
            int far = -1;
            float farDist = -1f;
            int victim = -1;
            float victimDist = -1f;
            for (int j = 0; j < _spawner.PointCount; j++)
            {
                float d = Vector3.Distance(Entry(best).Position, Entry(j).Position);
                if (d > farDist)
                {
                    farDist = d;
                    far = j;
                }

                bool common = _registry.GetDefinition(Entry(j).MobTypeID).MobTier == MobTier.Common;
                if (j != best && d > victimDist && common)
                {
                    victimDist = d;
                    victim = j;
                }
            }

            Assert.That(victim, Is.GreaterThanOrEqualTo(0), "a second Common spawn point to be the victim");
            MobDefinition def = _registry.GetDefinition(Entry(best).MobTypeID);
            int cooldownTail = Mathf.Max(0, def.AttackCooldownTicks - def.AttackRecoveryTicks);
            int cycle = def.AttackWindUpTicks + def.AttackRecoveryTicks + cooldownTail;
            return new Scenario
            {
                Point = best,
                Mob = _spawner.GetPoint(best).CurrentEntityID,
                FarMob = _spawner.GetPoint(far).CurrentEntityID,
                Victim = _spawner.GetPoint(victim).CurrentEntityID,
                Definition = def,
                CycleTicks = cycle,
                KillLimit = (EnemyAIConfigValues.Defaults.AggroScanIntervalTicks + cycle + PHASE_MARGIN_TICKS)
                    * PHASE_LIMIT_FACTOR,
            };
        }

        private SpawnPointEntry Entry(int pointIndex) =>
            _definition.SpawnTable.Entries[_spawner.GetPoint(pointIndex).EntryIndex];

        private Vector3 MobPosition(EntityID mob)
        {
            Assert.That(_store.TryGet(mob, out MobRuntimeEntry entry), Is.True, "mob store entry");
            return entry.CommittedPosition;
        }

        private void PlaceBehind(EntityID mob)
        {
            Vector3 p = MobPosition(mob) + new Vector3(-BEHIND_METRES, 0f, 0f);
            Assert.That(_movement.TrySetAuthoritativePosition(_player, p), Is.True, "teleport behind the mob");
        }

        // Places the player behind the scenario mob, turns auto-attack on at a far mob, ticks until the player dies.
        private IEnumerator RunToKill(Scenario s)
        {
            yield return Tick();
            PlaceBehind(s.Mob);
            _combat.SetAutoAttack(_player, true);
            _combat.SetTarget(_player, s.FarMob);
            for (int i = 0; i < s.KillLimit && _deathTick == 0u; i++)
            {
                yield return Tick();
            }

            Assert.That(_deathTick, Is.Not.EqualTo(0u), "the player died within " + s.KillLimit + " ticks");
        }

        private int RespawnDelay => DeathAndRespawnConfigValues.Defaults.RespawnDelayTicks;

        // Every emit and applied hit on the player up to and including tick D came from the killer; both exist.
        private void AssertOnlyKillerHitPlayerBeforeDeath(Scenario s, uint d)
        {
            int emits = 0;
            foreach ((uint tick, EntityID attacker, EntityID target) in _mobHitEmitter.Emits)
            {
                if (target == _player && tick <= d)
                {
                    Assert.That(attacker, Is.EqualTo(s.Mob),
                        "hit result on the player at tick " + tick + " from the killer");
                    emits++;
                }
            }

            int hits = 0;
            foreach ((uint tick, EntityID attacker, EntityID target) in _output.Hits)
            {
                if (target == _player && tick <= d)
                {
                    Assert.That(attacker, Is.EqualTo(s.Mob),
                        "applied hit on the player at tick " + tick + " from the killer");
                    hits++;
                }
            }

            Assert.That(emits, Is.GreaterThanOrEqualTo(1), "positive control: a hit result on the player by D");
            Assert.That(hits, Is.GreaterThanOrEqualTo(1), "positive control: an applied hit on the player by D");
        }

        private void AssertSentCounts(ushort type, string name, int toPlayerClient, int toOtherClient)
        {
            Assert.That(_seam.Count(type, PLAYER_CLIENT, _player.RawValue), Is.EqualTo(toPlayerClient),
                name + " to the player's client");
            Assert.That(_seam.Count(type, OTHER_CLIENT, _player.RawValue), Is.EqualTo(toOtherClient),
                name + " to the other client");
        }

        private void AssertQuiet()
        {
            Assert.That(_errors, Is.Empty, string.Join(" | ", _errors));
            Assert.That(_ghostSink.Calls, Is.EqualTo(0), "ghost death sink never called");
        }

        private void LogStepCost()
        {
            if (_stepMs.Count == 0)
            {
                return;
            }

            double[] sorted = _stepMs.ToArray();
            Array.Sort(sorted);
            Debug.Log("[DeathAndRespawn009] DeathAndRespawnPhaseStep.Run max="
                + sorted[sorted.Length - 1].ToString("F4")
                + " ms, p95=" + sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * P95))].ToString("F4")
                + " ms over " + sorted.Length + " ticks");
        }

        // ---- Tests ----

        [UnityTest]
        public IEnumerator test_end_to_end_mob_kill_player_is_dead_and_prohibited()
        {
            // Arrange
            Scenario s = ChooseScenario();

            // Act
            yield return RunToKill(s);

            // Assert
            Assert.That(_service.TryGetDeathState(_player, out DeathState state), Is.True);
            Assert.That(state.CurrentState, Is.EqualTo(DeathStateKind.Dead), "dead on tick D");
            Assert.That(_stats.GetCurrentHP(_player), Is.LessThanOrEqualTo(0f));
            Assert.That(_combat.GetState(_player), Is.EqualTo(AutoAttackState.Prohibited));
            AssertOnlyKillerHitPlayerBeforeDeath(s, _deathTick);
            AssertSentCounts(DeathStateEntered.MessageTypeId, "DeathStateEntered", 1, 0);
            AssertSentCounts(EntityDied.MessageTypeId, "EntityDied", 1, 1);
            AssertQuiet();
        }

        [UnityTest]
        public IEnumerator test_end_to_end_mob_lets_go_no_damage_after_death_and_returns_dormant()
        {
            // Arrange
            Scenario s = ChooseScenario();
            yield return RunToKill(s);
            uint d = _deathTick;
            Assert.That(_ai.TryGetMobState(s.Mob, out MobState stateAtDeath), Is.True, "mob known at D");
            bool engaged = stateAtDeath == MobState.Pursuing || stateAtDeath == MobState.WindingUp
                || stateAtDeath == MobState.AttackExecution || stateAtDeath == MobState.Recovering;
            Assert.That(engaged, Is.True,
                "the killer is engaged (not Dormant, not Returning) at D, was " + stateAtDeath);
            AssertOnlyKillerHitPlayerBeforeDeath(s, d);
            float leash = s.Definition.LeashRange;
            int returnTicks = Mathf.CeilToInt(leash / (ReturnSpeed() * TICK_SECONDS));
            int dormantLimit = s.CycleTicks + 1 + returnTicks * PHASE_LIMIT_FACTOR
                + PHASE_MARGIN_TICKS;
            // The computed Dormant limit may exceed the respawn delay with the 4x slack; polling then runs past the
            // respawn (the player is then in town); the no-emit window stays D + 1 .. D + delay - 1.
            Debug.Log("[DeathAndRespawn009] dormantLimit=" + dormantLimit + " respawnDelay=" + RespawnDelay);
            uint returningBy = d + (uint)(s.Definition.AttackWindUpTicks + s.Definition.AttackRecoveryTicks + 1);
            bool returningSeen = false;
            uint dormantTick = 0u;

            // Act: poll every tick from D + 1 to D + RespawnDelay - 1
            int total = Mathf.Max(RespawnDelay - 1, dormantLimit);
            for (int i = 1; i <= total; i++)
            {
                yield return Tick();
                Assert.That(_ai.TryGetMobState(s.Mob, out MobState mobState), Is.True,
                    "the killer is still known to Enemy AI at tick " + _ticks.Tick);
                if (_ticks.Tick <= returningBy && mobState == MobState.Returning)
                {
                    returningSeen = true;
                }

                if (dormantTick == 0u && mobState == MobState.Dormant)
                {
                    dormantTick = _ticks.Tick;
                }
            }

            // Assert
            int emitsOnPlayerAfterD = 0;
            foreach ((uint tick, EntityID attacker, EntityID target) in _mobHitEmitter.Emits)
            {
                emitsOnPlayerAfterD += target == _player && tick > d && tick < d + (uint)RespawnDelay ? 1 : 0;
            }

            int hitsOnPlayerAfterD = 0;
            foreach ((uint tick, EntityID attacker, EntityID target) in _output.Hits)
            {
                hitsOnPlayerAfterD += target == _player && tick > d ? 1 : 0;
            }

            Assert.That(emitsOnPlayerAfterD, Is.EqualTo(0), "no hit result emitted against the dead player");
            Assert.That(hitsOnPlayerAfterD, Is.EqualTo(0), "no damage against the dead player");
            Assert.That(returningSeen, Is.True, "Returning observed in D + 1 .. D + wind-up + recovery + 1");
            Assert.That(dormantTick, Is.Not.EqualTo(0u), "Dormant within " + dormantLimit + " ticks of D + 1");
            Assert.That(dormantTick, Is.LessThanOrEqualTo(d + (uint)dormantLimit), "Dormant by the stated limit");
            AssertQuiet();
        }

        [UnityTest]
        public IEnumerator test_end_to_end_respawn_exactly_at_delay_with_full_vitals_and_messages()
        {
            // Arrange
            Scenario s = ChooseScenario();
            yield return RunToKill(s);
            uint d = _deathTick;
            int delay = RespawnDelay;

            // Act: to D + delay - 1
            for (int i = 1; i < delay; i++)
            {
                yield return Tick();
            }

            // Assert: still dead
            Assert.That(_ticks.Tick, Is.EqualTo(d + (uint)delay - 1u));
            Assert.That(_service.IsDead(_player), Is.True, "still dead at D + delay - 1");
            AssertSentCounts(RespawnConfirmed.MessageTypeId, "RespawnConfirmed before the delay", 0, 0);
            yield return Tick();

            // Assert: alive at D + delay
            Assert.That(_ticks.Tick, Is.EqualTo(d + (uint)delay));
            Assert.That(_service.IsDead(_player), Is.False, "alive at D + delay");
            Vector3 town = _respawn.GetTownRespawnPoint(ZONE);
            Assert.That(town, Is.EqualTo(AUTHORED_TOWN_POINT), "the authored starting-zone town point (TD-112)");
            Assert.That(_movement.TryGetPosition(_player, out Vector3 pos), Is.True);
            Assert.That(Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(town.x, town.z)),
                Is.LessThanOrEqualTo(POSITION_TOLERANCE), "respawn XZ is the provider point");
            Assert.That(NavMesh.SamplePosition(town, out NavMeshHit hit, 2f, NavMesh.AllAreas), Is.True);
            Assert.That(Mathf.Abs(pos.y - hit.position.y), Is.LessThanOrEqualTo(POSITION_TOLERANCE),
                "on the mesh surface");
            Assert.That(_stats.GetCurrentHP(_player),
                Is.EqualTo((float)_stats.GetEffectiveStat(_player, StatID.MaxHP)));
            Assert.That(_stats.GetCurrentMP(_player),
                Is.EqualTo((float)_stats.GetEffectiveStat(_player, StatID.MaxMP)));
            Assert.That(_combat.GetState(_player), Is.Not.EqualTo(AutoAttackState.Prohibited));
            AssertSentCounts(EntityRespawned.MessageTypeId, "EntityRespawned", 1, 1);
            AssertSentCounts(RespawnConfirmed.MessageTypeId, "RespawnConfirmed", 1, 0);
            AssertQuiet();
        }

        [UnityTest]
        public IEnumerator test_end_to_end_after_respawn_player_hits_a_mob_again()
        {
            // Arrange
            Scenario s = ChooseScenario();
            yield return RunToKill(s);
            for (int i = 0; i < RespawnDelay; i++)
            {
                yield return Tick();
            }

            Assert.That(_service.IsDead(_player), Is.False, "respawned");
            EntityID victim = s.Victim;
            Assert.That(_store.TryGet(victim, out _), Is.True, "victim mob alive in the store");

            // Act: player 1 m behind the mob, target + auto-attack on
            PlaceBehind(victim);
            uint teleportTick = _ticks.Tick;
            _combat.SetTarget(_player, victim);
            _combat.SetAutoAttack(_player, true);
            int cycle = AutoAttackCycle.ComputeCycleTicks(1.0f);
            int limit = cycle + 1;
            for (int i = 0; i < limit && !_diedMobs.Contains(victim); i++)
            {
                yield return Tick();
            }

            // Assert: the player's own hit on the victim landed within the attack cycle + 1 of the teleport
            uint firstHit = 0u;
            foreach ((uint tick, EntityID attacker, EntityID target) in _applier.Applied)
            {
                if (attacker == _player && target == victim)
                {
                    firstHit = tick;
                    break;
                }
            }

            Assert.That(firstHit, Is.Not.EqualTo(0u), "the player hit the victim");
            Assert.That(firstHit, Is.LessThanOrEqualTo(teleportTick + (uint)limit), "first hit within cycle + 1");
            Assert.That(_diedMobs, Does.Contain(victim), "one hit one-shots the victim, so MobDied names it");
            Assert.That(_stats.GetCurrentHP(victim), Is.LessThanOrEqualTo(0f));
            Assert.That(_service.IsDead(_player), Is.False, "the player is still alive at the end");
            AssertQuiet();
        }

        [UnityTest]
        public IEnumerator test_end_to_end_whole_run_has_no_errors_and_no_warnings()
        {
            // Arrange
            Scenario s = ChooseScenario();

            // Act: kill, respawn, hit again
            yield return RunToKill(s);
            for (int i = 0; i < RespawnDelay; i++)
            {
                yield return Tick();
            }

            PlaceBehind(s.Victim);
            _combat.SetTarget(_player, s.Victim);
            _combat.SetAutoAttack(_player, true);
            for (int i = 0; i < AutoAttackCycle.ComputeCycleTicks(1.0f) + 1; i++)
            {
                yield return Tick();
            }

            // Assert
            AssertQuiet();
            Assert.That(_warnings, Is.Empty,
                "no warnings (the town point is authored): " + string.Join(" | ", _warnings));
            LogStepCost();
        }
    }
}
#endif

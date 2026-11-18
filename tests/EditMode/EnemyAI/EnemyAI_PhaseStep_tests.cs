#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.DeathAndRespawn;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZoneStats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EnemyAI
{
    /// <summary>
    /// EditMode tests for Enemy AI Story 009: <see cref="EnemyAISystem"/>, <see cref="MobPositionIntegrationStep"/> and
    /// <see cref="ZoneAggroCandidateSource"/> (AC-1..AC-6).
    /// </summary>
    [TestFixture]
    internal sealed class EnemyAI_PhaseStep_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint OTHER_ZONE_RAW = 8u;
        private const uint KNOWN_TYPE_RAW = 1u;
        private const uint UNKNOWN_TYPE_RAW = 99u;
        private const uint MOB_RAW_ID = 2001u;
        private const uint PLAYER_RAW_ID = 101u;
        private const float RETURN_SPEED_CAP = 5f;
        private const float DELTA = 0.05f;
        private const float EPSILON = 1e-5f;
        private const int MANY_MOBS = 150;
        private const int WARM_UP_TICKS = 12;
        private const int MEASURED_TICKS = 10;
        private const double ENRAGED_ROLL = 0.0;
        private const double NON_ENRAGED_ROLL = 0.5;

        private static readonly EntityID PLAYER = new EntityID(PLAYER_RAW_ID);

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            public readonly Dictionary<MobTypeID, MobDefinition> Defs = new Dictionary<MobTypeID, MobDefinition>();

            public MobDefinition GetDefinition(MobTypeID id) => Defs.TryGetValue(id, out MobDefinition? d) ? d : null!;
        }

        private sealed class VelocityNav : INavigationProvider
        {
            public Vector3 Velocity;
            public readonly Dictionary<EntityID, Vector3> PerAgent = new Dictionary<EntityID, Vector3>();
            public readonly List<EntityID> VelocityReads = new List<EntityID>(1024);

            public void SetDestination(EntityID agent, Vector3 target) { }

            public void SetSpeed(EntityID agent, float speed) { }

            public void Stop(EntityID agent) { }

            public void Resume(EntityID agent) { }

            public bool IsPathStale(EntityID agent) => false;

            public Vector3 GetCurrentVelocity(EntityID agent)
            {
                VelocityReads.Add(agent);
                return PerAgent.TryGetValue(agent, out Vector3 own) ? own : Velocity;
            }
        }

        private sealed class CountingNav : INavigationProvider
        {
            public Vector3 Velocity;
            public int Reads;

            public void SetDestination(EntityID agent, Vector3 target) { }

            public void SetSpeed(EntityID agent, float speed) { }

            public void Stop(EntityID agent) { }

            public void Resume(EntityID agent) { }

            public bool IsPathStale(EntityID agent) => false;

            public Vector3 GetCurrentVelocity(EntityID agent)
            {
                Reads++;
                return Velocity;
            }
        }

        private sealed class FakeStats : IZoneEntityStatReader
        {
            public EntityID? ThrowFor;
            public Action<EntityID>? OnGetHP;
            public readonly Dictionary<EntityID, int> HPReads = new Dictionary<EntityID, int>();

            public float GetCurrentHP(EntityID entityId)
            {
                HPReads[entityId] = HPReads.TryGetValue(entityId, out int n) ? n + 1 : 1;
                OnGetHP?.Invoke(entityId);
                if (ThrowFor.HasValue && ThrowFor.Value.Equals(entityId))
                {
                    throw new InvalidOperationException("stat failure");
                }

                return 100f;
            }

            public float GetCurrentMP(EntityID entityId) => 0f;

            public int GetEffectiveStat(EntityID entityId, StatID statId) => 0;
        }

        private sealed class NullResolver : IMobAttackResolver
        {
            public void ResolveAttack(EntityID mob, EntityID target, Vector3 mobPos, Vector3 targetPos, float attackRange, Vector3 capturedForward) { }
        }

        private sealed class FakeCandidates : IAggroCandidateSource
        {
            public readonly List<(EntityID Id, Vector3 Position)> Players = new List<(EntityID Id, Vector3 Position)>();

            public void CopyPlayers(List<(EntityID Id, Vector3 Position)> into) => into.AddRange(Players);

            public bool TryGetPlayerPosition(EntityID id, out Vector3 position)
            {
                for (int i = 0; i < Players.Count; i++)
                {
                    if (Players[i].Id.Equals(id))
                    {
                        position = Players[i].Position;
                        return true;
                    }
                }

                position = Vector3.zero;
                return false;
            }
        }

        private sealed class NullCombat : IronGrind.AutoAttackCombat.ICombatService
        {
#pragma warning disable CS0067
            public event Action<MobDeathContext>? OnMobDied;
#pragma warning restore CS0067
        }

        private sealed class NullLoot : ILootTableService
        {
            public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage) { }

            public void ClearMob(EntityID mobEntityId) { }

            public void ResolveMobDrop(EntityID mobEntityID, int tierShift) { }

            public void Clear() { }
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 100u;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakePositions : IZonePlayerPositionReader
        {
            public readonly Dictionary<EntityID, Vector3> Map = new Dictionary<EntityID, Vector3>();

            public bool TryGetPosition(EntityID id, out Vector3 position) => Map.TryGetValue(id, out position);
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static MobDefinition Def(uint typeRaw, float chance)
        {
            return new MobDefinition(
                mobTypeID: new MobTypeID(typeRaw),
                displayName: "Test Mob",
                baseMaxHP: 100,
                baseAttackPower: 10,
                baseDefense: 5,
                attackRange: 2f,
                moveSpeed: 3.5f,
                aggroRange: 8f,
                leashRange: 20f,
                attackWindUpTicks: 12,
                attackRecoveryTicks: 6,
                attackCooldownTicks: 10,
                killXP: 10,
                mobTier: MobTier.Common,
                enragedChance: chance,
                enragedMaxHPMultiplier: 1.5f,
                enragedAttackPowerMultiplier: 1.3f,
                enragedXPMultiplier: 1.5f,
                enragedLootQualityBonusTier: 1);
        }

        private ZoneStats _zoneStats = null!;
        private MobEventBus _bus = null!;
        private FakeRegistry _registry = null!;
        private ScriptedRandomProvider _rng = null!;
        private MobRuntimeStore _store = null!;
        private VelocityNav _nav = null!;
        private FakeStats _stats = null!;
        private FakeCandidates _candidates = null!;
        private EnemyAISystem _system = null!;
        private NullCombat _combat = null!;
        private NullLoot _loot = null!;

        [SetUp]
        public void SetUp()
        {
            _combat = new NullCombat();
            _loot = new NullLoot();
            _zoneStats = CharacterStatsFixture.Create();
            _bus = new MobEventBus();
            _registry = new FakeRegistry();
            _registry.Defs[new MobTypeID(KNOWN_TYPE_RAW)] = Def(KNOWN_TYPE_RAW, 1.0f);
            _rng = new ScriptedRandomProvider();
            _store = new MobRuntimeStore(Zone, _bus, _registry, _rng, new CharacterStatsMobStatWriter(_zoneStats), MANY_MOBS);
            _nav = new VelocityNav();
            _stats = new FakeStats();
            _candidates = new FakeCandidates();
            _system = NewSystem(_nav);
        }

        [TearDown]
        public void TearDown()
        {
            _system.Dispose();
            _store.Dispose();
        }

        private EnemyAISystem NewSystem(INavigationProvider nav)
        {
            return new EnemyAISystem(Zone, _bus, _store, _registry, nav, _stats, new NullResolver(),
                EnemyAIConfigValues.Defaults, RETURN_SPEED_CAP, _candidates, _combat, _loot, MANY_MOBS);
        }

        private static EntityID MobId(uint offset) => new EntityID(MOB_RAW_ID + offset);

        private MobController Spawn(EntityID id, Vector3 pos, double roll = NON_ENRAGED_ROLL)
        {
            _rng.EnqueueDouble(roll);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, new MobTypeID(KNOWN_TYPE_RAW), pos));
            Assert.IsTrue(_system.TryGetController(id, out MobController c));
            return c;
        }

        // A Pursuing mob whose target (a player 5 m to +X of the mob) is valid and out of attack range.
        private MobController SpawnPursuer(EntityID id, Vector3 pos)
        {
            MobController c = Spawn(id, pos);
            c.ForceStateForTest(MobState.Pursuing);
            c.TargetEntityID = PLAYER;
            return c;
        }

        private void Tick(uint tick = 1u, float dt = DELTA)
        {
            _system.Run(tick, dt);
            _system.IntegrationStep.Run(tick, dt);
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, EPSILON, "x");
            Assert.AreEqual(expected.y, actual.y, EPSILON, "y");
            Assert.AreEqual(expected.z, actual.z, EPSILON, "z");
        }

        // QA 1
        [Test]
        public void test_spawn_createsControllerWithSpawnPointAndVariant()
        {
            var pos = new Vector3(1.5f, 0.25f, -2f);

            MobController enraged = Spawn(MobId(0), pos, ENRAGED_ROLL);
            _registry.Defs[new MobTypeID(KNOWN_TYPE_RAW)] = Def(KNOWN_TYPE_RAW, 0.0f);
            MobController normal = Spawn(MobId(1), pos, NON_ENRAGED_ROLL);

            Assert.AreEqual(2, _system.Count);
            Assert.AreEqual(pos, enraged.SpawnPoint);
            Assert.AreEqual(pos, normal.Position);
            Assert.IsTrue(enraged.IsEnraged);
            Assert.IsFalse(normal.IsEnraged);
            Assert.AreEqual(MobState.Dormant, normal.State);
        }

        // QA 2
        [Test]
        public void test_spawn_refused_noControllerAndSystemLogsNothing()
        {
            Spawn(MobId(0), Vector3.zero);
            var rx = new Regex(@"^\[MobRuntimeStore\]");

            LogAssert.Expect(LogType.Warning, rx);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(new ZoneID(OTHER_ZONE_RAW), MobId(1), new MobTypeID(KNOWN_TYPE_RAW), Vector3.zero));
            LogAssert.Expect(LogType.Error, rx);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, MobId(2), new MobTypeID(UNKNOWN_TYPE_RAW), Vector3.zero));
            LogAssert.Expect(LogType.Error, rx);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, MobId(0), new MobTypeID(KNOWN_TYPE_RAW), Vector3.one));

            Assert.AreEqual(1, _system.Count);
            Assert.IsTrue(_system.TryGetController(MobId(0), out MobController c));
            Assert.AreEqual(Vector3.zero, c.SpawnPoint);
            LogAssert.NoUnexpectedReceived();
        }

        // QA 3
        [Test]
        public void test_construction_afterStoreSeesSeededEntry_disposeUnsubscribesAndIsIdempotent()
        {
            Spawn(MobId(0), Vector3.zero);
            Assert.IsTrue(_store.TryGet(MobId(0), out _));
            Assert.AreEqual(1, _system.Count);

            _system.Dispose();
            Assert.DoesNotThrow(() => _system.Dispose());
            RaiseSpawnWithoutLookup(MobId(1));

            Assert.AreEqual(1, _system.Count);
            Assert.IsFalse(_system.TryGetController(MobId(1), out _));
        }

        private void RaiseSpawnWithoutLookup(EntityID id)
        {
            _rng.EnqueueDouble(NON_ENRAGED_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, id, new MobTypeID(KNOWN_TYPE_RAW), Vector3.zero));
        }

        [Test]
        public void test_spawn_sameIdTwice_noSecondController()
        {
            Spawn(MobId(0), Vector3.zero);

            // The store refuses the duplicate (one error); the system must not add a controller either.
            LogAssert.Expect(LogType.Error, new Regex(@"^\[MobRuntimeStore\]"));
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, MobId(0), new MobTypeID(KNOWN_TYPE_RAW), Vector3.one));

            Assert.AreEqual(1, _system.Count);
        }

        // QA 4
        [Test]
        public void test_remove_knownTrueThenFalse_unknownFalse_removedMobNotTickedOrIntegrated()
        {
            MobController c = SpawnPursuer(MobId(0), Vector3.zero);
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            _nav.Velocity = new Vector3(2f, 0f, 1f);

            Assert.IsTrue(_system.Remove(MobId(0)));
            Assert.AreEqual(0, _system.Count);
            Assert.IsFalse(_system.Remove(MobId(0)));
            Assert.IsFalse(_system.Remove(MobId(77)));

            Tick();

            Assert.AreEqual(0, _nav.VelocityReads.Count);
            Assert.AreEqual(Vector3.zero, c.Position);
        }

        // QA 5
        [Test]
        public void test_run_velocityReadsFollowSpawnOrder_andSwapRemoveOrderIsDeterministic()
        {
            for (uint i = 0; i < 3; i++)
            {
                SpawnPursuer(MobId(i), Vector3.zero);
            }

            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));

            Tick();
            CollectionAssert.AreEqual(new[] { MobId(0), MobId(1), MobId(2) }, _nav.VelocityReads);

            _nav.VelocityReads.Clear();
            Assert.IsTrue(_system.Remove(MobId(0)));
            Tick();

            // Swap-remove: the last mob (2) takes the first slot.
            CollectionAssert.AreEqual(new[] { MobId(2), MobId(1) }, _nav.VelocityReads);
        }

        // QA 6
        [Test]
        public void test_run_throwingController_oneErrorWithIdOthersStillTickAndMove()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController a = SpawnPursuer(MobId(0), Vector3.zero);
            MobController bad = SpawnPursuer(MobId(1), Vector3.zero);
            MobController c = SpawnPursuer(MobId(2), Vector3.zero);
            _stats.ThrowFor = MobId(1);
            _nav.Velocity = new Vector3(2f, 0f, 0f);

            LogAssert.Expect(LogType.Error, new Regex(@"^\[EnemyAISystem\].*" + Regex.Escape(MobId(1).ToString())));
            Tick();

            AssertNear(new Vector3(0.1f, 0f, 0f), a.Position);
            AssertNear(new Vector3(0.1f, 0f, 0f), c.Position);
            Assert.AreEqual(Vector3.zero, bad.Position);
            CollectionAssert.AreEqual(new[] { MobId(0), MobId(2) }, _nav.VelocityReads);
        }

        // QA 7
        [Test]
        public void test_integration_pursuing_movesVelocityXZTimesDeltaAndKeepsY()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            var spawn = new Vector3(0f, 0.5f, 0f);
            MobController c = SpawnPursuer(MobId(0), spawn);
            _nav.Velocity = new Vector3(2f, 5f, 1f);

            for (int i = 1; i <= 3; i++)
            {
                Tick((uint)i);
                AssertNear(spawn + new Vector3(0.1f * i, 0f, 0.05f * i), c.Position);
            }

            Assert.AreEqual(MobState.Pursuing, c.State);
        }

        // QA 8
        [Test]
        public void test_integration_returning_moves()
        {
            var spawn = new Vector3(10f, 0.5f, 10f);
            MobController c = Spawn(MobId(0), spawn);
            Vector3 start = spawn + new Vector3(5f, 0f, 0f);
            c.Position = start;
            c.ForceStateForTest(MobState.Returning);
            _nav.Velocity = new Vector3(2f, 5f, 1f);

            for (int i = 1; i <= 3; i++)
            {
                Tick((uint)i);
                AssertNear(start + new Vector3(0.1f * i, 0f, 0.05f * i), c.Position);
            }

            Assert.AreEqual(MobState.Returning, c.State);
        }

        // QA 9
        [Test]
        public void test_integration_dormantWindingUpRecoveringAttackExecution_noVelocityReadNoMove()
        {
            _nav.Velocity = new Vector3(2f, 5f, 1f);
            MobController dormant = Spawn(MobId(0), Vector3.zero);

            // WindingUp reached for real: target 1.5 m to +X is inside the attack range (2).
            _candidates.Players.Add((PLAYER, new Vector3(1.5f, 0f, 0f)));
            MobController winding = SpawnPursuer(MobId(1), Vector3.zero);

            Tick(1u);
            Tick(2u);

            Assert.AreEqual(MobState.Dormant, dormant.State);
            Assert.AreEqual(MobState.WindingUp, winding.State);
            Assert.AreEqual(0, _nav.VelocityReads.Count);
            Assert.AreEqual(Vector3.zero, dormant.Position);
            Assert.AreEqual(Vector3.zero, winding.Position);
        }

        // QA 10
        [Test]
        public void test_integration_storeMatchesControllerAndFacingWrittenForWindingUp()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController mover = SpawnPursuer(MobId(0), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 0f, 1f);
            Tick(1u);
            Tick(2u);

            Assert.IsTrue(_store.TryGet(MobId(0), out MobRuntimeEntry e));
            Assert.AreEqual(mover.Position, e.CommittedPosition);
            Assert.AreEqual((short)mover.FacingAngle, e.FacingAngle);
            var into = new Dictionary<EntityID, Vector3>();
            _store.CopyCommittedPositions(into);
            Assert.AreEqual(mover.Position, into[MobId(0)]);
            AssertNear(new Vector3(0.2f, 0f, 0.1f), into[MobId(0)]);

            // WindingUp mob: stands still, facing (towards +X, non-zero) still lands in the store.
            _candidates.Players.Clear();
            _candidates.Players.Add((PLAYER, new Vector3(1.5f, 0f, 0f)));
            MobController winding = SpawnPursuer(MobId(1), Vector3.zero);
            Tick(3u);
            Tick(4u);

            Assert.AreEqual(MobState.WindingUp, winding.State);
            Assert.Greater(winding.FacingAngle, 0);
            Assert.IsTrue(_store.TryGet(MobId(1), out MobRuntimeEntry w));
            Assert.AreEqual((short)winding.FacingAngle, w.FacingAngle);
            Assert.AreEqual(Vector3.zero, w.CommittedPosition);
        }

        // QA 11
        [Test]
        public void test_integration_usesDeltaTimeParameter()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController c = SpawnPursuer(MobId(0), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 5f, 1f);

            Tick(1u, 0.1f);

            AssertNear(new Vector3(0.2f, 0f, 0.1f), c.Position);
        }

        // QA 12
        [Test]
        public void test_integration_nonFiniteVelocity_keepsPositionOneWarningThenFiniteMovesAgain()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController c = SpawnPursuer(MobId(0), Vector3.zero);

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MobPositionIntegrationStep\]"));
            _nav.Velocity = new Vector3(float.NaN, 0f, 0f);
            Tick(1u);
            _nav.Velocity = new Vector3(float.PositiveInfinity, 0f, 0f);
            Tick(2u);

            Assert.AreEqual(Vector3.zero, c.Position);
            LogAssert.NoUnexpectedReceived();

            _nav.Velocity = new Vector3(2f, 0f, 0f);
            Tick(3u);
            AssertNear(new Vector3(0.1f, 0f, 0f), c.Position);
        }

        // QA 13
        [Test]
        public void test_integration_cacheClearedAfterApply()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController c = SpawnPursuer(MobId(0), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 0f, 0f);

            _system.IntegrationStep.Run(1u, DELTA);
            Assert.AreEqual(Vector3.zero, c.Position);

            _system.Run(1u, DELTA);
            _system.IntegrationStep.Run(1u, DELTA);
            _system.IntegrationStep.Run(1u, DELTA);

            AssertNear(new Vector3(0.1f, 0f, 0f), c.Position);
        }

        // QA 14
        [Test]
        public void test_candidateSource_connectedWithPositionOnly_inPoolOrder()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakeListener());
            var positions = new FakePositions();
            var ids = new EntityID[6];
            for (uint i = 0; i < ids.Length; i++)
            {
                Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(i + 1u), new AccountID(i + 1001u), i + 1u, out _, out ids[i]));
                positions.Map[ids[i]] = new Vector3(i, 0f, 0f);
            }

            pool.SetSessionState(ids[1], ZoneSessionState.Disconnected_SessionActive);
            var deaths = new FakePlayerDeathStateReader(); // Story 007: dead-ness comes from DeathState, not the slot
            deaths.SetDead(ids[2]);
            deaths.SetDead(ids[3]);
            positions.Map.Remove(ids[4]);
            var source = new ZoneAggroCandidateSource(Zone, pool, positions, deaths);

            var into = new List<(EntityID Id, Vector3 Position)>();
            source.CopyPlayers(into);

            Assert.AreEqual(2, into.Count);
            Assert.AreEqual(ids[0], into[0].Id);
            Assert.AreEqual(ids[5], into[1].Id);
            Assert.AreEqual(new Vector3(5f, 0f, 0f), into[1].Position);
            Assert.IsTrue(source.TryGetPlayerPosition(ids[0], out Vector3 p0));
            Assert.AreEqual(Vector3.zero, p0);
            Assert.IsTrue(source.TryGetPlayerPosition(ids[5], out _));
            for (int i = 1; i <= 4; i++)
            {
                Assert.IsFalse(source.TryGetPlayerPosition(ids[i], out _), $"player {i}");
            }

            Assert.IsFalse(source.TryGetPlayerPosition(new EntityID(9999u), out _));
        }

        // QA 15
        [Test]
        public void test_run_150MixedControllers_allocatesNothingAfterWarmUp()
        {
            var nav = new CountingNav { Velocity = new Vector3(0.5f, 1f, 0f) };
            _system.Dispose();
            _system = NewSystem(nav);
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));

            for (uint i = 0; i < MANY_MOBS; i++)
            {
                if (i % 3 == 0)
                {
                    // Dormant, far from the only player: scans but never aggros.
                    Spawn(MobId(i), new Vector3(100f, 0f, 0f));
                }
                else if (i % 3 == 1)
                {
                    SpawnPursuer(MobId(i), Vector3.zero);
                }
                else
                {
                    MobController r = Spawn(MobId(i), Vector3.zero);
                    r.Position = new Vector3(0f, 0f, 6f);
                    r.ForceStateForTest(MobState.Returning);
                }
            }

            for (int t = 0; t < WARM_UP_TICKS; t++)
            {
                Tick((uint)t);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < MEASURED_TICKS; t++)
            {
                _system.Run((uint)(WARM_UP_TICKS + t), DELTA);
                _system.IntegrationStep.Run((uint)(WARM_UP_TICKS + t), DELTA);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Greater(nav.Reads, 0);
            Assert.AreEqual(0L, after - before);

            // CopyPlayers into a pre-sized list.
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakeListener());
            var positions = new FakePositions();
            for (uint i = 0; i < 20u; i++)
            {
                Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(i + 1u), new AccountID(i + 1001u), i + 1u, out _, out EntityID id));
                positions.Map[id] = Vector3.one;
            }

            var source = new ZoneAggroCandidateSource(Zone, pool, positions);
            var into = new List<(EntityID Id, Vector3 Position)>(50);
            source.CopyPlayers(into);
            long beforeCopy = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                into.Clear();
                source.CopyPlayers(into);
            }

            long afterCopy = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(20, into.Count);
            Assert.AreEqual(0L, afterCopy - beforeCopy);
            LogAssert.NoUnexpectedReceived();
        }

        // QA 9 (Recovering reached for real; AttackExecution is entered and left within one tick, so it is never observable after a tick)
        [Test]
        public void test_integration_realRecovering_noVelocityReadAndNoMove()
        {
            _nav.Velocity = new Vector3(2f, 5f, 1f);
            _candidates.Players.Add((PLAYER, new Vector3(1.5f, 0f, 0f)));
            MobController c = SpawnPursuer(MobId(0), Vector3.zero);
            int recoveringTicks = 0;

            for (uint t = 1u; t <= 40u; t++)
            {
                int reads = _nav.VelocityReads.Count;
                Vector3 before = c.Position;
                Tick(t);
                if (c.State == MobState.Recovering)
                {
                    recoveringTicks++;
                    Assert.AreEqual(reads, _nav.VelocityReads.Count, $"tick {t}");
                    Assert.AreEqual(before, c.Position, $"tick {t}");
                }
            }

            Assert.GreaterOrEqual(recoveringTicks, 1);
        }

        [Test]
        public void test_run_removeOwnMobDuringItsTick_deferredOthersTickOnceAndRemovedMobDoesNotMove()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController a = SpawnPursuer(MobId(0), Vector3.zero);
            MobController b = SpawnPursuer(MobId(1), Vector3.zero);
            MobController c = SpawnPursuer(MobId(2), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 0f, 0f);
            bool first = false;
            bool second = true;
            _stats.OnGetHP = id =>
            {
                if (id.Equals(MobId(1)))
                {
                    first = _system.Remove(MobId(1));
                    second = _system.Remove(MobId(1));
                }
            };

            _system.Run(1u, DELTA);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(2, _system.Count);
            Assert.AreEqual(1, _stats.HPReads[MobId(0)]);
            Assert.AreEqual(1, _stats.HPReads[MobId(1)]);
            Assert.AreEqual(1, _stats.HPReads[MobId(2)]);
            _system.IntegrationStep.Run(1u, DELTA);
            AssertNear(new Vector3(0.1f, 0f, 0f), a.Position);
            AssertNear(new Vector3(0.1f, 0f, 0f), c.Position);
            Assert.AreEqual(Vector3.zero, b.Position);
            CollectionAssert.DoesNotContain(_nav.VelocityReads, MobId(1));
        }

        [Test]
        public void test_run_removeEarlierMobDuringLaterTick_deferredOthersTickOnceAndRemovedMobDoesNotMove()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController a = SpawnPursuer(MobId(0), Vector3.zero);
            MobController b = SpawnPursuer(MobId(1), Vector3.zero);
            MobController c = SpawnPursuer(MobId(2), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 0f, 0f);
            bool removed = false;
            _stats.OnGetHP = id =>
            {
                if (id.Equals(MobId(2)))
                {
                    removed = _system.Remove(MobId(0));
                }
            };

            _system.Run(1u, DELTA);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, _system.Count);
            Assert.AreEqual(1, _stats.HPReads[MobId(0)]);
            Assert.AreEqual(1, _stats.HPReads[MobId(1)]);
            Assert.AreEqual(1, _stats.HPReads[MobId(2)]);
            _system.IntegrationStep.Run(1u, DELTA);
            Assert.AreEqual(Vector3.zero, a.Position);
            AssertNear(new Vector3(0.1f, 0f, 0f), b.Position);
            AssertNear(new Vector3(0.1f, 0f, 0f), c.Position);
            Assert.IsFalse(_system.TryGetController(MobId(0), out _));
        }

        [Test]
        public void test_integration_removeFirstAfterRun_eachRemainingMobMovesOnceByItsOwnVelocity()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController a = SpawnPursuer(MobId(0), Vector3.zero);
            MobController b = SpawnPursuer(MobId(1), Vector3.zero);
            MobController c = SpawnPursuer(MobId(2), Vector3.zero);
            _nav.PerAgent[MobId(0)] = new Vector3(1f, 0f, 0f);
            _nav.PerAgent[MobId(1)] = new Vector3(0f, 0f, 2f);
            _nav.PerAgent[MobId(2)] = new Vector3(3f, 0f, 3f);

            _system.Run(1u, DELTA);
            Assert.IsTrue(_system.Remove(MobId(0)));
            _system.IntegrationStep.Run(1u, DELTA);
            _system.IntegrationStep.Run(1u, DELTA);

            Assert.AreEqual(Vector3.zero, a.Position);
            AssertNear(new Vector3(0f, 0f, 0.1f), b.Position);
            AssertNear(new Vector3(0.15f, 0f, 0.15f), c.Position);
        }

        [Test]
        public void test_integration_spawnIntoFreedSlotAfterRun_doesNotInheritStaleVelocity()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            SpawnPursuer(MobId(0), Vector3.zero);
            SpawnPursuer(MobId(1), Vector3.zero);
            _nav.Velocity = new Vector3(2f, 0f, 0f);

            _system.Run(1u, DELTA);
            Assert.IsTrue(_system.Remove(MobId(1)));
            MobController d = SpawnPursuer(MobId(2), Vector3.zero);
            _system.IntegrationStep.Run(1u, DELTA);

            Assert.AreEqual(Vector3.zero, d.Position);
        }

        [Test]
        public void test_integration_warnedFlagSurvivesSwapRemove_noSecondWarning()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            SpawnPursuer(MobId(0), Vector3.zero);
            MobController b = SpawnPursuer(MobId(1), Vector3.zero);
            _nav.PerAgent[MobId(1)] = new Vector3(float.NaN, 0f, 0f);

            LogAssert.Expect(LogType.Warning, new Regex(@"^\[MobPositionIntegrationStep\]"));
            Tick(1u);
            Assert.IsTrue(_system.Remove(MobId(0)));
            Tick(2u);

            Assert.AreEqual(Vector3.zero, b.Position);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void test_run_capacityBelowMobCount_growsAndAllMobsMoveWithStoreInSync(int capacity)
        {
            _system.Dispose();
            _system = new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, new NullResolver(),
                EnemyAIConfigValues.Defaults, RETURN_SPEED_CAP, _candidates, _combat, _loot, capacity);
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            _nav.Velocity = new Vector3(2f, 0f, 1f);
            var mobs = new MobController[5];
            for (uint i = 0; i < mobs.Length; i++)
            {
                mobs[i] = SpawnPursuer(MobId(i), Vector3.zero);
            }

            Tick();

            for (uint i = 0; i < mobs.Length; i++)
            {
                AssertNear(new Vector3(0.1f, 0f, 0.05f), mobs[i].Position);
                Assert.IsTrue(_store.TryGet(MobId(i), out MobRuntimeEntry e));
                Assert.AreEqual(mobs[i].Position, e.CommittedPosition);
            }
        }

        [Test]
        public void test_constructors_nullReferenceArgument_throwArgumentNullException()
        {
            EnemyAIConfigValues cfg = EnemyAIConfigValues.Defaults;
            var res = new NullResolver();
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, null!, _store, _registry, _nav, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, null!, _registry, _nav, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, null!, _nav, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, null!, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, _nav, null!, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, null!, cfg, RETURN_SPEED_CAP, _candidates, _combat, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, res, cfg, RETURN_SPEED_CAP, null!, _combat, _loot, 1));

            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, null!, _loot, 1));
            Assert.Throws<ArgumentNullException>(() => new EnemyAISystem(Zone, _bus, _store, _registry, _nav, _stats, res, cfg, RETURN_SPEED_CAP, _candidates, _combat, null!, 1));

            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakeListener());
            Assert.Throws<ArgumentNullException>(() => new ZoneAggroCandidateSource(Zone, null!, new FakePositions()));
            Assert.Throws<ArgumentNullException>(() => new ZoneAggroCandidateSource(Zone, pool, null!));
            Assert.Throws<ArgumentNullException>(() => new MovementPlayerPositionReader(null!));
        }

        [Test]
        public void test_integration_dormantMobWithStaleStoreFacing_facingOverwrittenEveryTick()
        {
            MobController c = Spawn(MobId(0), Vector3.zero);
            Assert.IsTrue(_store.TrySetCommitted(MobId(0), Vector3.zero, 450));

            Tick();

            Assert.IsTrue(_store.TryGet(MobId(0), out MobRuntimeEntry e));
            Assert.AreEqual((short)c.FacingAngle, e.FacingAngle);
            Assert.AreEqual(MobState.Dormant, c.State);
        }

        [Test]
        public void test_integration_nonFiniteVelocityY_ignoredAndMobStillMoves()
        {
            _candidates.Players.Add((PLAYER, new Vector3(5f, 0f, 0f)));
            MobController c = SpawnPursuer(MobId(0), new Vector3(0f, 0.5f, 0f));
            _nav.Velocity = new Vector3(2f, float.NaN, 1f);

            Tick();

            AssertNear(new Vector3(0.1f, 0.5f, 0.05f), c.Position);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_spawn_twoMobTypes_eachControllerGetsItsOwnDefinition()
        {
            var typeTwo = new MobTypeID(2u);
            _registry.Defs[typeTwo] = Def(2u, 0.0f);
            MobController one = Spawn(MobId(0), Vector3.zero);
            _rng.EnqueueDouble(NON_ENRAGED_ROLL);
            _bus.RaiseMobSpawned(new MobSpawnedArgs(Zone, MobId(1), typeTwo, Vector3.zero));

            Assert.IsTrue(_system.TryGetController(MobId(1), out MobController two));
            Assert.AreEqual(new MobTypeID(KNOWN_TYPE_RAW), one.Definition.MobTypeID);
            Assert.AreEqual(typeTwo, two.Definition.MobTypeID);
        }

        [Test]
        public void test_candidateSource_copyPlayers_appendsWithoutClearingTheList()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new FakeListener());
            var positions = new FakePositions();
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(1001u), 1u, out _, out EntityID id));
            positions.Map[id] = Vector3.one;
            var source = new ZoneAggroCandidateSource(Zone, pool, positions);
            var existingId = new EntityID(5555u);
            var into = new List<(EntityID Id, Vector3 Position)> { (existingId, new Vector3(9f, 9f, 9f)) };

            source.CopyPlayers(into);

            Assert.AreEqual(2, into.Count);
            Assert.AreEqual(existingId, into[0].Id);
            Assert.AreEqual(id, into[1].Id);
        }
    }
}

using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.Navigation;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MobSpawning
{
    /// <summary>
    /// EditMode tests for Mob Spawning Story 005: <see cref="MobSpawningZoneLoader.TryLoad"/> (CR-MS-2, AC-MS-1,
    /// CR-NAV-13, AC-NAV-21; AC-1 to AC-7; QA cases 1 to 8). A shared call log records the registry lookups (table
    /// validation) and NavMesh samples (spawn point validation) in order; the fake roster counts every call.
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_ZoneLoad_Tests
    {
        private const uint MOB_TYPE_A = 1u;
        private const int LINGER = 3;
        private const int CAP = ZoneSlotPool.MAX_MOBS_PER_ZONE;
        private const string REGISTRY_CALL = "registry";
        private const string SAMPLE_CALL = "sample";
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly Vector3 BOUNDS_MIN = new Vector3(-1000f, -100f, -1000f);
        private static readonly Vector3 BOUNDS_MAX = new Vector3(1000f, 100f, 1000f);

        private readonly List<Object> _assets = new List<Object>();
        private List<string> _calls;
        private FakeRoster _roster;
        private FakeRegistry _registry;
        private FakeMeshQuery _query;
        private SpawnPointValidator _validator;
        private MobEventBus _bus;
        private HashSet<int> _offMesh;

        [SetUp]
        public void SetUp()
        {
            _calls = new List<string>();
            _roster = new FakeRoster();
            _registry = new FakeRegistry(_calls);
            _registry.Set(MOB_TYPE_A, MobTier.Common);
            _bus = new MobEventBus();
            _offMesh = new HashSet<int>();
            _query = new FakeMeshQuery();
            _query.Hits = pos =>
            {
                _calls.Add(SAMPLE_CALL);
                return !_offMesh.Contains((int)pos.x);
            };
            var navConfig = ScriptableObject.CreateInstance<NavigationConfig>(); // defaults: fraction 0.90
            _assets.Add(navConfig);
            _validator = new SpawnPointValidator(_query, navConfig);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _assets)
            {
                Object.DestroyImmediate(o);
            }

            _assets.Clear();
        }

        private sealed class FakeRoster : IZoneMobRoster
        {
            public readonly List<Vector3> AddedPositions = new List<Vector3>();
            public int Count;
            public int CountCalls;
            public int RemoveCalls;
            private uint _nextId = 1000u;

            public int TotalCalls => AddedPositions.Count + CountCalls + RemoveCalls;

            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
            {
                AddedPositions.Add(spawnPosition);
                Count++;
                return new EntityID(_nextId++);
            }

            public void RemoveMob(ZoneID zone, EntityID id)
            {
                RemoveCalls++;
            }

            public int GetMobCount(ZoneID zone)
            {
                CountCalls++;
                return Count;
            }

            public bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition)
            {
                type = default;
                spawnPosition = Vector3.zero;
                return false;
            }
        }

        private sealed class FakeRegistry : IMobDefinitionRegistry
        {
            private readonly Dictionary<uint, MobDefinition> _defs = new Dictionary<uint, MobDefinition>();
            private readonly List<string> _calls;

            public FakeRegistry(List<string> calls)
            {
                _calls = calls;
            }

            public void Set(uint type, MobTier tier)
            {
                _defs[type] = new MobDefinition(
                    mobTypeID: new MobTypeID(type), displayName: "m", baseMaxHP: 1, baseAttackPower: 1,
                    baseDefense: 0, attackRange: 1f, moveSpeed: 1f, aggroRange: 1f, leashRange: 1f,
                    attackWindUpTicks: 1, attackRecoveryTicks: 1, attackCooldownTicks: 1, killXP: 1,
                    mobTier: tier, enragedChance: 0f, enragedMaxHPMultiplier: 1f,
                    enragedAttackPowerMultiplier: 1f, enragedXPMultiplier: 1f, enragedLootQualityBonusTier: 0);
            }

            public MobDefinition GetDefinition(MobTypeID id)
            {
                _calls.Add(REGISTRY_CALL);
                return _defs.TryGetValue(id.RawValue, out MobDefinition def) ? def : null;
            }
        }

        // Entry i has SpawnPointID i + 1 and position (i, 0, 0); the fake query keys off-mesh points by x.
        private static SpawnPointEntry[] Entries(int n)
        {
            var list = new SpawnPointEntry[n];
            for (int i = 0; i < n; i++)
            {
                list[i] = new SpawnPointEntry((uint)(i + 1), new MobTypeID(MOB_TYPE_A), new Vector3(i, 0f, 0f), 0);
            }

            return list;
        }

        private ZoneDefinition MakeDefinition(SpawnPointEntry[] entries, ZoneTemplateID tableTemplate)
        {
            var def = ScriptableObject.CreateInstance<ZoneDefinition>();
            _assets.Add(def);
            def.Initialize(ZoneTemplateID.StartingZone, BOUNDS_MIN, BOUNDS_MAX, Vector3.zero, false, null);
            if (entries != null)
            {
                var table = ScriptableObject.CreateInstance<ZoneSpawnTable>();
                _assets.Add(table);
                table.SetForTests(tableTemplate, entries);
                def.SetSpawnTableForTests(table);
            }

            return def;
        }

        private ZoneDefinition MakeDefinition(SpawnPointEntry[] entries)
        {
            return MakeDefinition(entries, ZoneTemplateID.StartingZone);
        }

        private bool Load(ZoneDefinition def, out MobSpawningService service)
        {
            return MobSpawningZoneLoader.TryLoad(ZONE, def, _registry, _validator, _roster, _bus,
                MobSpawningConfigValues.Defaults, LINGER, out service);
        }

        private static void ExpectError(string pattern)
        {
            LogAssert.Expect(LogType.Error, new Regex(pattern));
        }

        private static void ExpectOffMesh(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ExpectError("Spawn point off the NavMesh");
            }
        }

        private void AssertFailed(bool ok, MobSpawningService service)
        {
            Assert.IsFalse(ok, "TryLoad must fail");
            Assert.IsNull(service, "no service on failure");
            Assert.AreEqual(0, _roster.TotalCalls, "no roster calls");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-1 / QA 1-2: the 150 cap ----

        [Test]
        public void test_zoneLoad_151Entries_failsWithoutNavMeshQuery()
        {
            ZoneDefinition def = MakeDefinition(Entries(CAP + 1));
            ExpectError("SpawnTableOverCapacity");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount, "the cap fails before any NavMesh query");
        }

        [Test]
        public void test_zoneLoad_150EntriesAllOnMesh_buildsServiceWith150Points()
        {
            ZoneDefinition def = MakeDefinition(Entries(CAP));

            bool ok = Load(def, out MobSpawningService service);

            Assert.IsTrue(ok);
            Assert.IsNotNull(service);
            Assert.AreEqual(CAP, service.PointCount);
            Assert.AreEqual(CAP, _query.CallCount, "each point sampled once (Validate called once)");
            Assert.AreEqual(0, _roster.TotalCalls, "the loader never calls the roster");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-2 / QA 3: other table validation failures ----

        [Test]
        public void test_zoneLoad_duplicateSpawnPointId_failsBeforeNavMeshQuery()
        {
            SpawnPointEntry[] entries = Entries(5);
            entries[3] = new SpawnPointEntry(1u, new MobTypeID(MOB_TYPE_A), new Vector3(3f, 0f, 0f), 0);
            ZoneDefinition def = MakeDefinition(entries);
            ExpectError("duplicate SpawnPointID");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [Test]
        public void test_zoneLoad_templateMismatch_failsBeforeNavMeshQuery()
        {
            ZoneDefinition def = MakeDefinition(Entries(3), (ZoneTemplateID)7);
            ExpectError("ZoneTemplateID .* differs from the zone.s");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [Test]
        public void test_zoneLoad_unknownMobType_failsBeforeNavMeshQuery()
        {
            SpawnPointEntry[] entries = Entries(3);
            entries[1] = new SpawnPointEntry(2u, new MobTypeID(99u), new Vector3(1f, 0f, 0f), 0);
            ZoneDefinition def = MakeDefinition(entries);
            ExpectError("unknown");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [TestCase(0, "outside the walkable bounds")]
        [TestCase(1, "NaN or infinite")]
        [TestCase(2, "BossSpawnPointNotSupported")]
        public void test_zoneLoad_invalidEntry_failsBeforeNavMeshQuery(int fault, string logPattern)
        {
            SpawnPointEntry[] entries = Entries(3);
            entries[1] = fault switch
            {
                0 => new SpawnPointEntry(2u, new MobTypeID(MOB_TYPE_A), new Vector3(5000f, 0f, 0f), 0),
                1 => new SpawnPointEntry(2u, new MobTypeID(MOB_TYPE_A), new Vector3(float.NaN, 0f, 0f), 0),
                _ => new SpawnPointEntry(2u, new MobTypeID(MOB_TYPE_A), new Vector3(1f, 0f, 0f), 30),
            };
            ZoneDefinition def = MakeDefinition(entries);
            ExpectError(logPattern);

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [Test]
        public void test_zoneLoad_nullRegistry_failsWithOneError()
        {
            ZoneDefinition def = MakeDefinition(Entries(3));
            ExpectError("mob definition registry is null");

            bool ok = MobSpawningZoneLoader.TryLoad(ZONE, def, null, _validator, _roster, _bus,
                MobSpawningConfigValues.Defaults, LINGER, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [TestCase("validator")]
        [TestCase("roster")]
        [TestCase("bus")]
        public void test_zoneLoad_nullDependency_throwsEvenWithNullDefinition(string missing)
        {
            SpawnPointValidator validator = missing == "validator" ? null : _validator;
            IZoneMobRoster roster = missing == "roster" ? null : _roster;
            MobEventBus bus = missing == "bus" ? null : _bus;

            var ex = Assert.Throws<System.ArgumentNullException>(() => MobSpawningZoneLoader.TryLoad(ZONE, null,
                _registry, validator, roster, bus, MobSpawningConfigValues.Defaults, LINGER, out _));

            Assert.AreEqual(missing, ex.ParamName);
            Assert.AreEqual(0, _query.CallCount);
            Assert.AreEqual(0, _roster.TotalCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-3 / QA 4: exclusion at the 0.90 boundary ----

        [Test]
        public void test_zoneLoad_2Of20OffMesh_boundaryBuilds18PointsAndExcludedNeverSpawn()
        {
            ZoneDefinition def = MakeDefinition(Entries(20));
            _offMesh.Add(4);
            _offMesh.Add(11);
            ExpectOffMesh(2);

            bool ok = Load(def, out MobSpawningService service);

            Assert.IsTrue(ok, "18/20 = 0.90 is at the threshold: no abort");
            Assert.AreEqual(18, service.PointCount);
            Assert.AreEqual(20, _query.CallCount);
            Assert.AreEqual(0, _roster.TotalCalls);

            service.PopulateOnActive();

            var expected = new List<Vector3>();
            for (int i = 0; i < 20; i++)
            {
                if (i != 4 && i != 11)
                {
                    expected.Add(new Vector3(i, 0f, 0f));
                }
            }

            CollectionAssert.AreEqual(expected, _roster.AddedPositions,
                "exactly the 18 valid points, in authored order");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_zoneLoad_90Of100Valid_builds()
        {
            ZoneDefinition def = MakeDefinition(Entries(100));
            for (int i = 0; i < 10; i++)
            {
                _offMesh.Add(i * 10);
            }

            ExpectOffMesh(10);

            bool ok = Load(def, out MobSpawningService service);

            Assert.IsTrue(ok, "90/100 = 0.90 is at the threshold: no abort");
            Assert.AreEqual(90, service.PointCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-4 / QA 5: abort ----

        [Test]
        public void test_zoneLoad_3Of20OffMesh_abortsWithValidatorErrorsOnly()
        {
            ZoneDefinition def = MakeDefinition(Entries(20));
            _offMesh.Add(0);
            _offMesh.Add(9);
            _offMesh.Add(19);
            ExpectOffMesh(3);
            ExpectError(Regex.Escape("CRITICAL: zone " + ZONE + " has only 17 of 20 valid spawn points"));

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(20, _query.CallCount);
        }

        [Test]
        public void test_zoneLoad_89Of100Valid_aborts()
        {
            ZoneDefinition def = MakeDefinition(Entries(100));
            for (int i = 0; i < 11; i++)
            {
                _offMesh.Add(i * 9);
            }

            ExpectOffMesh(11);
            ExpectError("CRITICAL: zone .* has only 89 of 100 valid spawn points");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
        }

        [Test]
        public void test_zoneLoad_allOffMesh_aborts()
        {
            ZoneDefinition def = MakeDefinition(Entries(20));
            for (int i = 0; i < 20; i++)
            {
                _offMesh.Add(i);
            }

            ExpectOffMesh(20);
            ExpectError("CRITICAL: zone .* has only 0 of 20 valid spawn points");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(20, _query.CallCount);
        }

        // ---- AC-5 / QA 6: load seam ----

        [Test]
        public void test_zoneLoad_nullSpawnTable_failsWithOneError()
        {
            ZoneDefinition def = MakeDefinition(null);
            ExpectError("spawn table is null");

            bool ok = Load(def, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
        }

        [Test]
        public void test_zoneLoad_nullDefinition_failsWithOneLoaderError()
        {
            ExpectError(Regex.Escape(MobSpawningZoneLoader.NO_DEFINITION_ERROR) + ".*7");

            bool ok = Load(null, out MobSpawningService service);

            AssertFailed(ok, service);
            Assert.AreEqual(0, _query.CallCount);
            Assert.AreEqual(0, _calls.Count);
        }

        // ---- AC-6 / QA 7: order ----

        [Test]
        public void test_zoneLoad_validTable_tableValidationPrecedesEverySampleAndValidateRunsOnce()
        {
            const int n = 6;
            ZoneDefinition def = MakeDefinition(Entries(n));

            bool ok = Load(def, out MobSpawningService service);

            Assert.IsTrue(ok);
            Assert.IsNotNull(service);
            var expected = new List<string>();
            for (int i = 0; i < n; i++)
            {
                expected.Add(REGISTRY_CALL);
            }

            for (int i = 0; i < n; i++)
            {
                expected.Add(SAMPLE_CALL);
            }

            CollectionAssert.AreEqual(expected, _calls,
                "table validation (registry) → one Validate pass (one sample per point) → service built");
            Assert.AreEqual(0, _roster.TotalCalls, "no roster call before PopulateOnActive (Story 006)");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- AC-7 / QA 8: empty table ----

        [Test]
        public void test_zoneLoad_emptyTable_buildsZeroPointServiceWithTwoWarnings()
        {
            ZoneDefinition def = MakeDefinition(new SpawnPointEntry[0]);
            LogAssert.Expect(LogType.Warning, new Regex("spawn table is empty"));
            LogAssert.Expect(LogType.Warning, new Regex("has an empty spawn table"));

            bool ok = Load(def, out MobSpawningService service);

            Assert.IsTrue(ok, "an empty table is not an abort");
            Assert.IsNotNull(service);
            Assert.AreEqual(0, service.PointCount);
            Assert.AreEqual(0, _query.CallCount);
            Assert.AreEqual(0, _roster.TotalCalls);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

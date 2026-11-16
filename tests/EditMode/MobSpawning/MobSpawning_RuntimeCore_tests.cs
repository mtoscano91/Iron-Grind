using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Events;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.EnemyAI;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MobSpawning
{
    /// <summary>
    /// EditMode tests for Mob Spawning Story 002: <see cref="MobSpawningService"/> T-2 fill and MobDied
    /// (AC-1 to AC-10; QA cases 1 to 11).
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_RuntimeCore_Tests
    {
        private const uint MOB_TYPE_A = 1u;
        private const uint MOB_TYPE_B = 2u;
        private const int DEATH_LINGER = 30;
        private const int CAP = ZoneSlotPool.MAX_MOBS_PER_ZONE;
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly ZoneID OTHER_ZONE = new ZoneID(8u);

        private FakeRoster _roster;
        private MobEventBus _bus;
        private List<MobSpawnedArgs> _spawned;

        [SetUp]
        public void SetUp()
        {
            _roster = new FakeRoster();
            _bus = new MobEventBus();
            _spawned = new List<MobSpawnedArgs>(256);
            _bus.OnMobSpawned += OnSpawned;
        }

        private void OnSpawned(MobSpawnedArgs args)
        {
            _spawned.Add(args);
        }

        private sealed class FakeRoster : IZoneMobRoster
        {
            public readonly List<string> Log = new List<string>(512);
            public readonly List<MobTypeID> AddedTypes = new List<MobTypeID>(256);
            public readonly List<Vector3> AddedPositions = new List<Vector3>(256);
            public readonly HashSet<int> InvalidOnAddCall = new HashSet<int>();
            public int Count;
            public int AddCalls;
            public int RemoveCalls;
            public uint NextId = 1000u;

            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
            {
                Log.Add("Add");
                AddCalls++;
                AddedTypes.Add(type);
                AddedPositions.Add(spawnPosition);
                if (InvalidOnAddCall.Contains(AddCalls))
                {
                    return EntityID.Invalid;
                }

                Count++;
                return new EntityID(NextId++);
            }

            public void RemoveMob(ZoneID zone, EntityID id)
            {
                Log.Add("Remove");
                RemoveCalls++;
            }

            public int GetMobCount(ZoneID zone)
            {
                Log.Add("Count");
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
            public MobDefinition GetDefinition(MobTypeID id) => null;
        }

        private static List<SpawnPointEntry> Entries(int n)
        {
            var list = new List<SpawnPointEntry>(n);
            for (int i = 0; i < n; i++)
            {
                uint type = (i % 2 == 0) ? MOB_TYPE_A : MOB_TYPE_B;
                list.Add(new SpawnPointEntry((uint)(i + 1), new MobTypeID(type), new Vector3(i, 0f, i * 2f), 0));
            }

            return list;
        }

        private MobSpawningService Make(int pointCount)
        {
            return new MobSpawningService(ZONE, Entries(pointCount), _roster, _bus, new FakeRegistry(),
                MobSpawningConfigValues.Defaults, DEATH_LINGER);
        }

        private static SpawnPoint[] Snapshot(MobSpawningService s)
        {
            var copy = new SpawnPoint[s.PointCount];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = s.GetPoint(i);
            }

            return copy;
        }

        private static void AssertSame(SpawnPoint[] before, MobSpawningService s)
        {
            Assert.AreEqual(before.Length, s.PointCount);
            for (int i = 0; i < before.Length; i++)
            {
                SpawnPoint now = s.GetPoint(i);
                Assert.AreEqual(before[i].State, now.State, "state " + i);
                Assert.AreEqual(before[i].CurrentEntityID, now.CurrentEntityID, "id " + i);
                Assert.AreEqual(before[i].TimerTick, now.TimerTick, "timer " + i);
            }
        }

        private void AssertRosterLog(params string[] expected)
        {
            CollectionAssert.AreEqual(expected, _roster.Log, "roster call log: " + string.Join(",", _roster.Log));
        }

        // ---- QA 1 (AC-1, AC-2) ----

        [Test]
        public void test_construct_allPointsUninitialized_idInvalid_timerZero()
        {
            MobSpawningService s = Make(10);

            Assert.AreEqual(10, s.PointCount);
            Assert.AreEqual(0, s.DeferredCount);
            for (int i = 0; i < 10; i++)
            {
                SpawnPoint p = s.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Uninitialized, p.State);
                Assert.AreEqual(EntityID.Invalid, p.CurrentEntityID);
                Assert.AreEqual(0, p.TimerTick);
                Assert.AreEqual(i, p.EntryIndex);
            }
        }

        [Test]
        public void test_construct_subscribesToMobDied()
        {
            MobSpawningService s = Make(1);
            s.PopulateOnActive();
            EntityID id = s.GetPoint(0).CurrentEntityID;

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, id));

            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(0).State);
        }

        [Test]
        public void test_construct_twoServicesOnOneBus_eachReactsOnlyToItsOwnZone()
        {
            // Arrange: two services share one bus (the production bus is per zone; this proves each handler is
            // registered and filters by zone).
            MobSpawningService a = Make(1);
            var b = new MobSpawningService(OTHER_ZONE, Entries(1), _roster, _bus, new FakeRegistry(),
                MobSpawningConfigValues.Defaults, DEATH_LINGER);
            a.PopulateOnActive();
            b.PopulateOnActive();
            EntityID idA = a.GetPoint(0).CurrentEntityID;

            // Act: A's death reaches B too, which warns once as foreign.
            LogAssert.Expect(LogType.Warning, new Regex("foreign zone"));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, idA));

            // Assert
            Assert.AreEqual(SpawnPointState.Dying, a.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, b.GetPoint(0).State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_construct_invalidZone_throwsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new MobSpawningService(ZoneID.Invalid, Entries(1), _roster, _bus,
                new FakeRegistry(), MobSpawningConfigValues.Defaults, DEATH_LINGER));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void test_construct_deathLingerNotPositive_throwsOutOfRange(int linger)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MobSpawningService(ZONE, Entries(1), _roster, _bus,
                new FakeRegistry(), MobSpawningConfigValues.Defaults, linger));
        }

        [Test]
        public void test_construct_capPlusOneEntries_throws_capExactlySucceeds()
        {
            Assert.Throws<ArgumentException>(() => Make(CAP + 1));

            MobSpawningService s = Make(CAP);

            Assert.AreEqual(CAP, s.PointCount);
        }

        [Test]
        public void test_construct_callerListChangedAfterwards_serviceUsesOriginalEntries()
        {
            // Arrange
            List<SpawnPointEntry> list = Entries(2);
            var s = new MobSpawningService(ZONE, list, _roster, _bus, new FakeRegistry(),
                MobSpawningConfigValues.Defaults, DEATH_LINGER);
            Vector3 original = list[0].Position;
            list[0] = new SpawnPointEntry(99u, new MobTypeID(MOB_TYPE_B), new Vector3(500f, 0f, 500f), 0);
            list.Clear();

            // Act
            s.PopulateOnActive();

            // Assert
            Assert.AreEqual(2, _spawned.Count);
            Assert.AreEqual(original, _spawned[0].SpawnPosition);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), _spawned[0].MobType);
        }

        [Test]
        public void test_ctor_invalidConfig_throwsArgumentException_logsKnobError()
        {
            // Arrange: retry interval 5 is below the [10,200] range; every other knob is valid
            var bad = new MobSpawningConfigValues(60, 120, 300, 600, 5);

            // Act
            LogAssert.Expect(LogType.Error, new Regex("SPAWN_DENSITY_RETRY_INTERVAL_TICKS"));

            // Assert
            Assert.Throws<ArgumentException>(() => new MobSpawningService(ZONE, Entries(1), _roster, _bus,
                new FakeRegistry(), bad, DEATH_LINGER));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_construct_nullReferenceArgs_throw()
        {
            List<SpawnPointEntry> e = Entries(1);
            var reg = new FakeRegistry();
            MobSpawningConfigValues cfg = MobSpawningConfigValues.Defaults;

            Assert.Throws<ArgumentNullException>(() => new MobSpawningService(ZONE, null, _roster, _bus, reg, cfg, 30));
            Assert.Throws<ArgumentNullException>(() => new MobSpawningService(ZONE, e, null, _bus, reg, cfg, 30));
            Assert.Throws<ArgumentNullException>(() => new MobSpawningService(ZONE, e, _roster, null, reg, cfg, 30));
            Assert.Throws<ArgumentNullException>(() => new MobSpawningService(ZONE, e, _roster, _bus, null, cfg, 30));
        }

        // ---- QA 2 (AC-3) ----

        [Test]
        public void test_populate_tenPoints_countThenAddAlternates_inDeclarationOrder_allLive()
        {
            MobSpawningService s = Make(10);

            s.PopulateOnActive();

            Assert.AreEqual(20, _roster.Log.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual("Count", _roster.Log[i * 2]);
                Assert.AreEqual("Add", _roster.Log[i * 2 + 1]);
            }

            List<SpawnPointEntry> expected = Entries(10);
            Assert.AreEqual(10, _spawned.Count);
            for (int i = 0; i < 10; i++)
            {
                SpawnPoint p = s.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Live, p.State);
                Assert.AreEqual(new EntityID(1000u + (uint)i), p.CurrentEntityID);
                Assert.AreEqual(0, p.TimerTick);
                Assert.AreEqual(expected[i].MobTypeID, _roster.AddedTypes[i]);
                Assert.AreEqual(expected[i].Position, _roster.AddedPositions[i]);
                Assert.AreEqual(ZONE, _spawned[i].Zone);
                Assert.AreEqual(p.CurrentEntityID, _spawned[i].Entity);
                Assert.AreEqual(expected[i].MobTypeID, _spawned[i].MobType);
                Assert.AreEqual(expected[i].Position, _spawned[i].SpawnPosition);
            }

            Assert.AreEqual(0, s.DeferredCount);
        }

        // ---- QA 3 (AC-3) ----

        [Test]
        public void test_populate_secondCall_noRosterCalls_oneWarning()
        {
            MobSpawningService s = Make(3);
            s.PopulateOnActive();
            int logCount = _roster.Log.Count;
            SpawnPoint[] before = Snapshot(s);

            LogAssert.Expect(LogType.Warning, new Regex("already populated"));
            s.PopulateOnActive();
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Warning, new Regex("already populated"));
            s.PopulateOnActive();

            Assert.AreEqual(logCount, _roster.Log.Count);
            Assert.AreEqual(3, _spawned.Count);
            AssertSame(before, s);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 4 (AC-4) ----

        [Test]
        public void test_populate_atCap_noAdd_allDeferred_listed_noSpawnedEvent()
        {
            _roster.Count = CAP;
            MobSpawningService s = Make(5);

            s.PopulateOnActive();

            AssertRosterLog("Count", "Count", "Count", "Count", "Count");
            Assert.AreEqual(0, _roster.AddCalls);
            Assert.AreEqual(0, _spawned.Count);
            Assert.AreEqual(5, s.DeferredCount);
            LogAssert.NoUnexpectedReceived();
            for (int i = 0; i < 5; i++)
            {
                SpawnPoint p = s.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Deferred, p.State);
                Assert.AreEqual(EntityID.Invalid, p.CurrentEntityID);
                Assert.AreEqual(0, p.TimerTick);
                Assert.AreEqual(i, s.GetDeferredPointIndex(i));
            }
        }

        // ---- QA 5 ----

        [Test]
        public void test_populate_partialCap_twoLive_threeDeferred()
        {
            _roster.Count = CAP - 2;
            MobSpawningService s = Make(5);

            s.PopulateOnActive();

            AssertRosterLog("Count", "Add", "Count", "Add", "Count", "Count", "Count");
            Assert.AreEqual(2, _spawned.Count);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(1).State);
            Assert.AreEqual(new EntityID(1000u), s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(new EntityID(1001u), s.GetPoint(1).CurrentEntityID);
            Assert.AreEqual(3, s.DeferredCount);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(2 + i).State);
                Assert.AreEqual(EntityID.Invalid, s.GetPoint(2 + i).CurrentEntityID);
                Assert.AreEqual(2 + i, s.GetDeferredPointIndex(i));
            }

            Assert.AreEqual(CAP, _roster.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_populate_invalidThenCapInOneSweep_deferredListInDeclarationOrder()
        {
            // Arrange: first add returns Invalid (count stays CAP-2), next two succeed, the last two hit the cap.
            _roster.Count = CAP - 2;
            _roster.InvalidOnAddCall.Add(1);
            MobSpawningService s = Make(5);

            // Act
            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure"));
            s.PopulateOnActive();

            // Assert
            AssertRosterLog("Count", "Add", "Count", "Add", "Count", "Add", "Count", "Count");
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(1).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(2).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(3).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(4).State);
            Assert.AreEqual(3, s.DeferredCount);
            Assert.AreEqual(0, s.GetDeferredPointIndex(0));
            Assert.AreEqual(3, s.GetDeferredPointIndex(1));
            Assert.AreEqual(4, s.GetDeferredPointIndex(2));
            Assert.AreEqual(2, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 6 (AC-5) ----

        [Test]
        public void test_mobDied_livePoint_becomesDying_timerSet_idKept_noRemove()
        {
            MobSpawningService s = Make(3);
            s.PopulateOnActive();
            EntityID e = s.GetPoint(1).CurrentEntityID;

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, e));

            SpawnPoint p = s.GetPoint(1);
            Assert.AreEqual(SpawnPointState.Dying, p.State);
            Assert.AreEqual(DEATH_LINGER, p.TimerTick);
            Assert.AreEqual(e, p.CurrentEntityID);
            Assert.AreEqual(0, _roster.RemoveCalls);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(2).State);
        }

        // ---- QA 7 (AC-6) ----

        [Test]
        public void test_mobDied_unknownEntity_noChange_noLog_noRemove()
        {
            MobSpawningService s = Make(3);
            s.PopulateOnActive();
            SpawnPoint[] before = Snapshot(s);

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, new EntityID(55555u)));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, EntityID.Invalid));

            AssertSame(before, s);
            Assert.AreEqual(0, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_mobDied_invalidIdWhileAllPointsDeferred_noChange_noLog()
        {
            // Arrange: every point Deferred, so every CurrentEntityID is Invalid.
            _roster.Count = CAP;
            MobSpawningService s = Make(3);
            s.PopulateOnActive();
            SpawnPoint[] before = Snapshot(s);

            // Act
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, EntityID.Invalid));

            // Assert
            AssertSame(before, s);
            Assert.AreEqual(3, s.DeferredCount);
            Assert.AreEqual(0, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_mobDied_beforePopulate_ignored()
        {
            MobSpawningService s = Make(2);
            SpawnPoint[] before = Snapshot(s);

            _bus.RaiseMobDied(new MobDiedArgs(ZONE, new EntityID(1000u)));

            AssertSame(before, s);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 8 (AC-7) ----

        [Test]
        public void test_mobDied_duplicateWhileDying_leavesPointUnchanged()
        {
            // Arrange (the partial-countdown regression lives in MobSpawning_TickTimers_tests, Story 003)
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            EntityID e = s.GetPoint(0).CurrentEntityID;
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, e));
            SpawnPoint[] before = Snapshot(s);

            // Act
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, e));

            // Assert
            AssertSame(before, s);
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(0).State);
            Assert.AreEqual(DEATH_LINGER, s.GetPoint(0).TimerTick);
            Assert.AreEqual(e, s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(0, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 9 (AC-8) ----

        [Test]
        public void test_mobDied_foreignZone_ignored_oneWarning()
        {
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            EntityID e = s.GetPoint(0).CurrentEntityID;
            SpawnPoint[] before = Snapshot(s);

            LogAssert.Expect(LogType.Warning, new Regex("foreign zone"));
            _bus.RaiseMobDied(new MobDiedArgs(OTHER_ZONE, e));
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Warning, new Regex("foreign zone"));
            _bus.RaiseMobDied(new MobDiedArgs(OTHER_ZONE, new EntityID(55555u)));

            AssertSame(before, s);
            Assert.AreEqual(0, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 10 (AC-9) ----

        [Test]
        public void test_mobDied_handling_allocatesNothing()
        {
            MobSpawningService s = Make(CAP);
            s.PopulateOnActive();
            // Warm-up runs every measured path once (Live -> Dying near the end of the scan, duplicate, unknown).
            var warmLive = new MobDiedArgs(ZONE, s.GetPoint(CAP - 2).CurrentEntityID);
            var measuredLive = new MobDiedArgs(ZONE, s.GetPoint(CAP - 1).CurrentEntityID);
            var unk = new MobDiedArgs(ZONE, new EntityID(99999u));
            _bus.RaiseMobDied(warmLive);
            _bus.RaiseMobDied(warmLive);
            _bus.RaiseMobDied(unk);
            SpawnPoint before0 = s.GetPoint(0);

            long before = GC.GetAllocatedBytesForCurrentThread();
            _bus.RaiseMobDied(measuredLive);
            _bus.RaiseMobDied(measuredLive);
            _bus.RaiseMobDied(unk);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(CAP - 1).State);
            Assert.AreEqual(DEATH_LINGER, s.GetPoint(CAP - 1).TimerTick);
            Assert.AreEqual(before0.State, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
        }

        // ---- QA 11 (AC-10) ----

        [Test]
        public void test_populate_invalidIdOnSecondAdd_pointDeferredOnce_sweepContinues()
        {
            _roster.InvalidOnAddCall.Add(2);
            MobSpawningService s = Make(3);

            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure"));
            s.PopulateOnActive();

            AssertRosterLog("Count", "Add", "Count", "Add", "Count", "Add");
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(2).State);
            Assert.AreEqual(EntityID.Invalid, s.GetPoint(1).CurrentEntityID);
            Assert.AreEqual(0, s.GetPoint(1).TimerTick);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(1, s.GetDeferredPointIndex(0));

            List<SpawnPointEntry> expected = Entries(3);
            Assert.AreEqual(2, _spawned.Count);
            int[] livePoints = { 0, 2 };
            for (int k = 0; k < 2; k++)
            {
                int p = livePoints[k];
                Assert.AreEqual(ZONE, _spawned[k].Zone);
                Assert.AreEqual(s.GetPoint(p).CurrentEntityID, _spawned[k].Entity);
                Assert.AreEqual(expected[p].MobTypeID, _spawned[k].MobType);
                Assert.AreEqual(expected[p].Position, _spawned[k].SpawnPosition);
            }

            // Story 003's shared guard logs UnexpectedTryAddMobFailure for the invalid add (expected above).
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_populate_emptyTable_noRosterCalls()
        {
            MobSpawningService s = Make(0);

            s.PopulateOnActive();

            Assert.AreEqual(0, _roster.Log.Count);
            Assert.AreEqual(0, _spawned.Count);
        }
    }
}

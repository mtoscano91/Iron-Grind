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
    /// EditMode tests for Mob Spawning Story 003: <see cref="MobSpawningService.Tick"/> timers, F-MS-1 respawn
    /// delay, deferred queue and the shared TryAddMob Invalid guard (AC-1 to AC-8; QA cases 1 to 13).
    /// AC-7's Cancelled part belongs to Story 004.
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_TickTimers_Tests
    {
        private const uint MOB_TYPE_A = 1u;
        private const uint MOB_TYPE_B = 2u;
        private const int LINGER = 3;
        private const int CAP = ZoneSlotPool.MAX_MOBS_PER_ZONE;
        private const int RETRY_INTERVAL = 20;
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly ZoneID OTHER_ZONE = new ZoneID(8u);

        private FakeRoster _roster;
        private FakeRegistry _registry;
        private MobEventBus _bus;
        private List<MobSpawnedArgs> _spawned;

        [SetUp]
        public void SetUp()
        {
            _roster = new FakeRoster();
            _registry = new FakeRegistry();
            _registry.Set(MOB_TYPE_A, MobTier.Common);
            _registry.Set(MOB_TYPE_B, MobTier.Common);
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
            public readonly Queue<int> CountScript = new Queue<int>(16);
            public int Count;
            public int AddCalls;
            public int RemoveCalls;
            public int CountCalls;
            public bool ForceInvalidAdd;

            /// <summary>Per-call script: dequeue true means this TryAddMob returns Invalid. Empty falls back to ForceInvalidAdd.</summary>
            public readonly Queue<bool> InvalidAddScript = new Queue<bool>(8);

            public EntityID LastRemoved = EntityID.Invalid;
            public ZoneID LastRemoveZone = ZoneID.Invalid;
            public ZoneID LastAddZone = ZoneID.Invalid;
            public MobTypeID LastAddType;
            public Vector3 LastAddPos;
            public uint NextId = 1000u;

            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
            {
                AddCalls++;
                LastAddZone = zone;
                LastAddType = type;
                LastAddPos = spawnPosition;
                bool invalid = InvalidAddScript.Count > 0 ? InvalidAddScript.Dequeue() : ForceInvalidAdd;
                if (invalid)
                {
                    return EntityID.Invalid;
                }

                Count++;
                return new EntityID(NextId++);
            }

            public void RemoveMob(ZoneID zone, EntityID id)
            {
                RemoveCalls++;
                LastRemoved = id;
                LastRemoveZone = zone;
                if (Count > 0)
                {
                    Count--;
                }
            }

            public int GetMobCount(ZoneID zone)
            {
                CountCalls++;
                return CountScript.Count > 0 ? CountScript.Dequeue() : Count;
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
            public int GetCalls;

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
                GetCalls++;
                return _defs.TryGetValue(id.RawValue, out MobDefinition def) ? def : null;
            }
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

        private MobSpawningService Make(int pointCount, int linger = LINGER)
        {
            return new MobSpawningService(ZONE, Entries(pointCount), _roster, _bus, _registry,
                MobSpawningConfigValues.Defaults, linger);
        }

        private MobSpawningService MakeFrom(List<SpawnPointEntry> entries, int linger = LINGER)
        {
            return new MobSpawningService(ZONE, entries, _roster, _bus, _registry,
                MobSpawningConfigValues.Defaults, linger);
        }

        private MobSpawningService MakeWithConfig(int pointCount, MobSpawningConfigValues config)
        {
            return new MobSpawningService(ZONE, Entries(pointCount), _roster, _bus, _registry, config, LINGER);
        }

        private static void TickN(MobSpawningService s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                s.Tick();
            }
        }

        private void Kill(MobSpawningService s, int pointIndex)
        {
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, s.GetPoint(pointIndex).CurrentEntityID));
        }

        private void KillAndLinger(MobSpawningService s, int pointIndex, int linger = LINGER)
        {
            Kill(s, pointIndex);
            TickN(s, linger);
        }

        // ---- QA 1 (AC-1, AC-7) ----

        [Test]
        public void test_tick_dyingLingerBoundary_removesOnLastTick_setsRespawningWithCommonDelay()
        {
            // Arrange
            MobSpawningService s = Make(1, 30);
            s.PopulateOnActive();
            EntityID e = s.GetPoint(0).CurrentEntityID;
            Kill(s, 0);

            // Act
            TickN(s, 29);

            // Assert: still Dying one tick before the boundary
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(0).State);
            Assert.AreEqual(1, s.GetPoint(0).TimerTick);
            Assert.AreEqual(0, _roster.RemoveCalls);

            s.Tick();

            Assert.AreEqual(1, _roster.RemoveCalls);
            Assert.AreEqual(e, _roster.LastRemoved);
            Assert.AreEqual(ZONE, _roster.LastRemoveZone);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(EntityID.Invalid, s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(60, s.GetPoint(0).TimerTick);
            Assert.AreEqual(1, _roster.AddCalls, "no TryAddMob on the Dying to Respawning transition");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 2 (AC-2) ----

        [TestCase(MobTier.Common, 60)]
        [TestCase(MobTier.Uncommon, 120)]
        [TestCase(MobTier.Rare, 300)]
        [TestCase(MobTier.Elite, 600)]
        public void test_tick_respawnDelayFromTier_noOverride_matchesTable(MobTier tier, int expectedDelay)
        {
            _registry.Set(10u, tier);
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(1u, new MobTypeID(10u), Vector3.zero, 0),
            };
            MobSpawningService s = MakeFrom(entries);
            s.PopulateOnActive();

            KillAndLinger(s, 0);

            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(expectedDelay, s.GetPoint(0).TimerTick);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_respawnDelayOverride_winsOverTier_noRegistryCall()
        {
            _registry.Set(11u, MobTier.Elite);
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(1u, new MobTypeID(11u), Vector3.zero, 200),
            };
            MobSpawningService s = MakeFrom(entries);
            s.PopulateOnActive();

            KillAndLinger(s, 0);

            Assert.AreEqual(200, s.GetPoint(0).TimerTick);
            Assert.AreEqual(0, _registry.GetCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 3 (AC-2) ----

        [Test]
        public void test_tick_delayCommittedAtTransition_twoServicesRespawnAfterTheirOwnDelay()
        {
            // Arrange: two zones, separate rosters and buses, Common delay 60 vs 100.
            var rosterB = new FakeRoster();
            var busB = new MobEventBus();
            MobSpawningService a = Make(1);
            var b = new MobSpawningService(OTHER_ZONE, Entries(1), rosterB, busB, _registry,
                new MobSpawningConfigValues(100, 120, 300, 600, RETRY_INTERVAL), LINGER);
            a.PopulateOnActive();
            b.PopulateOnActive();
            Kill(a, 0);
            busB.RaiseMobDied(new MobDiedArgs(OTHER_ZONE, b.GetPoint(0).CurrentEntityID));
            TickN(a, LINGER);
            TickN(b, LINGER);

            // Assert: each point holds its own service's delay
            Assert.AreEqual(60, a.GetPoint(0).TimerTick);
            Assert.AreEqual(100, b.GetPoint(0).TimerTick);

            // Act and assert: exactly one decrement per Tick, never recomputed
            a.Tick();
            b.Tick();
            Assert.AreEqual(59, a.GetPoint(0).TimerTick);
            Assert.AreEqual(99, b.GetPoint(0).TimerTick);
            TickN(a, 58);
            TickN(b, 98);
            Assert.AreEqual(SpawnPointState.Respawning, a.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Respawning, b.GetPoint(0).State);
            a.Tick();
            b.Tick();
            Assert.AreEqual(SpawnPointState.Live, a.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, b.GetPoint(0).State);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 4 (AC-3) ----

        [Test]
        public void test_tick_respawnBelowCap_addsOnce_liveWithNewId_raisesMobSpawned()
        {
            // Arrange
            _roster.Count = 10;
            MobSpawningService s = Make(1);
            s.PopulateOnActive();
            KillAndLinger(s, 0);
            int addsBefore = _roster.AddCalls;
            int spawnedBefore = _spawned.Count;

            // Act
            TickN(s, 60);

            // Assert
            Assert.AreEqual(addsBefore + 1, _roster.AddCalls);
            SpawnPoint p = s.GetPoint(0);
            Assert.AreEqual(SpawnPointState.Live, p.State);
            Assert.AreEqual(new EntityID(1001u), p.CurrentEntityID);
            Assert.AreEqual(0, p.TimerTick);
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count);
            MobSpawnedArgs last = _spawned[_spawned.Count - 1];
            Assert.AreEqual(ZONE, last.Zone);
            Assert.AreEqual(new EntityID(1001u), last.Entity);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), last.MobType);
            Assert.AreEqual(Entries(1)[0].Position, last.SpawnPosition);
            Assert.AreEqual(ZONE, _roster.LastAddZone);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), _roster.LastAddType);
            Assert.AreEqual(Entries(1)[0].Position, _roster.LastAddPos);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 5 (AC-4) ----

        [Test]
        public void test_tick_respawnAtCap_defers_noAdd_noSpawnedEvent_noSecondRemove()
        {
            // Arrange
            MobSpawningService s = Make(1);
            s.PopulateOnActive();
            KillAndLinger(s, 0);
            _roster.Count = CAP;
            int addsBefore = _roster.AddCalls;
            int spawnedBefore = _spawned.Count;

            // Act
            TickN(s, 60);

            // Assert
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(EntityID.Invalid, s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, s.GetDeferredPointIndex(0));
            Assert.AreEqual(addsBefore, _roster.AddCalls);
            Assert.AreEqual(spawnedBefore, _spawned.Count);
            Assert.AreEqual(1, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 6 (AC-5) ----

        [Test]
        public void test_tick_deferredRetry_polledEveryTwentiethCall_secondPointWaitsForCallForty()
        {
            // Arrange: both points deferred at the cap
            _roster.Count = CAP;
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Assert.AreEqual(2, s.DeferredCount);

            // Act and assert: calls 1 to 4 with the cap still full
            TickN(s, 4);
            Assert.AreEqual(4, s.RetryCounter);
            _roster.Count = CAP - 1; // room appears before call 5
            TickN(s, 15); // calls 5 to 19
            Assert.AreEqual(0, _roster.AddCalls, "no TryAddMob on calls 1 to 19");
            Assert.AreEqual(19, s.RetryCounter);

            s.Tick(); // call 20

            Assert.AreEqual(1, _roster.AddCalls);
            Assert.AreEqual(0, s.RetryCounter);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(1, s.GetDeferredPointIndex(0));
            Assert.AreEqual(1, _spawned.Count);

            // The second point is next tried on call 40, not 21.
            _roster.Count = CAP - 1;
            TickN(s, 19); // calls 21 to 39
            Assert.AreEqual(1, _roster.AddCalls);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            s.Tick(); // call 40
            Assert.AreEqual(2, _roster.AddCalls);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(1).State);
            Assert.AreEqual(0, s.DeferredCount);
            Assert.AreEqual(2, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_retryCounter_countsTicksBeforePopulate()
        {
            // Arrange: 19 ticks before PopulateOnActive still advance the counter.
            MobSpawningService s = Make(1);
            TickN(s, 19);
            Assert.AreEqual(19, s.RetryCounter);
            _roster.Count = CAP;
            s.PopulateOnActive();
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            _roster.Count = CAP - 1;

            // Act: call 20 polls
            s.Tick();

            // Assert
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(0, s.DeferredCount);
            Assert.AreEqual(0, s.RetryCounter);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_nonDefaultRetryInterval_pollsOnConfiguredCall()
        {
            // Arrange: interval 13, one point deferred at the cap
            _roster.Count = CAP;
            MobSpawningService s = MakeWithConfig(1, new MobSpawningConfigValues(60, 120, 300, 600, 13));
            s.PopulateOnActive();
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            _roster.Count = CAP - 1; // room appears

            // Act: calls 1 to 12 must not poll
            TickN(s, 12);

            // Assert
            Assert.AreEqual(0, _roster.AddCalls, "no TryAddMob on calls 1 to 12");
            Assert.AreEqual(12, s.RetryCounter);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);

            s.Tick(); // call 13

            Assert.AreEqual(1, _roster.AddCalls);
            Assert.AreEqual(0, s.RetryCounter);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(0, s.DeferredCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_minimumRetryInterval_pollsEveryTenthCall()
        {
            // Arrange: interval 10, two points deferred at the cap, room for one on call 10
            _roster.Count = CAP;
            MobSpawningService s = MakeWithConfig(2, new MobSpawningConfigValues(60, 120, 300, 600, 10));
            s.PopulateOnActive();
            Assert.AreEqual(2, s.DeferredCount);
            _roster.Count = CAP - 1;

            // Act: calls 1 to 9 do not poll, call 10 fills one point and hits the cap for the other
            TickN(s, 9);
            Assert.AreEqual(0, _roster.AddCalls, "no TryAddMob on calls 1 to 9");
            s.Tick(); // call 10

            // Assert
            Assert.AreEqual(1, _roster.AddCalls);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(1, s.DeferredCount);

            // Room again before call 20: calls 11 to 19 do not poll, call 20 does.
            _roster.Count = CAP - 1;
            TickN(s, 9);
            Assert.AreEqual(1, _roster.AddCalls);
            s.Tick(); // call 20
            Assert.AreEqual(2, _roster.AddCalls);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(1).State);
            Assert.AreEqual(0, s.DeferredCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 7 (AC-5) ----

        [Test]
        public void test_tick_deferredRetry_stillFull_staysDeferredAfterThreePolls()
        {
            _roster.Count = CAP;
            MobSpawningService s = Make(1);
            s.PopulateOnActive();

            TickN(s, 60);

            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, _roster.AddCalls);
            Assert.AreEqual(1 + 3, _roster.CountCalls, "one populate check plus one per poll");
            Assert.AreEqual(0, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 8 (AC-5) ----

        [Test]
        public void test_tick_multipleDeferred_pollFillsAsManyAsCountAllows_noneSkipped()
        {
            // Arrange: three deferred, room for two
            _roster.Count = CAP;
            MobSpawningService s = Make(3);
            s.PopulateOnActive();
            _roster.Count = CAP - 2;
            int countCallsBefore = _roster.CountCalls;

            // Act
            TickN(s, RETRY_INTERVAL);

            // Assert: poll order from the swap-remove loop: list [0,1,2] -> point 0 fills, point 2 swaps into
            // slot 0 and fills, point 1 hits the cap and the poll stops.
            Assert.AreEqual(2, _roster.AddCalls);
            Assert.AreEqual(2, _spawned.Count);
            Assert.AreEqual(Entries(3)[0].Position, _spawned[0].SpawnPosition, "first fill is point 0");
            Assert.AreEqual(Entries(3)[2].Position, _spawned[1].SpawnPosition, "second fill is point 2");
            Assert.AreEqual(ZONE, _spawned[0].Zone);
            Assert.AreEqual(new EntityID(1000u), _spawned[0].Entity);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), _spawned[0].MobType);
            Assert.AreEqual(ZONE, _spawned[1].Zone);
            Assert.AreEqual(new EntityID(1001u), _spawned[1].Entity);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), _spawned[1].MobType);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(2).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(1, s.GetDeferredPointIndex(0));
            Assert.AreEqual(3, _roster.CountCalls - countCallsBefore,
                "one count check per fill plus one that hit the cap, then the poll stops");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 9 (AC-6) ----

        [Test]
        public void test_tick_invalidAddOnPopulate_defersOnce_logsGuardWithSpawnPointId_noSpawnedEvent()
        {
            _roster.ForceInvalidAdd = true;
            MobSpawningService s = Make(1);

            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure.*SpawnPointID 1\\b"));
            s.PopulateOnActive();

            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_invalidAddOnRespawn_defersOnce_logsGuard_noSpawnedEvent()
        {
            // Arrange
            MobSpawningService s = Make(1);
            s.PopulateOnActive();
            KillAndLinger(s, 0);
            _roster.ForceInvalidAdd = true;
            int spawnedBefore = _spawned.Count;

            // Act
            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure.*SpawnPointID 1\\b"));
            TickN(s, 60);

            // Assert
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, s.GetDeferredPointIndex(0));
            Assert.AreEqual(spawnedBefore, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_invalidAddOnRetry_pointNotDuplicated_logsGuard_laterPollFills()
        {
            // Arrange: deferred through an Invalid on the T-2 path
            _roster.ForceInvalidAdd = true;
            MobSpawningService s = Make(1);
            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure"));
            s.PopulateOnActive();

            // Act: the call-20 poll also gets Invalid
            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure.*SpawnPointID 1\\b"));
            TickN(s, RETRY_INTERVAL);

            // Assert
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, s.GetDeferredPointIndex(0));
            Assert.AreEqual(0, _spawned.Count);
            LogAssert.NoUnexpectedReceived();

            // The next poll succeeds and removes the point from the list.
            _roster.ForceInvalidAdd = false;
            TickN(s, RETRY_INTERVAL);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(0, s.DeferredCount);
            Assert.AreEqual(1, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 10 (AC-7) ----

        [Test]
        public void test_tick_liveAndDeferredPoints_keepTimerZeroAcross100Ticks()
        {
            // Arrange: point 0 Live, point 1 Deferred (cap stays full)
            _roster.Count = CAP - 1;
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            EntityID live = s.GetPoint(0).CurrentEntityID;

            // Act
            TickN(s, 100);

            // Assert
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(live, s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(0, s.GetPoint(0).TimerTick);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(0, s.GetPoint(1).TimerTick);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_uninitializedPoints_untouched_noRosterCalls_counterStillRuns()
        {
            MobSpawningService s = Make(2);

            TickN(s, 100);

            Assert.AreEqual(SpawnPointState.Uninitialized, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Uninitialized, s.GetPoint(1).State);
            Assert.AreEqual(0, s.GetPoint(0).TimerTick);
            Assert.AreEqual(0, s.GetPoint(1).TimerTick);
            Assert.AreEqual(0, _roster.CountCalls);
            Assert.AreEqual(0, _roster.AddCalls);
            Assert.AreEqual(0, s.RetryCounter, "100 ticks is five full intervals");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_dyingAndRespawning_decrementOncePerTick()
        {
            MobSpawningService s = Make(1, 30);
            s.PopulateOnActive();
            Kill(s, 0);

            s.Tick();
            Assert.AreEqual(29, s.GetPoint(0).TimerTick);
            TickN(s, 29);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(60, s.GetPoint(0).TimerTick);
            s.Tick();

            Assert.AreEqual(59, s.GetPoint(0).TimerTick);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_duplicateMobDiedMidLinger_doesNotResetPartiallyElapsedTimer()
        {
            // Arrange: a Dying point 10 ticks into a 30-tick linger
            MobSpawningService s = Make(1, 30);
            s.PopulateOnActive();
            Kill(s, 0);
            TickN(s, 10);
            Assert.AreEqual(20, s.GetPoint(0).TimerTick);

            // Act: the same death is published again
            Kill(s, 0);

            // Assert: countdown keeps its progress and still ends on the original tick
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(0).State);
            Assert.AreEqual(20, s.GetPoint(0).TimerTick);
            TickN(s, 20);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(1, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 11 (AC-8) ----

        [Test]
        public void test_tick_removeRespawnAndDeferredFill_allocateNothing()
        {
            // Arrange: four live points; warm up every measured path once.
            MobSpawningService s = Make(4);
            s.PopulateOnActive();
            Kill(s, 0);
            TickN(s, LINGER + 60);                     // remove + respawn
            Kill(s, 1);
            TickN(s, LINGER);
            _roster.Count = CAP;
            TickN(s, 60);                              // respawn at cap -> deferred
            _roster.Count = CAP - 1;
            TickN(s, RETRY_INTERVAL);                  // deferred fill
            Assert.AreEqual(0, s.DeferredCount);

            // Path a: a tick with RemoveMob
            Kill(s, 0);
            TickN(s, LINGER - 1);
            int removesBefore = _roster.RemoveCalls;
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            s.Tick();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(removesBefore + 1, _roster.RemoveCalls);
            Assert.AreEqual(0L, a0 - b0, "removal tick");

            // Path b: a tick with a respawn
            TickN(s, 59);
            int spawnedBefore = _spawned.Count;
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            s.Tick();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count);
            Assert.AreEqual(0L, a1 - b1, "respawn tick");

            // Path c: a tick with a deferred fill
            Kill(s, 1);
            TickN(s, LINGER);
            _roster.Count = CAP;
            TickN(s, 60);
            Assert.AreEqual(1, s.DeferredCount);
            while (s.RetryCounter != RETRY_INTERVAL - 1)
            {
                s.Tick();
            }

            _roster.Count = CAP - 1;
            spawnedBefore = _spawned.Count;
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            s.Tick();
            long a2 = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count);
            Assert.AreEqual(0, s.DeferredCount);
            Assert.AreEqual(0L, a2 - b2, "deferred fill tick");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 12 (AC-2) ----

        [Test]
        public void test_tick_registryMissNoOverride_logsErrorNamingIds_usesCommonDelay()
        {
            // Arrange: type 77 has no definition
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(41u, new MobTypeID(77u), Vector3.zero, 0),
            };
            MobSpawningService s = MakeFrom(entries);
            s.PopulateOnActive();
            Kill(s, 0);

            // Act
            LogAssert.Expect(LogType.Error, new Regex("SpawnPointID 41, MobTypeID 77"));
            TickN(s, LINGER);

            // Assert
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(60, s.GetPoint(0).TimerTick);
            Assert.AreEqual(1, _registry.GetCalls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_registryMissWithOverride_usesOverride_noRegistryCall_noLog()
        {
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(41u, new MobTypeID(77u), Vector3.zero, 200),
            };
            MobSpawningService s = MakeFrom(entries);
            s.PopulateOnActive();

            KillAndLinger(s, 0);

            Assert.AreEqual(200, s.GetPoint(0).TimerTick);
            Assert.AreEqual(0, _registry.GetCalls);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 13 (AC-5) ----

        [Test]
        public void test_tick_respawnExpiresOnPollTick_defersInPassThenFilledByPollSameTick()
        {
            // Arrange: override 17 + linger 3 -> the respawn timer expires on Tick call 20, a poll tick.
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(1u, new MobTypeID(MOB_TYPE_A), new Vector3(3f, 0f, 4f), 17),
            };
            MobSpawningService s = MakeFrom(entries);
            s.PopulateOnActive();
            Kill(s, 0);
            TickN(s, 19);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(1, s.GetPoint(0).TimerTick);
            int addsBefore = _roster.AddCalls;
            int spawnedBefore = _spawned.Count;
            _roster.CountScript.Enqueue(CAP);     // array pass: at the cap
            _roster.CountScript.Enqueue(CAP - 1); // poll: room

            // Act: call 20
            s.Tick();

            // Assert
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(addsBefore + 1, _roster.AddCalls);
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count);
            Assert.AreEqual(0, s.DeferredCount);
            Assert.AreEqual(1, _roster.RemoveCalls);
            Assert.AreEqual(0, _roster.CountScript.Count);
            MobSpawnedArgs last = _spawned[_spawned.Count - 1];
            Assert.AreEqual(ZONE, last.Zone);
            Assert.AreEqual(new EntityID(1001u), last.Entity);
            Assert.AreEqual(new MobTypeID(MOB_TYPE_A), last.MobType);
            Assert.AreEqual(new Vector3(3f, 0f, 4f), last.SpawnPosition);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- Review additions ----

        [Test]
        public void test_tick_poll_invalidFirstDeferred_secondStillFilled_firstStaysOnListOnce()
        {
            // Arrange: two deferred points, then room for both; the first add returns Invalid
            _roster.Count = CAP;
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Assert.AreEqual(2, s.DeferredCount);
            _roster.Count = CAP - 2;
            _roster.InvalidAddScript.Enqueue(true);

            // Act
            LogAssert.Expect(LogType.Error, new Regex("UnexpectedTryAddMobFailure.*SpawnPointID 1\\b"));
            TickN(s, RETRY_INTERVAL);

            // Assert
            Assert.AreEqual(2, _roster.AddCalls);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(1).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(0, s.GetDeferredPointIndex(0));
            Assert.AreEqual(1, _spawned.Count);
            Assert.AreEqual(Entries(2)[1].Position, _spawned[0].SpawnPosition);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_twoRespawnsSameTick_oneSlot_firstLiveSecondDeferredInDeclarationOrder()
        {
            // Arrange: both points respawn on call 63 (3 linger + 60 Common), not a poll tick
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Kill(s, 0);
            Kill(s, 1);
            TickN(s, LINGER);
            TickN(s, 59);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(1).State);
            Assert.AreEqual(1, s.GetPoint(0).TimerTick);
            Assert.AreEqual(1, s.GetPoint(1).TimerTick);
            int spawnedBefore = _spawned.Count;
            _roster.CountScript.Enqueue(CAP - 1); // first point fits
            _roster.CountScript.Enqueue(CAP);     // second point at the cap

            // Act
            s.Tick();

            // Assert
            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(1, s.GetDeferredPointIndex(0));
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count);
            Assert.AreEqual(0, _roster.CountScript.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_mobDiedForRemovedEntity_whileRespawningOrDeferred_ignored()
        {
            // Arrange: point 0 Respawning after its corpse was removed, point 1 Deferred at the cap
            _roster.Count = CAP - 1;
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            EntityID oldEntity = s.GetPoint(0).CurrentEntityID;
            KillAndLinger(s, 0);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            int timerBefore = s.GetPoint(0).TimerTick;

            // Act
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, oldEntity));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, EntityID.Invalid));

            // Assert
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State);
            Assert.AreEqual(timerBefore, s.GetPoint(0).TimerTick);
            Assert.AreEqual(EntityID.Invalid, s.GetPoint(0).CurrentEntityID);
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(1).State);
            Assert.AreEqual(0, s.GetPoint(1).TimerTick);
            Assert.AreEqual(1, s.DeferredCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_tick_customConfigTierDelays_usedForEveryTier()
        {
            // Arrange: one point per tier, custom delays 50/150/400/999
            _registry.Set(20u, MobTier.Common);
            _registry.Set(21u, MobTier.Uncommon);
            _registry.Set(22u, MobTier.Rare);
            _registry.Set(23u, MobTier.Elite);
            var entries = new List<SpawnPointEntry>
            {
                new SpawnPointEntry(1u, new MobTypeID(20u), Vector3.zero, 0),
                new SpawnPointEntry(2u, new MobTypeID(21u), Vector3.zero, 0),
                new SpawnPointEntry(3u, new MobTypeID(22u), Vector3.zero, 0),
                new SpawnPointEntry(4u, new MobTypeID(23u), Vector3.zero, 0),
            };
            var s = new MobSpawningService(ZONE, entries, _roster, _bus, _registry,
                new MobSpawningConfigValues(50, 150, 400, 999, 20), LINGER);
            s.PopulateOnActive();
            for (int i = 0; i < 4; i++)
            {
                Kill(s, i);
            }

            // Act
            TickN(s, LINGER);

            // Assert
            Assert.AreEqual(50, s.GetPoint(0).TimerTick);
            Assert.AreEqual(150, s.GetPoint(1).TimerTick);
            Assert.AreEqual(400, s.GetPoint(2).TimerTick);
            Assert.AreEqual(999, s.GetPoint(3).TimerTick);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// EditMode tests for Mob Spawning Story 004: <see cref="MobSpawningService.CancelAllSpawnTimers"/>
    /// (CR-MS-8, CR-MS-4 transition 8, CR-MS-5; AC-1 to AC-7; QA cases 1 to 6). Roster call counts are the call log:
    /// a snapshot is taken at the cancel marker and compared afterwards.
    /// </summary>
    [TestFixture]
    internal sealed class MobSpawning_Teardown_Tests
    {
        private const uint MOB_TYPE_A = 1u;
        private const int LINGER = 3;
        private const int COMMON_DELAY = 60; // MobSpawningConfigValues.Defaults
        private const int RETRY_INTERVAL = 20; // MobSpawningConfigValues.Defaults
        private const int CAP = ZoneSlotPool.MAX_MOBS_PER_ZONE;
        private static readonly ZoneID ZONE = new ZoneID(7u);
        private static readonly ZoneID OTHER_ZONE = new ZoneID(8u);

        private FakeRoster _roster;
        private FakeRegistry _registry;
        private MobEventBus _bus;
        private List<MobSpawnedArgs> _spawned;
        private MobSpawningService _cancelTarget;

        [SetUp]
        public void SetUp()
        {
            _roster = new FakeRoster();
            _registry = new FakeRegistry();
            _registry.Set(MOB_TYPE_A, MobTier.Common);
            _bus = new MobEventBus();
            _spawned = new List<MobSpawnedArgs>(64);
            _bus.OnMobSpawned += OnSpawned;
            _cancelTarget = null;
        }

        private void OnSpawned(MobSpawnedArgs args)
        {
            _spawned.Add(args);
        }

        /// <summary>Re-entrant teardown: cancels <see cref="_cancelTarget"/> from inside a bus raise.</summary>
        private void CancelFromSubscriber(MobSpawnedArgs args)
        {
            _cancelTarget.CancelAllSpawnTimers(ZONE);
        }

        private void CancelFromMobDied(MobDiedArgs args)
        {
            _cancelTarget.CancelAllSpawnTimers(ZONE);
        }

        private sealed class FakeRoster : IZoneMobRoster
        {
            public int Count;
            public int AddCalls;
            public int RemoveCalls;
            public int CountCalls;
            public uint NextId = 1000u;

            /// <summary>Optional callback run inside RemoveMob (re-entrant teardown test).</summary>
            public Action OnRemove;

            public int TotalCalls => AddCalls + RemoveCalls + CountCalls;

            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition)
            {
                AddCalls++;
                Count++;
                return new EntityID(NextId++);
            }

            public void RemoveMob(ZoneID zone, EntityID id)
            {
                RemoveCalls++;
                if (Count > 0)
                {
                    Count--;
                }

                OnRemove?.Invoke();
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
                return _defs.TryGetValue(id.RawValue, out MobDefinition def) ? def : null;
            }
        }

        private static List<SpawnPointEntry> Entries(int n)
        {
            var list = new List<SpawnPointEntry>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new SpawnPointEntry((uint)(i + 1), new MobTypeID(MOB_TYPE_A), new Vector3(i, 0f, i * 2f), 0));
            }

            return list;
        }

        private MobSpawningService Make(int pointCount)
        {
            return new MobSpawningService(ZONE, Entries(pointCount), _roster, _bus, _registry,
                MobSpawningConfigValues.Defaults, LINGER);
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

        /// <summary>Number of OnMobDied subscribers on the bus (its private handler array; no public count exists).</summary>
        private int MobDiedSubscriberCount()
        {
            FieldInfo field = typeof(MobEventBus).GetField("_diedHandlers", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "MobEventBus._diedHandlers renamed; update this helper");
            return ((Array)field.GetValue(_bus)).Length;
        }

        /// <summary>
        /// Four points: 0 Live, 1 Dying, 2 Respawning, 3 Deferred. Populate starts at CAP - 3, so point 3 defers;
        /// point 2 lingers out (one RemoveMob) before the first poll at call 20.
        /// </summary>
        private MobSpawningService MakeMixed()
        {
            _roster.Count = CAP - 3;
            MobSpawningService s = Make(4);
            s.PopulateOnActive();
            Kill(s, 2);
            TickN(s, LINGER);
            Kill(s, 1);

            Assert.AreEqual(SpawnPointState.Live, s.GetPoint(0).State, "arrange: point 0 Live");
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(1).State, "arrange: point 1 Dying");
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(2).State, "arrange: point 2 Respawning");
            Assert.AreEqual(SpawnPointState.Deferred, s.GetPoint(3).State, "arrange: point 3 Deferred");
            Assert.AreEqual(1, s.DeferredCount, "arrange: one deferred");
            Assert.AreEqual(1, _roster.RemoveCalls, "arrange: one RemoveMob from point 2's linger");
            return s;
        }

        private static void AssertAllCancelled(MobSpawningService s)
        {
            for (int i = 0; i < s.PointCount; i++)
            {
                SpawnPoint p = s.GetPoint(i);
                Assert.AreEqual(SpawnPointState.Cancelled, p.State, "point " + i + " state");
                Assert.AreEqual(0, p.TimerTick, "point " + i + " TimerTick");
                Assert.AreEqual(EntityID.Invalid, p.CurrentEntityID, "point " + i + " CurrentEntityID");
            }

            Assert.AreEqual(0, s.DeferredCount, "deferred list empty");
            Assert.IsTrue(s.IsCancelled);
        }

        // ---- QA 1 (AC-1, AC-2, AC-3) ----

        [Test]
        public void test_cancel_mixedStates_viaInterface_allCancelled_timersZero_idsInvalid_deferredEmpty_noRemove()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            IZoneMobSpawnTimers timers = s;
            int removesBefore = _roster.RemoveCalls;

            // Act
            timers.CancelAllSpawnTimers(ZONE);

            // Assert
            AssertAllCancelled(s);
            Assert.AreEqual(removesBefore, _roster.RemoveCalls, "no RemoveMob during teardown (AC-MS-14)");
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 2 (AC-2, AC-3) ----

        [Test]
        public void test_cancel_thenTicksPopulateAndMobDied_noRosterCalls_noSpawned_noLog()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            EntityID formerLive = s.GetPoint(0).CurrentEntityID;
            EntityID formerDying = s.GetPoint(1).CurrentEntityID;
            s.CancelAllSpawnTimers(ZONE);
            int callsAtMarker = _roster.TotalCalls;
            int spawnedAtMarker = _spawned.Count;
            int retryAtMarker = s.RetryCounter;
            Assert.AreEqual(LINGER, retryAtMarker, "arrange: MakeMixed ran LINGER ticks");

            // Act: well past the linger, the respawn delay and several polls
            TickN(s, COMMON_DELAY + 2 * RETRY_INTERVAL + 20);
            s.PopulateOnActive();
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, formerLive));
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, formerDying));

            // Assert
            Assert.AreEqual(callsAtMarker, _roster.TotalCalls, "no TryAddMob / RemoveMob / GetMobCount after cancel");
            Assert.AreEqual(spawnedAtMarker, _spawned.Count, "no MobSpawned after cancel");
            Assert.AreEqual(retryAtMarker, s.RetryCounter, "Tick returns before the retry counter after cancel");
            AssertAllCancelled(s);
            LogAssert.NoUnexpectedReceived(); // no "PopulateOnActive called twice" warning
        }

        // ---- QA 3 (AC-4) ----

        [Test]
        public void test_cancel_unsubscribesFromMobDied_otherSubscriberStillReceives()
        {
            // Arrange
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            int otherCalls = 0;
            Action<MobDiedArgs> other = _ => otherCalls++;
            _bus.OnMobDied += other;
            Assert.AreEqual(2, MobDiedSubscriberCount(), "arrange: service + other");
            EntityID live = s.GetPoint(0).CurrentEntityID;

            // Act
            s.CancelAllSpawnTimers(ZONE);
            _bus.RaiseMobDied(new MobDiedArgs(ZONE, live));

            // Assert
            Assert.AreEqual(1, MobDiedSubscriberCount(), "service handler removed");
            Assert.AreEqual(1, otherCalls, "other subscriber still gets the raise");
            Assert.AreEqual(SpawnPointState.Cancelled, s.GetPoint(0).State);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 4 (AC-5) ----

        [Test]
        public void test_cancel_twice_secondIsSilentNoOp()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            s.CancelAllSpawnTimers(ZONE);
            int callsAtMarker = _roster.TotalCalls;

            // Act
            Assert.DoesNotThrow(() => s.CancelAllSpawnTimers(ZONE));

            // Assert
            AssertAllCancelled(s);
            Assert.AreEqual(callsAtMarker, _roster.TotalCalls);
            Assert.AreEqual(0, MobDiedSubscriberCount(), "the service's single subscription is gone, not double-removed");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_foreignZone_logsOneWarningNamingBothZones_changesNothing_keepsSubscription()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            SpawnPoint[] before = new SpawnPoint[s.PointCount];
            for (int i = 0; i < s.PointCount; i++)
            {
                before[i] = s.GetPoint(i);
            }

            int callsAtMarker = _roster.TotalCalls;
            int retryAtMarker = s.RetryCounter;
            ExpectForeignWarning();

            // Act
            s.CancelAllSpawnTimers(OTHER_ZONE);

            // Assert: nothing changed
            Assert.IsFalse(s.IsCancelled);
            Assert.AreEqual(1, MobDiedSubscriberCount(), "subscription kept");
            Assert.AreEqual(retryAtMarker, s.RetryCounter);
            for (int i = 0; i < s.PointCount; i++)
            {
                SpawnPoint p = s.GetPoint(i);
                Assert.AreEqual(before[i].State, p.State, "point " + i + " state");
                Assert.AreEqual(before[i].TimerTick, p.TimerTick, "point " + i + " TimerTick");
                Assert.AreEqual(before[i].CurrentEntityID, p.CurrentEntityID, "point " + i + " CurrentEntityID");
            }

            Assert.AreEqual(1, s.DeferredCount);
            Assert.AreEqual(3, s.GetDeferredPointIndex(0));
            Assert.AreEqual(callsAtMarker, _roster.TotalCalls);

            // Subscription kept: a MobDied for the Live point still moves it to Dying
            Kill(s, 0);
            Assert.AreEqual(SpawnPointState.Dying, s.GetPoint(0).State);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_foreignZoneAfterRealCancel_logsOneWarning_staysCancelled()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            s.CancelAllSpawnTimers(ZONE);
            ExpectForeignWarning();

            // Act
            s.CancelAllSpawnTimers(OTHER_ZONE);

            // Assert
            AssertAllCancelled(s);
            Assert.AreEqual(0, MobDiedSubscriberCount());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_realCancelAfterForeignCancel_cancelsEverything_unsubscribes()
        {
            // Arrange
            MobSpawningService s = MakeMixed();
            ExpectForeignWarning();
            s.CancelAllSpawnTimers(OTHER_ZONE);
            int removesBefore = _roster.RemoveCalls;

            // Act
            s.CancelAllSpawnTimers(ZONE);

            // Assert
            AssertAllCancelled(s);
            Assert.AreEqual(0, MobDiedSubscriberCount());
            Assert.AreEqual(removesBefore, _roster.RemoveCalls);
            LogAssert.NoUnexpectedReceived();
        }

        private static void ExpectForeignWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(
                "CancelAllSpawnTimers for a foreign zone ignored.*" + Regex.Escape(OTHER_ZONE.ToString())
                + ".*" + Regex.Escape(ZONE.ToString())));
        }

        // ---- QA 5 (AC-6) ----

        [Test]
        public void test_cancel_beforePopulate_allCancelled_laterPopulateNoRosterCalls_noLog()
        {
            // Arrange
            MobSpawningService s = Make(3);

            // Act
            s.CancelAllSpawnTimers(ZONE);
            s.PopulateOnActive();
            s.PopulateOnActive();

            // Assert
            AssertAllCancelled(s);
            Assert.AreEqual(0, _roster.TotalCalls);
            Assert.AreEqual(0, _spawned.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- QA 6 (AC-7) ----

        [Test]
        public void test_cancel_insideTickFromMobSpawned_restOfTickMakesNoRosterCalls()
        {
            // Arrange: two points Respawning, both due on the same tick
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Kill(s, 0);
            Kill(s, 1);
            TickN(s, LINGER);
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(0).State, "arrange");
            Assert.AreEqual(SpawnPointState.Respawning, s.GetPoint(1).State, "arrange");
            TickN(s, COMMON_DELAY - 1);
            int addsBefore = _roster.AddCalls;
            int countsBefore = _roster.CountCalls;
            int spawnedBefore = _spawned.Count;
            int retryBefore = s.RetryCounter;
            _cancelTarget = s;
            _bus.OnMobSpawned += CancelFromSubscriber;

            // Act: the tick both respawns expire; the first spawn's subscriber tears the zone down
            s.Tick();

            // Assert
            Assert.AreEqual(retryBefore, s.RetryCounter, "Tick returned before the retry counter");
            Assert.AreEqual(addsBefore + 1, _roster.AddCalls, "only the first point's TryAddMob");
            Assert.AreEqual(countsBefore + 1, _roster.CountCalls, "only the first point's count check");
            Assert.AreEqual(spawnedBefore + 1, _spawned.Count, "one MobSpawned");
            AssertAllCancelled(s);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_insideRemoveMobDuringLinger_pointStaysCancelled_restOfTickSkipped()
        {
            // Arrange: two points Dying, both lingering out on the same tick
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Kill(s, 0);
            Kill(s, 1);
            TickN(s, LINGER - 1);
            int retryBefore = s.RetryCounter;
            _roster.OnRemove = () => s.CancelAllSpawnTimers(ZONE);

            // Act: point 0's RemoveMob re-enters teardown
            s.Tick();

            // Assert: point 0 not overwritten to Respawning, point 1 never reached
            Assert.AreEqual(1, _roster.RemoveCalls, "only point 0's RemoveMob");
            Assert.AreEqual(retryBefore, s.RetryCounter, "Tick returned before the retry counter");
            AssertAllCancelled(s);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_insidePollFromMobSpawned_stopsPoll_noException()
        {
            // Arrange: two points deferred at the cap, then room for both before the first poll
            _roster.Count = CAP;
            MobSpawningService s = Make(2);
            s.PopulateOnActive();
            Assert.AreEqual(2, s.DeferredCount, "arrange");
            _roster.Count = CAP - 2;
            TickN(s, RETRY_INTERVAL - 1);
            int addsBefore = _roster.AddCalls;
            _cancelTarget = s;
            _bus.OnMobSpawned += CancelFromSubscriber;

            // Act: poll call; the first fill cancels and clears the list mid-poll
            Assert.DoesNotThrow(() => s.Tick());

            // Assert
            Assert.AreEqual(addsBefore + 1, _roster.AddCalls, "poll stopped after the first fill");
            AssertAllCancelled(s);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_insidePopulateFromMobSpawned_restOfSweepMakesNoRosterCalls()
        {
            // Arrange
            MobSpawningService s = Make(3);
            _cancelTarget = s;
            _bus.OnMobSpawned += CancelFromSubscriber;

            // Act
            s.PopulateOnActive();

            // Assert
            Assert.AreEqual(1, _roster.AddCalls);
            Assert.AreEqual(1, _roster.CountCalls);
            Assert.AreEqual(1, _spawned.Count);
            AssertAllCancelled(s);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cancel_insideMobDiedRaiseBeforeServiceHandler_inFlightCallIgnored()
        {
            // Arrange: a subscriber added before the service cancels first; the bus raise iterates a snapshot,
            // so the service handler is still called for this raise
            _bus.OnMobDied += CancelFromMobDied;
            MobSpawningService s = Make(1);
            int laterCalls = 0;
            Action<MobDiedArgs> later = _ => laterCalls++;
            _bus.OnMobDied += later; // after the service: proves the raise iterates a snapshot
            s.PopulateOnActive();
            _cancelTarget = s;
            int callsAtMarker = _roster.TotalCalls;

            // Act
            Kill(s, 0);

            // Assert: Cancelled, not Dying; the snapshot reached the subscriber after the removed handler
            Assert.AreEqual(1, laterCalls, "raise iterated its snapshot past the removed service handler");
            AssertAllCancelled(s);
            Assert.AreEqual(callsAtMarker, _roster.TotalCalls);
            TickN(s, LINGER + COMMON_DELAY);
            Assert.AreEqual(callsAtMarker, _roster.TotalCalls);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

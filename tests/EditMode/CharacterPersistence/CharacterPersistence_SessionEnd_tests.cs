using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Character Persistence Story 010: session-end triggers (<see cref="SessionEndCoordinator"/>) driven through the real
    /// <see cref="TickCompletionQueue"/>, <see cref="CharacterPersistenceService"/> over <see cref="InMemoryCharacterRecordStore"/>
    /// and <see cref="ConnectionStateMachine"/>. Acceptance criteria covered: AC-CP-16 (TTL expiry), clean logout, release after
    /// success, logout failure (CR-CP-10 step 9), TTL failure (step 10), no double end (EC-CP-4), held TTL save, rollback,
    /// no await and shutdown. Deterministic: the only blocking waits are bounded 5 s waits on tasks that complete on the thread pool.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_SessionEnd_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const uint ACCOUNT = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const int WAIT_MS = 5000;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int SHORT_WATCHDOG_TICKS = 3;
        private const int START_LEVEL = 10;
        private const int START_EXPERIENCE = 50;
        private const int SEED_EXPERIENCE = 0;
        private const uint CONNECT_TICK = 100u;
        private const uint TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TIMEOUT_TICKS = 60u;
        private const uint CONNECTING_TIMEOUT_TICKS = 200u;
        private const uint EXPIRY_TICK = 500u;

        private const string SERVICE_FAILED_LOG = @"SaveSession \(.*\) for .* failed";
        private const string REREAD_WARNING = "re-read the stored SaveVersion";
        private const string NO_LONGER_LOG = "no longer";

        private static readonly Regex QUEUE_WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");

        public enum FailureKind
        {
            Fault,
            Cancel,
            WatchdogTimeout,
        }

        /// <summary>Forwards to the real service, records each SaveSession call and its task, and can replace the save with a test-controlled task.</summary>
        private sealed class RecordingPersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            private readonly CharacterPersistenceService _inner;

            public RecordingPersistence(CharacterPersistenceService inner)
            {
                _inner = inner;
            }

            public Func<Task<CharacterSaveResult>> SaveOverride { get; set; }

            public List<(CharacterID Id, SessionEndReason Reason, CancellationToken Token)> SaveCalls { get; } =
                new List<(CharacterID, SessionEndReason, CancellationToken)>();

            public Task<CharacterSaveResult> LastTask { get; private set; }

            public int ReleaseCalls { get; private set; }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
            {
                SaveCalls.Add((id, reason, ct));
                LastTask = SaveOverride != null ? SaveOverride() : _inner.SaveSession(id, reason, ct);
                return LastTask;
            }

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                ReleaseCalls++;
                return _inner.ReleaseSession(id, out entityId);
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => _inner.LoadCharacter(id, ownerAccountID, ct);

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => _inner.SaveIrreversibleOutcome(id, trigger, ct);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => _inner.SaveSession(id, reason, hpOverride, ct);

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => _inner.CreateStub(accountId, characterId, classType, ct);

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, CancellationToken ct) => _inner.BeginPurchase(charId, record, ct);

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => _inner.CompletePurchase(purchaseId, ct);

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => _inner.RefundPurchase(purchaseId, charId, refundAmount, ct);

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => _inner.LoadOutstandingPurchases(charId, ct);
        }

        private sealed class Rig
        {
            public LevelingService Leveling;
            public IronGrind.CharacterStats.CharacterStats Stats;
            public CurrencySystem CurrencySystem;
            public RecordingInventory Inventory;
            public FakeZoneLocator Zones;
            public CharacterWriteQueue Queue;
            public CharacterPersistenceService Service;
            public EntityID Entity;
        }

        private DateTime _storeTime;
        private InMemoryCharacterRecordStore _store;
        private Rig _rig;
        private RecordingPersistence _persist;
        private TickCompletionQueue _queue;
        private ConnectionStateMachine _sm;
        private SessionEndCoordinator _ends;
        private uint _tick;

        private readonly List<EntityID> _unregisteredEntities = new List<EntityID>();
        private readonly List<uint> _removed = new List<uint>();
        private readonly List<(uint CharacterId, DisconnectType Type)> _left = new List<(uint, DisconnectType)>();
        private readonly List<CharacterSaveResult> _results = new List<CharacterSaveResult>();

        // Each release step seen by the delegates, with how many earlier steps had run then (release order).
        private readonly List<string> _steps = new List<string>();
        private bool _levelingThrows;

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);
            _rig = BuildRig();
            _persist = new RecordingPersistence(_rig.Service);
            _sm = new ConnectionStateMachine();
            _tick = 0u;
            _unregisteredEntities.Clear();
            _removed.Clear();
            _left.Clear();
            _results.Clear();
            _steps.Clear();
            _levelingThrows = false;
            BuildEnds(LONG_WATCHDOG_TICKS);
        }

        private void BuildEnds(int watchdogTicks)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _ends = new SessionEndCoordinator(
                _persist,
                _persist,
                _queue,
                _sm,
                _rig.Inventory,
                entity =>
                {
                    _steps.Add($"leveling persist={_persist.ReleaseCalls} inventory={_rig.Inventory.UnregisterCalls.Count}");
                    if (_levelingThrows)
                    {
                        throw new InvalidOperationException("leveling unregister failed");
                    }

                    _unregisteredEntities.Add(entity);
                },
                characterId =>
                {
                    _steps.Add($"remove leveling={_unregisteredEntities.Count}");
                    _removed.Add(characterId);
                },
                (characterId, type) => _left.Add((characterId, type)));
        }

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private Rig BuildRig()
        {
            var rig = new Rig();
            rig.Leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            rig.Stats = CharacterStatsFixture.CreateWithLeveling(rig.Leveling);
            rig.Leveling.AttachCharacterStats(rig.Stats);
            rig.Entity = CharacterStatsFixture.PlayerEntityId;
            rig.CurrencySystem = new CurrencySystem();
            rig.Inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty };
            rig.Zones = new FakeZoneLocator();
            rig.Queue = new CharacterWriteQueue();
            rig.Service = new CharacterPersistenceService(
                _store,
                rig.Stats,
                rig.Leveling,
                rig.Inventory,
                rig.CurrencySystem,
                rig.Zones,
                rig.Queue,
                () => T2,
                TimeSpan.FromSeconds(5));
            return rig;
        }

        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WARRIOR_CLASS_TYPE, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            return registry;
        }

        private static int[] BuildXpThresholdsWithCapSentinel()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within 5 s.");
            return task.Result;
        }

        private static void WaitUntil(Func<bool> condition, string what)
        {
            Assert.IsTrue(SpinWait.SpinUntil(condition, WAIT_MS), "Timed out after 5 s waiting for: " + what);
        }

        private static CharacterRecord MakeSeed()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = START_LEVEL;
            r.Experience = SEED_EXPERIENCE;
            r.HeldFreePoints = 4;
            r.STR = 12;
            r.DEX = 11;
            r.VIT = 13;
            r.INT = 9;
            r.MaxHP = 1000;
            r.MaxMP = 220;
            r.AttackPower = 30;
            r.Defense = 20;
            r.MagicDefense = 10;
            r.CurrentHP = 650f;
            r.CurrentMP = 100f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.LastZoneID = LOADED_ZONE;
            return r;
        }

        // Inserts the seed, loads it into the rig and moves the store clock on to T2. Returns the loaded record.
        private CharacterRecord SeedAndLoad()
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterLoadResult result = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _rig.Service.ApplyLoadedRecord(_rig.Entity, result.Record));
            _storeTime = T2;
            return result.Record;
        }

        private CharacterRecord Stored() => Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));

        private void ConnectAccount()
        {
            _sm.EnterConnecting(ACCOUNT, CONNECT_TICK);
            _sm.CompleteAuthSuccess(ACCOUNT, CHAR_A.RawValue, CONNECT_TICK);
            AssertState(SessionState.Connected);
        }

        private void DisconnectByHeartbeat()
        {
            _sm.EvaluateTimeouts(TIMEOUT_TICK, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS);
            AssertState(SessionState.Disconnected_SessionActive);
        }

        private void AssertState(SessionState expected)
        {
            Assert.IsTrue(_sm.TryGetSessionState(ACCOUNT, out SessionState actual));
            Assert.AreEqual(expected, actual);
        }

        private void BeginLogout() => Assert.IsTrue(_ends.BeginCleanLogout(ACCOUNT, r => _results.Add(r)));

        private static void ExpectError(string pattern) => LogAssert.Expect(LogType.Error, new Regex(pattern));

        private static void ExpectWarning(string pattern) => LogAssert.Expect(LogType.Warning, new Regex(pattern));

        private void AssertNoReleaseSteps()
        {
            Assert.AreEqual(0, _persist.ReleaseCalls, "persistence session release");
            Assert.AreEqual(0, _rig.Inventory.UnregisterCalls.Count, "inventory unregister");
            Assert.AreEqual(0, _unregisteredEntities.Count, "leveling unregister");
            Assert.AreEqual(0, _removed.Count, "removeEntityFromZone");
            Assert.AreEqual(0, _left.Count, "PlayerLeftZone broadcast");
            Assert.IsTrue(_sm.IsAccountRegistered(ACCOUNT), "state machine session");
        }

        private void AssertReleasedOnce(DisconnectType expectedType)
        {
            Assert.AreEqual(1, _persist.ReleaseCalls, "persistence session release");
            Assert.IsFalse(_rig.Service.ReleaseSession(CHAR_A, out _), "the session-table entry must be gone");
            Assert.AreEqual(1, _rig.Inventory.UnregisterCalls.Count, "inventory unregister");
            Assert.AreEqual(CHAR_A, _rig.Inventory.UnregisterCalls[0]);
            Assert.AreEqual(1, _unregisteredEntities.Count, "leveling unregister");
            Assert.AreEqual(_rig.Entity, _unregisteredEntities[0]);
            Assert.AreEqual(1, _removed.Count, "removeEntityFromZone");
            Assert.AreEqual(CHAR_A.RawValue, _removed[0]);
            Assert.AreEqual(1, _left.Count, "PlayerLeftZone broadcast");
            Assert.AreEqual((CHAR_A.RawValue, expectedType), _left[0]);
            Assert.IsFalse(_sm.IsAccountRegistered(ACCOUNT), "state machine session");
            Assert.AreEqual(0, _ends.InFlightCount);
        }

        private static Task<CharacterSaveResult> ControlledFailure(FailureKind kind)
        {
            var source = new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (kind == FailureKind.Fault)
            {
                source.SetException(new InvalidOperationException("db down"));
            }
            else if (kind == FailureKind.Cancel)
            {
                source.SetCanceled();
            }

            return source.Task;
        }

        // ---------------------------------------------------------------- AC-CP-16: TTL expiry

        [Test]
        public void Test_AcCp16_TtlExpiry_TickBeforeExpiryStartsNothing_ExpiryTickSavesOnceAndReleasesOnceAfterDrain()
        {
            CharacterRecord loaded = SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();

            bool before = _ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK - 1u, EXPIRY_TICK);
            Assert.IsFalse(before);
            Assert.AreEqual(0, _persist.SaveCalls.Count);
            Assert.AreEqual(0, _queue.InFlightCount);

            bool started = _ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK);

            Assert.IsTrue(started);
            Assert.AreEqual(1, _persist.SaveCalls.Count);
            Assert.AreEqual(CHAR_A, _persist.SaveCalls[0].Id);
            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _persist.SaveCalls[0].Reason);
            Assert.AreEqual(1, _queue.InFlightCount, "The save task must be tracked.");
            AssertNoReleaseSteps();

            WaitUntil(() => _persist.LastTask.IsCompleted, "the TTL save to complete");
            DrainNextTick();

            CharacterRecord stored = Stored();
            Assert.AreEqual(CharacterSaveResult.Success, _persist.LastTask.Result);
            Assert.AreEqual(T2, stored.LastSavedAtUtc);
            Assert.AreEqual(loaded.SaveVersion + 1u, stored.SaveVersion);
            AssertReleasedOnce(DisconnectType.Timeout);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_TtlExpiry_AccountStillConnected_StartsNothing()
        {
            SeedAndLoad();
            ConnectAccount();

            bool started = _ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK);

            Assert.IsFalse(started);
            Assert.AreEqual(0, _persist.SaveCalls.Count);
        }

        // ---------------------------------------------------------------- clean logout

        [Test]
        public void Test_CleanLogout_OneSave_NoReleaseBeforeDrain_ReleaseAndSuccessOnceAfterDrain()
        {
            CharacterRecord loaded = SeedAndLoad();
            ConnectAccount();

            BeginLogout();

            Assert.AreEqual(1, _persist.SaveCalls.Count);
            Assert.AreEqual(SessionEndReason.CleanLogout, _persist.SaveCalls[0].Reason);
            Assert.AreEqual(1, _queue.InFlightCount);

            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            AssertNoReleaseSteps();
            Assert.AreEqual(0, _results.Count, "No result before the drain.");

            DrainNextTick();

            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
            Assert.AreEqual(loaded.SaveVersion + 1u, Stored().SaveVersion);
            AssertReleasedOnce(DisconnectType.Graceful);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_CleanLogout_AccountNotConnected_ThrowsInvalidOperationException()
        {
            SeedAndLoad();

            Assert.Throws<InvalidOperationException>(() => _ends.BeginCleanLogout(ACCOUNT, r => _results.Add(r)));
            Assert.AreEqual(0, _persist.SaveCalls.Count);
        }

        // ---------------------------------------------------------------- logout failure (CR-CP-10 step 9)

        [Test]
        public void Test_LogoutFailure_DatabaseError_NoReleaseStillConnectedOneResult_RetrySucceedsReleasesOnce()
        {
            SeedAndLoad();
            ConnectAccount();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);

            BeginLogout();
            WaitUntil(() => _persist.LastTask.IsCompleted, "the failed save to complete");
            DrainNextTick();

            CollectionAssert.AreEqual(new[] { CharacterSaveResult.DatabaseError }, _results);
            AssertState(SessionState.Connected);
            AssertNoReleaseSteps();
            Assert.AreEqual(0, _ends.InFlightCount);

            // The retry starts a new save, which re-reads the unknown stored version and then succeeds.
            ExpectWarning(REREAD_WARNING);
            BeginLogout();
            Assert.AreEqual(2, _persist.SaveCalls.Count);
            WaitUntil(() => _persist.LastTask.IsCompleted, "the retried save to complete");
            DrainNextTick();

            CollectionAssert.AreEqual(new[] { CharacterSaveResult.DatabaseError, CharacterSaveResult.Success }, _results);
            AssertReleasedOnce(DisconnectType.Graceful);
        }

        [TestCase(FailureKind.Fault)]
        [TestCase(FailureKind.Cancel)]
        [TestCase(FailureKind.WatchdogTimeout)]
        public void Test_LogoutFailure_FaultCancelOrWatchdog_NoReleaseStillConnectedReportsDatabaseErrorOnce(FailureKind kind)
        {
            SeedAndLoad();
            ConnectAccount();
            _persist.SaveOverride = () => ControlledFailure(kind);
            if (kind == FailureKind.WatchdogTimeout)
            {
                BuildEnds(SHORT_WATCHDOG_TICKS);
                LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
            }

            BeginLogout();
            for (int i = 0; i < SHORT_WATCHDOG_TICKS; i++)
            {
                DrainNextTick();
            }

            CollectionAssert.AreEqual(new[] { CharacterSaveResult.DatabaseError }, _results);
            AssertState(SessionState.Connected);
            AssertNoReleaseSteps();
            Assert.AreEqual(0, _ends.InFlightCount);
            if (kind == FailureKind.WatchdogTimeout)
            {
                CancellationToken token = _persist.SaveCalls[0].Token;
                Assert.IsTrue(token.IsCancellationRequested, "The watchdog cancels the save's token.");
                Assert.DoesNotThrow(() => { WaitHandle unused = token.WaitHandle; }, "The token source of a still-running save must not be disposed.");
            }
        }

        // ---------------------------------------------------------------- TTL failure (step 10)

        [Test]
        public void Test_TtlFailure_DatabaseError_ReleasedOnceRegardless_NoSecondCriticalLog()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);

            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            WaitUntil(() => _persist.LastTask.IsCompleted, "the failed TTL save to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.DatabaseError, _persist.LastTask.Result);
            AssertReleasedOnce(DisconnectType.Timeout);
            Assert.AreEqual(SEED_EXPERIENCE, Stored().Experience, "Nothing was stored.");
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(FailureKind.Fault)]
        [TestCase(FailureKind.Cancel)]
        [TestCase(FailureKind.WatchdogTimeout)]
        public void Test_TtlFailure_FaultCancelOrWatchdog_ReleasedOnce(FailureKind kind)
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            _persist.SaveOverride = () => ControlledFailure(kind);
            if (kind == FailureKind.WatchdogTimeout)
            {
                BuildEnds(SHORT_WATCHDOG_TICKS);
                LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
            }

            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            for (int i = 0; i < SHORT_WATCHDOG_TICKS; i++)
            {
                DrainNextTick();
            }

            AssertReleasedOnce(DisconnectType.Timeout);
            if (kind == FailureKind.WatchdogTimeout)
            {
                // The timed-out save still runs and holds the token; its source must not have been disposed under it.
                CancellationToken token = _persist.SaveCalls[0].Token;
                Assert.IsTrue(token.IsCancellationRequested);
                Assert.DoesNotThrow(() => { WaitHandle unused = token.WaitHandle; });
            }
        }

        // ---------------------------------------------------------------- double trigger (EC-CP-4)

        [Test]
        public void Test_DoubleTrigger_TwoLogoutsBeforeDrain_OneSaveOneReleaseOneResult()
        {
            SeedAndLoad();
            ConnectAccount();
            int secondCalls = 0;

            BeginLogout();
            bool second = _ends.BeginCleanLogout(ACCOUNT, r => secondCalls++);
            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            DrainNextTick();
            DrainNextTick();

            Assert.IsFalse(second);
            Assert.AreEqual(1, _persist.SaveCalls.Count);
            Assert.AreEqual(0, secondCalls);
            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
            AssertReleasedOnce(DisconnectType.Graceful);
        }

        [Test]
        public void Test_DoubleTrigger_RepeatedTtlEvaluationWhileSaveInFlight_OneSave()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            Action release = _store.HoldNext();

            try
            {
                Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
                for (uint tick = EXPIRY_TICK + 1u; tick <= EXPIRY_TICK + 3u; tick++)
                {
                    Assert.IsFalse(_ends.EvaluateSessionTTL(ACCOUNT, tick, EXPIRY_TICK));
                }

                Assert.AreEqual(1, _persist.SaveCalls.Count);
                Assert.AreEqual(1, _ends.InFlightCount);
            }
            finally
            {
                release();
            }

            WaitUntil(() => _persist.LastTask.IsCompleted, "the held save to complete");
            DrainNextTick();

            Assert.AreEqual(1, _persist.SaveCalls.Count);
            AssertReleasedOnce(DisconnectType.Timeout);
        }

        // ---------------------------------------------------------------- held TTL save

        [Test]
        public void Test_HeldTtlSave_AcrossSeveralTicks_ReleaseRunsOnceOnTheDrainAfterCompletion()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            Action release = _store.HoldNext();

            try
            {
                Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
                DrainNextTick();
                DrainNextTick();
                DrainNextTick();

                AssertNoReleaseSteps();
                AssertState(SessionState.Disconnected_SessionActive);
                Assert.AreEqual(1, _queue.InFlightCount);
            }
            finally
            {
                release();
            }

            WaitUntil(() => _persist.LastTask.IsCompleted, "the held save to complete");
            AssertNoReleaseSteps();
            DrainNextTick();

            AssertReleasedOnce(DisconnectType.Timeout);
            DrainNextTick();
            Assert.AreEqual(1, _persist.ReleaseCalls, "A later drain must not release again.");
        }

        // ---------------------------------------------------------------- after a failed irreversible save

        [Test]
        public void Test_AfterRollback_TtlSaveWritesTheRolledBackLevelAndExperience()
        {
            SeedAndLoad();
            // The state a failed level-up leaves in memory (Story 009): Level back at 10, Experience restored to 50.
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Level, START_LEVEL);
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Experience, START_EXPERIENCE);
            ConnectAccount();
            DisconnectByHeartbeat();

            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            WaitUntil(() => _persist.LastTask.IsCompleted, "the TTL save to complete");
            DrainNextTick();

            CharacterRecord stored = Stored();
            Assert.AreEqual(START_LEVEL, stored.Level);
            Assert.AreEqual(START_EXPERIENCE, stored.Experience);
            AssertReleasedOnce(DisconnectType.Timeout);
        }

        // ---------------------------------------------------------------- state re-check

        [Test]
        public void Test_StateChangedDuringLogoutSave_NoReleaseStepsOneErrorResultStillReported()
        {
            SeedAndLoad();
            ConnectAccount();
            BeginLogout();
            DisconnectByHeartbeat(); // the heartbeat timeout wins the race while the save is in flight
            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            ExpectError(NO_LONGER_LOG);

            DrainNextTick();

            Assert.AreEqual(0, _persist.ReleaseCalls);
            Assert.AreEqual(0, _rig.Inventory.UnregisterCalls.Count);
            Assert.AreEqual(0, _removed.Count);
            Assert.IsTrue(_sm.IsAccountRegistered(ACCOUNT));
            AssertState(SessionState.Disconnected_SessionActive);
            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
            Assert.AreEqual(0, _ends.InFlightCount);
        }

        // ---------------------------------------------------------------- shutdown

        [Test]
        public void Test_Shutdown_SaveInFlight_CallbackRunsOnceFromShutdownDrainAndReleasesOnce()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();

            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            Assert.AreEqual(1, _queue.InFlightCount);
            AssertNoReleaseSteps();

            _queue.DrainOnShutdown(TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, _queue.InFlightCount);
            AssertReleasedOnce(DisconnectType.Timeout);
            DrainNextTick();
            Assert.AreEqual(1, _persist.ReleaseCalls);
        }

        [Test]
        public void Test_Shutdown_LogoutInFlight_ResultSuccessOnceAndReleasesOnce()
        {
            SeedAndLoad();
            ConnectAccount();
            BeginLogout();

            _queue.DrainOnShutdown(TimeSpan.FromSeconds(5));

            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
            AssertReleasedOnce(DisconnectType.Graceful);
        }

        // ---------------------------------------------------------------- state re-check, TTL path

        [Test]
        public void Test_StateChangedDuringTtlSave_NoReleaseStepsOneError()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            _sm.EnterReconnecting(ACCOUNT, EXPIRY_TICK); // nothing refuses a reconnect after the expiry tick today
            WaitUntil(() => _persist.LastTask.IsCompleted, "the TTL save to complete");
            ExpectError(NO_LONGER_LOG);

            DrainNextTick();

            Assert.AreEqual(0, _persist.ReleaseCalls);
            Assert.AreEqual(0, _rig.Inventory.UnregisterCalls.Count);
            Assert.AreEqual(0, _unregisteredEntities.Count);
            Assert.AreEqual(0, _removed.Count);
            AssertState(SessionState.Reconnecting);
            Assert.AreEqual(0, _ends.InFlightCount);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- mixed triggers (EC-CP-4)

        [Test]
        public void Test_DoubleTrigger_TtlWhileLogoutInFlight_StartsNothing_LaterTtlStartsANewSave()
        {
            SeedAndLoad();
            ConnectAccount();
            BeginLogout();
            DisconnectByHeartbeat(); // the session is now Disconnected_SessionActive while the logout save is in flight

            Assert.IsFalse(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            Assert.AreEqual(1, _persist.SaveCalls.Count);

            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            ExpectError(NO_LONGER_LOG);
            DrainNextTick();

            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK + 1u, EXPIRY_TICK));
            Assert.AreEqual(SessionEndReason.SessionTTLExpiry, _persist.SaveCalls[1].Reason);
            WaitUntil(() => _persist.LastTask.IsCompleted, "the TTL save to complete");
            DrainNextTick();

            AssertReleasedOnce(DisconnectType.Timeout);
        }

        [Test]
        public void Test_DoubleTrigger_LogoutWhileTtlInFlight_ReturnsFalseWithoutThrowing()
        {
            SeedAndLoad();
            ConnectAccount();
            DisconnectByHeartbeat();
            Assert.IsTrue(_ends.EvaluateSessionTTL(ACCOUNT, EXPIRY_TICK, EXPIRY_TICK));
            int calls = 0;

            bool started = _ends.BeginCleanLogout(ACCOUNT, r => calls++);
            WaitUntil(() => _persist.LastTask.IsCompleted, "the TTL save to complete");
            DrainNextTick();

            Assert.IsFalse(started);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(1, _persist.SaveCalls.Count);
            AssertReleasedOnce(DisconnectType.Timeout);
        }

        // ---------------------------------------------------------------- release details

        [Test]
        public void Test_Release_StepsRunInOrder_PersistenceInventoryLevelingThenStateMachine()
        {
            SeedAndLoad();
            ConnectAccount();
            BeginLogout();
            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");

            DrainNextTick();

            CollectionAssert.AreEqual(new[] { "leveling persist=1 inventory=1", "remove leveling=1" }, _steps);
        }

        [Test]
        public void Test_Release_NoPersistenceEntry_SkipsLevelingRunsOtherSteps()
        {
            SeedAndLoad();
            ConnectAccount();
            BeginLogout();
            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            Assert.IsTrue(_rig.Service.ReleaseSession(CHAR_A, out _)); // the entry is gone before the callback runs

            DrainNextTick();

            Assert.AreEqual(1, _persist.ReleaseCalls);
            Assert.AreEqual(1, _rig.Inventory.UnregisterCalls.Count);
            Assert.AreEqual(0, _unregisteredEntities.Count, "The entity is unknown, so Leveling is not called.");
            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(1, _left.Count);
            Assert.IsFalse(_sm.IsAccountRegistered(ACCOUNT));
            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
        }

        [Test]
        public void Test_Release_StepThrows_LoggedOnce_LaterStepsSkipped_LogoutStillReportsSuccess()
        {
            SeedAndLoad();
            ConnectAccount();
            _levelingThrows = true;
            BeginLogout();
            WaitUntil(() => _persist.LastTask.IsCompleted, "the logout save to complete");
            ExpectError("release is incomplete");

            DrainNextTick();

            Assert.AreEqual(1, _persist.ReleaseCalls);
            Assert.AreEqual(1, _rig.Inventory.UnregisterCalls.Count);
            Assert.AreEqual(0, _removed.Count, "The state-machine step after the throw does not run.");
            Assert.IsTrue(_sm.IsAccountRegistered(ACCOUNT));
            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, _results);
            Assert.AreEqual(0, _ends.InFlightCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Test_CleanLogout_SaveThrowsSynchronously_NothingInFlight()
        {
            SeedAndLoad();
            ConnectAccount();
            _persist.SaveOverride = () => throw new InvalidOperationException("wiring bug");

            Assert.Throws<InvalidOperationException>(() => _ends.BeginCleanLogout(ACCOUNT, r => _results.Add(r)));

            Assert.AreEqual(0, _ends.InFlightCount);
            Assert.AreEqual(0, _queue.InFlightCount);
            AssertState(SessionState.Connected);
        }

        [Test]
        public void Test_CleanLogout_NullOnResult_ThrowsArgumentNullException()
        {
            SeedAndLoad();
            ConnectAccount();

            Assert.Throws<ArgumentNullException>(() => _ends.BeginCleanLogout(ACCOUNT, null));
            Assert.AreEqual(0, _persist.SaveCalls.Count);
        }

        // ---------------------------------------------------------------- no await

        [Test]
        public void SessionEndCoordinator_Source_ContainsNoAwaitResultWaitGetResultOrContinueWith()
        {
            string repo = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(repo, "src", "ServerLogic", "CharacterPersistence", "SessionEndCoordinator.cs");
            string[] code = File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//")).ToArray();
            Assert.IsNotEmpty(code, "no source found at " + path);
            string[] forbidden = { "await", ".Result", ".Wait(", "GetResult(", "ContinueWith" };

            foreach (string line in code)
            {
                foreach (string token in forbidden)
                {
                    Assert.IsFalse(line.Contains(token), "SessionEndCoordinator.cs contains '" + token + "': " + line.Trim());
                }
            }
        }
    }
}

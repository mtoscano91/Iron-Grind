using System;
using System.Collections.Generic;
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
    /// Character Persistence Story 009: the irreversible-outcome persist step
    /// (<see cref="IrreversibleOutcomePersistStep"/>) driven through the real
    /// <see cref="IrreversibleOutcomeCoordinator"/>, <see cref="TickCompletionQueue"/> and
    /// <see cref="CharacterMutationGate"/>, with a test caller that plays the level-up. Acceptance criteria covered:
    /// the persist step, AC-CP-13 (held write), AC-CP-14 (failure from Level 10, for DatabaseError, ConcurrencyConflict,
    /// CharacterNotFound, a fault, a cancellation and a watchdog timeout), AC-CP-17 (two level-ups, one write), the gate,
    /// the already-complete edge and the later session save of the rolled-back state. Deterministic: no async tests; the
    /// only blocking waits are bounded 5 s waits (<c>Task.Wait</c> and <c>SpinWait.SpinUntil</c>) on tasks that complete
    /// on the thread pool.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_IrreversibleOutcome_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const uint CLIENT_ID = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;
        private const int WAIT_MS = 5000;
        private const int WATCHDOG_TICKS = 3;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int EXPECTED_TTL_SECONDS = 300;
        private const int START_LEVEL = 10;
        private const int START_EXPERIENCE = 50;
        private const int SEED_EXPERIENCE = 0;
        private const int POST_LEVEL_UP_EXPERIENCE = 3;

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string CAUSE_FAULTED = "write faulted";
        private const string CAUSE_CANCELLED = "write was cancelled";
        private const string CAUSE_WATCHDOG = "watchdog timeout";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(LevelUp\) for .* failed";
        private const string SERVICE_CONFLICT_LOG = @"CRITICAL: SaveIrreversibleOutcome \(LevelUp\) for .* conflicted";
        private const string REREAD_WARNING = "re-read the stored SaveVersion";

        private static readonly Regex QUEUE_WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");

        /// <summary>A persistence fake: forwards to an inner service, or (no inner) returns a task the test completes.</summary>
        private sealed class FakePersistence : ICharacterPersistence
        {
            private readonly ICharacterPersistence _inner;

            public FakePersistence(ICharacterPersistence inner)
            {
                _inner = inner;
            }

            public TaskCompletionSource<CharacterSaveResult> Source { get; } =
                new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<(CharacterID Id, IrreversibleOutcomeTrigger Trigger, CancellationToken Token)> Calls { get; } =
                new List<(CharacterID, IrreversibleOutcomeTrigger, CancellationToken)>();

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                Calls.Add((id, trigger, ct));
                return _inner != null ? _inner.SaveIrreversibleOutcome(id, trigger, ct) : Source.Task;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class LevelUpOutcome
        {
            public int FinalLevel;
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

        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private NetworkTestObserver _observer;
        private IrreversibleOutcomeCoordinator _coordinator;
        private uint _tick;

        // The test caller's record of what the coordinator did.
        private int _deliveries;
        private int _reverts;
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();
        private readonly List<(uint ClientId, int Ttl)> _preserves = new List<(uint, int)>();
        private int _capturedLevel;
        private int _capturedExperience;
        private Task<CharacterSaveResult> _lastTask;

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);
            _rig = BuildRig();
            _tick = 0u;
            _deliveries = 0;
            _reverts = 0;
            _disconnects.Clear();
            _preserves.Clear();
            _lastTask = null;
            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            Rebuild(LONG_WATCHDOG_TICKS);
        }

        private void Rebuild(int watchdogTicks)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
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
                rig.CurrencySystem, new FakeEquipmentService(),
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

        // Inserts the seed, loads it into the rig, gives the live character the test Experience, moves the store clock on.
        private CharacterRecord SeedAndLoad()
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterLoadResult result = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _rig.Service.ApplyLoadedRecord(_rig.Entity, result.Record));
            _storeTime = T2;
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Experience, START_EXPERIENCE);
            Assert.AreEqual(START_LEVEL, _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level));
            return result.Record;
        }

        private CharacterRecord Stored() => Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));

        private static void ExpectError(string pattern) => LogAssert.Expect(LogType.Error, new Regex(pattern));

        private static void ExpectWarning(string pattern) => LogAssert.Expect(LogType.Warning, new Regex(pattern));

        private static void ExpectCoordinatorAlert(string cause) => LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + cause));

        // The test caller's level-up: captures Level / Experience, raises Level by levelUps and resets Experience, in memory.
        private LevelUpOutcome ApplyLevelUps(int levelUps)
        {
            _capturedLevel = _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level);
            _capturedExperience = _rig.Stats.GetBaseStat(_rig.Entity, StatID.Experience);
            int level = _capturedLevel;
            for (int i = 0; i < levelUps; i++)
            {
                level++;
                _rig.Stats.SetBaseStat(_rig.Entity, StatID.Level, level);
                _rig.Stats.SetBaseStat(_rig.Entity, StatID.Experience, POST_LEVEL_UP_EXPERIENCE);
            }

            return new LevelUpOutcome { FinalLevel = level };
        }

        private void RevertLevelUp()
        {
            _reverts++;
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Level, _capturedLevel);
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Experience, _capturedExperience);
        }

        // Starts the level-up through the coordinator, using the persist step; records the write task.
        private IrreversibleOutcomeBeginResult BeginLevelUp(ICharacterPersistence persistence, CharacterID charId, int levelUps = 1)
        {
            Func<LevelUpOutcome, CancellationToken, Task<CharacterSaveResult>> persist =
                IrreversibleOutcomePersistStep.StartWrite<LevelUpOutcome>(persistence, charId, IrreversibleOutcomeTrigger.LevelUp);
            return _coordinator.Begin<LevelUpOutcome, CharacterSaveResult>(
                CLIENT_ID,
                charId,
                () => true,
                () => { },
                () => ApplyLevelUps(levelUps),
                (outcome, token) => _lastTask = persist(outcome, token),
                IrreversibleOutcomePersistStep.IsSuccess,
                outcome => _deliveries++,
                RevertLevelUp,
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => _preserves.Add((id, ttl)));
        }

        private void AssertNoFailureEffects()
        {
            Assert.AreEqual(0, _reverts);
            Assert.AreEqual(0, _disconnects.Count);
            Assert.AreEqual(0, _preserves.Count);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        // The failed write persisted nothing: CHAR_A's stored row still holds the seed's Level and Experience.
        private void AssertStoredRowUnchanged()
        {
            CharacterRecord stored = Stored();
            Assert.AreEqual(START_LEVEL, stored.Level);
            Assert.AreEqual(SEED_EXPERIENCE, stored.Experience);
        }

        // AC-CP-14: no delivery, Level and Experience back, one disconnect (Other), one alert, one preserve (300), gate open.
        private void AssertFailureEffectsOnce(CharacterID charId)
        {
            Assert.AreEqual(0, _deliveries, "The outcome must not be delivered.");
            Assert.AreEqual(1, _reverts);
            Assert.AreEqual(START_LEVEL, _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level));
            Assert.AreEqual(START_EXPERIENCE, _rig.Stats.GetBaseStat(_rig.Entity, StatID.Experience));
            Assert.AreEqual(1, _disconnects.Count);
            Assert.AreEqual(CLIENT_ID, _disconnects[0].ClientId);
            Assert.AreEqual(DisconnectReason.Other, _disconnects[0].Reason);
            Assert.AreEqual(1, _preserves.Count);
            Assert.AreEqual(CLIENT_ID, _preserves[0].ClientId);
            Assert.AreEqual(EXPECTED_TTL_SECONDS, _preserves[0].Ttl);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.IsFalse(_gate.IsHeld(charId), "The gate must be open after the failure was handled.");
            Assert.AreEqual(0, _queue.InFlightCount, "No write may still be tracked.");
        }

        // ---------------------------------------------------------------- the persist step

        // Every CharacterSaveResult value, so a member added later is non-success unless this test says otherwise.
        private static IEnumerable<CharacterSaveResult> AllSaveResults() =>
            (CharacterSaveResult[])Enum.GetValues(typeof(CharacterSaveResult));

        private static IEnumerable<IrreversibleOutcomeTrigger> AllTriggers() =>
            (IrreversibleOutcomeTrigger[])Enum.GetValues(typeof(IrreversibleOutcomeTrigger));

        [Test]
        public void Test_PersistStep_IsSuccess_TrueOnlyForSuccess([ValueSource(nameof(AllSaveResults))] CharacterSaveResult result)
        {
            Assert.AreEqual(result == CharacterSaveResult.Success, IrreversibleOutcomePersistStep.IsSuccess(result));
        }

        [Test]
        public void Test_PersistStep_StartWrite_CallsSaveIrreversibleOutcomeOnceWithIdTriggerAndToken(
            [ValueSource(nameof(AllTriggers))] IrreversibleOutcomeTrigger trigger)
        {
            var fake = new FakePersistence(null);
            using (var cts = new CancellationTokenSource())
            {
                Func<LevelUpOutcome, CancellationToken, Task<CharacterSaveResult>> start =
                    IrreversibleOutcomePersistStep.StartWrite<LevelUpOutcome>(fake, CHAR_A, trigger);
                Assert.AreEqual(0, fake.Calls.Count, "Building the delegate must not start a write.");

                Task<CharacterSaveResult> task = start(new LevelUpOutcome(), cts.Token);

                Assert.AreSame(fake.Source.Task, task);
                Assert.AreEqual(1, fake.Calls.Count);
                Assert.AreEqual(CHAR_A, fake.Calls[0].Id);
                Assert.AreEqual(trigger, fake.Calls[0].Trigger);
                Assert.AreEqual(cts.Token, fake.Calls[0].Token);
                Assert.IsFalse(task.IsCompleted, "The step must only start the write.");
            }
        }

        [Test]
        public void Test_PersistStep_StartWrite_NullPersistence_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => IrreversibleOutcomePersistStep.StartWrite<LevelUpOutcome>(null, CHAR_A, IrreversibleOutcomeTrigger.LevelUp));
        }

        [Test]
        public void Test_PersistStep_PassedToCoordinator_TokenIsTheCoordinatorsWatchdogToken()
        {
            var fake = new FakePersistence(null);

            BeginLevelUp(fake, CHAR_A);

            Assert.AreEqual(1, fake.Calls.Count);
            Assert.IsFalse(fake.Calls[0].Token.IsCancellationRequested);
            Assert.IsTrue(fake.Calls[0].Token.CanBeCanceled, "The coordinator passes a token its watchdog can cancel.");
        }

        // ---------------------------------------------------------------- AC-CP-13

        [Test]
        public void Test_AcCp13_HeldWrite_NoDeliveryUntilReleasedThenDeliveredOnNextDrain()
        {
            SeedAndLoad();
            Action release = _store.HoldNext();
            var persistence = new FakePersistence(_rig.Service);

            try
            {
                IrreversibleOutcomeBeginResult result = BeginLevelUp(persistence, CHAR_A);
                DrainNextTick();
                DrainNextTick();
                DrainNextTick();

                Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
                Assert.AreEqual(0, _deliveries, "No outcome while the write is held.");
                Assert.IsTrue(_gate.IsHeld(CHAR_A));
                Assert.AreEqual(1, _queue.InFlightCount);
                AssertNoFailureEffects();
            }
            finally
            {
                release(); // a second release does nothing; releasing here keeps a failed assertion from leaking the held call
            }

            WaitUntil(() => _lastTask.IsCompleted, "the held write to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, _lastTask.Result);
            Assert.AreEqual(1, _deliveries);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.AreEqual(START_LEVEL + 1, Stored().Level);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-CP-14

        [Test]
        public void Test_AcCp14_DatabaseError_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            SeedAndLoad();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            BeginLevelUp(new FakePersistence(_rig.Service), CHAR_A);
            WaitUntil(() => _lastTask.IsCompleted, "the failed write to complete");
            Assert.IsTrue(_gate.IsHeld(CHAR_A), "The gate stays closed until the result is handled on a tick.");
            DrainNextTick();
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.DatabaseError, _lastTask.Result);
            AssertFailureEffectsOnce(CHAR_A);
            AssertStoredRowUnchanged();
        }

        [Test]
        public void Test_AcCp14_ConcurrencyConflict_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            CharacterRecord loaded = SeedAndLoad();
            Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, loaded.SaveVersion + 5u));
            ExpectError(SERVICE_CONFLICT_LOG);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            BeginLevelUp(new FakePersistence(_rig.Service), CHAR_A);
            WaitUntil(() => _lastTask.IsCompleted, "the conflicting write to complete");
            DrainNextTick();
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.ConcurrencyConflict, _lastTask.Result);
            AssertFailureEffectsOnce(CHAR_A);
            AssertStoredRowUnchanged();
        }

        [Test]
        public void Test_AcCp14_CharacterNotFound_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            SeedAndLoad();
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            BeginLevelUp(new FakePersistence(_rig.Service), CHAR_UNKNOWN);
            WaitUntil(() => _lastTask.IsCompleted, "the write for the unknown character to complete");
            DrainNextTick();
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.CharacterNotFound, _lastTask.Result);
            AssertFailureEffectsOnce(CHAR_UNKNOWN);
            AssertStoredRowUnchanged();
        }

        [Test]
        public void Test_AcCp14_WriteFaults_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(null);
            ExpectCoordinatorAlert(CAUSE_FAULTED);

            BeginLevelUp(persistence, CHAR_A);
            persistence.Source.SetException(new InvalidOperationException("db down"));
            DrainNextTick();
            DrainNextTick();

            AssertFailureEffectsOnce(CHAR_A);
        }

        [Test]
        public void Test_AcCp14_WriteCancelled_FromLevel10_RevertsDisconnectsAlertsPreservesOnce()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(null);
            ExpectCoordinatorAlert(CAUSE_CANCELLED);

            BeginLevelUp(persistence, CHAR_A);
            persistence.Source.SetCanceled();
            DrainNextTick();
            DrainNextTick();

            AssertFailureEffectsOnce(CHAR_A);
        }

        [Test]
        public void Test_AcCp14_WatchdogTimeout_FromLevel10_RevertsDisconnectsAlertsPreservesOnceAndCancelsToken()
        {
            SeedAndLoad();
            Rebuild(WATCHDOG_TICKS);
            var persistence = new FakePersistence(null);
            LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
            ExpectCoordinatorAlert(CAUSE_WATCHDOG);

            BeginLevelUp(persistence, CHAR_A);
            _tick = WATCHDOG_TICKS - 1;
            DrainNextTick();
            DrainNextTick();

            AssertFailureEffectsOnce(CHAR_A);
            Assert.IsTrue(persistence.Calls[0].Token.IsCancellationRequested, "The watchdog must cancel the write's token.");
        }

        // ---------------------------------------------------------------- AC-CP-17

        [Test]
        public void Test_AcCp17_TwoLevelUpsInOneOutcome_OneLevelUpWriteAndStoredLevelIsTheFinalOne()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_rig.Service);

            BeginLevelUp(persistence, CHAR_A, levelUps: 2);
            WaitUntil(() => _lastTask.IsCompleted, "the write to complete");
            DrainNextTick();

            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.AreEqual(IrreversibleOutcomeTrigger.LevelUp, persistence.Calls[0].Trigger);
            Assert.AreEqual(CharacterSaveResult.Success, _lastTask.Result);
            Assert.AreEqual(START_LEVEL + 2, Stored().Level);
            Assert.AreEqual(POST_LEVEL_UP_EXPERIENCE, Stored().Experience);
            Assert.AreEqual(1, _deliveries);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- gate

        [Test]
        public void Test_Gate_ClosedFromBeginUntilHandled_OpenAfterSuccess()
        {
            var persistence = new FakePersistence(null);

            BeginLevelUp(persistence, CHAR_A);
            bool heldAfterBegin = _gate.IsHeld(CHAR_A);
            DrainNextTick();
            bool heldWhileWriting = _gate.IsHeld(CHAR_A);
            persistence.Source.SetResult(CharacterSaveResult.Success);
            DrainNextTick();

            Assert.IsTrue(heldAfterBegin);
            Assert.IsTrue(heldWhileWriting);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, _deliveries);
            Assert.AreEqual(0, _queue.InFlightCount);
            AssertNoFailureEffects();
        }

        [Test]
        public void Test_Gate_SecondLevelUpWhileWriteInFlight_RejectedAndGateOpenAfterFirstFailure()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(null);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            BeginLevelUp(persistence, CHAR_A);
            IrreversibleOutcomeBeginResult second = BeginLevelUp(persistence, CHAR_A);
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedWriteInFlight, second);
            Assert.AreEqual(1, persistence.Calls.Count);
            AssertFailureEffectsOnce(CHAR_A);
        }

        // ---------------------------------------------------------------- edge

        [Test]
        public void Test_Edge_WriteAlreadyCompleteWhenTracked_HandledByTheNextDrainNotInsideBegin()
        {
            var persistence = new FakePersistence(null);
            persistence.Source.SetResult(CharacterSaveResult.Success);

            IrreversibleOutcomeBeginResult result = BeginLevelUp(persistence, CHAR_A);
            int deliveriesAfterBegin = _deliveries;
            bool heldAfterBegin = _gate.IsHeld(CHAR_A);
            DrainNextTick();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(0, deliveriesAfterBegin);
            Assert.IsTrue(heldAfterBegin);
            Assert.AreEqual(1, _deliveries);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            AssertNoFailureEffects();
        }

        // ---------------------------------------------------------------- after the failure

        [Test]
        public void Test_AfterFailure_LaterSessionTtlExpirySave_WritesTheRolledBackLevelAndExperience()
        {
            SeedAndLoad();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
            BeginLevelUp(new FakePersistence(_rig.Service), CHAR_A);
            WaitUntil(() => _lastTask.IsCompleted, "the failed write to complete");
            DrainNextTick();
            AssertFailureEffectsOnce(CHAR_A);
            ExpectWarning(REREAD_WARNING);

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.SessionTTLExpiry, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord stored = Stored();
            Assert.AreEqual(START_LEVEL, stored.Level);
            Assert.AreEqual(START_EXPERIENCE, stored.Experience);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

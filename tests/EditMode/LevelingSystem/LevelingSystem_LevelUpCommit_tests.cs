using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterPersistence;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.LevelingSystem
{
    /// <summary>
    /// Leveling Story 016: the level-up behind the irreversible-outcome coordinator, driven through the real
    /// <see cref="LevelingService"/>, <c>CharacterStats.AddExperience</c>, <see cref="CoordinatedLevelUpCommit"/>,
    /// <see cref="IrreversibleOutcomeCoordinator"/>, <see cref="TickCompletionQueue"/>, <see cref="CharacterMutationGate"/>
    /// and the in-memory store. Covers AC-CP-13 / 14 / 17, the same revert for every failure kind, 59 to 60, an XP gain
    /// while the write is in flight, HP clamping, an unresolved session, the immediate default, the later session save
    /// and the no-await source scan. Deterministic: no async tests; only bounded 5 s waits on thread-pool tasks.
    /// </summary>
    [TestFixture]
    internal sealed class LevelingSystem_LevelUpCommit_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);
        private const uint CLIENT_ID = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int WAIT_MS = 5000;
        private const int WATCHDOG_TICKS = 3;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int EXPECTED_TTL_SECONDS = 300;
        private const int START_LEVEL = 10;
        private const int SEED_EXPERIENCE = 0;

        // Threshold table: XpThreshold[i] = i * 100, so the threshold out of Level L is (L + 1) * 100.
        private const int XP_PER_INDEX = 100;
        private const int START_EXPERIENCE = 1050;   // Level 10, threshold 1100
        private const int SINGLE_GAIN = 100;         // 1150: crosses 1100 only (Level 11 needs 1200)
        private const int DOUBLE_GAIN = 200;         // 1250: crosses 1100 and 1200 (Level 12 needs 1300)
        private const int LEVEL_59_EXPERIENCE = 5950; // Level 59, threshold 6000
        private const int CAP_THRESHOLD = 6000;

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string CAUSE_FAULTED = "write faulted";
        private const string CAUSE_CANCELLED = "write was cancelled";
        private const string CAUSE_WATCHDOG = "watchdog timeout";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(LevelUp\) for .* failed";
        private const string SERVICE_CONFLICT_LOG = @"CRITICAL: SaveIrreversibleOutcome \(LevelUp\) for .* conflicted";
        private const string REREAD_WARNING = "re-read the stored SaveVersion";
        private const string UNRESOLVED_LOG = @"\[CoordinatedLevelUpCommit\] Could not resolve the session";

        private static readonly Regex QUEUE_WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");

        public enum FailureKind
        {
            DatabaseError,
            ConcurrencyConflict,
            CharacterNotFound,
            Fault,
            Cancellation,
            WatchdogTimeout,
        }

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

            public Task<CharacterSaveResult> LastTask { get; private set; }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                Calls.Add((id, trigger, ct));
                LastTask = _inner != null ? _inner.SaveIrreversibleOutcome(id, trigger, ct) : Source.Task;
                return LastTask;
            }

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class FakeResolver : ILevelUpSessionResolver
        {
            public bool Succeeds = true;
            public CharacterID CharId = CHAR_A;
            public int Calls;

            public bool TryResolve(EntityID entityId, out uint clientId, out CharacterID charId)
            {
                Calls++;
                clientId = CLIENT_ID;
                charId = CharId;
                return Succeeds;
            }
        }

        /// <summary>Forwards to a real registry; throws on the configured call so a level-up loop can fail part-way.</summary>
        private sealed class ThrowingClassRegistry : IClassRegistry
        {
            private readonly IClassRegistry _inner;
            private int _calls;

            public ThrowingClassRegistry(IClassRegistry inner)
            {
                _inner = inner;
            }

            /// <summary>Makes the <paramref name="n"/>-th call from now throw (1-based); earlier calls, such as the load's, do not count.</summary>
            public void ThrowOnCallFromNow(int n)
            {
                _calls = 0;
                ThrowOnCall = n;
            }

            /// <summary>1-based call number, counted from the last <see cref="ThrowOnCallFromNow"/>, that throws; 0 never throws.</summary>
            public int ThrowOnCall;

            public bool TryGetClass(byte classType, out ClassDefinition definition)
            {
                _calls++;
                if (ThrowOnCall > 0 && _calls == ThrowOnCall)
                    throw new InvalidOperationException("registry failure");
                return _inner.TryGetClass(classType, out definition);
            }
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
        private FakeResolver _resolver;
        private ThrowingClassRegistry _registry;
        private uint _tick;

        private readonly List<int> _levelUps = new List<int>();
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();
        private readonly List<(uint ClientId, int Ttl)> _preserves = new List<(uint, int)>();

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);
            _rig = BuildRig();
            _tick = 0u;
            _levelUps.Clear();
            _disconnects.Clear();
            _preserves.Clear();
            _resolver = new FakeResolver();
            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            Rebuild(LONG_WATCHDOG_TICKS);
            _rig.Leveling.OnLevelUp += args => _levelUps.Add(args.NewLevel);
        }

        private void Rebuild(int watchdogTicks)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
        }

        // Injects the coordinator-backed seam over the given persistence; call after Rebuild.
        private void AttachCoordinatedCommit(ICharacterPersistence persistence)
        {
            _rig.Leveling.AttachLevelUpCommit(new CoordinatedLevelUpCommit(
                _coordinator,
                persistence,
                _resolver,
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => _preserves.Add((id, ttl))));
        }

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
        }

        private Rig BuildRig()
        {
            var rig = new Rig();
            _registry = new ThrowingClassRegistry(CreateClassRegistry());
            rig.Leveling = new LevelingService(BuildXpThresholds(), _registry);
            rig.Stats = CharacterStatsFixture.CreateWithLeveling(rig.Leveling);
            rig.Leveling.AttachCharacterStats(rig.Stats);
            rig.Entity = CharacterStatsFixture.PlayerEntityId;
            rig.Leveling.RegisterPlayerEntity(rig.Entity);
            rig.Leveling.RegisterPlayerClassType(rig.Entity, WARRIOR_CLASS_TYPE);
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
                intelligenceAutoAlloc: 1, // every attribute changes on a level-up, so every revert check is real
                freePointsPerLevel: 1));
            return registry;
        }

        private static int[] BuildXpThresholds()
        {
            var t = new int[62];
            for (int i = 0; i <= 60; i++)
                t[i] = i * XP_PER_INDEX;
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

        private static CharacterRecord MakeSeed(int level)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = level;
            r.Experience = SEED_EXPERIENCE;
            r.HeldFreePoints = 4;
            r.STR = 12;
            r.DEX = 11;
            r.VIT = 13;
            r.INT = 9;
            r.MaxHP = 300;
            r.MaxMP = 150;
            r.AttackPower = 30;
            r.Defense = 20;
            r.MagicDefense = 10;
            r.CurrentHP = 250f;
            r.CurrentMP = 120f;
            r.GoldBalance = 1250u;
            r.GoldVersion = 9u;
            r.LastZoneID = LOADED_ZONE;
            return r;
        }

        // Inserts the seed, loads it into the rig, gives the live character the test Experience, moves the store clock on.
        private void SeedAndLoad(int level = START_LEVEL, int experience = START_EXPERIENCE)
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(level), CancellationToken.None)));
            CharacterLoadResult result = Complete(_rig.Service.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _rig.Service.ApplyLoadedRecord(_rig.Entity, result.Record));
            _storeTime = T2;
            _rig.Stats.SetBaseStat(_rig.Entity, StatID.Experience, experience);
            Assert.AreEqual(level, _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level));
        }

        private CharacterRecord Stored() => Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));

        private static void ExpectError(string pattern) => LogAssert.Expect(LogType.Error, new Regex(pattern));

        private static void ExpectWarning(string pattern) => LogAssert.Expect(LogType.Warning, new Regex(pattern));

        private static void ExpectCoordinatorAlert(string cause) => LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + cause));

        private void AddXp(int amount) => _rig.Stats.AddExperience(_rig.Entity, amount);

        private int Level => _rig.Stats.GetBaseStat(_rig.Entity, StatID.Level);

        private int Experience => _rig.Stats.GetBaseStat(_rig.Entity, StatID.Experience);

        // Level, Experience, the 4 attributes, held free points and the 7 derived stats, in a fixed order.
        private float[] CaptureState()
        {
            EntityID e = _rig.Entity;
            IronGrind.CharacterStats.CharacterStats s = _rig.Stats;
            return new float[]
            {
                s.GetBaseStat(e, StatID.Level),
                s.GetBaseStat(e, StatID.Experience),
                s.GetBaseStat(e, StatID.Strength),
                s.GetBaseStat(e, StatID.Dexterity),
                s.GetBaseStat(e, StatID.Vitality),
                s.GetBaseStat(e, StatID.Intelligence),
                _rig.Leveling.GetHeldFreePoints(e),
                s.GetBaseStat(e, StatID.MaxHP),
                s.GetBaseStat(e, StatID.MaxMP),
                s.GetBaseStat(e, StatID.AttackPower),
                s.GetBaseStat(e, StatID.Defense),
                s.GetBaseStat(e, StatID.MagicDefense),
                s.GetBaseStatFloat(e, StatID.CritChance),
                s.GetBaseStatFloat(e, StatID.AttackSpeedMultiplier),
            };
        }

        // AC-CP-14: no OnLevelUp, state back, one disconnect (Other), one alert, one preserve (300), gate open.
        private void AssertFailureEffectsOnce(float[] expectedState)
        {
            CollectionAssert.AreEqual(expectedState, CaptureState(), "Level, Experience, attributes, held free points and derived stats must be back.");
            Assert.AreEqual(0, _levelUps.Count, "OnLevelUp must not fire.");
            Assert.AreEqual(1, _disconnects.Count);
            Assert.AreEqual(CLIENT_ID, _disconnects[0].ClientId);
            Assert.AreEqual(DisconnectReason.Other, _disconnects[0].Reason);
            Assert.AreEqual(1, _preserves.Count);
            Assert.AreEqual(CLIENT_ID, _preserves[0].ClientId);
            Assert.AreEqual(EXPECTED_TTL_SECONDS, _preserves[0].Ttl);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A), "The gate must be open after the failure was handled.");
            Assert.AreEqual(0, _queue.InFlightCount, "No write may still be tracked.");
            Assert.LessOrEqual(_rig.Stats.GetCurrentHP(_rig.Entity), _rig.Stats.GetBaseStat(_rig.Entity, StatID.MaxHP));
            Assert.LessOrEqual(_rig.Stats.GetCurrentMP(_rig.Entity), _rig.Stats.GetBaseStat(_rig.Entity, StatID.MaxMP));
        }

        // Every captured value must differ while the level-up is applied, so the revert check proves something for each.
        private void AssertEveryValueChanged(float[] before)
        {
            float[] during = CaptureState();
            for (int i = 0; i < before.Length; i++)
                Assert.AreNotEqual(before[i], during[i], "State index " + i + " must change when the level-up is applied.");
        }

        private void AssertNoFailureEffects()
        {
            Assert.AreEqual(0, _disconnects.Count);
            Assert.AreEqual(0, _preserves.Count);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        // ---------------------------------------------------------------- AC-CP-13

        [Test]
        public void Test_AcCp13_HeldWrite_NoOnLevelUpUntilReleasedThenFiresOnNextDrain()
        {
            SeedAndLoad();
            Action release = _store.HoldNext();
            var persistence = new FakePersistence(_rig.Service);
            AttachCoordinatedCommit(persistence);

            try
            {
                AddXp(SINGLE_GAIN);
                DrainNextTick();
                DrainNextTick();

                Assert.AreEqual(START_LEVEL + 1, Level, "The level-up is applied in memory.");
                Assert.AreEqual(0, _levelUps.Count, "No OnLevelUp while the write is held.");
                Assert.IsTrue(_gate.IsHeld(CHAR_A));
                Assert.AreEqual(1, _queue.InFlightCount);
                AssertNoFailureEffects();
            }
            finally
            {
                release();
            }

            WaitUntil(() => persistence.LastTask.IsCompleted, "the held write to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result);
            CollectionAssert.AreEqual(new[] { START_LEVEL + 1 }, _levelUps);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(START_LEVEL + 1, Stored().Level);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-CP-14 and the same revert

        [Test]
        public void Test_AcCp14_Failure_RevertsToPreLevelUpStateAndDisconnectsAlertsPreservesOnce(
            [Values] FailureKind kind)
        {
            SeedAndLoad();
            float[] before = CaptureState();
            FakePersistence persistence;
            switch (kind)
            {
                case FailureKind.DatabaseError:
                    persistence = new FakePersistence(_rig.Service);
                    _store.FailNext(new InvalidOperationException("database down"));
                    ExpectError(SERVICE_FAILED_LOG);
                    ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
                    break;
                case FailureKind.ConcurrencyConflict:
                    persistence = new FakePersistence(_rig.Service);
                    Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, Stored().SaveVersion + 5u));
                    ExpectError(SERVICE_CONFLICT_LOG);
                    ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
                    break;
                case FailureKind.CharacterNotFound:
                    persistence = new FakePersistence(_rig.Service);
                    _resolver.CharId = CHAR_UNKNOWN;
                    ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
                    break;
                case FailureKind.Fault:
                    persistence = new FakePersistence(null);
                    ExpectCoordinatorAlert(CAUSE_FAULTED);
                    break;
                case FailureKind.Cancellation:
                    persistence = new FakePersistence(null);
                    ExpectCoordinatorAlert(CAUSE_CANCELLED);
                    break;
                default:
                    Rebuild(WATCHDOG_TICKS);
                    persistence = new FakePersistence(null);
                    LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
                    ExpectCoordinatorAlert(CAUSE_WATCHDOG);
                    break;
            }

            AttachCoordinatedCommit(persistence);
            AddXp(SINGLE_GAIN);
            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.AreEqual(IrreversibleOutcomeTrigger.LevelUp, persistence.Calls[0].Trigger);
            Assert.AreEqual(START_LEVEL + 1, Level, "Applied in memory before the write result.");
            AssertEveryValueChanged(before);

            switch (kind)
            {
                case FailureKind.DatabaseError:
                case FailureKind.ConcurrencyConflict:
                case FailureKind.CharacterNotFound:
                    WaitUntil(() => persistence.LastTask.IsCompleted, "the failed write to complete");
                    Assert.IsTrue(_gate.IsHeld(_resolver.CharId), "The gate stays closed until the result is handled on a tick.");
                    break;
                case FailureKind.Fault:
                    persistence.Source.SetException(new InvalidOperationException("db down"));
                    break;
                case FailureKind.Cancellation:
                    persistence.Source.SetCanceled();
                    break;
                default:
                    _tick = WATCHDOG_TICKS - 1;
                    break;
            }

            DrainNextTick();
            DrainNextTick();

            AssertFailureEffectsOnce(before);
            Assert.AreEqual(START_EXPERIENCE, Experience, "Experience is the value before the triggering gain.");
            if (kind == FailureKind.WatchdogTimeout)
                Assert.IsTrue(persistence.Calls[0].Token.IsCancellationRequested, "The watchdog must cancel the write's token.");
            CharacterRecord stored = Stored();
            Assert.AreEqual(START_LEVEL, stored.Level, "The failed write persisted nothing.");
            Assert.AreEqual(SEED_EXPERIENCE, stored.Experience);
        }

        // ---------------------------------------------------------------- AC-CP-17

        [Test]
        public void Test_AcCp17_OneGainCrossingTwoThresholds_OneWriteStoredFinalLevelAndTwoOnLevelUpAfterSuccess()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_rig.Service);
            AttachCoordinatedCommit(persistence);

            AddXp(DOUBLE_GAIN);
            Assert.AreEqual(START_LEVEL + 2, Level);
            Assert.AreEqual(0, _levelUps.Count, "Nothing is broadcast before the write succeeds.");
            WaitUntil(() => persistence.LastTask.IsCompleted, "the write to complete");
            DrainNextTick();

            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.AreEqual(IrreversibleOutcomeTrigger.LevelUp, persistence.Calls[0].Trigger);
            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result);
            Assert.AreEqual(START_LEVEL + 2, Stored().Level);
            CollectionAssert.AreEqual(new[] { START_LEVEL + 1, START_LEVEL + 2 }, _levelUps);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- 59 -> 60

        [Test]
        public void Test_FailedLevelUpTo60_RevertsTheCapExperienceClamp()
        {
            SeedAndLoad(59, LEVEL_59_EXPERIENCE);
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            AddXp(SINGLE_GAIN);
            Assert.AreEqual(60, Level);
            Assert.AreEqual(CAP_THRESHOLD, Experience, "CR-2.2a clamps Experience while the write is in flight.");
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            AssertFailureEffectsOnce(before);
            Assert.AreEqual(59, Level);
            Assert.AreEqual(LEVEL_59_EXPERIENCE, Experience);
        }

        // ---------------------------------------------------------------- in flight (decision 2)

        [Test]
        public void Test_GainWhileWriteInFlight_AddsXpStartsNoSecondWriteAndNextGainAfterSuccessStartsTheNextLevelUp()
        {
            SeedAndLoad();
            Action release = _store.HoldNext();
            var persistence = new FakePersistence(_rig.Service);
            AttachCoordinatedCommit(persistence);

            try
            {
                AddXp(SINGLE_GAIN);                       // 1150 -> Level 11, write held
                AddXp(SINGLE_GAIN);                       // 1250 >= 1200, but the gate is closed
                Assert.AreEqual(START_EXPERIENCE + 2 * SINGLE_GAIN, Experience, "The XP of the second gain is added.");
                Assert.AreEqual(START_LEVEL + 1, Level, "No second level-up starts.");
                Assert.AreEqual(1, persistence.Calls.Count, "No second write starts.");
            }
            finally
            {
                release();
            }

            WaitUntil(() => persistence.LastTask.IsCompleted, "the first write to complete");
            DrainNextTick();
            CollectionAssert.AreEqual(new[] { START_LEVEL + 1 }, _levelUps);

            AddXp(1);                                     // 1251 >= 1200 -> Level 12

            Assert.AreEqual(2, persistence.Calls.Count, "The next gain after the gate opened starts the next write.");
            Assert.AreEqual(START_LEVEL + 2, Level);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the second write to complete");
            DrainNextTick();
            CollectionAssert.AreEqual(new[] { START_LEVEL + 1, START_LEVEL + 2 }, _levelUps);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- HP / MP (decision 3)

        [Test]
        public void Test_DamageDuringHold_ThenFailure_CurrentHpIsMinOfDamagedValueAndRevertedMaxHp()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
            const float DAMAGED_HP = 5f;

            AddXp(SINGLE_GAIN);
            _rig.Stats.SetCurrentHP(_rig.Entity, DAMAGED_HP);
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            AssertFailureEffectsOnce(before);
            float revertedMaxHp = _rig.Stats.GetBaseStat(_rig.Entity, StatID.MaxHP);
            Assert.AreEqual(Mathf.Min(DAMAGED_HP, revertedMaxHp), _rig.Stats.GetCurrentHP(_rig.Entity), 0.0001f);
        }

        [Test]
        public void Test_HpMpAboveOldMaximaDuringHold_ThenFailure_ClampedToRevertedMaxima()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            AddXp(SINGLE_GAIN); // CR-2.7 restores HP / MP to the new, higher maxima
            Assert.Greater(_rig.Stats.GetCurrentHP(_rig.Entity), before[7], "Precondition: CurrentHP is above the old MaxHP.");
            Assert.Greater(_rig.Stats.GetCurrentMP(_rig.Entity), before[8], "Precondition: CurrentMP is above the old MaxMP.");
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            AssertFailureEffectsOnce(before);
            Assert.AreEqual(before[7], _rig.Stats.GetCurrentHP(_rig.Entity), 0.0001f, "CurrentHP is clamped to the reverted MaxHP.");
            Assert.AreEqual(before[8], _rig.Stats.GetCurrentMP(_rig.Entity), 0.0001f, "CurrentMP is clamped to the reverted MaxMP.");
        }

        // ---------------------------------------------------------------- more failure paths

        [Test]
        public void Test_DoubleLevelUp_ThenFailure_RevertsBothLevelsAfterOneWrite()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            AddXp(DOUBLE_GAIN);
            Assert.AreEqual(START_LEVEL + 2, Level);
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            Assert.AreEqual(1, persistence.Calls.Count);
            AssertFailureEffectsOnce(before);
            Assert.AreEqual(START_LEVEL, Level);
        }

        [Test]
        public void Test_GainWhileWriteInFlight_ThenFailure_TheInFlightXpIsRemovedToo()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            AddXp(SINGLE_GAIN);
            AddXp(SINGLE_GAIN);
            Assert.AreEqual(START_EXPERIENCE + 2 * SINGLE_GAIN, Experience);
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            Assert.AreEqual(1, persistence.Calls.Count);
            AssertFailureEffectsOnce(before);
            Assert.AreEqual(START_EXPERIENCE, Experience, "Decision 2: the revert also removes the XP gained in flight.");
        }

        [Test]
        public void Test_ApplyThrowsPartWay_PartialLevelUpRevertedGateOpenNoWrite()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            _registry.ThrowOnCallFromNow(2); // the second level of the double level-up

            Assert.Throws<InvalidOperationException>(() => AddXp(DOUBLE_GAIN));

            float[] after = CaptureState();
            for (int i = 0; i < before.Length; i++)
                Assert.AreEqual(i == 1 ? START_EXPERIENCE : before[i], after[i], "State index " + i + " must be back.");
            Assert.IsFalse(_rig.Leveling.IsLevelingUpInProgress);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, persistence.Calls.Count);
            Assert.AreEqual(0, _levelUps.Count);
            AssertNoFailureEffects();
        }

        [Test]
        public void Test_EntityReleasedDuringHold_ThenFailure_RevertSkippedNoGhostEntry()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(null);
            AttachCoordinatedCommit(persistence);
            ExpectWarning(@"Level-up revert skipped: entity=.* is no longer a registered player");
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);

            AddXp(SINGLE_GAIN);
            int heldDuringHold = _rig.Leveling.GetHeldFreePoints(_rig.Entity);
            _rig.Leveling.UnregisterPlayerEntity(_rig.Entity);
            persistence.Source.SetResult(CharacterSaveResult.DatabaseError);
            DrainNextTick();

            Assert.AreEqual(START_LEVEL + 1, Level, "No revert writes for a released entity.");
            Assert.AreEqual(heldDuringHold, _rig.Leveling.GetHeldFreePoints(_rig.Entity));
            Assert.AreEqual(1, _disconnects.Count);
            Assert.AreEqual(1, _preserves.Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- unresolved session (decision 5)

        [Test]
        public void Test_ResolverFails_NoLevelUpNoWriteOneErrorAndXpKept()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            var persistence = new FakePersistence(null);
            _resolver.Succeeds = false;
            AttachCoordinatedCommit(persistence);
            ExpectError(UNRESOLVED_LOG);

            AddXp(SINGLE_GAIN);

            Assert.AreEqual(1, _resolver.Calls);
            Assert.AreEqual(0, persistence.Calls.Count);
            Assert.AreEqual(START_LEVEL, Level);
            Assert.AreEqual(START_EXPERIENCE + SINGLE_GAIN, Experience);
            float[] after = CaptureState();
            for (int i = 2; i < before.Length; i++)
                Assert.AreEqual(before[i], after[i], "State index " + i + " must be unchanged.");
            Assert.AreEqual(0, _levelUps.Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- immediate default (decision 4)

        [Test]
        public void Test_DefaultSeam_OnLevelUpFiresInsideAddExperience()
        {
            SeedAndLoad();

            AddXp(SINGLE_GAIN);

            Assert.AreEqual(START_LEVEL + 1, Level);
            CollectionAssert.AreEqual(new[] { START_LEVEL + 1 }, _levelUps);
            AssertNoFailureEffects();
        }

        [Test]
        public void Test_AttachLevelUpCommit_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _rig.Leveling.AttachLevelUpCommit(null));
        }

        /// <summary>Holds every Commit's steps so a test can apply, then revert, level-ups of different entities.</summary>
        private sealed class HoldingCommit : ILevelUpCommit
        {
            public readonly List<Action> Reverts = new List<Action>();

            public void Commit(EntityID entityId, Func<bool> wouldLevelUp, Func<IReadOnlyList<int>> applyLevelUp,
                Action<IReadOnlyList<int>> deliverLevelUp, Action revertLevelUp)
            {
                if (!wouldLevelUp())
                    return;
                applyLevelUp();
                Reverts.Add(revertLevelUp);
            }
        }

        // Regression: the revert snapshot was once a single field, so B's level-up overwrote A's snapshot.
        [Test]
        public void Test_TwoEntitiesInFlight_RevertOfOneRestoresItsOwnSnapshotAndLeavesTheOtherUntouched()
        {
            var thresholds = new int[] { 0, 0, 100, 999999 };
            var leveling = new LevelingService(thresholds, CreateClassRegistry());
            var stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            var commit = new HoldingCommit();
            leveling.AttachLevelUpCommit(commit);
            EntityID a = CharacterStatsFixture.PlayerEntityId;
            EntityID b = CharacterStatsFixture.PlayerBEntityId;
            foreach (EntityID e in new[] { a, b })
            {
                leveling.RegisterPlayerEntity(e);
                leveling.RegisterPlayerClassType(e, WARRIOR_CLASS_TYPE);
                stats.SetBaseStat(e, StatID.Level, 1);
            }
            stats.SetBaseStat(b, StatID.Strength, 50);

            stats.AddExperience(a, 100);
            stats.AddExperience(b, 100);
            Assert.AreEqual(2, commit.Reverts.Count);
            int bStrengthAfterLevelUp = stats.GetBaseStat(b, StatID.Strength);

            commit.Reverts[0]();

            Assert.AreEqual(1, stats.GetBaseStat(a, StatID.Level), "A reverts to its own pre-level-up Level.");
            Assert.AreEqual(0, stats.GetBaseStat(a, StatID.Experience), "A's Experience is its own pre-gain value.");
            Assert.AreEqual(2, stats.GetBaseStat(b, StatID.Level), "B is untouched by A's revert.");
            Assert.AreEqual(bStrengthAfterLevelUp, stats.GetBaseStat(b, StatID.Strength));
            Assert.AreEqual(100, stats.GetBaseStat(b, StatID.Experience));
        }

        // ---------------------------------------------------------------- after the failure

        [Test]
        public void Test_AfterFailure_LaterSessionTtlExpirySave_WritesTheRolledBackLevelAndExperience()
        {
            SeedAndLoad();
            float[] before = CaptureState();
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
            var persistence = new FakePersistence(_rig.Service);
            AttachCoordinatedCommit(persistence);

            AddXp(SINGLE_GAIN);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the failed write to complete");
            DrainNextTick();
            AssertFailureEffectsOnce(before);
            ExpectWarning(REREAD_WARNING);

            CharacterSaveResult result = Complete(_rig.Service.SaveSession(CHAR_A, SessionEndReason.SessionTTLExpiry, CancellationToken.None));

            Assert.AreEqual(CharacterSaveResult.Success, result);
            CharacterRecord stored = Stored();
            Assert.AreEqual(START_LEVEL, stored.Level);
            Assert.AreEqual(START_EXPERIENCE, stored.Experience);
            Assert.AreEqual((int)before[2], stored.STR);
            Assert.AreEqual((int)before[3], stored.DEX);
            Assert.AreEqual((int)before[4], stored.VIT);
            Assert.AreEqual((int)before[5], stored.INT);
            Assert.AreEqual((int)before[6], stored.HeldFreePoints);
            Assert.AreEqual((int)before[7], stored.MaxHP);
            Assert.AreEqual((int)before[8], stored.MaxMP);
            Assert.AreEqual((int)before[9], stored.AttackPower);
            Assert.AreEqual((int)before[10], stored.Defense);
            Assert.AreEqual((int)before[11], stored.MagicDefense);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- no await

        private static readonly string[] SCANNED_SOURCE_FILES =
        {
            "src/ServerLogic/LevelingSystem/ILevelUpCommit.cs",
            "src/ServerLogic/LevelingSystem/ImmediateLevelUpCommit.cs",
            "src/ServerLogic/LevelingSystem/LevelingService.cs",
            "src/ServerLogic/CharacterPersistence/CoordinatedLevelUpCommit.cs",
            "src/ServerLogic/CharacterPersistence/ILevelUpSessionResolver.cs",
            "src/ServerLogic/CharacterStats/ILevelingService.cs",
            "src/ServerLogic/CharacterStats/CharacterStats.cs",
        };

        private static readonly Regex FORBIDDEN_TOKENS = new Regex(@"\bawait\b|\.Result\b|\.Wait\(|GetResult\(|ContinueWith");

        [Test]
        public void Test_Source_NoAwaitResultWaitGetResultOrContinueWithInTheFilesThisStoryTouches()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            foreach (string relative in SCANNED_SOURCE_FILES)
            {
                string text = File.ReadAllText(Path.Combine(root, relative));
                string code = Regex.Replace(text, @"//[^\r\n]*", string.Empty); // comments (including /// doc comments) are not code
                Match match = FORBIDDEN_TOKENS.Match(code);
                Assert.IsFalse(match.Success, relative + " contains forbidden token '" + match.Value + "'.");
            }
        }
    }
}

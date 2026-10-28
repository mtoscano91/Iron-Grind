using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LevelingSystem;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterPersistence;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.EquipmentSystem;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// Enhancement Story 011: the commit orchestration (<see cref="EnhancementCommitOrchestrator"/>) driven through the
    /// real <see cref="EnhancementService"/>, <see cref="InventoryService"/>, <see cref="IrreversibleOutcomeCoordinator"/>,
    /// <see cref="TickCompletionQueue"/>, <see cref="CharacterMutationGate"/>, <see cref="CharacterPersistenceService"/>
    /// and the in-memory store (decision D4). Covers AC-ENH-13, AC-ENH-23, AC-ENH-34, AC-ENH-35, EC-ENH-2, the D1
    /// rejection, the D2 seam (with the handler test in the ClientMessages file) and the no-await scan.
    /// Deterministic: a scripted random source, held writes, the queue's tick-based watchdog; the only waits are
    /// bounded 5 s waits on thread-pool tasks.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_CommitOrchestration_Integration_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID CHAR_UNKNOWN = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);

        // The client and the account share one id so the coordinator's "clientId=7" alert and the state machine agree.
        private const uint CLIENT_ID = 7u;
        private const uint ACCOUNT = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int WAIT_MS = 5000;
        private const int WATCHDOG_TICKS = 3;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int EXPECTED_TTL_SECONDS = 300;
        private const int START_LEVEL = 10;
        private const int CAP_THRESHOLD = 300;
        private const uint CONNECT_TICK = 100u;
        private const uint TIMEOUT_TICK = 300u;
        private const uint HEARTBEAT_TIMEOUT_TICKS = 60u;
        private const uint CONNECTING_TIMEOUT_TICKS = 200u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const int STACK_LIMIT_CONSUMABLE = 99;
        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_THREE = 3;
        private const int SCROLLS_AT_START = 3;

        // Draws. P_s[2] = 0.85: DRAW_SUCCESS succeeds, DRAW_DESTRUCTION destroys at level 2.
        private const double DRAW_SUCCESS = 0.00;
        private const double DRAW_DESTRUCTION = 0.90;

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string CAUSE_FAULTED = "write faulted";
        private const string CAUSE_CANCELLED = "write was cancelled";
        private const string CAUSE_WATCHDOG = "watchdog timeout";
        private const string CAUSE_DID_NOT_START = "write did not start";
        private const string WRITE_FAILED_LOG = @"\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(EnhancementResult\) for .* failed";
        private const string SERVICE_CONFLICT_LOG = @"CRITICAL: SaveIrreversibleOutcome \(EnhancementResult\) for .* conflicted";

        private static readonly Regex QUEUE_WATCHDOG_ALERT = new Regex("CRITICAL infrastructure alert");

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);

        public enum FailureKind
        {
            DatabaseError,
            ConcurrencyConflict,
            CharacterNotFound,
            Fault,
            Cancellation,
            WatchdogTimeout,
        }

        /// <summary>
        /// A persistence fake: forwards to the real service (or, with no inner service, returns a task the test completes),
        /// records every <c>SaveIrreversibleOutcome</c> call, and can throw synchronously or redirect the call to another character.
        /// </summary>
        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            private readonly CharacterPersistenceService _inner;

            public FakePersistence(CharacterPersistenceService inner)
            {
                _inner = inner;
            }

            public TaskCompletionSource<CharacterSaveResult> Source { get; } =
                new TaskCompletionSource<CharacterSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<(CharacterID Id, IrreversibleOutcomeTrigger Trigger, CancellationToken Token)> Calls { get; } =
                new List<(CharacterID, IrreversibleOutcomeTrigger, CancellationToken)>();

            public Task<CharacterSaveResult> LastTask { get; private set; }

            public Task<CharacterSaveResult> LastSessionTask { get; private set; }

            public Exception ThrowOnSave { get; set; }

            public CharacterID? RedirectTo { get; set; }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                Calls.Add((id, trigger, ct));
                if (ThrowOnSave != null)
                {
                    throw ThrowOnSave;
                }

                LastTask = _inner != null ? _inner.SaveIrreversibleOutcome(RedirectTo ?? id, trigger, ct) : Source.Task;
                return LastTask;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
            {
                LastSessionTask = _inner.SaveSession(id, reason, ct);
                return LastSessionTask;
            }

            public bool ReleaseSession(CharacterID id, out EntityID entityId) => _inner.ReleaseSession(id, out entityId);

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        private DateTime _storeTime;
        private InMemoryCharacterRecordStore _store;
        private StubItemDatabase _itemDatabase;
        private List<ItemDefinition> _definitions;
        private InventoryService _inventory;
        private RecordingInventoryDecorator _recording;
        private StubNpcSessions _sessions;
        private ScriptedRandomProvider _random;
        private EnhancementService _service;

        private LevelingService _leveling;
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _entity;
        private CharacterPersistenceService _persistenceService;

        private TickCompletionQueue _queue;
        private CharacterMutationGate _gate;
        private NetworkTestObserver _observer;
        private IrreversibleOutcomeCoordinator _coordinator;
        private uint _tick;
        private Action _releaseWrite;

        private readonly List<string> _order = new List<string>();
        private readonly List<EnhancementAttemptResult> _deliveries = new List<EnhancementAttemptResult>();
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();
        private readonly List<(uint ClientId, int Ttl)> _preserves = new List<(uint, int)>();
        private int _acks;
        private int _successEvents;
        private int _destructionEvents;

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);

            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(BRONZE_SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SCROLL_ID, "Iron Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Iron)),
            };
            _itemDatabase = new StubItemDatabase();
            foreach (ItemDefinition definition in _definitions)
            {
                _itemDatabase.Add(definition);
            }

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _recording = new RecordingInventoryDecorator(_inventory);
            _sessions = new StubNpcSessions();
            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_recording, _itemDatabase, EnhancementConfig.Default, _sessions, _random);

            BuildPersistenceRig();

            _tick = 0u;
            _connection = new ConnectionStateMachine();
            _releaseWrite = null;
            _order.Clear();
            _deliveries.Clear();
            _disconnects.Clear();
            _preserves.Clear();
            _acks = 0;
            _successEvents = 0;
            _destructionEvents = 0;
            _service.OnEnhancementSuccess += _ =>
            {
                _successEvents++;
                _order.Add("success-event");
            };
            _service.OnEnhancementDestruction += _ =>
            {
                _destructionEvents++;
                _order.Add("destruction-event");
            };

            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            Rebuild(LONG_WATCHDOG_TICKS);
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseWrite(); // a held write must never outlive its test
            foreach (ItemDefinition definition in _definitions)
            {
                Object.DestroyImmediate(definition);
            }
        }

        private void BuildPersistenceRig()
        {
            _leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _persistenceService = new CharacterPersistenceService(
                _store,
                _stats,
                _leveling,
                _inventory,
                new CurrencySystem(), new FakeEquipmentService(),
                new FakeZoneLocator(),
                new CharacterWriteQueue(),
                () => T2,
                TimeSpan.FromSeconds(5));
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

        private void Rebuild(int watchdogTicks)
        {
            _queue = new TickCompletionQueue(watchdogTicks);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);
        }

        private EnhancementCommitOrchestrator BuildOrchestrator(ICharacterPersistence persistence)
        {
            return new EnhancementCommitOrchestrator(
                _coordinator,
                _service,
                persistence,
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => _preserves.Add((id, ttl)));
        }

        private void DrainNextTick()
        {
            _tick++;
            _queue.Drain(_tick);
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

        // Seed record: a level-10 warrior holding a level-2 Bronze Sword in slot 0 and three Bronze scrolls in slot 1.
        private CharacterRecord MakeSeed()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = START_LEVEL;
            r.Experience = 0;
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
            r.InventorySlots[ITEM_SLOT] = BronzeSwordId;
            r.InventoryItemCounts[ITEM_SLOT] = 1;
            r.InventoryEnhancementLevels[ITEM_SLOT] = LEVEL_TWO;
            r.InventorySlots[SCROLL_SLOT] = BronzeScrollId;
            r.InventoryItemCounts[SCROLL_SLOT] = SCROLLS_AT_START;
            return r;
        }

        // Inserts the seed, loads it (which fills the bag) and moves the store clock on to T2.
        private void SeedAndLoad()
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterLoadResult result = Complete(_persistenceService.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _persistenceService.ApplyLoadedRecord(_entity, result.Record));
            _storeTime = T2;
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "Arrange: the bag must be loaded.");
        }

        private CharacterRecord Stored() => Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));

        // Holds the next store operation (the enhancement write). Released by ReleaseWrite or, at the latest, by TearDown.
        private void HoldWrite()
        {
            _releaseWrite = _store.HoldNext();
        }

        private void ReleaseWrite()
        {
            Action release = _releaseWrite;
            _releaseWrite = null;
            release?.Invoke();
        }

        private IrreversibleOutcomeBeginResult RunBegin(
            EnhancementCommitOrchestrator orchestrator, out EnhancementResultCode rejection, Action acknowledge = null)
        {
            return orchestrator.Begin(
                CLIENT_ID,
                CHAR_A,
                ITEM_SLOT,
                SCROLL_SLOT,
                acknowledge ?? (() =>
                {
                    _acks++;
                    _order.Add("ack");
                }),
                result =>
                {
                    _deliveries.Add(result);
                    _order.Add("delivered");
                },
                out rejection);
        }

        private static void ExpectError(string pattern) => LogAssert.Expect(LogType.Error, new Regex(pattern));

        private static void ExpectCoordinatorAlert(string cause) => LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + cause));

        private void AssertBagBeforeAttempt()
        {
            InventorySlot sword = _inventory.GetSlot(CHAR_A, ITEM_SLOT);
            Assert.AreEqual(BronzeSwordId, sword.ItemId, "The sword is back in its slot.");
            Assert.AreEqual(LEVEL_TWO, sword.EnhancementLevel, "The sword is back at its previous level.");
            InventorySlot scrolls = _inventory.GetSlot(CHAR_A, SCROLL_SLOT);
            Assert.AreEqual(BronzeScrollId, scrolls.ItemId);
            Assert.AreEqual(SCROLLS_AT_START, scrolls.Quantity, "The scroll is back.");
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT), "The item slot is unlocked.");
        }

        private void AssertNothingAnnounced()
        {
            Assert.AreEqual(0, _deliveries.Count, "The delivery callback must not be called.");
            Assert.AreEqual(0, _successEvents, "No OnEnhancementSuccess.");
            Assert.AreEqual(0, _destructionEvents, "No OnEnhancementDestruction.");
        }

        private void AssertNoFailureEffects()
        {
            Assert.AreEqual(0, _disconnects.Count);
            Assert.AreEqual(0, _preserves.Count);
            Assert.AreEqual(0, _observer.CriticalInfrastructureAlertFiredCalls.Count);
        }

        // AC-ENH-23 / AC-ENH-34: rolled back once, nothing announced, one disconnect (Other), one preserve (300), one alert, gate open.
        private void AssertFailureEffectsOnce(FakePersistence persistence)
        {
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A), "The attempt must have ended.");
            AssertNothingAnnounced();
            Assert.AreEqual(1, _disconnects.Count);
            Assert.AreEqual(CLIENT_ID, _disconnects[0].ClientId);
            Assert.AreEqual(DisconnectReason.Other, _disconnects[0].Reason);
            Assert.AreEqual(1, _preserves.Count);
            Assert.AreEqual(CLIENT_ID, _preserves[0].ClientId);
            Assert.AreEqual(EXPECTED_TTL_SECONDS, _preserves[0].Ttl);
            Assert.AreEqual(1, _observer.CriticalInfrastructureAlertFiredCalls.Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A), "The gate must be open after the failure was handled.");
            Assert.AreEqual(0, _queue.InFlightCount, "No write may still be tracked.");
            Assert.AreEqual(1, persistence.Calls.Count, "CR-NET-5.5: exactly one write, no retry.");
            Assert.AreEqual(IrreversibleOutcomeTrigger.EnhancementResult, persistence.Calls[0].Trigger);
        }

        // ---------------------------------------------------------------- AC-ENH-35: one commit per attempt

        [Test]
        public void Commit_PendingAttempt_MakesOneEnhancementResultWriteAndHoldsTheGateUntilTheDrain()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            HoldWrite();

            IrreversibleOutcomeBeginResult result = RunBegin(orchestrator, out EnhancementResultCode rejection);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(EnhancementResultCode.Success, rejection, "No rejection code on a started attempt.");
            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.AreEqual(CHAR_A, persistence.Calls[0].Id);
            Assert.AreEqual(IrreversibleOutcomeTrigger.EnhancementResult, persistence.Calls[0].Trigger);
            Assert.AreEqual(1, _acks);
            Assert.AreEqual(1, _random.DoubleDrawCount);
            Assert.IsTrue(_gate.IsHeld(CHAR_A));

            ReleaseWrite();
            WaitUntil(() => persistence.LastTask.IsCompleted, "the held write to complete");
            Assert.IsTrue(_gate.IsHeld(CHAR_A), "The gate stays closed until the drain that handles the result.");
            DrainNextTick();

            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(1, persistence.Calls.Count);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- D1: rejected attempt

        [Test]
        public void Commit_TierMismatchRejection_ReturnsRejectedInvalidRequestWithCodeAndNoAckWriteDrawOrGateChange()
        {
            SeedAndLoad();
            _inventory.SeedSlotForTesting(CHAR_A, SCROLL_SLOT, new ItemID(IRON_SCROLL_ID), SCROLLS_AT_START);
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            int drawsBefore = _random.DrawCount;

            IrreversibleOutcomeBeginResult result = RunBegin(orchestrator, out EnhancementResultCode rejection);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedInvalidRequest, result);
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, rejection);
            Assert.AreEqual(0, _acks, "A rejection never follows an acknowledgment.");
            Assert.AreEqual(0, persistence.Calls.Count, "No write.");
            Assert.AreEqual(drawsBefore, _random.DrawCount, "No draw.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A), "No gate change.");
            Assert.AreEqual(0, _queue.InFlightCount);
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            Assert.AreEqual(SCROLLS_AT_START, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            AssertNothingAnnounced();
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_ScrollConsumptionFailsAtStepFour_ReturnsRejectedInvalidRequestWithScrollNotFoundAndChangesNothing()
        {
            SeedAndLoad();
            _recording.FailConsumeItem = true;
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            int drawsBefore = _random.DrawCount;

            IrreversibleOutcomeBeginResult result = RunBegin(orchestrator, out EnhancementResultCode rejection);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedInvalidRequest, result);
            Assert.AreEqual(EnhancementResultCode.RejectedScrollNotFound, rejection);
            Assert.AreEqual(0, _acks);
            Assert.AreEqual(0, persistence.Calls.Count);
            Assert.AreEqual(drawsBefore, _random.DrawCount);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT), "The step-4 rejection unlocks the item slot.");
            Assert.AreEqual(SCROLLS_AT_START, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "The sword level is unchanged.");
            AssertNothingAnnounced();
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-ENH-35: the committed record

        [Test]
        public void Commit_SuccessfulWrite_StoredRecordHoldsTheEnhancedItemAndOneScrollFewer()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);

            RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the write to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result);
            CharacterRecord stored = Stored();
            Assert.AreEqual(BronzeSwordId, stored.InventorySlots[ITEM_SLOT]);
            Assert.AreEqual(LEVEL_THREE, stored.InventoryEnhancementLevels[ITEM_SLOT]);
            Assert.AreEqual(SCROLLS_AT_START - 1, stored.InventoryItemCounts[SCROLL_SLOT]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_SuccessfulDestruction_StoredRecordHasNoItemInTheSlotAndOneScrollFewer()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_DESTRUCTION);

            RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the write to complete");
            DrainNextTick();

            CharacterRecord stored = Stored();
            Assert.AreEqual(ItemID.Invalid, stored.InventorySlots[ITEM_SLOT], "The destroyed item is gone from the record.");
            Assert.AreEqual(SCROLLS_AT_START - 1, stored.InventoryItemCounts[SCROLL_SLOT]);
            Assert.AreEqual(1, _destructionEvents);
            Assert.AreEqual(1, _deliveries.Count);
            Assert.AreEqual(EnhancementOutcome.Destruction, _deliveries[0].Outcome);
            Assert.AreEqual(0, _deliveries[0].NewLevel);
            CollectionAssert.AreEqual(new[] { "ack", "destruction-event", "delivered" }, _order);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- success path

        [Test]
        public void Commit_HeldWrite_NothingAnnouncedUntilReleasedThenCompletesOnceAndDeliversOnceAfterTheEvent()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            HoldWrite();

            RunBegin(orchestrator, out _);
            DrainNextTick();
            DrainNextTick();

            Assert.IsTrue(_service.IsAttemptInProgress(CHAR_A));
            Assert.IsTrue(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT));
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "Applied in memory.");
            AssertNothingAnnounced();
            Assert.AreEqual(1, _queue.InFlightCount);
            AssertNoFailureEffects();

            ReleaseWrite();
            WaitUntil(() => persistence.LastTask.IsCompleted, "the held write to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result);
            Assert.AreEqual(1, _deliveries.Count);
            Assert.AreEqual(EnhancementOutcome.Success, _deliveries[0].Outcome);
            Assert.AreEqual(LEVEL_THREE, _deliveries[0].NewLevel);
            Assert.AreEqual(EnhancementResultCode.Success, _deliveries[0].ResultCode);
            CollectionAssert.AreEqual(new[] { "ack", "success-event", "delivered" }, _order, "The event precedes the delivery.");
            Assert.AreEqual(1, _successEvents);
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT));
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-ENH-23 / AC-ENH-34: each failure

        [Test]
        public void Commit_Failure_RollsBackOnceDisconnectsAlertsAndPreservesOnce([Values] FailureKind kind)
        {
            SeedAndLoad();
            FakePersistence persistence;
            switch (kind)
            {
                case FailureKind.DatabaseError:
                    persistence = new FakePersistence(_persistenceService);
                    _store.FailNext(new InvalidOperationException("database down"));
                    ExpectError(SERVICE_FAILED_LOG);
                    break;
                case FailureKind.ConcurrencyConflict:
                    persistence = new FakePersistence(_persistenceService);
                    Assert.IsTrue(_store.OverwriteSaveVersion(CHAR_A, Stored().SaveVersion + 5u));
                    ExpectError(SERVICE_CONFLICT_LOG);
                    break;
                case FailureKind.CharacterNotFound:
                    persistence = new FakePersistence(_persistenceService) { RedirectTo = CHAR_UNKNOWN };
                    break;
                case FailureKind.Fault:
                case FailureKind.Cancellation:
                    persistence = new FakePersistence(null);
                    break;
                default:
                    Rebuild(WATCHDOG_TICKS);
                    persistence = new FakePersistence(null);
                    LogAssert.Expect(LogType.Error, QUEUE_WATCHDOG_ALERT);
                    break;
            }

            // Logged in this order at the drain: the rollback, then the coordinator's alert.
            ExpectError(WRITE_FAILED_LOG);
            ExpectCoordinatorAlert(
                kind == FailureKind.Fault ? CAUSE_FAULTED
                : kind == FailureKind.Cancellation ? CAUSE_CANCELLED
                : kind == FailureKind.WatchdogTimeout ? CAUSE_WATCHDOG
                : CAUSE_NON_SUCCESS);

            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            IrreversibleOutcomeBeginResult result = RunBegin(orchestrator, out _);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "Applied in memory before the write result.");

            switch (kind)
            {
                case FailureKind.DatabaseError:
                case FailureKind.ConcurrencyConflict:
                case FailureKind.CharacterNotFound:
                    WaitUntil(() => persistence.LastTask.IsCompleted, "the failed write to complete");
                    Assert.IsTrue(_gate.IsHeld(CHAR_A), "The gate stays closed until the result is handled on a tick.");
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

            AssertBagBeforeAttempt();
            AssertFailureEffectsOnce(persistence);
            DrainNextTick();
            Assert.AreEqual(1, persistence.Calls.Count, "CR-NET-5.5: still exactly one write after a further tick.");
            if (kind == FailureKind.WatchdogTimeout)
            {
                Assert.IsTrue(persistence.Calls[0].Token.IsCancellationRequested, "The watchdog must cancel the write's token.");
            }

            CharacterRecord stored = Stored();
            Assert.AreEqual(LEVEL_TWO, stored.InventoryEnhancementLevels[ITEM_SLOT], "The failed write persisted nothing.");
            Assert.AreEqual(SCROLLS_AT_START, stored.InventoryItemCounts[SCROLL_SLOT]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_FailedWriteAfterDestruction_ReinsertsTheItemAndRestoresTheScroll()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(null);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            ExpectError(WRITE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_FAULTED);
            _random.EnqueueDouble(DRAW_DESTRUCTION);

            RunBegin(orchestrator, out _);
            Assert.AreEqual(ItemID.Invalid, _inventory.GetSlot(CHAR_A, ITEM_SLOT).ItemId, "Destroyed in memory before the write result.");
            persistence.Source.SetException(new InvalidOperationException("db down"));
            DrainNextTick();
            DrainNextTick();

            Assert.IsTrue(_inventory.HasItem(CHAR_A, BronzeSwordId), "The destroyed item is back in the bag.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            Assert.AreEqual(SCROLLS_AT_START, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            AssertFailureEffectsOnce(persistence);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- write fails to start

        [Test]
        public void Commit_WriteThrowsSynchronously_ReturnsPersistenceFailedWithTheSameRollbackDisconnectAndPreserveOnce()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService) { ThrowOnSave = new InvalidOperationException("cannot start") };
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            ExpectError(WRITE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_DID_NOT_START);
            _random.EnqueueDouble(DRAW_SUCCESS);

            IrreversibleOutcomeBeginResult result = RunBegin(orchestrator, out EnhancementResultCode rejection);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.PersistenceFailed, result);
            Assert.AreEqual(EnhancementResultCode.Success, rejection);
            AssertBagBeforeAttempt();
            AssertFailureEffectsOnce(persistence);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- throw after BeginAttempt

        [Test]
        public void Commit_AcknowledgmentThrows_RollsTheAttemptBackStartsNoWriteOpensTheGateAndRethrows()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            ExpectError(WRITE_FAILED_LOG);
            _random.EnqueueDouble(DRAW_SUCCESS);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => RunBegin(orchestrator, out _, () => throw new InvalidOperationException("ack failed")));

            Assert.AreEqual("ack failed", thrown.Message, "The caller's exception reaches the caller.");
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            AssertBagBeforeAttempt();
            Assert.AreEqual(0, persistence.Calls.Count, "No write started.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, _queue.InFlightCount);
            AssertNothingAnnounced();
            Assert.AreEqual(0, _disconnects.Count, "The failure protocol did not run: nothing was written.");
            Assert.AreEqual(0, _preserves.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- AC-ENH-13 / EC-ENH-2 (D5)

        private void ConnectAccount()
        {
            var stateMachine = _connection;
            stateMachine.EnterConnecting(ACCOUNT, CONNECT_TICK);
            stateMachine.CompleteAuthSuccess(ACCOUNT, CHAR_A.RawValue, CONNECT_TICK);
            Assert.IsTrue(stateMachine.TryGetSessionState(ACCOUNT, out SessionState state));
            Assert.AreEqual(SessionState.Connected, state);
        }

        private ConnectionStateMachine _connection;

        [Test]
        public void Commit_DisconnectWhileTheWriteIsInFlight_DoesNotAbortIt_OutcomeStandsOnSuccess()
        {
            SeedAndLoad();
            ConnectAccount();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            HoldWrite();
            RunBegin(orchestrator, out _);

            _connection.EvaluateTimeouts(TIMEOUT_TICK, HEARTBEAT_TIMEOUT_TICKS, CONNECTING_TIMEOUT_TICKS);
            Assert.IsTrue(_connection.TryGetSessionState(ACCOUNT, out SessionState state));
            Assert.AreEqual(SessionState.Disconnected_SessionActive, state);
            Assert.IsTrue(_service.IsAttemptInProgress(CHAR_A), "The disconnect does not abort the attempt.");
            Assert.AreEqual(1, _queue.InFlightCount);

            ReleaseWrite();
            WaitUntil(() => persistence.LastTask.IsCompleted, "the held write to complete");
            DrainNextTick();

            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            Assert.AreEqual(SCROLLS_AT_START - 1, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT));
            Assert.AreEqual(1, _deliveries.Count);
            Assert.AreEqual(LEVEL_THREE, Stored().InventoryEnhancementLevels[ITEM_SLOT]);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_CleanLogoutStartedWhileTheWriteIsInFlight_CompleteAttemptRunsBeforeTheReleaseAndTheStoredRecordHoldsTheOutcome()
        {
            SeedAndLoad();
            ConnectAccount();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            var results = new List<CharacterSaveResult>();
            var ends = new SessionEndCoordinator(
                persistence,
                persistence,
                _queue,
                _gate,
                _connection,
                _inventory,
                new RecordingEquipmentService(),
                characterId => { },
                entity => _order.Add("release"),
                characterId => { },
                (characterId, type) => { });
            _random.EnqueueDouble(DRAW_SUCCESS);
            HoldWrite();
            RunBegin(orchestrator, out _);

            Assert.IsTrue(ends.BeginCleanLogout(ACCOUNT, r =>
            {
                results.Add(r);
                _order.Add("logout-result");
            }));
            Assert.AreEqual(1, persistence.Calls.Count);
            Assert.IsNull(persistence.LastSessionTask, "No logout save while the mutation gate is held (Story 017).");
            ReleaseWrite();
            WaitUntil(() => persistence.LastTask.IsCompleted, "the enhancement write to complete");
            DrainNextTick(); // delivers, opens the gate; the logout save starts then
            Assert.IsNotNull(persistence.LastSessionTask, "The logout save starts when the gate opens.");
            WaitUntil(() => persistence.LastSessionTask.IsCompleted, "the logout save to complete");
            DrainNextTick();

            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result, "The enhancement write committed.");
            CollectionAssert.AreEqual(new[] { CharacterSaveResult.Success }, results);
            CollectionAssert.AreEqual(
                new[] { "ack", "success-event", "delivered", "release", "logout-result" },
                _order,
                "CompleteAttempt and the delivery run before the logout save starts; the release follows the logout save.");
            Assert.AreEqual(1, _deliveries.Count);
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            Assert.IsFalse(_connection.TryGetSessionState(ACCOUNT, out _), "The logout released the session (the inventory is unregistered too).");
            CharacterRecord stored = Stored();
            Assert.AreEqual(BronzeSwordId, stored.InventorySlots[ITEM_SLOT]);
            Assert.AreEqual(LEVEL_THREE, stored.InventoryEnhancementLevels[ITEM_SLOT], "The stored record holds the outcome.");
            Assert.AreEqual(SCROLLS_AT_START - 1, stored.InventoryItemCounts[SCROLL_SLOT]);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- review additions

        [Test]
        public void Commit_SecondBeginWhileTheFirstWriteIsHeld_RejectedWriteInFlightAndTheFirstAttemptStands()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            HoldWrite();
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, RunBegin(orchestrator, out _));

            IrreversibleOutcomeBeginResult second = RunBegin(orchestrator, out EnhancementResultCode rejection);

            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedWriteInFlight, second);
            Assert.AreEqual(EnhancementResultCode.Success, rejection);
            Assert.AreEqual(1, _acks, "No second acknowledgment.");
            Assert.AreEqual(SCROLLS_AT_START - 1, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity, "The scroll was consumed once.");
            Assert.AreEqual(1, _random.DoubleDrawCount, "One draw.");
            Assert.AreEqual(1, persistence.Calls.Count, "One write.");

            ReleaseWrite();
            WaitUntil(() => persistence.LastTask.IsCompleted, "the held write to complete");
            DrainNextTick();

            Assert.AreEqual(1, _deliveries.Count);
            Assert.AreEqual(LEVEL_THREE, _deliveries[0].NewLevel);
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_ConstructorWithANullArgument_ThrowsArgumentNullException([NUnit.Framework.Range(0, 4)] int nullIndex)
        {
            var persistence = new FakePersistence(_persistenceService);
            object[] args = { _coordinator, _service, persistence, (Action<uint, DisconnectReason>)((id, r) => { }), (Action<uint, int>)((id, t) => { }) };
            args[nullIndex] = null;

            Assert.Throws<ArgumentNullException>(() => new EnhancementCommitOrchestrator(
                (IIrreversibleOutcomeCoordinator)args[0],
                (EnhancementService)args[1],
                (ICharacterPersistence)args[2],
                (Action<uint, DisconnectReason>)args[3],
                (Action<uint, int>)args[4]));
        }

        [Test]
        public void Commit_BeginWithANullCallback_ThrowsArgumentNullExceptionAndChangesNothing([Values] bool nullAcknowledgment)
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);

            Assert.Throws<ArgumentNullException>(() => orchestrator.Begin(
                CLIENT_ID,
                CHAR_A,
                ITEM_SLOT,
                SCROLL_SLOT,
                nullAcknowledgment ? null : (Action)(() => _acks++),
                nullAcknowledgment ? (Action<EnhancementAttemptResult>)(r => _deliveries.Add(r)) : null,
                out _));

            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            AssertBagBeforeAttempt();
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            Assert.AreEqual(0, persistence.Calls.Count);
            Assert.AreEqual(0, _random.DrawCount, "No draw.");
            Assert.AreEqual(0, _acks);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_SecondBeginAfterACompletedAttempt_StartsCleanlyAndCompletes()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _random.EnqueueDouble(DRAW_SUCCESS);
            RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the first write to complete");
            DrainNextTick();

            _random.EnqueueDouble(DRAW_SUCCESS);
            IrreversibleOutcomeBeginResult second = RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.Calls.Count == 2 && persistence.LastTask.IsCompleted, "the second write to complete");
            DrainNextTick();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, second);
            Assert.AreEqual(2, _deliveries.Count);
            Assert.AreEqual(LEVEL_THREE, _deliveries[0].NewLevel);
            Assert.AreEqual(LEVEL_THREE + 1, _deliveries[1].NewLevel);
            Assert.AreEqual(SCROLLS_AT_START - 2, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            AssertNoFailureEffects();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_SecondBeginAfterAFailedAttempt_StartsCleanlyAndCompletes()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            _store.FailNext(new InvalidOperationException("database down"));
            ExpectError(SERVICE_FAILED_LOG);
            ExpectError(WRITE_FAILED_LOG);
            ExpectCoordinatorAlert(CAUSE_NON_SUCCESS);
            _random.EnqueueDouble(DRAW_SUCCESS);
            RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.LastTask.IsCompleted, "the failed write to complete");
            DrainNextTick();
            AssertBagBeforeAttempt();
            Assert.AreEqual(1, _disconnects.Count);

            // The failed write left the session's SaveVersion unknown, so the next write re-reads it (one warning).
            LogAssert.Expect(LogType.Warning, new Regex("re-read the stored SaveVersion"));
            _random.EnqueueDouble(DRAW_SUCCESS);
            IrreversibleOutcomeBeginResult second = RunBegin(orchestrator, out _);
            WaitUntil(() => persistence.Calls.Count == 2 && persistence.LastTask.IsCompleted, "the second write to complete");
            DrainNextTick();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, second);
            Assert.AreEqual(CharacterSaveResult.Success, persistence.LastTask.Result);
            Assert.AreEqual(1, _deliveries.Count);
            Assert.AreEqual(LEVEL_THREE, _deliveries[0].NewLevel);
            Assert.AreEqual(LEVEL_THREE, Stored().InventoryEnhancementLevels[ITEM_SLOT]);
            Assert.AreEqual(1, _disconnects.Count, "No further failure effects.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_AcknowledgmentThrowsAfterADestructionDraw_ReinsertsTheItemAtItsPreviousLevelAndRestoresTheScroll()
        {
            SeedAndLoad();
            var persistence = new FakePersistence(_persistenceService);
            EnhancementCommitOrchestrator orchestrator = BuildOrchestrator(persistence);
            ExpectError(WRITE_FAILED_LOG);
            _random.EnqueueDouble(DRAW_DESTRUCTION);

            Assert.Throws<InvalidOperationException>(
                () => RunBegin(orchestrator, out _, () => throw new InvalidOperationException("ack failed")));

            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            AssertBagBeforeAttempt();
            Assert.AreEqual(0, persistence.Calls.Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            AssertNothingAnnounced();
            LogAssert.NoUnexpectedReceived();
        }

        // ---------------------------------------------------------------- no await

        private static readonly string[] SCANNED_SOURCE_FILES =
        {
            "src/ServerLogic/EnhancementSystem/EnhancementCommitOrchestrator.cs",
            "src/ServerLogic/EnhancementSystem/IEnhancementAttemptStarter.cs",
            "src/ServerLogic/EnhancementSystem/EnhancementClientRequestHandlers.cs",
        };

        private static readonly Regex FORBIDDEN_TOKENS = new Regex(@"\bawait\b|\basync\b|\.Result\b|\.Wait\(|GetResult\(|ContinueWith");

        [Test]
        public void Source_NoAwaitAsyncResultWaitGetResultOrContinueWithInTheFilesThisStoryTouches()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            foreach (string relative in SCANNED_SOURCE_FILES)
            {
                string text = File.ReadAllText(Path.Combine(root, relative));
                // Comments (// and /* */, including doc comments) are not code. Limitation: a "//" or "/*" inside a string literal
                // would be stripped as well; the scanned files contain no such literal.
                string withoutBlocks = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                string code = Regex.Replace(withoutBlocks, @"//[^\r\n]*", string.Empty);
                Match match = FORBIDDEN_TOKENS.Match(code);
                Assert.IsFalse(match.Success, relative + " contains forbidden token '" + match.Value + "'.");
            }
        }
    }
}

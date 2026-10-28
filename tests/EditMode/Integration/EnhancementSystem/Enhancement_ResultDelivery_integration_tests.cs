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
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Networking;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// Enhancement Story 015: what a client receives because an attempt was accepted and committed
    /// (<see cref="EnhancementAttemptStarter"/>), driven through the real <see cref="EnhancementCommitOrchestrator"/>,
    /// <see cref="EnhancementService"/>, <see cref="InventoryService"/>, <see cref="IrreversibleOutcomeCoordinator"/>,
    /// <see cref="TickCompletionQueue"/>, <see cref="CharacterPersistenceService"/> with the in-memory store, the real
    /// <see cref="ClientMessageOutbox"/> (flushed per tick into a recording send seam), the real
    /// <see cref="OwnerInventorySyncSender"/> and the real <see cref="EnhancementClientRequestHandlers"/>.
    /// Covers CR-ENH-11, CR-ENH-15 steps 3, 4, 6b and 9, EC-ENH-1, EC-NET-9, CR-NET-5.5, CR-NET-7.7, AC-NC-35,
    /// AC-NC-43 and AC-ENH-13 (delivery half). Deterministic: a scripted random source, held writes, tick-driven drains;
    /// the only waits are bounded 5 s waits on thread-pool tasks.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_ResultDelivery_Integration_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);

        // The client and the account share one id so the coordinator's "clientId=7" alert and the state machine agree.
        private const uint CLIENT_ID = 7u;
        private const uint NEW_CLIENT_ID = 8u;
        private const uint ACCOUNT = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int WAIT_MS = 5000;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int START_LEVEL = 10;
        private const int CAP_THRESHOLD = 300;
        private const uint REQUEST_ID = 7u;
        private const uint SECOND_REQUEST_ID = 8u;
        private const uint PRIOR_REQUEST_ID = 41u;
        private const int CAP = PriorityPathQueue<int>.PRIORITY_PATH_CAP;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const int STACK_LIMIT_CONSUMABLE = 99;
        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int FREE_SLOT = 2;
        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_THREE = 3;
        private const int SCROLLS_AT_START = 3;

        // P_s[2] = 0.85: DRAW_SUCCESS succeeds, DRAW_DESTRUCTION destroys at level 2.
        private const double DRAW_SUCCESS = 0.00;
        private const double DRAW_DESTRUCTION = 0.90;

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string CAUSE_DID_NOT_START = "write did not start";
        private const string WRITE_FAILED_LOG = @"\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(EnhancementResult\) for .* failed";
        private const string STARTER_LOG_PREFIX = "[EnhancementAttemptStarter]";
        private const string HANDLER_LOG_PREFIX = "[EnhancementClientRequestHandlers]";
        private const string DUPLICATE_WARNING = "DuplicateEnhancementRequest";
        private const string UNKNOWN_CLIENT_WARNING = @"UnknownClient: clientId=7 ";
        private const string SAVE_VERSION_WARNING = "re-read the stored SaveVersion";

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);

        // ------------------------------------------------------------------ doubles

        /// <summary>A persistence fake: forwards to the real service, records every irreversible write, can throw on start.</summary>
        private sealed class ControlledPersistence : ICharacterPersistence
        {
            private readonly CharacterPersistenceService _inner;

            public ControlledPersistence(CharacterPersistenceService inner)
            {
                _inner = inner;
            }

            public List<(CharacterID Id, IrreversibleOutcomeTrigger Trigger)> Calls { get; } =
                new List<(CharacterID, IrreversibleOutcomeTrigger)>();

            public Task<CharacterSaveResult> LastTask { get; private set; }

            public Exception ThrowOnSave { get; set; }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                Calls.Add((id, trigger));
                if (ThrowOnSave != null)
                {
                    throw ThrowOnSave;
                }

                LastTask = _inner.SaveIrreversibleOutcome(id, trigger, ct);
                return LastTask;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        /// <summary>Forwards to the real outbox; records type and cap flag per call; can run a hook before, or throw on, a type.</summary>
        private sealed class ProbeOutbox : IClientMessageOutbox
        {
            private readonly IClientMessageOutbox _inner;

            public ProbeOutbox(IClientMessageOutbox inner)
            {
                _inner = inner;
            }

            public List<(ushort Type, bool Exempt)> Calls { get; } = new List<(ushort, bool)>();

            public ushort? ThrowOnType { get; set; }

            public Action<ushort> BeforeEnqueue { get; set; }

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                Calls.Add((messageTypeId, isCapExempt));
                BeforeEnqueue?.Invoke(messageTypeId);
                if (ThrowOnType.HasValue && ThrowOnType.Value == messageTypeId)
                {
                    throw new InvalidOperationException("probe outbox failure");
                }

                _inner.Enqueue(clientId, messageTypeId, body, isCapExempt);
            }

            public int CountOf(ushort messageTypeId) => Calls.FindAll(call => call.Type == messageTypeId).Count;
        }

        private sealed class ThrowingLookup : IEnhancementRequestDedupLookup
        {
            public bool TryGetDeduplicator(CharacterID characterId, out EnhancementRequestDeduplicator deduplicator)
            {
                throw new InvalidOperationException("probe lookup failure");
            }
        }

        /// <summary>One message that left the outbox: the receiving client, the type and a copy of the body.</summary>
        private sealed class Sent
        {
            public Sent(uint clientId, ushort type, byte[] body)
            {
                ClientId = clientId;
                Type = type;
                Body = body;
            }

            public uint ClientId { get; }

            public ushort Type { get; }

            public byte[] Body { get; }
        }

        // ------------------------------------------------------------------ rig

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
        private ControlledPersistence _persistence;
        private EnhancementCommitOrchestrator _orchestrator;

        private OutboxRecordingSendSeam _seam;
        private ClientMessageOutbox _outbox;
        private ProbeOutbox _probe;
        private OwnerInventorySyncSender _sender;
        private DictionaryDedupLookup _dedupLookup;
        private EnhancementRequestDeduplicator _dedup;
        private EnhancementAttemptStarter _starter;
        private EnhancementClientRequestHandlers _handlers;

        private uint _tick;
        private uint _outTick;
        private Action _releaseWrite;
        private readonly List<Sent> _captured = new List<Sent>();
        private readonly List<string> _raised = new List<string>();
        private readonly List<EnhancementAttemptResult> _deliveries = new List<EnhancementAttemptResult>();
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();
        private int _failedCount;

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

            _leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _persistenceService = new CharacterPersistenceService(
                _store, _stats, _leveling, _inventory, new CurrencySystem(), new FakeEquipmentService(), new FakeZoneLocator(),
                new CharacterWriteQueue(), () => T2, TimeSpan.FromSeconds(5));

            _tick = 0u;
            _outTick = 0u;
            _releaseWrite = null;
            _captured.Clear();
            _raised.Clear();
            _deliveries.Clear();
            _disconnects.Clear();
            _failedCount = 0;

            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            _queue = new TickCompletionQueue(LONG_WATCHDOG_TICKS);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);

            _seam = new OutboxRecordingSendSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(CLIENT_ID);
            _probe = new ProbeOutbox(_outbox);
            _sender = new OwnerInventorySyncSender(_inventory, _outbox);
            _inventory.OnInventoryChanged += RecordRaised;

            _dedup = new EnhancementRequestDeduplicator();
            _dedupLookup = new DictionaryDedupLookup();
            _dedupLookup.Set(CHAR_A, _dedup);

            _persistence = new ControlledPersistence(_persistenceService);
            _orchestrator = BuildOrchestrator(_persistence);
            RebuildStarter(_probe, _dedupLookup);
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseWrite(); // a held write must never outlive its test
            _inventory.OnInventoryChanged -= RecordRaised;
            _sender.Dispose();
            foreach (ItemDefinition definition in _definitions)
            {
                Object.DestroyImmediate(definition);
            }
        }

        private void RecordRaised(InventoryChangedEventArgs args)
        {
            var slots = new string[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                slots[i] = args[i].SlotIndex.ToString();
            }

            _raised.Add(string.Join(",", slots));
        }

        private static IClassRegistry CreateClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WARRIOR_CLASS_TYPE, new ClassDefinition(
                strengthAutoAlloc: 2, dexterityAutoAlloc: 1, vitalityAutoAlloc: 1, intelligenceAutoAlloc: 0, freePointsPerLevel: 1));
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

        private EnhancementCommitOrchestrator BuildOrchestrator(ICharacterPersistence persistence)
        {
            return new EnhancementCommitOrchestrator(
                _coordinator, _service, persistence,
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => { });
        }

        private void RebuildStarter(IClientMessageOutbox starterOutbox, IEnhancementRequestDedupLookup lookup)
        {
            _starter = new EnhancementAttemptStarter(_orchestrator, starterOutbox, _sender, lookup);
            _handlers = new EnhancementClientRequestHandlers(_service, _probe, _dedupLookup, _starter);
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

        // Inserts the seed, loads it (which fills the bag), moves the store clock on to T2, then connects the client:
        // the zone-entry full sync is flushed and forgotten so a test sees only what follows.
        private void Arrange()
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(), CancellationToken.None)));
            CharacterLoadResult result = Complete(_persistenceService.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _persistenceService.ApplyLoadedRecord(_entity, result.Record));
            _storeTime = T2;
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "Arrange: the bag must be loaded.");

            _sender.OnSessionReadySent(CLIENT_ID, CHAR_A);
            FlushTick();
            _captured.Clear();
            _raised.Clear();
        }

        private CharacterRecord Stored() => Complete(_store.FetchAsync(CHAR_A, CancellationToken.None));

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

        // Releases a held write (if any), waits for it and handles its result on the next tick.
        private void CompleteWrite()
        {
            ReleaseWrite();
            WaitUntil(() => _persistence.LastTask != null && _persistence.LastTask.IsCompleted, "the write to complete");
            DrainNextTick();
        }

        // A database failure of the next write: logs in the order Story 011 expects them.
        private void ExpectDatabaseFailure()
        {
            _store.FailNext(new InvalidOperationException("database down"));
            LogAssert.Expect(LogType.Error, new Regex(SERVICE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex(WRITE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + CAUSE_NON_SUCCESS));
        }

        private void CompleteFailedWrite()
        {
            WaitUntil(() => _persistence.LastTask != null && _persistence.LastTask.IsCompleted, "the failed write to complete");
            DrainNextTick();
            DrainNextTick();
        }

        private void Start(uint requestId = REQUEST_ID)
        {
            _starter.StartAttempt(CLIENT_ID, CHAR_A, requestId, ITEM_SLOT, SCROLL_SLOT);
        }

        private void SendRequest(uint requestId, int itemSlot = ITEM_SLOT, int scrollSlot = SCROLL_SLOT)
        {
            var body = new byte[EnhancementAttemptRequest.BodySize];
            EnhancementAttemptRequestCodec.WriteBody(body, requestId, (byte)itemSlot, (byte)scrollSlot);
            var context = new InboundRequestContext(CLIENT_ID, default, CHAR_A, EnhancementAttemptRequest.MessageTypeId, 0u, 1u, false);
            _handlers.HandleAttemptRequest(in context, body);
        }

        private void DropAndReconnect()
        {
            _sender.OnClientDisconnected(CLIENT_ID);
            _outbox.RemoveClient(CLIENT_ID);
            _outbox.AddClient(NEW_CLIENT_ID);
        }

        // Flushes the outbox once and returns what left it on this tick, decoded to (client, type, body).
        private List<Sent> FlushTick()
        {
            _outTick++;
            _seam.Sends.Clear();
            _outbox.Flush(_outTick);
            var slice = new List<Sent>();
            foreach (OutboxRecordedSend send in _seam.Sends)
            {
                Assert.IsTrue(MessageEnvelopeCodec.TryRead(send.Bytes, out ServerMessageEnvelope envelope));
                slice.Add(new Sent(send.ClientId, envelope.MessageTypeId, send.Bytes.AsSpan(ServerMessageEnvelope.WireSize).ToArray()));
            }

            _captured.AddRange(slice);
            return slice;
        }

        private static List<Sent> OfType(List<Sent> messages, ushort messageTypeId)
        {
            return messages.FindAll(message => message.Type == messageTypeId);
        }

        private static List<Sent> OfType(List<Sent> messages, ushort messageTypeId, uint clientId)
        {
            return messages.FindAll(message => message.Type == messageTypeId && message.ClientId == clientId);
        }

        private static EnhancementResultCode DecodeResult(Sent message, out uint requestId, out byte newLevel)
        {
            Assert.AreEqual(EnhancementAttemptResultMessage.MessageTypeId, message.Type);
            Assert.IsTrue(EnhancementAttemptResultMessageCodec.TryReadBody(
                message.Body, out requestId, out EnhancementResultCode code, out newLevel, out bool isUnknown));
            Assert.IsFalse(isUnknown);
            return code;
        }

        private static InventorySlotEntry[] DecodeUpdate(Sent message)
        {
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, message.Type);
            var entries = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            Assert.IsTrue(InventorySlotUpdateCodec.TryReadBody(message.Body, entries, out int count), "The update body must decode.");
            var result = new InventorySlotEntry[count];
            Array.Copy(entries, result, count);
            return result;
        }

        private static InventorySlotEntry[] DecodeFullSync(Sent message)
        {
            Assert.AreEqual(InventoryFullSync.MessageTypeId, message.Type);
            var entries = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            Assert.IsTrue(InventoryFullSyncCodec.TryReadBody(message.Body, entries), "The full sync body must decode.");
            return entries;
        }

        private static string SlotsOf(InventorySlotEntry[] entries)
        {
            var slots = new string[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                slots[i] = entries[i].SlotIndex.ToString();
            }

            return string.Join(",", slots);
        }

        private void AssertFullSyncEqualsBag(Sent message)
        {
            InventorySlotEntry[] entries = DecodeFullSync(message);
            for (int i = 0; i < entries.Length; i++)
            {
                InventorySlot slot = _inventory.GetSlot(CHAR_A, i);
                Assert.AreEqual(i, entries[i].SlotIndex, "Entries are in ascending slot order.");
                Assert.AreEqual(slot.ItemId, entries[i].ItemId, $"Slot {i} item.");
                Assert.AreEqual(slot.Quantity, entries[i].Quantity, $"Slot {i} quantity.");
                Assert.AreEqual(slot.EnhancementLevel, entries[i].EnhancementLevel, $"Slot {i} level.");
            }
        }

        // Queues `count` non-exempt InventorySlotUpdate messages, entry i naming slot i, straight into the real outbox.
        private void EnqueueFillers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entries = new[] { new InventorySlotEntry((byte)i, ItemID.Invalid, 0, 0) };
                var buffer = new byte[InventorySlotUpdate.MaxBodySize];
                int written = InventorySlotUpdateCodec.WriteBody(buffer, entries);
                _outbox.Enqueue(CLIENT_ID, InventorySlotUpdate.MessageTypeId, new ReadOnlySpan<byte>(buffer, 0, written), false);
            }
        }

        private void AssertBagBeforeAttempt()
        {
            InventorySlot sword = _inventory.GetSlot(CHAR_A, ITEM_SLOT);
            Assert.AreEqual(BronzeSwordId, sword.ItemId);
            Assert.AreEqual(LEVEL_TWO, sword.EnhancementLevel);
            Assert.AreEqual(SCROLLS_AT_START, _inventory.GetSlot(CHAR_A, SCROLL_SLOT).Quantity);
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT));
        }

        private void AssertHoldIsClosed()
        {
            // With the hold closed a bag change reaches the client at once.
            Assert.IsTrue(_inventory.Pickup(CHAR_A, BronzeSwordId, 1).Success);
            List<Sent> tick = FlushTick();
            Assert.GreaterOrEqual(OfType(tick, InventorySlotUpdate.MessageTypeId, CLIENT_ID).Count, 1, "The hold must be closed.");
        }

        // ------------------------------------------------------------------ acknowledgment (CR-ENH-15 step 4)

        [Test]
        public void Ack_AcceptedRequest_SendsOneRequestReceivedWithIdAndItemSlotOnTheDispatchTickAndNoRejectionFollows()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);

            SendRequest(REQUEST_ID);
            List<Sent> dispatchTick = FlushTick();

            Assert.AreEqual(1, dispatchTick.Count, "Only the acknowledgment leaves on the dispatch tick: the bag updates are held.");
            Assert.AreEqual(EnhancementRequestReceived.MessageTypeId, dispatchTick[0].Type);
            Assert.AreEqual(CLIENT_ID, dispatchTick[0].ClientId);
            Assert.IsTrue(EnhancementRequestReceivedCodec.TryReadBody(dispatchTick[0].Body, out uint requestId, out byte itemSlot));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(ITEM_SLOT, itemSlot);

            CompleteWrite();
            List<Sent> after = FlushTick();

            Assert.AreEqual(1, OfType(_captured, EnhancementRequestReceived.MessageTypeId).Count, "Exactly one acknowledgment in total.");
            List<Sent> results = OfType(after, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out _, out _), "No Rejected* result follows an acknowledgment.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Ack_AndResult_AreEnqueuedCapExempt()
        {
            Arrange();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();

            Assert.AreEqual(1, _probe.CountOf(EnhancementRequestReceived.MessageTypeId));
            Assert.AreEqual(1, _probe.CountOf(EnhancementAttemptResultMessage.MessageTypeId));
            foreach ((ushort type, bool exempt) in _probe.Calls)
            {
                Assert.IsTrue(exempt, $"Message 0x{type:X4} must be cap-exempt (CR-NET-7.7).");
            }

            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ step 4 failure (AC-NC-43)

        [Test]
        public void StepFour_ConsumeItemFails_NoAckOneRejectedScrollNotFoundAndALaterSlotUpdateIsNotHeld()
        {
            Arrange();
            _recording.FailConsumeItem = true;

            SendRequest(REQUEST_ID);
            Assert.IsTrue(_inventory.Pickup(CHAR_A, BronzeSwordId, 1).Success, "Arrange: a bag change after the rejection.");
            List<Sent> tick = FlushTick();

            Assert.AreEqual(0, OfType(tick, EnhancementRequestReceived.MessageTypeId).Count);
            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedScrollNotFound, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);
            List<Sent> updates = OfType(tick, InventorySlotUpdate.MessageTypeId);
            Assert.AreEqual(1, updates.Count, "The update raised after the rejection is sent, not held.");
            InventorySlotEntry[] entries = DecodeUpdate(updates[0]);
            Assert.AreEqual(FREE_SLOT, entries[0].SlotIndex);
            Assert.AreEqual(BronzeSwordId, entries[0].ItemId);
            Assert.AreEqual(0, _persistence.Calls.Count, "No write.");
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StepFour_RejectionResult_IsCapExempt()
        {
            Arrange();
            _recording.FailConsumeItem = true;

            Start();

            Assert.AreEqual(1, _probe.Calls.Count);
            Assert.AreEqual((EnhancementAttemptResultMessage.MessageTypeId, true), _probe.Calls[0]);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ result only after the commit (CR-ENH-11)

        [Test]
        public void Commit_HeldOpen_NoSlotUpdateAndNoResultThenSuccessSendsHeldUpdatesInRaisedOrderAndOneResult()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            DrainNextTick();
            List<Sent> whileHeld = FlushTick();

            Assert.AreEqual(0, OfType(whileHeld, InventorySlotUpdate.MessageTypeId).Count, "No slot update while the commit is open.");
            Assert.AreEqual(0, OfType(whileHeld, EnhancementAttemptResultMessage.MessageTypeId).Count, "No result while the commit is open.");
            Assert.GreaterOrEqual(_raised.Count, 1, "Arrange: the attempt raised inventory events.");
            List<string> raisedBeforeCommit = new List<string>(_raised);

            CompleteWrite();
            List<Sent> afterCommit = FlushTick();

            List<Sent> updates = OfType(afterCommit, InventorySlotUpdate.MessageTypeId);
            var decodedOrder = updates.ConvertAll(update => SlotsOf(DecodeUpdate(update)));
            CollectionAssert.AreEqual(raisedBeforeCommit, decodedOrder, "Held updates arrive in the order they were raised.");
            List<Sent> results = OfType(afterCommit, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(LEVEL_THREE, newLevel);
            Assert.AreEqual(0, OfType(_captured, InventoryFullSync.MessageTypeId).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_FailsWithDatabaseError_NeitherUpdatesNorResultAreSentEvenAfterTheRollback()
        {
            Arrange();
            ExpectDatabaseFailure();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteFailedWrite();
            FlushTick();
            FlushTick();

            Assert.AreEqual(0, OfType(_captured, InventorySlotUpdate.MessageTypeId).Count, "Neither the attempt's nor the rollback's updates are sent.");
            Assert.AreEqual(0, OfType(_captured, EnhancementAttemptResultMessage.MessageTypeId).Count);
            Assert.AreEqual(0, OfType(_captured, InventoryFullSync.MessageTypeId).Count);
            Assert.AreEqual(1, OfType(_captured, EnhancementRequestReceived.MessageTypeId).Count, "The acknowledgment was already sent.");
            AssertBagBeforeAttempt();
            Assert.AreEqual(1, _disconnects.Count, "CR-NET-5.5: the client is disconnected.");
            _captured.Clear();
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Commit_WriteCannotStart_AckSentNoResultNoUpdatesAndTheHoldIsDiscarded()
        {
            Arrange();
            _persistence.ThrowOnSave = new InvalidOperationException("cannot start");
            LogAssert.Expect(LogType.Error, new Regex(WRITE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + CAUSE_DID_NOT_START));
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            FlushTick();

            Assert.AreEqual(1, OfType(_captured, EnhancementRequestReceived.MessageTypeId).Count);
            Assert.AreEqual(0, OfType(_captured, EnhancementAttemptResultMessage.MessageTypeId).Count);
            Assert.AreEqual(0, OfType(_captured, InventorySlotUpdate.MessageTypeId).Count);
            AssertBagBeforeAttempt();
            Assert.AreEqual(1, _disconnects.Count);
            _captured.Clear();
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ destruction

        [Test]
        public void Commit_Destruction_SendsDestructionResultWithLevelZeroAndASlotUpdateWithItemIdZero()
        {
            Arrange();
            _random.EnqueueDouble(DRAW_DESTRUCTION);

            Start();
            CompleteWrite();
            List<Sent> tick = FlushTick();

            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.Destruction, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);

            bool destroyedSlotSeen = false;
            foreach (Sent update in OfType(tick, InventorySlotUpdate.MessageTypeId))
            {
                foreach (InventorySlotEntry entry in DecodeUpdate(update))
                {
                    if (entry.SlotIndex == ITEM_SLOT && entry.ItemId == ItemID.Invalid)
                    {
                        destroyedSlotSeen = true;
                    }
                }
            }

            Assert.IsTrue(destroyedSlotSeen, "A slot update empties the destroyed item's slot (itemId 0).");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ cap exemption (AC-NC-35)

        [Test]
        public void Cap_AckWithTheQueueAtItsCap_IsFirstOfTheTickAndTheDisplacedMessageLeadsTheNextTick()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            EnqueueFillers(CAP);

            Start();
            List<Sent> first = FlushTick();
            List<Sent> second = FlushTick();

            Assert.AreEqual(CAP, first.Count, "The tick sends 8, not 9.");
            Assert.AreEqual(EnhancementRequestReceived.MessageTypeId, first[0].Type, "The exempt acknowledgment is at position 1.");
            Assert.AreEqual(1, second.Count);
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, second[0].Type);
            Assert.AreEqual(0, DecodeUpdate(second[0])[0].SlotIndex, "The displaced (oldest) message is at position 1 of the next tick.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Cap_RejectionResultWithTheQueueAtItsCap_IsFirstOfTheTickAndTheDisplacedMessageLeadsTheNextTick()
        {
            Arrange();
            _recording.FailConsumeItem = true;
            EnqueueFillers(CAP);

            Start();
            List<Sent> first = FlushTick();
            List<Sent> second = FlushTick();

            Assert.AreEqual(CAP, first.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedScrollNotFound, DecodeResult(first[0], out _, out _), "The exempt result is at position 1.");
            Assert.AreEqual(1, second.Count);
            Assert.AreEqual(0, DecodeUpdate(second[0])[0].SlotIndex, "The displaced message is at position 1 of the next tick.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Cap_SuccessResultWithTheQueueAtItsCap_IsFirstOfTheTickAndTheDisplacedMessageLeadsTheNextTick()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();
            EnqueueFillers(CAP);

            CompleteWrite();
            List<Sent> tick = FlushTick();
            List<Sent> next = FlushTick();

            Assert.AreEqual(CAP, tick.Count);
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(tick[0], out _, out byte newLevel), "The exempt result is at position 1.");
            Assert.AreEqual(LEVEL_THREE, newLevel);
            // The next tick also carries the released held updates; only its first position is asserted.
            Assert.GreaterOrEqual(next.Count, 1);
            InventorySlotEntry displaced = DecodeUpdate(next[0])[0];
            Assert.AreEqual(0, displaced.SlotIndex, "The displaced (oldest) filler is at position 1 of the next tick.");
            Assert.AreEqual(ItemID.Invalid, displaced.ItemId, "Position 1 is the filler, not a released held update.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ reconnect during the commit (AC-NC-43, EC-ENH-1)

        [Test]
        public void Reconnect_NewConnectionAnnouncedWhileTheCommitIsOpen_GetsNoFullSyncUntilTheCommitReturns()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();

            DropAndReconnect();
            _sender.OnSessionReadySent(NEW_CLIENT_ID, CHAR_A);
            DrainNextTick();
            List<Sent> whileHeld = FlushTick();

            Assert.AreEqual(0, OfType(whileHeld, InventoryFullSync.MessageTypeId, NEW_CLIENT_ID).Count);
            Assert.AreEqual(0, whileHeld.FindAll(message => message.ClientId == NEW_CLIENT_ID).Count, "Nothing at all reaches the new connection.");

            LogAssert.Expect(LogType.Warning, new Regex(UNKNOWN_CLIENT_WARNING));
            CompleteWrite();
            FlushTick();
        }

        [Test]
        public void Reconnect_CommitSucceeds_OneFullSyncEqualToTheCommittedBagNoPreCommitUpdatesAndNoResultOnTheNewConnection()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();
            DropAndReconnect();
            _sender.OnSessionReadySent(NEW_CLIENT_ID, CHAR_A);

            // The result is enqueued for the old, removed connection: the outbox warns once.
            LogAssert.Expect(LogType.Warning, new Regex(UNKNOWN_CLIENT_WARNING));
            CompleteWrite();
            List<Sent> tick = FlushTick();

            List<Sent> forNewClient = tick.FindAll(message => message.ClientId == NEW_CLIENT_ID);
            Assert.AreEqual(1, forNewClient.Count, "Exactly one message reaches the new connection.");
            Assert.AreEqual(InventoryFullSync.MessageTypeId, forNewClient[0].Type);
            AssertFullSyncEqualsBag(forNewClient[0]);
            Assert.AreEqual(LEVEL_THREE, DecodeFullSync(forNewClient[0])[ITEM_SLOT].EnhancementLevel, "The full sync shows the committed bag.");
            Assert.AreEqual(0, OfType(_captured, InventorySlotUpdate.MessageTypeId, NEW_CLIENT_ID).Count);
            Assert.AreEqual(0, OfType(_captured, EnhancementAttemptResultMessage.MessageTypeId, NEW_CLIENT_ID).Count, "No replay of the result (EC-ENH-1).");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Reconnect_CommitFails_NoFullSyncAndNothingElseOnTheNewConnection()
        {
            Arrange();
            ExpectDatabaseFailure();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();
            DropAndReconnect();
            _sender.OnSessionReadySent(NEW_CLIENT_ID, CHAR_A);

            CompleteFailedWrite();
            FlushTick();

            Assert.AreEqual(0, _captured.FindAll(message => message.ClientId == NEW_CLIENT_ID).Count, "No full sync, update or result on the new connection.");
            AssertBagBeforeAttempt();
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ AC-ENH-13

        [TestCase(false)]
        [TestCase(true)]
        public void Ac_Enh_13_ConnectionDropsAfterTheCommit_NewConnectionGetsTheCommittedBagAndNoResult(bool dropAfterDelivery)
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();

            ReleaseWrite();
            WaitUntil(() => _persistence.LastTask != null && _persistence.LastTask.IsCompleted, "the write to complete");
            if (dropAfterDelivery)
            {
                DrainNextTick(); // result and held updates are queued, not yet flushed
                DropAndReconnect();
            }
            else
            {
                DropAndReconnect(); // dropped after the write committed, before the result is handled
                LogAssert.Expect(LogType.Warning, new Regex(UNKNOWN_CLIENT_WARNING));
                DrainNextTick();
            }

            _sender.OnSessionReadySent(NEW_CLIENT_ID, CHAR_A);
            List<Sent> tick = FlushTick();

            List<Sent> forNewClient = tick.FindAll(message => message.ClientId == NEW_CLIENT_ID);
            Assert.AreEqual(1, forNewClient.Count);
            InventorySlotEntry[] bag = DecodeFullSync(forNewClient[0]);
            Assert.AreEqual(LEVEL_THREE, bag[ITEM_SLOT].EnhancementLevel, "The committed level.");
            Assert.AreEqual(SCROLLS_AT_START - 1, bag[SCROLL_SLOT].Quantity, "The scroll is gone.");
            Assert.IsFalse(_inventory.IsSlotLocked(CHAR_A, ITEM_SLOT), "The slot is unlocked.");
            AssertFullSyncEqualsBag(forNewClient[0]);
            Assert.AreEqual(0, OfType(_captured, EnhancementAttemptResultMessage.MessageTypeId, NEW_CLIENT_ID).Count);
            Assert.AreEqual(LEVEL_THREE, Stored().InventoryEnhancementLevels[ITEM_SLOT], "The outcome is committed.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ LastEnhancementRequestID (EC-NET-9)

        [Test]
        public void LastRequestId_Committed_IsRecordedBeforeTheResultIsEnqueuedAndAResendIsDropped()
        {
            Arrange();
            uint? valueWhenResultEnqueued = null;
            _probe.BeforeEnqueue = type =>
            {
                if (type == EnhancementAttemptResultMessage.MessageTypeId)
                {
                    valueWhenResultEnqueued = _dedup.LastEnhancementRequestId;
                }
            };
            _random.EnqueueDouble(DRAW_SUCCESS);

            SendRequest(REQUEST_ID);
            CompleteWrite();
            FlushTick();

            Assert.AreEqual(REQUEST_ID, _dedup.LastEnhancementRequestId);
            Assert.AreEqual(REQUEST_ID, valueWhenResultEnqueued, "RecordCommitted runs before the result is enqueued.");

            int callsBefore = _probe.Calls.Count;
            LogAssert.Expect(LogType.Warning, new Regex(DUPLICATE_WARNING));
            using (var capture = new InboundLogCapture(HANDLER_LOG_PREFIX))
            {
                SendRequest(REQUEST_ID);
                Assert.AreEqual(1, capture.CountWarnings(DUPLICATE_WARNING));
            }

            Assert.AreEqual(callsBefore, _probe.Calls.Count, "The resend gets no reply.");
            Assert.AreEqual(1, _persistence.Calls.Count, "The resend starts no second attempt.");
            Assert.AreEqual(0, FlushTick().Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LastRequestId_FailedCommit_IsUnchangedAndAResendOfTheSameIdIsProcessed()
        {
            Arrange();
            _dedup = new EnhancementRequestDeduplicator(PRIOR_REQUEST_ID);
            _dedupLookup.Set(CHAR_A, _dedup);
            ExpectDatabaseFailure();
            _random.EnqueueDouble(DRAW_SUCCESS);

            SendRequest(REQUEST_ID);
            CompleteFailedWrite();

            Assert.AreEqual(PRIOR_REQUEST_ID, _dedup.LastEnhancementRequestId, "A failed commit records nothing.");
            AssertBagBeforeAttempt();

            // The failed write left the session's SaveVersion unknown, so the next write re-reads it (one warning).
            LogAssert.Expect(LogType.Warning, new Regex(SAVE_VERSION_WARNING));
            _random.EnqueueDouble(DRAW_SUCCESS);
            SendRequest(REQUEST_ID);
            WaitUntil(() => _persistence.Calls.Count == 2 && _persistence.LastTask.IsCompleted, "the second write to complete");
            DrainNextTick();

            Assert.AreEqual(2, _persistence.Calls.Count, "The resend was processed.");
            Assert.AreEqual(REQUEST_ID, _dedup.LastEnhancementRequestId);
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LastRequestId_RejectedAtTheHandler_IsUnchanged()
        {
            Arrange();
            _dedup = new EnhancementRequestDeduplicator(PRIOR_REQUEST_ID);
            _dedupLookup.Set(CHAR_A, _dedup);
            _inventory.SeedSlotForTesting(CHAR_A, SCROLL_SLOT, new ItemID(IRON_SCROLL_ID), SCROLLS_AT_START);

            SendRequest(REQUEST_ID);
            List<Sent> tick = FlushTick();

            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, DecodeResult(results[0], out _, out _));
            Assert.AreEqual(PRIOR_REQUEST_ID, _dedup.LastEnhancementRequestId);
            Assert.AreEqual(0, _persistence.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LastRequestId_StepFourFailure_IsUnchanged()
        {
            Arrange();
            _dedup = new EnhancementRequestDeduplicator(PRIOR_REQUEST_ID);
            _dedupLookup.Set(CHAR_A, _dedup);
            _recording.FailConsumeItem = true;

            SendRequest(REQUEST_ID);

            Assert.AreEqual(PRIOR_REQUEST_ID, _dedup.LastEnhancementRequestId);
            Assert.AreEqual(1, _probe.CountOf(EnhancementAttemptResultMessage.MessageTypeId));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ rejections at the starter (re-validation, write in flight)

        [Test]
        public void Start_RevalidationRejects_NoAckOneRejectionAndTheHoldIsReleased()
        {
            Arrange();
            _inventory.SeedSlotForTesting(CHAR_A, SCROLL_SLOT, new ItemID(IRON_SCROLL_ID), SCROLLS_AT_START);

            Start(); // bypasses the handler, so BeginAttempt's own step 2 check rejects
            List<Sent> tick = FlushTick();

            Assert.AreEqual(0, OfType(tick, EnhancementRequestReceived.MessageTypeId).Count);
            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);
            Assert.AreEqual(0, _persistence.Calls.Count);
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void WriteInFlight_AnotherSystemsWriteIsInFlight_OneRejectedConcurrentAttemptNoAckBagUnchangedAndTheHoldIsReleased()
        {
            Arrange();
            _gate.Close(CHAR_A); // another system's irreversible write (for example a respec) is in flight

            SendRequest(REQUEST_ID);
            List<Sent> tick = FlushTick();

            Assert.AreEqual(0, OfType(tick, EnhancementRequestReceived.MessageTypeId).Count);
            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count, "The handler's system sends the rejection (ADR-014 Decision 3).");
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);
            Assert.AreEqual(1, _probe.CountOf(EnhancementAttemptResultMessage.MessageTypeId));
            Assert.IsTrue(_probe.Calls.TrueForAll(call => call.Exempt), "The rejection is cap-exempt.");
            AssertBagBeforeAttempt();
            Assert.AreEqual(0, _persistence.Calls.Count);
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HoldOwnership_SecondStartWhileTheAttemptIsInFlight_IsRejectedAndLeavesTheFirstAttemptsHoldOpen()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start(REQUEST_ID);
            FlushTick();

            Start(SECOND_REQUEST_ID); // a caller that skips the handler's validation
            List<Sent> whileHeld = FlushTick();

            Assert.AreEqual(1, whileHeld.Count, "Only the rejection: the first attempt's hold still holds its updates.");
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, DecodeResult(whileHeld[0], out uint requestId, out _));
            Assert.AreEqual(SECOND_REQUEST_ID, requestId);

            CompleteWrite();
            List<Sent> afterCommit = FlushTick();

            List<Sent> results = OfType(afterCommit, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out uint committedId, out _));
            Assert.AreEqual(REQUEST_ID, committedId);
            Assert.GreaterOrEqual(OfType(afterCommit, InventorySlotUpdate.MessageTypeId).Count, 1, "The first attempt releases its own hold.");
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ RecordCommitted

        [Test]
        public void RecordCommitted_SetsTheLastRequestIdAndTheNextTryProcessOfThatIdIsADuplicate()
        {
            var deduplicator = new EnhancementRequestDeduplicator(PRIOR_REQUEST_ID);

            deduplicator.RecordCommitted(REQUEST_ID);

            Assert.AreEqual(REQUEST_ID, deduplicator.LastEnhancementRequestId);
            EnhancementRequestDedupResult result = deduplicator.TryProcess(
                REQUEST_ID, () => 1, (outcome, id) => true, outcome => { }, out int _);
            Assert.AreEqual(EnhancementRequestDedupResult.RejectedDuplicate, result);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RecordCommitted_CharacterWithoutADeduplicator_StillGetsTheResultAndOneWarning(bool lookupHoldsNull)
        {
            Arrange();
            var emptyLookup = new DictionaryDedupLookup();
            if (lookupHoldsNull)
            {
                emptyLookup.Set(CHAR_A, null);
            }

            RebuildStarter(_probe, emptyLookup);
            _random.EnqueueDouble(DRAW_SUCCESS);
            LogAssert.Expect(LogType.Warning, new Regex("NoDeduplicator"));
            using var capture = new InboundLogCapture(STARTER_LOG_PREFIX);

            Start();
            CompleteWrite();
            List<Sent> tick = FlushTick();

            Assert.AreEqual(1, capture.CountWarnings("NoDeduplicator"), "Exactly one warning.");
            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count, "The result is still sent.");
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out _, out _));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ onCommitFailed overload (decision D2)

        private IrreversibleOutcomeBeginResult BeginWithHook(Action acknowledge = null)
        {
            return _orchestrator.Begin(
                CLIENT_ID, CHAR_A, ITEM_SLOT, SCROLL_SLOT,
                acknowledge ?? (() => { }),
                result => _deliveries.Add(result),
                () => _failedCount++,
                out _);
        }

        [Test]
        public void Overload_OnFailedWrite_OnCommitFailedRunsOnceAfterTheRollback()
        {
            Arrange();
            ExpectDatabaseFailure();
            _random.EnqueueDouble(DRAW_SUCCESS);
            byte levelWhenHookRan = 0;
            IrreversibleOutcomeBeginResult result = _orchestrator.Begin(
                CLIENT_ID, CHAR_A, ITEM_SLOT, SCROLL_SLOT, () => { }, r => _deliveries.Add(r),
                () =>
                {
                    _failedCount++;
                    levelWhenHookRan = _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel;
                },
                out _);

            CompleteFailedWrite();
            DrainNextTick();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result);
            Assert.AreEqual(1, _failedCount);
            Assert.AreEqual(LEVEL_TWO, levelWhenHookRan, "The hook runs after RollBackAttempt.");
            Assert.AreEqual(0, _deliveries.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_OnPersistenceFailed_OnCommitFailedRunsOnce()
        {
            Arrange();
            _persistence.ThrowOnSave = new InvalidOperationException("cannot start");
            LogAssert.Expect(LogType.Error, new Regex(WRITE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex(COORDINATOR_ALERT_PREFIX + CAUSE_DID_NOT_START));
            _random.EnqueueDouble(DRAW_SUCCESS);

            IrreversibleOutcomeBeginResult result = BeginWithHook();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.PersistenceFailed, result);
            Assert.AreEqual(1, _failedCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_OnSuccess_OnCommitFailedDoesNotRun()
        {
            Arrange();
            _random.EnqueueDouble(DRAW_SUCCESS);

            BeginWithHook();
            CompleteWrite();

            Assert.AreEqual(0, _failedCount);
            Assert.AreEqual(1, _deliveries.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_OnRejection_OnCommitFailedDoesNotRun()
        {
            Arrange();
            _inventory.SeedSlotForTesting(CHAR_A, SCROLL_SLOT, new ItemID(IRON_SCROLL_ID), SCROLLS_AT_START);

            IrreversibleOutcomeBeginResult result = BeginWithHook();

            Assert.AreEqual(IrreversibleOutcomeBeginResult.RejectedInvalidRequest, result);
            Assert.AreEqual(0, _failedCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_AcknowledgmentThrows_OnCommitFailedDoesNotRun()
        {
            Arrange();
            LogAssert.Expect(LogType.Error, new Regex(WRITE_FAILED_LOG));
            _random.EnqueueDouble(DRAW_SUCCESS);

            Assert.Throws<InvalidOperationException>(() => BeginWithHook(() => throw new InvalidOperationException("ack failed")));

            Assert.AreEqual(0, _failedCount, "The rollback-before-write exception path rethrows instead of running the hook.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_NullOnCommitFailed_ThrowsArgumentNullExceptionAndChangesNothing()
        {
            Arrange();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Assert.Throws<ArgumentNullException>(() => _orchestrator.Begin(
                CLIENT_ID, CHAR_A, ITEM_SLOT, SCROLL_SLOT, () => { }, r => { }, null, out _));

            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            AssertBagBeforeAttempt();
            Assert.AreEqual(0, _persistence.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ deliverResult never throws

        [Test]
        public void Delivery_OutboxThrowsOnTheResult_DoesNotEscapeIsLoggedAndTheHoldIsReleased()
        {
            Arrange();
            _probe.ThrowOnType = EnhancementAttemptResultMessage.MessageTypeId;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: probe outbox failure"));
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();

            Assert.DoesNotThrow(CompleteWrite);
            List<Sent> tick = FlushTick();

            Assert.AreEqual(REQUEST_ID, _dedup.LastEnhancementRequestId, "The commit stands and its id is recorded.");
            Assert.GreaterOrEqual(OfType(tick, InventorySlotUpdate.MessageTypeId).Count, 1, "The hold was released: the held updates are sent.");
            Assert.AreEqual(0, OfType(tick, EnhancementAttemptResultMessage.MessageTypeId).Count);
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Delivery_LookupThrows_DoesNotEscapeIsLoggedTheResultIsStillSentAndTheHoldIsReleased()
        {
            Arrange();
            RebuildStarter(_probe, new ThrowingLookup());
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: probe lookup failure"));
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();

            Assert.DoesNotThrow(CompleteWrite);
            List<Sent> tick = FlushTick();

            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count, "The client still gets its result.");
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out _, out _));
            Assert.GreaterOrEqual(OfType(tick, InventorySlotUpdate.MessageTypeId).Count, 1, "The hold was released.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Delivery_CompleteAttemptThrows_TheResultIsBuiltFromThePendingAttemptAndTheHoldIsReleased()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            FlushTick();
            _service.CompleteAttempt(CHAR_A); // nothing is pending any more, so the delivery's CompleteAttempt throws
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: .*CompleteAttempt: no attempt is pending"));

            Assert.DoesNotThrow(CompleteWrite);
            List<Sent> tick = FlushTick();

            List<Sent> results = OfType(tick, EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count, "The commit stands: the client still gets its result.");
            Assert.AreEqual(EnhancementResultCode.Success, DecodeResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(LEVEL_THREE, newLevel);
            Assert.AreEqual(REQUEST_ID, _dedup.LastEnhancementRequestId);
            Assert.GreaterOrEqual(OfType(tick, InventorySlotUpdate.MessageTypeId).Count, 1, "The hold was released.");
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Overload_RollBackAttemptThrows_OnCommitFailedStillRunsOnce()
        {
            Arrange();
            _store.FailNext(new InvalidOperationException("database down"));
            LogAssert.Expect(LogType.Error, new Regex(SERVICE_FAILED_LOG));
            LogAssert.Expect(LogType.Error, new Regex("Completion callback threw .*RollBackAttempt: no attempt is pending"));
            _random.EnqueueDouble(DRAW_SUCCESS);
            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, BeginWithHook());
            _service.CompleteAttempt(CHAR_A); // nothing is pending any more, so the revert's RollBackAttempt throws

            CompleteFailedWrite();

            Assert.AreEqual(1, _failedCount, "The hook runs in a finally around RollBackAttempt.");
            Assert.AreEqual(0, _deliveries.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Start_AcknowledgmentThrows_HoldIsReleasedTheAttemptRolledBackAndTheExceptionReachesTheCaller()
        {
            Arrange();
            _probe.ThrowOnType = EnhancementRequestReceived.MessageTypeId;
            LogAssert.Expect(LogType.Error, new Regex(WRITE_FAILED_LOG));
            _random.EnqueueDouble(DRAW_SUCCESS);

            var thrown = Assert.Throws<InvalidOperationException>(() => Start());

            Assert.AreEqual("probe outbox failure", thrown.Message);
            Assert.IsFalse(_service.IsAttemptInProgress(CHAR_A));
            Assert.IsFalse(_gate.IsHeld(CHAR_A));
            AssertBagBeforeAttempt();
            Assert.AreEqual(0, _persistence.Calls.Count);
            FlushTick();
            _captured.Clear();
            AssertHoldIsClosed();
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ construction and source scan

        [Test]
        public void Constructor_WithANullArgument_ThrowsArgumentNullException([NUnit.Framework.Range(0, 3)] int nullIndex)
        {
            object[] args = { _orchestrator, _probe, _sender, _dedupLookup };
            args[nullIndex] = null;

            Assert.Throws<ArgumentNullException>(() => new EnhancementAttemptStarter(
                (EnhancementCommitOrchestrator)args[0],
                (IClientMessageOutbox)args[1],
                (IOwnerInventorySyncHold)args[2],
                (IEnhancementRequestDedupLookup)args[3]));
        }

        private static readonly string[] SCANNED_SOURCE_FILES =
        {
            "src/ServerLogic/EnhancementSystem/EnhancementAttemptStarter.cs",
            "src/ServerLogic/EnhancementSystem/EnhancementCommitOrchestrator.cs",
        };

        private static readonly Regex FORBIDDEN_TOKENS = new Regex(@"\bawait\b|\basync\b|\.Result\b|\.Wait\(|GetResult\(|ContinueWith");

        [Test]
        public void Source_StarterAndOrchestratorHaveNoAwaitAsyncResultWaitContinueWithAndTheStarterDoesNotReadTheMutationGate()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            foreach (string relative in SCANNED_SOURCE_FILES)
            {
                string text = File.ReadAllText(Path.Combine(root, relative));
                // Comments (including doc comments) are not code.
                string withoutBlocks = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                string code = Regex.Replace(withoutBlocks, @"//[^\r\n]*", string.Empty);
                Match match = FORBIDDEN_TOKENS.Match(code);
                Assert.IsFalse(match.Success, relative + " contains forbidden token '" + match.Value + "'.");
                if (relative.EndsWith("EnhancementAttemptStarter.cs", StringComparison.Ordinal))
                {
                    StringAssert.DoesNotContain("ICharacterMutationGate", code, "A game system must not read the mutation gate (ADR-011).");
                }
            }
        }
    }
}

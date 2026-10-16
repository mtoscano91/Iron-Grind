using System;
using System.Collections.Generic;
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
    /// Enhancement Story 016: <c>ServerBroadcast_Enhancement9</c> delivery (<see cref="Enhancement9BroadcastSender"/>).
    /// Two zones, A and B, are <see cref="RelayTestZone"/>s on one <see cref="InMemoryCrossZoneRelayBus"/>. The attempt runs in
    /// zone A through the real <see cref="EnhancementService"/>, <see cref="EnhancementCommitOrchestrator"/>,
    /// <see cref="EnhancementAttemptStarter"/> and <see cref="CharacterPersistenceService"/> (in-memory store, writes held or
    /// failed on demand); zone A's outbox is the Story 015 outbox, so the owner's result and the broadcast leave the same flush.
    /// Covers CR-ENH-14, CR-ENH-15 step 8, UI-ENH-3, EC-ENH-8, AC-ENH-18 (delivery half), AC-NC-47 and ADR-015 Decision 1 and 6.
    /// Deterministic: scripted random source, held writes, tick-driven drains; the only waits are bounded 5 s waits on tasks.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_Broadcast_Integration_Tests
    {
        private static readonly DateTime T1 = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T2 = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);
        private static readonly CharacterID CHAR_A = new CharacterID(101u);
        private static readonly CharacterID UNKNOWN_CHAR = new CharacterID(999u);
        private static readonly AccountID ACCOUNT_A = new AccountID(7u);
        private static readonly ZoneID LOADED_ZONE = new ZoneID(7u);

        // The owner's client and the account share one id so the coordinator's "clientId=7" alert and the state machine agree.
        private const uint OWNER = 7u;
        private const uint NEW_OWNER = 9u;
        private const uint OTHER_IN_A = 2u;
        private const uint REMOTE_IN_B = 3u;
        private const uint ACCOUNT = 7u;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int WAIT_MS = 5000;
        private const int LONG_WATCHDOG_TICKS = 1000;
        private const int START_LEVEL = 10;
        private const int CAP_THRESHOLD = 300;
        private const uint REQUEST_ID = 7u;
        private const int CAP = PriorityPathQueue<int>.PRIORITY_PATH_CAP;
        private const int EXTRA_TICKS = 3;

        private const uint SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const int STACK_LIMIT_CONSUMABLE = 99;
        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int SCROLLS_AT_START = 3;
        private const byte LEVEL_EIGHT = 8;
        private const byte LEVEL_NINE = 9;

        private const string PLAYER_NAME = "TestPlayer";
        private const string ITEM_NAME = "Dark Steel Sword";

        // P_s[7] = 0.22, P_s[8] = 0.12, P_s[9] = 0.06: DRAW_SUCCESS succeeds at each, DRAW_DESTRUCTION destroys at each.
        private const double DRAW_SUCCESS = 0.00;
        private const double DRAW_DESTRUCTION = 0.99;

        private const string COORDINATOR_ALERT_PREFIX = @"\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId=7 charId=.* — ";
        private const string CAUSE_NON_SUCCESS = "write returned a non-success result";
        private const string WRITE_FAILED_LOG = @"\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private const string SERVICE_FAILED_LOG = @"SaveIrreversibleOutcome \(EnhancementResult\) for .* failed";
        private const string UNKNOWN_CLIENT_WARNING = @"UnknownClient: clientId=7 ";
        private const string SEND_THREW_CLIENT_TWO = @"\[ClientMessageOutbox\] SendThrew: clientId=2 ";

        private static readonly ItemID SwordId = new ItemID(SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);

        // ------------------------------------------------------------------ doubles

        /// <summary>A persistence fake: forwards to the real service and remembers the last irreversible write.</summary>
        private sealed class ControlledPersistence : ICharacterPersistence
        {
            private readonly CharacterPersistenceService _inner;

            public ControlledPersistence(CharacterPersistenceService inner)
            {
                _inner = inner;
            }

            public int CallCount { get; private set; }

            public Task<CharacterSaveResult> LastTask { get; private set; }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
            {
                CallCount++;
                LastTask = _inner.SaveIrreversibleOutcome(id, trigger, ct);
                return LastTask;
            }

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, IronGrind.CharacterPersistence.PendingPurchaseRecord record, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<IronGrind.CharacterPersistence.PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        /// <summary>Recording name lookup; a character without an entry is "missing".</summary>
        private sealed class RecordingNameLookup : ICharacterNameLookup
        {
            public readonly Dictionary<CharacterID, string> Names = new Dictionary<CharacterID, string>();

            public int Calls;

            public bool TryGetCharacterName(CharacterID id, out string name)
            {
                Calls++;
                if (Names.TryGetValue(id, out name))
                {
                    return true;
                }

                name = string.Empty;
                return false;
            }
        }

        /// <summary>A broadcaster that throws on every call.</summary>
        private sealed class ThrowingBroadcaster : IServerWideBroadcaster
        {
            public int Calls;

            public bool Broadcast(ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                Calls++;
                throw new InvalidOperationException("broadcaster fake");
            }
        }

        /// <summary>An item database that reports success with a null definition.</summary>
        private sealed class NullDefinitionDatabase : IItemDatabase
        {
            public bool IsReady => true;

            public event Action OnDatabaseReady
            {
                add { }
                remove { }
            }

            public ItemDefinition GetItem(ItemID id) => null;

            public bool TryGetItem(ItemID id, out ItemDefinition item)
            {
                item = null;
                return true;
            }

            public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category) => new List<ItemDefinition>();
        }

        /// <summary>One message that left an outbox: the receiving client, the type and a copy of the body.</summary>
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

        private InMemoryCrossZoneRelayBus _bus;
        private RelayTestZone _a;
        private RelayTestZone _b;
        private OwnerInventorySyncSender _ownerSync;
        private DictionaryDedupLookup _dedupLookup;
        private EnhancementAttemptStarter _starter;
        private RecordingNameLookup _names;
        private Enhancement9BroadcastSender _broadcastSender;

        private uint _tick;
        private uint _tickA;
        private uint _tickB;
        private Action _releaseWrite;
        private readonly List<Sent> _sentA = new List<Sent>();
        private readonly List<Sent> _sentB = new List<Sent>();
        private readonly List<(uint ClientId, DisconnectReason Reason)> _disconnects = new List<(uint, DisconnectReason)>();

        [SetUp]
        public void SetUp()
        {
            _storeTime = T1;
            _store = new InMemoryCharacterRecordStore(() => _storeTime);

            _definitions = new List<ItemDefinition>
            {
                BuildSword(ITEM_NAME),
                ItemDefinitionBuilder.Build(BRONZE_SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Bronze)),
            };
            _itemDatabase = new StubItemDatabase();
            foreach (ItemDefinition definition in _definitions)
            {
                _itemDatabase.Add(definition);
            }

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _sessions = new StubNpcSessions();
            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, _sessions, _random);

            _leveling = new LevelingService(BuildXpThresholdsWithCapSentinel(), CreateClassRegistry());
            _stats = CharacterStatsFixture.CreateWithLeveling(_leveling);
            _leveling.AttachCharacterStats(_stats);
            _entity = CharacterStatsFixture.PlayerEntityId;
            _persistenceService = new CharacterPersistenceService(
                _store, _stats, _leveling, _inventory, new CurrencySystem(), new FakeZoneLocator(),
                new CharacterWriteQueue(), () => T2, TimeSpan.FromSeconds(5));

            _tick = 0u;
            _tickA = 0u;
            _tickB = 0u;
            _releaseWrite = null;
            _sentA.Clear();
            _sentB.Clear();
            _disconnects.Clear();

            _observer = new NetworkTestObserver();
            _gate = new CharacterMutationGate();
            _queue = new TickCompletionQueue(LONG_WATCHDOG_TICKS);
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _gate, _observer);

            _bus = new InMemoryCrossZoneRelayBus();
            _a = new RelayTestZone(_bus, 64, OWNER, OTHER_IN_A);
            _b = new RelayTestZone(_bus, 64, REMOTE_IN_B);
            _ownerSync = new OwnerInventorySyncSender(_inventory, _a.Outbox);

            _dedupLookup = new DictionaryDedupLookup();
            _dedupLookup.Set(CHAR_A, new EnhancementRequestDeduplicator());

            _persistence = new ControlledPersistence(_persistenceService);
            _orchestrator = new EnhancementCommitOrchestrator(
                _coordinator, _service, _persistence,
                (id, reason) => _disconnects.Add((id, reason)),
                (id, ttl) => { });
            _starter = new EnhancementAttemptStarter(_orchestrator, _a.Outbox, _ownerSync, _dedupLookup);

            _names = new RecordingNameLookup();
            _names.Names[CHAR_A] = PLAYER_NAME;
            _broadcastSender = null;
            BuildSender();
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseWrite(); // a held write must never outlive its test
            _broadcastSender?.Dispose();
            _ownerSync.Dispose();
            foreach (ItemDefinition definition in _definitions)
            {
                Object.DestroyImmediate(definition);
            }
        }

        private static ItemDefinition BuildSword(string displayName)
        {
            return ItemDefinitionBuilder.Build(SWORD_ID, displayName, ItemCategory.Equipment,
                isUpgradeable: true, stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));
        }

        // Replaces the sword's definition (same id) so its display name differs; call before Arrange.
        private void ReplaceSword(string displayName)
        {
            ItemDefinition definition = BuildSword(displayName);
            _definitions.Add(definition);
            _itemDatabase.Add(definition);
        }

        // (Re)builds the system under test; the previous sender, if any, is disposed first.
        private Enhancement9BroadcastSender BuildSender(IServerWideBroadcaster broadcaster = null, ICharacterNameLookup names = null, IItemDatabase items = null)
        {
            _broadcastSender?.Dispose();
            _broadcastSender = new Enhancement9BroadcastSender(
                _service, broadcaster ?? _a.Broadcaster, names ?? _names, items ?? _itemDatabase);
            return _broadcastSender;
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

        // A level-10 warrior named "TestPlayer" holding a Sword at the given enhancement level in slot 0 and three scrolls in slot 1.
        private CharacterRecord MakeSeed(byte swordLevel)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT_A, CHAR_A, WARRIOR_CLASS_TYPE);
            r.CharacterName = PLAYER_NAME;
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
            r.InventorySlots[ITEM_SLOT] = SwordId;
            r.InventoryItemCounts[ITEM_SLOT] = 1;
            r.InventoryEnhancementLevels[ITEM_SLOT] = swordLevel;
            r.InventorySlots[SCROLL_SLOT] = BronzeScrollId;
            r.InventoryItemCounts[SCROLL_SLOT] = SCROLLS_AT_START;
            return r;
        }

        // Loads the seed, then connects the owner: the zone-entry full sync is flushed and forgotten.
        private void Arrange(byte swordLevel = LEVEL_EIGHT)
        {
            _storeTime = T1;
            Assert.IsTrue(Complete(_store.InsertAsync(MakeSeed(swordLevel), CancellationToken.None)));
            CharacterLoadResult result = Complete(_persistenceService.LoadCharacter(CHAR_A, ACCOUNT_A, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, result.Code);
            Assert.AreEqual(CharacterLoadCode.Success, _persistenceService.ApplyLoadedRecord(_entity, result.Record));
            _storeTime = T2;
            Assert.AreEqual(swordLevel, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "Arrange: the bag must be loaded.");

            _ownerSync.OnSessionReadySent(OWNER, CHAR_A);
            TickA();
            _sentA.Clear();
        }

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

        private void Start()
        {
            _starter.StartAttempt(OWNER, CHAR_A, REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);
        }

        private void DropAndReconnect()
        {
            _ownerSync.OnClientDisconnected(OWNER);
            _a.Outbox.RemoveClient(OWNER);
            _a.Outbox.AddClient(NEW_OWNER);
        }

        // Runs one tick of a zone (relay step, then the outbound flush) and returns what left its outbox, decoded.
        private List<Sent> TickA()
        {
            _a.Seam.Sends.Clear();
            _tickA++;
            _a.Tick(_tickA);
            return Decode(_a.Seam.Sends, _sentA);
        }

        private List<Sent> TickB()
        {
            _b.Seam.Sends.Clear();
            _tickB++;
            _b.Tick(_tickB);
            return Decode(_b.Seam.Sends, _sentB);
        }

        private static List<Sent> Decode(List<OutboxRecordedSend> sends, List<Sent> cumulative)
        {
            var slice = new List<Sent>();
            foreach (OutboxRecordedSend send in sends)
            {
                Assert.IsTrue(MessageEnvelopeCodec.TryRead(send.Bytes, out ServerMessageEnvelope envelope));
                slice.Add(new Sent(send.ClientId, envelope.MessageTypeId, send.Bytes.AsSpan(ServerMessageEnvelope.WireSize).ToArray()));
            }

            cumulative.AddRange(slice);
            return slice;
        }

        private void TickBothZones(int count)
        {
            for (int i = 0; i < count; i++)
            {
                TickA();
                TickB();
            }
        }

        private static int CountOf(List<Sent> messages, ushort type, uint clientId)
        {
            return messages.FindAll(message => message.Type == type && message.ClientId == clientId).Count;
        }

        private static int Broadcasts(List<Sent> messages, uint clientId) => CountOf(messages, ServerBroadcast_Enhancement9.MessageTypeId, clientId);

        private static int AllBroadcasts(List<Sent> messages) => messages.FindAll(m => m.Type == ServerBroadcast_Enhancement9.MessageTypeId).Count;

        private static void AssertBroadcastBody(Sent message, string expectedPlayer, string expectedItem)
        {
            Assert.AreEqual(ServerBroadcast_Enhancement9.MessageTypeId, message.Type);
            Assert.IsTrue(ServerBroadcast_Enhancement9Codec.TryReadBody(message.Body, out string player, out string item), "The body must decode.");
            Assert.AreEqual(expectedPlayer, player);
            Assert.AreEqual(expectedItem, item);
        }

        private static Sent FirstBroadcast(List<Sent> messages, uint clientId)
        {
            Sent found = messages.Find(m => m.Type == ServerBroadcast_Enhancement9.MessageTypeId && m.ClientId == clientId);
            Assert.IsNotNull(found, "Client " + clientId + " must have received the broadcast.");
            return found;
        }

        private static EnhancementResultCode DecodeResult(Sent message, out byte newLevel)
        {
            Assert.AreEqual(EnhancementAttemptResultMessage.MessageTypeId, message.Type);
            Assert.IsTrue(EnhancementAttemptResultMessageCodec.TryReadBody(
                message.Body, out uint _, out EnhancementResultCode code, out newLevel, out bool isUnknown));
            Assert.IsFalse(isUnknown);
            return code;
        }

        private void AssertOwnerResult(List<Sent> messages, uint clientId, EnhancementResultCode expectedCode)
        {
            List<Sent> results = messages.FindAll(m => m.Type == EnhancementAttemptResultMessage.MessageTypeId && m.ClientId == clientId);
            Assert.AreEqual(1, results.Count, "The owner receives exactly one result.");
            Assert.AreEqual(expectedCode, DecodeResult(results[0], out _));
        }

        // The NameMissing warning the sender logs, built from the real ids (the same interpolation the sender uses).
        private static string NameMissingPattern(bool nameFound, bool itemFound)
        {
            return Regex.Escape(
                $"[Enhancement9BroadcastSender] NameMissing: charId={CHAR_A} itemId={SwordId} "
                + $"nameFound={(nameFound ? "true" : "false")} itemFound={(itemFound ? "true" : "false")} - no broadcast.");
        }

        // Runs `act` while counting the logs of `type` whose text matches `regex`; the subscription is always removed.
        private static int CountLogs(LogType type, string regex, Action act)
        {
            int count = 0;
            var pattern = new Regex(regex);
            Application.LogCallback handler = (condition, stackTrace, logType) =>
            {
                if (logType == type && pattern.IsMatch(condition))
                {
                    count++;
                }
            };
            Application.logMessageReceived += handler;
            try
            {
                act();
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            return count;
        }

        // Queues `count` non-exempt InventorySlotUpdate messages for a client straight into zone A's outbox.
        private void EnqueueFillers(uint clientId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entries = new[] { new InventorySlotEntry((byte)i, ItemID.Invalid, 0, 0) };
                var buffer = new byte[InventorySlotUpdate.MaxBodySize];
                int written = InventorySlotUpdateCodec.WriteBody(buffer, entries);
                _a.Outbox.Enqueue(clientId, InventorySlotUpdate.MessageTypeId, new ReadOnlySpan<byte>(buffer, 0, written), false);
            }
        }

        // ------------------------------------------------------------------ AC-ENH-18 (delivery)

        [Test]
        public void test_ac_enh_18_plus_nine_commit_reaches_both_zone_a_clients_in_the_commit_tick_and_zone_b_client_once_in_its_next_tick()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();

            Assert.AreEqual(1, Broadcasts(aTick, OWNER));
            Assert.AreEqual(1, Broadcasts(aTick, OTHER_IN_A));
            AssertBroadcastBody(FirstBroadcast(aTick, OWNER), PLAYER_NAME, ITEM_NAME);
            AssertBroadcastBody(FirstBroadcast(aTick, OTHER_IN_A), PLAYER_NAME, ITEM_NAME);
            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(0, Broadcasts(_sentB, REMOTE_IN_B), "Zone B has not ticked yet.");
            Assert.AreEqual(1, _bus.Payloads.Count, "Exactly one payload is published for the other zones.");

            List<Sent> bTick = TickB();

            Assert.AreEqual(1, Broadcasts(bTick, REMOTE_IN_B));
            AssertBroadcastBody(FirstBroadcast(bTick, REMOTE_IN_B), PLAYER_NAME, ITEM_NAME);

            TickBothZones(EXTRA_TICKS);

            Assert.AreEqual(2, AllBroadcasts(_sentA), "Nothing more in zone A over 3 further ticks.");
            Assert.AreEqual(1, AllBroadcasts(_sentB), "Nothing more in zone B over 3 further ticks.");
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_is_not_filtered_by_session_state_and_reaches_every_client_in_the_outbox()
        {
            Arrange();
            _a.Outbox.AddClient(50u); // a client still loading: in the outbox, no SessionReady
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();

            Assert.AreEqual(1, Broadcasts(aTick, 50u));
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ owner reconnected during the commit

        [Test]
        public void test_owner_reconnected_during_the_commit_new_id_receives_the_broadcast_once_and_old_id_none()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);
            Start();
            TickA();

            DropAndReconnect();
            _ownerSync.OnSessionReadySent(NEW_OWNER, CHAR_A);

            // The owner's result goes to the old, removed connection: the outbox warns once. The broadcast does not.
            LogAssert.Expect(LogType.Warning, new Regex(UNKNOWN_CLIENT_WARNING));
            CompleteWrite();
            List<Sent> aTick = TickA();
            TickB();

            Assert.AreEqual(1, Broadcasts(aTick, NEW_OWNER));
            AssertBroadcastBody(FirstBroadcast(aTick, NEW_OWNER), PLAYER_NAME, ITEM_NAME);
            Assert.AreEqual(0, Broadcasts(_sentA, OWNER), "The old id receives nothing.");
            Assert.AreEqual(1, Broadcasts(aTick, OTHER_IN_A));
            Assert.AreEqual(1, Broadcasts(_sentB, REMOTE_IN_B));

            TickBothZones(EXTRA_TICKS);

            Assert.AreEqual(1, Broadcasts(_sentA, NEW_OWNER), "The new id's cumulative count stays at exactly one.");
            Assert.AreEqual(0, Broadcasts(_sentA, OWNER), "The old id's cumulative count stays at zero.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ only +9

        [TestCase(7, DRAW_SUCCESS, EnhancementResultCode.Success)]
        [TestCase(9, DRAW_SUCCESS, EnhancementResultCode.Success)]
        [TestCase(8, DRAW_DESTRUCTION, EnhancementResultCode.Destruction)]
        public void test_only_plus_nine_other_commits_send_no_broadcast_to_any_client(int startLevel, double draw, EnhancementResultCode expectedCode)
        {
            Arrange((byte)startLevel);
            _random.EnqueueDouble(draw);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();
            TickB();
            TickBothZones(EXTRA_TICKS);

            AssertOwnerResult(aTick, OWNER, expectedCode);
            Assert.AreEqual(0, AllBroadcasts(_sentA));
            Assert.AreEqual(0, AllBroadcasts(_sentB));
            Assert.AreEqual(0, _bus.Payloads.Count, "Nothing was published.");
            Assert.AreEqual(0, _names.Calls, "The sender was not even triggered.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ only after the commit (CR-ENH-15 step 8)

        [Test]
        public void test_held_write_sends_nothing_in_either_zone_until_the_commit_returns()
        {
            Arrange();
            HoldWrite();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            DrainNextTick();
            Assert.AreEqual(1, _persistence.CallCount, "The irreversible write has been requested once.");
            Assert.IsFalse(_persistence.LastTask.IsCompleted, "The write is still held.");
            TickBothZones(EXTRA_TICKS);

            Assert.AreEqual(0, AllBroadcasts(_sentA), "No broadcast in zone A while the write is held.");
            Assert.AreEqual(0, AllBroadcasts(_sentB), "No broadcast in zone B while the write is held.");
            Assert.AreEqual(0, _bus.Payloads.Count, "Nothing is published while the write is held.");

            CompleteWrite();
            TickBothZones(1);

            Assert.AreEqual(2, AllBroadcasts(_sentA));
            Assert.AreEqual(1, AllBroadcasts(_sentB));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_write_completing_with_a_non_success_code_sends_no_broadcast_ever()
        {
            Arrange();
            ExpectDatabaseFailure();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteFailedWrite();
            TickBothZones(EXTRA_TICKS + 1);

            Assert.AreEqual(0, AllBroadcasts(_sentA));
            Assert.AreEqual(0, AllBroadcasts(_sentB));
            Assert.AreEqual(0, _bus.Payloads.Count);
            Assert.AreEqual(LEVEL_EIGHT, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel, "The attempt was rolled back.");
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ delivery failure isolated (EC-ENH-8)

        [Test]
        public void test_send_seam_throwing_for_client_two_other_clients_and_the_owner_result_are_unaffected()
        {
            Arrange();
            _a.Seam.AfterSend = clientId =>
            {
                if (clientId == OTHER_IN_A)
                {
                    throw new InvalidOperationException("seam fake");
                }
            };
            LogAssert.Expect(LogType.Error, new Regex(SEND_THREW_CLIENT_TWO));
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = null;
            List<Sent> bTick = null;
            int sendThrewErrors = CountLogs(LogType.Error, SEND_THREW_CLIENT_TWO, () =>
            {
                Assert.DoesNotThrow(() => aTick = TickA());
                Assert.DoesNotThrow(() => bTick = TickB());
            });
            int sentABefore = _sentA.Count;
            int laterErrors = CountLogs(LogType.Error, SEND_THREW_CLIENT_TWO, () => TickBothZones(EXTRA_TICKS));

            Assert.AreEqual(1, sendThrewErrors, "Exactly one SendThrew error for client 2.");
            Assert.AreEqual(0, laterErrors, "No further SendThrew error in later ticks.");
            Assert.AreEqual(1, Broadcasts(aTick, OWNER));
            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(1, Broadcasts(bTick, REMOTE_IN_B));
            Assert.AreEqual(0, Broadcasts(_sentA.GetRange(sentABefore, _sentA.Count - sentABefore), OTHER_IN_A),
                "Client 2 receives no broadcast in any later tick.");
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_publisher_throwing_zone_a_clients_still_receive_and_outcome_and_result_are_unchanged()
        {
            Arrange();
            var throwingPublisher = new RelayRecordingPublisher(null) { Throw = true };
            BuildSender(new ServerWideBroadcaster(_a.Outbox, throwingPublisher, _a.Allowlist));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[ServerWideBroadcaster\] PublishFailed"));
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = null;
            Assert.DoesNotThrow(() => aTick = TickA());
            List<Sent> bTick = null;
            Assert.DoesNotThrow(() => bTick = TickB());

            Assert.AreEqual(1, throwingPublisher.Calls);
            Assert.AreEqual(1, Broadcasts(aTick, OWNER));
            Assert.AreEqual(1, Broadcasts(aTick, OTHER_IN_A));
            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(0, Broadcasts(bTick, REMOTE_IN_B), "The other zone is not reached.");
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ not cap-exempt (AC-NC-47)

        [Test]
        public void test_ac_nc_47_broadcast_is_not_cap_exempt_it_leaves_for_a_full_queue_in_the_next_flush()
        {
            Arrange();
            EnqueueFillers(OTHER_IN_A, CAP);
            EnqueueFillers(OWNER, CAP);
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> first = TickA();
            List<Sent> second = TickA();

            List<Sent> firstForClientTwo = first.FindAll(m => m.ClientId == OTHER_IN_A);
            Assert.AreEqual(CAP, firstForClientTwo.Count, "The 8 queued messages are sent.");
            Assert.AreEqual(0, Broadcasts(first, OTHER_IN_A), "The broadcast is the 9th non-exempt message and waits.");
            Assert.AreEqual(1, Broadcasts(second, OTHER_IN_A), "The broadcast is in client 2's next flush.");
            AssertBroadcastBody(FirstBroadcast(second, OTHER_IN_A), PLAYER_NAME, ITEM_NAME);

            // The owner's queue is full too. Start and commit happen before the first flush, so both cap-exempt messages
            // are in it, the acknowledgment (CR-ENH-15 step 4) before the result, and the non-exempt broadcast is not.
            AssertOwnerResult(first, OWNER, EnhancementResultCode.Success);
            List<Sent> firstForOwner = first.FindAll(m => m.ClientId == OWNER);
            int ackIndex = firstForOwner.FindIndex(m => m.Type == EnhancementRequestReceived.MessageTypeId);
            int resultIndex = firstForOwner.FindIndex(m => m.Type == EnhancementAttemptResultMessage.MessageTypeId);
            Assert.GreaterOrEqual(ackIndex, 0, "The exempt acknowledgment is in the owner's first flush.");
            Assert.Greater(resultIndex, ackIndex, "The exempt result follows the acknowledgment.");
            Assert.AreEqual(0, Broadcasts(first, OWNER), "The owner's broadcast waits behind the full queue.");
            Assert.AreEqual(1, Broadcasts(second, OWNER), "The owner's broadcast is in the second flush.");
            AssertBroadcastBody(FirstBroadcast(second, OWNER), PLAYER_NAME, ITEM_NAME);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ names

        [TestCase("TestPlayer", "Dark Steel Sword", "TestPlayer", "Dark Steel Sword")]
        [TestCase("ABCDEFGHIJKLMNOPQRSTUVWX", "ABCDEFGHIJKLMNOPQRSTUVWX", "ABCDEFGHIJKLMNOPQRSTUVWX", "ABCDEFGHIJKLMNOPQRSTUVWX")]
        [TestCase("TestPlayer", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123", "TestPlayer", "ABCDEFGHIJKLMNOPQRSTUVWX")]
        [TestCase("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123", "Dark Steel Sword", "ABCDEFGHIJKLMNOPQRSTUVWX", "Dark Steel Sword")]
        // 13 two-byte characters (26 bytes) arrive as 12 characters (24 bytes), never a split character.
        [TestCase("TestPlayer", "ééééééééééééé", "TestPlayer",
            "éééééééééééé")]
        public void test_names_come_from_the_lookup_and_the_item_database_and_are_cut_to_24_utf8_bytes(
            string playerName, string itemName, string expectedPlayer, string expectedItem)
        {
            _names.Names[CHAR_A] = playerName;
            ReplaceSword(itemName);
            Arrange();
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();
            List<Sent> bTick = TickB();

            AssertBroadcastBody(FirstBroadcast(aTick, OWNER), expectedPlayer, expectedItem);
            AssertBroadcastBody(FirstBroadcast(bTick, REMOTE_IN_B), expectedPlayer, expectedItem);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_player_name_comes_from_the_character_persistence_service_session()
        {
            Arrange();
            BuildSender(names: _persistenceService);
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();

            AssertBroadcastBody(FirstBroadcast(aTick, OWNER), PLAYER_NAME, ITEM_NAME);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------ missing name or item

        [Test]
        public void test_missing_character_name_sends_no_broadcast_logs_one_warning_and_keeps_the_outcome()
        {
            Arrange();
            _names.Names.Remove(CHAR_A);
            AssertNoBroadcastWithOneNameMissingWarning(nameFound: false, itemFound: true);
        }

        // Plays the +9 attempt to the end and checks: one NameMissing warning with the given flags, no broadcast anywhere,
        // the owner's result and the +9 outcome unchanged (EC-ENH-8).
        private void AssertNoBroadcastWithOneNameMissingWarning(bool nameFound, bool itemFound)
        {
            LogAssert.Expect(LogType.Warning, new Regex(NameMissingPattern(nameFound, itemFound)));
            _random.EnqueueDouble(DRAW_SUCCESS);

            List<Sent> aTick = null;
            int warnings = CountLogs(LogType.Warning, Regex.Escape("NameMissing:"), () =>
            {
                Start();
                CompleteWrite();
                aTick = TickA();
                TickBothZones(EXTRA_TICKS);
            });

            Assert.AreEqual(1, warnings, "Exactly one NameMissing warning.");
            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(0, AllBroadcasts(_sentA));
            Assert.AreEqual(0, AllBroadcasts(_sentB));
            Assert.AreEqual(0, _bus.Payloads.Count);
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("")]
        [TestCase(null)]
        public void test_lookup_returning_true_with_an_empty_or_null_name_sends_no_broadcast_logs_one_warning_and_keeps_the_outcome(string name)
        {
            Arrange();
            _names.Names[CHAR_A] = name;
            AssertNoBroadcastWithOneNameMissingWarning(nameFound: false, itemFound: true);
        }

        [Test]
        public void test_missing_item_definition_sends_no_broadcast_logs_one_warning_and_keeps_the_outcome()
        {
            Arrange();
            BuildSender(items: new StubItemDatabase());
            AssertNoBroadcastWithOneNameMissingWarning(nameFound: true, itemFound: false);
        }

        [Test]
        public void test_item_definition_with_an_empty_display_name_counts_as_missing()
        {
            ReplaceSword(string.Empty);
            Arrange();
            AssertNoBroadcastWithOneNameMissingWarning(nameFound: true, itemFound: false);
        }

        [Test]
        public void test_item_database_returning_a_null_definition_counts_as_missing()
        {
            Arrange();
            BuildSender(items: new NullDefinitionDatabase());
            AssertNoBroadcastWithOneNameMissingWarning(nameFound: true, itemFound: false);
        }

        // ------------------------------------------------------------------ +9 committed in zone B

        [Test]
        public void test_plus_nine_committed_in_zone_b_reaches_zone_a_clients_once_and_zone_b_clients_once_with_no_echo()
        {
            Arrange();

            // A second service, orchestrator, starter and sender wired to zone B (the owner connection is also a zone B client).
            _b.Outbox.AddClient(OWNER);
            var serviceB = new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, _sessions, _random);
            var orchestratorB = new EnhancementCommitOrchestrator(
                _coordinator, serviceB, _persistence, (id, reason) => _disconnects.Add((id, reason)), (id, ttl) => { });
            var ownerSyncB = new OwnerInventorySyncSender(_inventory, _b.Outbox);
            var senderB = new Enhancement9BroadcastSender(serviceB, _b.Broadcaster, _names, _itemDatabase);
            try
            {
                var starterB = new EnhancementAttemptStarter(orchestratorB, _b.Outbox, ownerSyncB, _dedupLookup);
                ownerSyncB.OnSessionReadySent(OWNER, CHAR_A);
                TickB();
                _sentB.Clear();
                _broadcastSender.Dispose(); // zone A's sender does not observe serviceB; this only rules out an accidental path
                _random.EnqueueDouble(DRAW_SUCCESS);

                starterB.StartAttempt(OWNER, CHAR_A, REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);
                CompleteWrite();
                TickBothZones(1 + EXTRA_TICKS);

                Assert.AreEqual(1, Broadcasts(_sentB, REMOTE_IN_B), "Zone B's own client receives it once.");
                Assert.AreEqual(1, Broadcasts(_sentB, OWNER), "Zone B's owner connection receives it once.");
                Assert.AreEqual(1, Broadcasts(_sentA, OTHER_IN_A), "Zone A's other client receives it once.");
                Assert.AreEqual(1, Broadcasts(_sentA, OWNER), "Zone A's owner-id client receives it once.");
                Assert.AreEqual(2, AllBroadcasts(_sentA), "No echo in zone A.");
                Assert.AreEqual(2, AllBroadcasts(_sentB), "No echo in zone B.");
                Assert.AreEqual(1, _bus.Payloads.Count);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                senderB.Dispose();
                ownerSyncB.Dispose();
            }
        }

        // ------------------------------------------------------------------ subscription

        [Test]
        public void test_dispose_unsubscribes_and_a_later_plus_nine_commit_sends_no_broadcast()
        {
            Arrange();
            _broadcastSender.Dispose();
            Assert.DoesNotThrow(() => _broadcastSender.Dispose(), "Dispose is idempotent.");
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            CompleteWrite();
            List<Sent> aTick = TickA();
            TickBothZones(EXTRA_TICKS);

            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(0, AllBroadcasts(_sentA));
            Assert.AreEqual(0, AllBroadcasts(_sentB));
            Assert.AreEqual(0, _names.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_throwing_broadcaster_is_contained_by_the_service_and_outcome_and_result_are_unchanged()
        {
            Arrange();
            var throwing = new ThrowingBroadcaster();
            BuildSender(throwing);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: broadcaster fake"));
            _random.EnqueueDouble(DRAW_SUCCESS);

            Start();
            Assert.DoesNotThrow(CompleteWrite);
            List<Sent> aTick = TickA();

            Assert.AreEqual(1, throwing.Calls);
            AssertOwnerResult(aTick, OWNER, EnhancementResultCode.Success);
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(CHAR_A, ITEM_SLOT).EnhancementLevel);
            Assert.AreEqual(0, AllBroadcasts(_sentA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_constructor_with_a_null_argument_throws_argument_null_exception([NUnit.Framework.Range(0, 3)] int nullIndex)
        {
            object[] args = { _service, _a.Broadcaster, _names, _itemDatabase };
            args[nullIndex] = null;

            Assert.Throws<ArgumentNullException>(() => new Enhancement9BroadcastSender(
                (EnhancementService)args[0],
                (IServerWideBroadcaster)args[1],
                (ICharacterNameLookup)args[2],
                (IItemDatabase)args[3]));
        }

        // ------------------------------------------------------------------ CharacterPersistenceService.TryGetCharacterName

        [Test]
        public void test_lookup_loaded_character_returns_true_and_the_name()
        {
            Arrange();

            bool found = _persistenceService.TryGetCharacterName(CHAR_A, out string name);

            Assert.IsTrue(found);
            Assert.AreEqual(PLAYER_NAME, name);
        }

        [Test]
        public void test_lookup_released_session_returns_false()
        {
            Arrange();
            Assert.IsTrue(_persistenceService.ReleaseSession(CHAR_A, out _));

            bool found = _persistenceService.TryGetCharacterName(CHAR_A, out string name);

            Assert.IsFalse(found);
            Assert.AreEqual(string.Empty, name);
        }

        [Test]
        public void test_lookup_unknown_or_not_yet_loaded_character_returns_false()
        {
            Assert.IsFalse(_persistenceService.TryGetCharacterName(CHAR_A, out string beforeLoad), "Before any load.");
            Assert.AreEqual(string.Empty, beforeLoad);

            Arrange();

            Assert.IsFalse(_persistenceService.TryGetCharacterName(UNKNOWN_CHAR, out string unknown), "A different character.");
            Assert.AreEqual(string.Empty, unknown);
        }

        [TestCase("")]
        [TestCase(null)]
        public void test_lookup_empty_or_null_character_name_returns_false(string characterName)
        {
            CharacterRecord record = MakeSeed(LEVEL_EIGHT);
            record.CharacterName = characterName;
            Assert.AreEqual(CharacterLoadCode.Success, _persistenceService.ApplyLoadedRecord(_entity, record));

            bool found = _persistenceService.TryGetCharacterName(CHAR_A, out string name);

            Assert.IsFalse(found);
            Assert.AreEqual(string.Empty, name);
        }
    }
}

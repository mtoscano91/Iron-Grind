using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.NpcInteraction;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Networking;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// EditMode integration tests for Enhancement System Story 010: the five client requests
    /// (<c>EnhancementAttemptRequest</c>, <c>EnhancementPreviewRequest</c>, <c>CancelEnhancement</c>,
    /// <c>OpenNPCInteraction</c>, <c>CloseNPCInteraction</c>) driven through the real inbound request
    /// dispatcher, the real <see cref="EnhancementService"/>, a real <see cref="InventoryService"/> and the
    /// real <see cref="NpcInteractionSessionTracker"/>. Replies are read from a recording outbox and
    /// decoded with the Part 1 codecs (design/gdd/enhancement-system.md CR-ENH-6, CR-ENH-15 steps 1-2,
    /// CR-ENH-16, UI-ENH-1, UI-ENH-2, AC-ENH-6; networking-wire-protocol.md AC-NC-48, AC-NC-49, CR-NET-7.7;
    /// ADR-014 Decisions 1 and 3).
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_ClientMessages_Integration_Tests
    {
        private const uint CLIENT = InboundTestIds.ClientOne;
        private const uint NPC_ID = 77u;
        private const double SESSION_LIFETIME_SECONDS = 600.0;
        private const uint FIRST_DISPATCH_TICK = 8u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint IRON_SWORD_ID = 4002u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const int STACK_LIMIT_CONSUMABLE = 99;
        private const int SCROLL_QUANTITY = 5;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int IRON_SCROLL_SLOT = 2;
        private const int EMPTY_SLOT = 9;

        private const byte LEVEL_FOUR = 4;
        private const byte LEVEL_TWO = 2;
        private const ushort LEVEL_FOUR_SUCCESS_RAW = 6500;
        private const ushort LEVEL_FOUR_DESTRUCTION_RAW = 3500;
        private const ushort LEVEL_TWO_SUCCESS_RAW = 8500;
        private const ushort LEVEL_TWO_DESTRUCTION_RAW = 1500;
        private const int SUCCESS_RAW_OFFSET = 3;
        private const int DESTRUCTION_RAW_OFFSET = 5;

        private const uint REQUEST_ID = 7u;
        private const uint PERSISTED_REQUEST_ID = 41u;
        private const string HANDLER_LOG_PREFIX = "[EnhancementClientRequestHandlers]";
        private const string NPC_HANDLER_LOG_PREFIX = "[NpcInteractionRequestHandlers]";
        private const string MALFORMED_WARNING = "InboundMessageMalformed";

        private static readonly CharacterID Player = InboundDispatchHarness.CharacterOf(CLIENT);
        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID IronSwordId = new ItemID(IRON_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);
        private static readonly ItemID IronScrollId = new ItemID(IRON_SCROLL_ID);

        private List<ItemDefinition> _definitions;
        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private StubTownHubQuery _townHub;
        private ManualClock _clock;
        private NpcInteractionSessionTracker _tracker;
        private ScriptedRandomProvider _random;
        private EnhancementService _service;
        private RecordingClientMessageOutbox _outbox;
        private RecordingAttemptStarter _starter;
        private DictionaryDedupLookup _dedupLookup;
        private InboundDispatchHarness _harness;
        private uint _sequence;
        private uint _tick;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SWORD_ID, "Iron Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron)),
                ItemDefinitionBuilder.Build(BRONZE_SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SCROLL_ID, "Iron Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Iron)),
            };
            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);

            _townHub = new StubTownHubQuery();
            _clock = new ManualClock();
            _tracker = new NpcInteractionSessionTracker(_townHub, () => _clock.Now, SESSION_LIFETIME_SECONDS);
            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, _tracker, _random);

            _outbox = new RecordingClientMessageOutbox();
            _starter = new RecordingAttemptStarter();
            _dedupLookup = new DictionaryDedupLookup();

            _harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            RegisterAll(_harness.Dispatcher);
            _harness.Dispatcher.Seal();
            _harness.AddClient(CLIENT);
            _sequence = 1u;
            _tick = FIRST_DISPATCH_TICK;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void RegisterAll(IInboundRequestDispatcher dispatcher)
        {
            var enhancementHandlers = new EnhancementClientRequestHandlers(_service, _outbox, _dedupLookup, _starter);
            var npcHandlers = new NpcInteractionRequestHandlers(_tracker, _outbox);
            EnhancementInboundRegistration.Register(dispatcher, enhancementHandlers, npcHandlers);
        }

        private void OpenSession()
        {
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, _tracker.Open(Player, NPC_ID));
        }

        // Baseline: Bronze sword at `level` in ITEM_SLOT, Bronze scrolls in SCROLL_SLOT, an open NPC session.
        private void SeedValidRequest(byte level)
        {
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, BronzeSwordId, 1, level);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, BronzeScrollId, SCROLL_QUANTITY);
            OpenSession();
        }

        private void Dispatch(ushort messageTypeId, byte[] body)
        {
            Assert.IsTrue(_harness.SendRequest(CLIENT, messageTypeId, _sequence++, body), "The intake must accept the request.");
            _harness.Dispatcher.DispatchTick(_tick++);
        }

        private void SendAttempt(uint requestId, int itemSlot, int scrollSlot)
        {
            var body = new byte[EnhancementAttemptRequest.BodySize];
            EnhancementAttemptRequestCodec.WriteBody(body, requestId, (byte)itemSlot, (byte)scrollSlot);
            Dispatch(EnhancementAttemptRequest.MessageTypeId, body);
        }

        private void SendPreview(int itemSlot, int scrollSlot)
        {
            var body = new byte[EnhancementPreviewRequest.BodySize];
            EnhancementPreviewRequestCodec.WriteBody(body, (byte)itemSlot, (byte)scrollSlot);
            Dispatch(EnhancementPreviewRequest.MessageTypeId, body);
        }

        private void SendCancel()
        {
            Dispatch(CancelEnhancement.MessageTypeId, new byte[CancelEnhancement.BodySize]);
        }

        private void SendOpenNpc()
        {
            var body = new byte[OpenNPCInteraction.BodySize];
            OpenNPCInteractionCodec.WriteBody(body, NPC_ID);
            Dispatch(OpenNPCInteraction.MessageTypeId, body);
        }

        private void SendCloseNpc()
        {
            Dispatch(CloseNPCInteraction.MessageTypeId, new byte[CloseNPCInteraction.BodySize]);
        }

        private List<RecordedClientMessage> Sent(ushort messageTypeId)
        {
            return _outbox.ForClient(CLIENT, messageTypeId);
        }

        private InventorySlot[] BagCopy()
        {
            var slots = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = _inventory.GetSlot(Player, i);
            return slots;
        }

        private bool[] LockCopy()
        {
            var locks = new bool[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < locks.Length; i++)
                locks[i] = _inventory.IsSlotLocked(Player, i);
            return locks;
        }

        private void AssertBagAndLocksUnchanged(InventorySlot[] bagBefore, bool[] locksBefore)
        {
            CollectionAssert.AreEqual(bagBefore, BagCopy(), "The bag must be unchanged.");
            CollectionAssert.AreEqual(locksBefore, LockCopy(), "The slot locks must be unchanged.");
        }

        private EnhancementResultCode DecodeAttemptResult(RecordedClientMessage message, out uint requestId, out byte newLevel)
        {
            Assert.IsTrue(EnhancementAttemptResultMessageCodec.TryReadBody(
                message.Body, out requestId, out EnhancementResultCode code, out newLevel, out bool isUnknown));
            Assert.IsFalse(isUnknown);
            return code;
        }

        private EnhancementResultCode DecodePreviewRejected(RecordedClientMessage message, out byte itemSlot, out byte scrollSlot)
        {
            Assert.IsTrue(EnhancementPreviewRejectedCodec.TryReadBody(
                message.Body, out itemSlot, out scrollSlot, out EnhancementResultCode code, out bool isUnknown));
            Assert.IsFalse(isUnknown);
            return code;
        }

        // ------------------------------------------------------------------
        // NPC session messages
        // ------------------------------------------------------------------

        [Test]
        public void OpenNpc_InTownHub_SendsOneOpenedAndSessionIsActive()
        {
            // Arrange / Act
            SendOpenNpc();

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(1, Sent(NPCInteractionOpened.MessageTypeId).Count);
            Assert.AreEqual(0, Sent(RejectedNotInTownHub.MessageTypeId).Count);
            Assert.IsTrue(_tracker.IsActive(Player));
        }

        [Test]
        public void OpenNpc_OutsideTownHub_SendsOneRejectedAndNoSession()
        {
            // Arrange
            _townHub.OutsideTownHub.Add(Player);

            // Act
            SendOpenNpc();

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(1, Sent(RejectedNotInTownHub.MessageTypeId).Count);
            Assert.AreEqual(0, Sent(NPCInteractionOpened.MessageTypeId).Count);
            Assert.IsFalse(_tracker.IsActive(Player));
        }

        [Test]
        public void CloseNpc_ClearsSessionAndSendsNothing()
        {
            // Arrange
            OpenSession();

            // Act
            SendCloseNpc();

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player));
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [TestCase(LEVEL_FOUR, LEVEL_FOUR_SUCCESS_RAW, LEVEL_FOUR_DESTRUCTION_RAW)]
        [TestCase(LEVEL_TWO, LEVEL_TWO_SUCCESS_RAW, LEVEL_TWO_DESTRUCTION_RAW)]
        public void Preview_ValidSelection_SendsOneStateUpdateWithTableValuesAndChangesNothing(
            byte level, ushort expectedSuccessRaw, ushort expectedDestructionRaw)
        {
            // Arrange
            SeedValidRequest(level);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            List<RecordedClientMessage> updates = Sent(EnhancementStateUpdate.MessageTypeId);
            Assert.AreEqual(1, updates.Count);
            Assert.AreEqual(0, Sent(EnhancementPreviewRejected.MessageTypeId).Count);
            byte[] body = updates[0].Body;
            Assert.IsTrue(EnhancementStateUpdateCodec.TryReadBody(
                body, out byte itemSlot, out byte scrollSlot, out byte currentLevel, out float _, out float _));
            Assert.AreEqual(ITEM_SLOT, itemSlot);
            Assert.AreEqual(SCROLL_SLOT, scrollSlot);
            Assert.AreEqual(level, currentLevel);
            Assert.AreEqual(expectedSuccessRaw, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(SUCCESS_RAW_OFFSET)));
            Assert.AreEqual(expectedDestructionRaw, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(DESTRUCTION_RAW_OFFSET)));
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsTrue(_tracker.IsActive(Player));
        }

        [Test]
        public void Preview_TierMismatch_SendsOneRejectedAndNoStateUpdate()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _inventory.SeedSlotForTesting(Player, IRON_SCROLL_SLOT, IronScrollId, SCROLL_QUANTITY);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendPreview(ITEM_SLOT, IRON_SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(0, Sent(EnhancementStateUpdate.MessageTypeId).Count);
            List<RecordedClientMessage> rejections = Sent(EnhancementPreviewRejected.MessageTypeId);
            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, DecodePreviewRejected(rejections[0], out byte itemSlot, out byte scrollSlot));
            Assert.AreEqual(ITEM_SLOT, itemSlot);
            Assert.AreEqual(IRON_SCROLL_SLOT, scrollSlot);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsTrue(_tracker.IsActive(Player));
        }

        [Test]
        public void Preview_NoNpcSession_SendsRejectedNoNpcSession()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _tracker.Close(Player);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(0, Sent(EnhancementStateUpdate.MessageTypeId).Count);
            List<RecordedClientMessage> rejections = Sent(EnhancementPreviewRejected.MessageTypeId);
            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedNoNPCSession, DecodePreviewRejected(rejections[0], out byte itemSlot, out byte scrollSlot));
            Assert.AreEqual(ITEM_SLOT, itemSlot);
            Assert.AreEqual(SCROLL_SLOT, scrollSlot);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsFalse(_tracker.IsActive(Player));
        }

        [Test]
        public void Preview_ItemAtMaxLevel_SendsRejectedAtMaxLevel()
        {
            // Arrange
            SeedValidRequest(EnhancementConfig.Default.MaxEnhancementLevel);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(0, Sent(EnhancementStateUpdate.MessageTypeId).Count);
            List<RecordedClientMessage> rejections = Sent(EnhancementPreviewRejected.MessageTypeId);
            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedAtMaxLevel, DecodePreviewRejected(rejections[0], out byte itemSlot, out byte scrollSlot));
            Assert.AreEqual(ITEM_SLOT, itemSlot);
            Assert.AreEqual(SCROLL_SLOT, scrollSlot);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsTrue(_tracker.IsActive(Player));
        }

        [Test]
        public void Preview_DuringOwnPendingAttempt_SendsRejectedConcurrentAttempt()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _random.EnqueueDouble(0.0);
            Assert.IsTrue(_service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT).IsPending, "Arrange: an attempt is pending.");
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(0, Sent(EnhancementStateUpdate.MessageTypeId).Count);
            List<RecordedClientMessage> rejections = Sent(EnhancementPreviewRejected.MessageTypeId);
            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual(EnhancementPreviewRejected.BodySize, rejections[0].Body.Length);
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, DecodePreviewRejected(rejections[0], out byte itemSlot, out byte scrollSlot));
            Assert.AreEqual(ITEM_SLOT, itemSlot);
            Assert.AreEqual(SCROLL_SLOT, scrollSlot);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsTrue(_tracker.IsActive(Player));
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "The preview must not disturb the pending attempt.");
        }

        [Test]
        public void Preview_PendingAttemptAndClosedNpcSession_ReportsNoNpcSessionBeforeConcurrentAttempt()
        {
            // Arrange — pins the validation order decided at readiness 2026-10-07 (user decision 2026-10-09: keep it):
            // EnhancementService.ValidateAttempt checks the NPC session before the pending-attempt check.
            SeedValidRequest(LEVEL_TWO);
            _random.EnqueueDouble(0.0);
            Assert.IsTrue(_service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT).IsPending, "Arrange: an attempt is pending.");
            _tracker.Close(Player);

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            List<RecordedClientMessage> rejections = Sent(EnhancementPreviewRejected.MessageTypeId);
            Assert.AreEqual(1, rejections.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedNoNPCSession, DecodePreviewRejected(rejections[0], out byte _, out byte _));
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "The attempt is still pending.");
        }

        // ------------------------------------------------------------------
        // Cancel
        // ------------------------------------------------------------------

        [Test]
        public void Cancel_BeforeAnyRequest_AddsNoMessageAndChangesNothing()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendCancel();

            // Assert
            Assert.AreEqual(0, _outbox.Messages.Count);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel);
        }

        // Covers the handler only: the starter is a recorder, so no real attempt is pending here.
        [Test]
        public void Cancel_AfterValidRequestReachedStarter_AddsNoMessageAndChangesNothing()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            SendAttempt(REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);
            Assert.AreEqual(1, _starter.Calls.Count, "Arrange: the request reached the starter.");
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendCancel();

            // Assert
            Assert.AreEqual(0, _outbox.Messages.Count);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT));
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel);
            Assert.AreEqual(SCROLL_QUANTITY, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity);
            Assert.AreEqual(1, _starter.Calls.Count);
        }

        [Test]
        public void Cancel_WhileAttemptPending_AddsNoMessageAndLeavesAttemptPending()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _random.EnqueueDouble(0.0);
            Assert.IsTrue(_service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT).IsPending, "Arrange: an attempt is pending.");
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();
            int messagesBefore = _outbox.Messages.Count;

            // Act
            SendCancel();

            // Assert
            Assert.AreEqual(messagesBefore, _outbox.Messages.Count);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
            Assert.IsTrue(_inventory.IsSlotLocked(Player, ITEM_SLOT));
            Assert.IsTrue(_service.IsAttemptInProgress(Player));
        }

        // ------------------------------------------------------------------
        // Rejected request
        // ------------------------------------------------------------------

        // Shared asserts of a rejected attempt request: one cap-exempt result with the echoed requestId,
        // newLevel 0, no EnhancementRequestReceived and no start.
        private void AssertSingleAttemptRejection(EnhancementResultCode expectedCode)
        {
            Assert.AreEqual(1, _outbox.Messages.Count);
            List<RecordedClientMessage> results = Sent(EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(expectedCode, DecodeAttemptResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);
            Assert.AreEqual(0, Sent(EnhancementRequestReceived.MessageTypeId).Count);
            Assert.AreEqual(0, _starter.Calls.Count);
        }

        [Test]
        public void AttemptRequest_TierMismatch_SendsOneRejectionResultAndDoesNotStart()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _inventory.SeedSlotForTesting(Player, IRON_SCROLL_SLOT, IronScrollId, SCROLL_QUANTITY);
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();

            // Act
            SendAttempt(REQUEST_ID, ITEM_SLOT, IRON_SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _outbox.Messages.Count);
            List<RecordedClientMessage> results = Sent(EnhancementAttemptResultMessage.MessageTypeId);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, DecodeAttemptResult(results[0], out uint requestId, out byte newLevel));
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(0, newLevel);
            Assert.AreEqual(0, Sent(EnhancementRequestReceived.MessageTypeId).Count);
            Assert.AreEqual(0, _starter.Calls.Count);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);
        }

        [Test]
        public void AttemptRequest_ItemSlotOutOfRange_SendsRejectedItemNotFound()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(REQUEST_ID, InventoryConstants.INVENTORY_SLOT_COUNT, SCROLL_SLOT);

            // Assert
            AssertSingleAttemptRejection(EnhancementResultCode.RejectedItemNotFound);
        }

        [Test]
        public void AttemptRequest_ScrollSlotOutOfRange_SendsRejectedScrollNotFound()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(REQUEST_ID, ITEM_SLOT, InventoryConstants.INVENTORY_SLOT_COUNT);

            // Assert
            AssertSingleAttemptRejection(EnhancementResultCode.RejectedScrollNotFound);
        }

        [Test]
        public void AttemptRequest_EmptyScrollSlot_SendsRejectedScrollNotFound()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(REQUEST_ID, ITEM_SLOT, EMPTY_SLOT);

            // Assert
            AssertSingleAttemptRejection(EnhancementResultCode.RejectedScrollNotFound);
        }

        // ------------------------------------------------------------------
        // Duplicate request (AC-NC-49)
        // ------------------------------------------------------------------

        [Test]
        public void AttemptRequest_DuplicateRequestId_SendsNothingLogsOnceAndDoesNotStart_ThenNextIdReachesStarter()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            _dedupLookup.Set(Player, new EnhancementRequestDeduplicator(PERSISTED_REQUEST_ID));
            InventorySlot[] bagBefore = BagCopy();
            bool[] locksBefore = LockCopy();
            using var capture = new InboundLogCapture(HANDLER_LOG_PREFIX);

            // Act
            SendAttempt(PERSISTED_REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.AreEqual(1, capture.CountWarnings("DuplicateEnhancementRequest"));
            Assert.AreEqual(0, _starter.Calls.Count);
            AssertBagAndLocksUnchanged(bagBefore, locksBefore);

            // Act: the next id is not a duplicate.
            SendAttempt(PERSISTED_REQUEST_ID + 1u, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _starter.Calls.Count);
            Assert.AreEqual(PERSISTED_REQUEST_ID + 1u, _starter.Calls[0].RequestId);
            Assert.AreEqual(1, capture.CountWarnings("DuplicateEnhancementRequest"));
        }

        [Test]
        public void AttemptRequest_CharacterWithoutDeduplicator_IsNotADuplicate()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(PERSISTED_REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _starter.Calls.Count);
        }

        [Test]
        public void AttemptRequest_LookupReturnsNullDeduplicator_IsNotADuplicate()
        {
            // Arrange: the lookup reports success but holds null.
            SeedValidRequest(LEVEL_TWO);
            _dedupLookup.Set(Player, null);

            // Act
            SendAttempt(PERSISTED_REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _starter.Calls.Count);
            Assert.AreEqual(PERSISTED_REQUEST_ID, _starter.Calls[0].RequestId);
        }

        [Test]
        public void AttemptRequest_DeduplicatorWithoutLastRequestId_IsNotADuplicate()
        {
            // Arrange: a deduplicator constructed without a persisted id.
            SeedValidRequest(LEVEL_TWO);
            var deduplicator = new EnhancementRequestDeduplicator();
            Assert.IsNull(deduplicator.LastEnhancementRequestId, "Arrange: no last request id.");
            _dedupLookup.Set(Player, deduplicator);

            // Act
            SendAttempt(PERSISTED_REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _starter.Calls.Count);
            Assert.AreEqual(PERSISTED_REQUEST_ID, _starter.Calls[0].RequestId);
        }

        [Test]
        public void AttemptRequest_DuplicateWithInvalidSlots_StillOnlyLogsDuplicate()
        {
            // Arrange: the duplicate check runs before validation.
            OpenSession();
            _dedupLookup.Set(Player, new EnhancementRequestDeduplicator(PERSISTED_REQUEST_ID));
            using var capture = new InboundLogCapture(HANDLER_LOG_PREFIX);

            // Act
            SendAttempt(PERSISTED_REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.AreEqual(1, capture.CountWarnings("DuplicateEnhancementRequest"));
        }

        // ------------------------------------------------------------------
        // Valid request
        // ------------------------------------------------------------------

        [Test]
        public void AttemptRequest_Valid_ReachesStarterOnceWithSenderAndSlotsAndSendsNothing()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(REQUEST_ID, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.AreEqual(1, _starter.Calls.Count);
            Assert.AreEqual(Player, _starter.Calls[0].CharacterId);
            Assert.AreEqual(REQUEST_ID, _starter.Calls[0].RequestId);
            Assert.AreEqual(ITEM_SLOT, _starter.Calls[0].ItemSlotIndex);
            Assert.AreEqual(SCROLL_SLOT, _starter.Calls[0].ScrollSlotIndex);
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void AttemptRequest_BodyShorterThanSix_IsDroppedWithMalformedWarning()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);
            using var capture = new InboundLogCapture(HANDLER_LOG_PREFIX);

            // Act
            Dispatch(EnhancementAttemptRequest.MessageTypeId, new byte[EnhancementAttemptRequest.BodySize - 1]);

            // Assert
            Assert.AreEqual(1, capture.CountWarnings("InboundMessageMalformed"));
            Assert.AreEqual(0, _starter.Calls.Count);
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [TestCase(EnhancementPreviewRequest.MessageTypeId, 1, HANDLER_LOG_PREFIX)]
        [TestCase(OpenNPCInteraction.MessageTypeId, 3, NPC_HANDLER_LOG_PREFIX)]
        public void TooShortBody_IsDroppedWithOneMalformedWarningAndNoReplyOrSession(
            ushort messageTypeId, int bodyLength, string logPrefix)
        {
            // Arrange
            using var capture = new InboundLogCapture(logPrefix);

            // Act
            Dispatch(messageTypeId, new byte[bodyLength]);

            // Assert
            Assert.AreEqual(1, capture.CountWarnings(MALFORMED_WARNING));
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.IsFalse(_tracker.IsActive(Player), "A malformed open must not start a session.");
        }

        // ------------------------------------------------------------------
        // Cap exemption flag (CR-NET-7.7)
        // ------------------------------------------------------------------

        [Test]
        public void CapFlag_RejectionResult_IsExempt()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendAttempt(REQUEST_ID, InventoryConstants.INVENTORY_SLOT_COUNT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(Sent(EnhancementAttemptResultMessage.MessageTypeId)[0].IsCapExempt);
        }

        [Test]
        public void CapFlag_StateUpdate_IsNotExempt()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendPreview(ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(Sent(EnhancementStateUpdate.MessageTypeId)[0].IsCapExempt);
        }

        [Test]
        public void CapFlag_PreviewRejected_IsNotExempt()
        {
            // Arrange
            SeedValidRequest(LEVEL_TWO);

            // Act
            SendPreview(InventoryConstants.INVENTORY_SLOT_COUNT, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(Sent(EnhancementPreviewRejected.MessageTypeId)[0].IsCapExempt);
        }

        [Test]
        public void CapFlag_NpcInteractionOpened_IsNotExempt()
        {
            // Act
            SendOpenNpc();

            // Assert
            Assert.IsFalse(Sent(NPCInteractionOpened.MessageTypeId)[0].IsCapExempt);
        }

        [Test]
        public void CapFlag_RejectedNotInTownHub_IsNotExempt()
        {
            // Arrange
            _townHub.OutsideTownHub.Add(Player);

            // Act
            SendOpenNpc();

            // Assert
            Assert.IsFalse(Sent(RejectedNotInTownHub.MessageTypeId)[0].IsCapExempt);
        }

        // ------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------

        /// <summary>Records <c>Register</c> calls; every other member is a no-op.</summary>
        private sealed class RecordingDispatcher : IInboundRequestDispatcher
        {
            public readonly List<InboundRequestDescriptor> Descriptors = new List<InboundRequestDescriptor>();

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) => Descriptors.Add(descriptor);

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        [Test]
        public void Register_FiveTypes_AcceptedWithHeldFalseBoundsAndOwnTags()
        {
            // Arrange
            var recorder = new RecordingDispatcher();

            // Act
            RegisterAll(recorder);

            // Assert
            var expected = new (ushort Type, int Bound, RpcTypeTag Tag)[]
            {
                (EnhancementAttemptRequest.MessageTypeId, EnhancementAttemptRequest.BodySize, RpcTypeTag.EnhancementAttemptRequest),
                (EnhancementPreviewRequest.MessageTypeId, EnhancementPreviewRequest.BodySize, RpcTypeTag.EnhancementPreviewRequest),
                (CancelEnhancement.MessageTypeId, CancelEnhancement.BodySize, RpcTypeTag.CancelEnhancement),
                (OpenNPCInteraction.MessageTypeId, OpenNPCInteraction.BodySize, RpcTypeTag.OpenNPCInteraction),
                (CloseNPCInteraction.MessageTypeId, CloseNPCInteraction.BodySize, RpcTypeTag.CloseNPCInteraction),
            };
            Assert.AreEqual(expected.Length, recorder.Descriptors.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].Type, recorder.Descriptors[i].MessageTypeId, "type " + i);
                Assert.AreEqual(expected[i].Bound, recorder.Descriptors[i].MaxBodyBytes, "bound " + i);
                Assert.AreEqual(expected[i].Tag, recorder.Descriptors[i].RpcTypeTag, "tag " + i);
                Assert.IsFalse(recorder.Descriptors[i].HeldDuringIrreversibleWrite, "held " + i);
            }
            CollectionAssert.AllItemsAreUnique(new List<RpcTypeTag>(Array.ConvertAll(expected, row => row.Tag)));
        }

        [Test]
        public void Register_OnRealDispatcherInDevelopmentBuild_DoesNotThrow()
        {
            // Arrange
            var harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);

            // Act / Assert
            Assert.DoesNotThrow(() =>
            {
                RegisterAll(harness.Dispatcher);
                harness.Dispatcher.Seal();
            });
        }

        [TestCase(EnhancementAttemptRequest.MessageTypeId, EnhancementAttemptRequest.BodySize + 1)]
        [TestCase(EnhancementPreviewRequest.MessageTypeId, EnhancementPreviewRequest.BodySize + 1)]
        [TestCase(CancelEnhancement.MessageTypeId, CancelEnhancement.BodySize + 1)]
        [TestCase(OpenNPCInteraction.MessageTypeId, OpenNPCInteraction.BodySize + 1)]
        [TestCase(CloseNPCInteraction.MessageTypeId, CloseNPCInteraction.BodySize + 1)]
        public void Register_OverLengthBody_IsDroppedAtIntakeAndHandlerIsNotReached(ushort messageTypeId, int bodyLength)
        {
            // Arrange: no session is opened, so a handler that ran would show in the tracker or outbox.
            byte[] body = InboundMessageBuilder.Body(bodyLength, InboundTestIds.SeedOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            // Act
            bool accepted = _harness.SendRequest(CLIENT, messageTypeId, _sequence++, body);
            _harness.Dispatcher.DispatchTick(_tick++);

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.AreEqual(0, _starter.Calls.Count);
            Assert.IsFalse(_tracker.IsActive(Player), "The open handler must not have run.");
            Assert.AreEqual(1, capture.CountWarnings(MALFORMED_WARNING));
        }

        [TestCase(RpcTypeTag.EnhancementAttemptRequest)]
        [TestCase(RpcTypeTag.EnhancementPreviewRequest)]
        [TestCase(RpcTypeTag.CancelEnhancement)]
        [TestCase(RpcTypeTag.OpenNPCInteraction)]
        [TestCase(RpcTypeTag.CloseNPCInteraction)]
        public void RpcTypeTag_NewEnhancementTags_AreNotRateLimited(RpcTypeTag tag)
        {
            // Act / Assert
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(tag));
        }
    }
}

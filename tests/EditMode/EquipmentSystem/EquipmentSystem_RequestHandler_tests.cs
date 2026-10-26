using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.EquipmentSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.CharacterStats;
using IronGrind.Tests.EditMode.Integration.EnhancementSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Stats = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.Tests.EditMode.EquipmentSystem
{
    /// <summary>
    /// EditMode integration tests for Equipment Story 008: <see cref="EquipRequest"/> driven through the real
    /// <see cref="InboundRequestDispatcher"/> (registered by <see cref="EquipmentInboundRegistration"/>), the real
    /// <see cref="EquipmentService"/>, <see cref="InventoryService"/> and <c>CharacterStats</c>; replies are read from a
    /// recording outbox and decoded with <see cref="EquipResultMessageCodec"/> (equipment-system.md CR-EQS-13 and the
    /// wire mapping table; networking-wire-protocol.md <c>EquipRequest</c>; ADR-014 Decisions 1 and 3; TR-equip-011).
    /// </summary>
    [TestFixture]
    internal sealed class EquipmentSystem_RequestHandler_Tests
    {
        private const uint CLIENT = InboundTestIds.ClientOne;
        private const uint OTHER_CLIENT = InboundTestIds.ClientTwo;
        private const uint FIRST_DISPATCH_TICK = 8u;
        private const byte MAX_ENHANCEMENT_LEVEL = 10;

        private const int BASE_STR = 62;
        private const float DARK_STEEL_MIN_STR = 85f;
        private const byte SWORD_LEVEL = 3;
        private const byte UNKNOWN_GEAR_SLOT = 7;

        private const string HANDLER_LOG_PREFIX = "[EquipmentClientRequestHandlers]";
        private const string MALFORMED_WARNING = "InboundMessageMalformed";

        private static readonly CharacterID Player = InboundDispatchHarness.CharacterOf(CLIENT);
        private static readonly EntityID PlayerEntity = InboundDispatchHarness.EntityOf(CLIENT);

        private static readonly ItemID IronSwordId = new ItemID(102u);
        private static readonly ItemID DarkSteelSwordId = new ItemID(101u);
        private static readonly ItemID IronHelmetId = new ItemID(201u);
        private static readonly ItemID PotionId = new ItemID(501u);

        private FakeItemDatabase _db;
        private InventoryService _inventory;
        private Stats _stats;
        private EquipmentService _equipment;
        private RecordingClientMessageOutbox _outbox;
        private EquipmentClientRequestHandlers _handlers;
        private InboundDispatchHarness _harness;

        // A second handler over a scripted service, called directly (results the real service cannot produce on demand).
        private RecordingEquipmentService _scripted;
        private RecordingClientMessageOutbox _scriptedOutbox;
        private EquipmentClientRequestHandlers _scriptedHandlers;

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private uint _sequence;
        private uint _tick;

        /// <summary>One decoded <see cref="EquipResultMessage"/>.</summary>
        private readonly struct DecodedResult
        {
            public readonly uint RequestId;
            public readonly bool Success;
            public readonly EquipFailReason Reason;
            public readonly byte Slot;
            public readonly uint ItemId;
            public readonly byte Level;

            public DecodedResult(uint requestId, bool success, EquipFailReason reason, byte slot, uint itemId, byte level)
            {
                RequestId = requestId;
                Success = success;
                Reason = reason;
                Slot = slot;
                ItemId = itemId;
                Level = level;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
            _db = new FakeItemDatabase();
            _inventory = new InventoryService(_db, () => 0u, MAX_ENHANCEMENT_LEVEL);
            _inventory.RegisterCharacter(Player);
            _stats = CharacterStatsFixture.Create();
            _stats.SetBaseStat(PlayerEntity, StatID.Strength, BASE_STR);
            _equipment = new EquipmentService(
                _inventory, _stats, _db, new EnhancementBonusProvider(EnhancementConfig.Default), EnhancementConfig.Default, false);
            _equipment.RegisterCharacter(Player, PlayerEntity);

            AddEquipment(IronSwordId, GearSlot.Weapon, null, 0f);
            AddEquipment(DarkSteelSwordId, GearSlot.Weapon, StatID.Strength, DARK_STEEL_MIN_STR);
            AddEquipment(IronHelmetId, GearSlot.Helmet, null, 0f);
            ItemDefinition potion = ItemDefinitionBuilder.Build(PotionId.RawValue, "Potion", ItemCategory.Consumable, stackLimit: 99);
            _created.Add(potion);
            _db.Add(potion);

            _outbox = new RecordingClientMessageOutbox();
            _handlers = new EquipmentClientRequestHandlers(_equipment, _outbox);
            _harness = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            EquipmentInboundRegistration.Register(_harness.Dispatcher, _handlers);
            _harness.Dispatcher.Seal();
            _harness.AddClient(CLIENT);
            _harness.AddClient(OTHER_CLIENT);
            _sequence = 1u;
            _tick = FIRST_DISPATCH_TICK;

            _scripted = new RecordingEquipmentService();
            _scriptedOutbox = new RecordingClientMessageOutbox();
            _scriptedHandlers = new EquipmentClientRequestHandlers(_scripted, _scriptedOutbox);
        }

        private static InboundRequestContext ScriptedContext(uint clientId)
            => new InboundRequestContext(clientId, InboundDispatchHarness.EntityOf(clientId), InboundDispatchHarness.CharacterOf(clientId),
                EquipRequest.MessageTypeId, FIRST_DISPATCH_TICK, FIRST_DISPATCH_TICK, false);

        [TearDown]
        public void TearDown()
        {
            foreach (ItemDefinition definition in _created)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }

            _created.Clear();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────

        private void AddEquipment(ItemID id, GearSlot slot, StatID? requirementStat, float requirementMin)
        {
            EquipmentData data = EquipmentData.CreateForTesting(slot, GearTier.Iron,
                new[] { StatModifierEntry.CreateForTesting(StatID.Strength, 5f) },
                equipRequirementStat: requirementStat, equipRequirementMin: requirementMin);
            ItemDefinition definition = ItemDefinitionBuilder.Build(
                id.RawValue, $"Item{id.RawValue}", ItemCategory.Equipment, isUpgradeable: true, equipmentData: data);
            _created.Add(definition);
            _db.Add(definition);
        }

        private int PutInBag(ItemID id, byte level = 0)
        {
            MoveItemInResult placed = _inventory.MoveItemIn(Player, id, level);
            Assert.IsTrue(placed.Success, "test setup: bag placement");
            return placed.SlotIndex;
        }

        private static byte[] RequestBody(uint requestId, GearSlot slot, ItemID itemId, int inventorySlot)
        {
            var body = new byte[EquipRequest.BodySize];
            EquipRequestCodec.WriteBody(body, requestId, (byte)slot, itemId.RawValue, (byte)inventorySlot);
            return body;
        }

        private void Accept(byte[] body)
        {
            Assert.IsTrue(_harness.SendRequest(CLIENT, EquipRequest.MessageTypeId, _sequence++, body), "The intake must accept the request.");
        }

        private void Send(byte[] body)
        {
            Accept(body);
            _harness.Dispatcher.DispatchTick(_tick++);
        }

        private void SendEquip(uint requestId, GearSlot slot, ItemID itemId, int inventorySlot)
            => Send(RequestBody(requestId, slot, itemId, inventorySlot));

        private void SendUnequip(uint requestId, GearSlot slot)
            => Send(RequestBody(requestId, slot, ItemID.Invalid, EquipRequest.UnusedInventorySlot));

        private List<RecordedClientMessage> Results() => _outbox.ForClient(CLIENT, EquipResultMessage.MessageTypeId);

        private static DecodedResult Decode(RecordedClientMessage message)
        {
            Assert.IsTrue(EquipResultMessageCodec.TryReadBody(message.Body, out uint requestId, out bool success,
                out EquipFailReason reason, out byte slot, out uint itemId, out byte level, out bool unknown), "decodable result");
            Assert.IsFalse(unknown);
            return new DecodedResult(requestId, success, reason, slot, itemId, level);
        }

        private DecodedResult SingleResult()
        {
            Assert.AreEqual(1, _outbox.Messages.Count, "exactly one message sent");
            List<RecordedClientMessage> results = Results();
            Assert.AreEqual(1, results.Count, "the result goes to the requesting connection");
            Assert.IsFalse(results[0].IsCapExempt, "EquipResult is not cap-exempt");
            return Decode(results[0]);
        }

        // Equips through the service directly (no message) for the test's starting state.
        private void EquipForSetup(GearSlot slot, ItemID id, byte level)
        {
            Assert.AreEqual(EquipResult.Success, _equipment.Equip(Player, slot, id, PutInBag(id, level)), "test setup: equip");
        }

        // ── Equip / unequip routing ────────────────────────────────────────────────────────

        [Test]
        public void Equip_BagItemIntoEmptyWeaponSlot_OneSuccessResultWithNewSlotContents()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId, SWORD_LEVEL);

            // Act
            SendEquip(11u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.AreEqual(11u, result.RequestId);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(EquipFailReason.None, result.Reason);
            Assert.AreEqual((byte)GearSlot.Weapon, result.Slot);
            Assert.AreEqual(IronSwordId.RawValue, result.ItemId);
            Assert.AreEqual(SWORD_LEVEL, result.Level);
            Assert.AreEqual(IronSwordId, _equipment.GetEquippedWeaponItemID(Player));
        }

        [Test]
        public void Equip_OtherConnection_ReceivesNothing()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);

            // Act
            SendEquip(1u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Assert
            Assert.AreEqual(0, _outbox.ForClient(OTHER_CLIENT).Count);
            Assert.IsTrue(_outbox.Messages.TrueForAll(message => message.ClientId == CLIENT));
        }

        [Test]
        public void Unequip_ItemIdZero_RoutesToUnequipAndEchoesEmptySlot()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSwordId, SWORD_LEVEL);

            // Act
            SendUnequip(12u, GearSlot.Weapon);

            // Assert
            DecodedResult result = SingleResult();
            Assert.AreEqual(12u, result.RequestId);
            Assert.IsTrue(result.Success);
            Assert.AreEqual(EquipFailReason.None, result.Reason);
            Assert.AreEqual(0u, result.ItemId);
            Assert.AreEqual(0, result.Level);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
            Assert.IsTrue(_inventory.HasItem(Player, IronSwordId), "The sword went back to the bag.");
        }

        [Test]
        public void Unequip_InventorySlotFieldIsIgnored()
        {
            // Arrange: a non-0xFF inventorySlot with itemId 0 is still an unequip.
            EquipForSetup(GearSlot.Weapon, IronSwordId, 0);

            // Act
            Send(RequestBody(13u, GearSlot.Weapon, ItemID.Invalid, 4));

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0u, result.ItemId);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
        }

        // ── Failures and NoChange echo the slot ────────────────────────────────────────────

        [Test]
        public void Equip_SwapRejectedByStatGate_FailureEchoesOldItemAndLevel()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSwordId, SWORD_LEVEL);
            int bagSlot = PutInBag(DarkSteelSwordId);

            // Act
            SendEquip(14u, GearSlot.Weapon, DarkSteelSwordId, bagSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.StatRequirementNotMet, result.Reason);
            Assert.AreEqual(1, (byte)result.Reason);
            Assert.AreEqual(IronSwordId.RawValue, result.ItemId);
            Assert.AreEqual(SWORD_LEVEL, result.Level);
        }

        [Test]
        public void Equip_SameItemAndLevel_NoChangeReportsSuccessWithUnchangedSlot()
        {
            // Arrange
            EquipForSetup(GearSlot.Weapon, IronSwordId, SWORD_LEVEL);
            int bagSlot = PutInBag(IronSwordId, SWORD_LEVEL);

            // Act
            SendEquip(15u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(EquipFailReason.None, result.Reason);
            Assert.AreEqual(IronSwordId.RawValue, result.ItemId);
            Assert.AreEqual(SWORD_LEVEL, result.Level);
            Assert.IsTrue(_inventory.HasItem(Player, IronSwordId), "The bag copy was not moved.");
        }

        [Test]
        public void Equip_LockedSource_ItemLocked()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);
            _inventory.LockSlot(Player, bagSlot);

            // Act
            SendEquip(16u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.ItemLocked, result.Reason);
            Assert.AreEqual(4, (byte)result.Reason);
            Assert.AreEqual(0u, result.ItemId);
        }

        [Test]
        public void Equip_StaleBagSlot_ItemNotInInventory()
        {
            // Arrange: the client's view says slot 0 holds the sword; it holds a potion.
            int potionSlot = PutInBag(PotionId);

            // Act
            SendEquip(17u, GearSlot.Weapon, IronSwordId, potionSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.ItemNotInInventory, result.Reason);
            Assert.AreEqual(5, (byte)result.Reason);
        }

        [Test]
        public void Equip_WrongSlotForItem_SlotMismatchEchoesUnchangedHelmetSlot()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);

            // Act
            SendEquip(18u, GearSlot.Helmet, IronSwordId, bagSlot);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.SlotMismatch, result.Reason);
            Assert.AreEqual((byte)GearSlot.Helmet, result.Slot);
            Assert.AreEqual(0u, result.ItemId);
        }

        [Test]
        public void Unequip_BagFull_InventoryFullEchoesEquippedItem()
        {
            // Arrange
            EquipForSetup(GearSlot.Helmet, IronHelmetId, 0);
            while (_inventory.HasFreeSlot(Player))
            {
                PutInBag(IronSwordId);
            }

            // Act
            SendUnequip(19u, GearSlot.Helmet);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.InventoryFull, result.Reason);
            Assert.AreEqual(IronHelmetId.RawValue, result.ItemId);
        }

        [Test]
        public void Equip_GearSlotOutOfRange_ReachesServiceGuard_SlotMismatchWithEmptyEcho()
        {
            // Arrange: the handler does not pre-validate; the service logs its own range error.
            int bagSlot = PutInBag(IronSwordId);
            var body = new byte[EquipRequest.BodySize];
            EquipRequestCodec.WriteBody(body, 20u, UNKNOWN_GEAR_SLOT, IronSwordId.RawValue, (byte)bagSlot);
            LogAssert.Expect(LogType.Error, new Regex(@"gear slot 7 is out of range"));

            // Act
            Send(body);

            // Assert
            DecodedResult result = SingleResult();
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.SlotMismatch, result.Reason);
            Assert.AreEqual(UNKNOWN_GEAR_SLOT, result.Slot);
            Assert.AreEqual(0u, result.ItemId);
            Assert.AreEqual(0, result.Level);
            LogAssert.NoUnexpectedReceived();
        }

        // ── Retransmits ────────────────────────────────────────────────────────────────────

        [Test]
        public void Duplicate_SameRequestIdTwice_SameResultResentAndEquipRanOnce()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId, SWORD_LEVEL);
            byte[] body = RequestBody(21u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Act
            Send(body);
            Send(body);

            // Assert: a second Equip would have answered ItemNotInInventory (the bag slot is now empty).
            List<RecordedClientMessage> results = Results();
            Assert.AreEqual(2, results.Count);
            CollectionAssert.AreEqual(results[0].Body, results[1].Body);
            Assert.IsTrue(Decode(results[1]).Success);
            Assert.AreEqual(IronSwordId, _equipment.GetEquippedWeaponItemID(Player));
            Assert.IsFalse(_inventory.HasItem(Player, IronSwordId));
        }

        [Test]
        public void Duplicate_OlderRequestIdReplayed_IsProcessedAndAnsweredByServiceGuards()
        {
            // Arrange: request 30 equipped the sword out of bagSlot.
            int bagSlot = PutInBag(IronSwordId);
            SendEquip(30u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Act: an older id (a client that restarted its counter, or a replay) with the same body.
            SendEquip(29u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Assert: processed, and the stale-render guard answers it; the sword stays equipped.
            List<RecordedClientMessage> results = Results();
            Assert.AreEqual(2, results.Count);
            DecodedResult second = Decode(results[1]);
            Assert.AreEqual(29u, second.RequestId);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(EquipFailReason.ItemNotInInventory, second.Reason);
            Assert.AreEqual(IronSwordId.RawValue, second.ItemId);
            Assert.AreEqual(IronSwordId, _equipment.GetEquippedWeaponItemID(Player));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Duplicate_FailureResultRetransmitted_SameBytesAndServiceRanOnce()
        {
            // Arrange
            var context = ScriptedContext(CLIENT);
            _scripted.NextResult = EquipResult.StatRequirementNotMet;
            _scripted.SlotItem = IronSwordId;
            _scripted.SlotLevel = SWORD_LEVEL;
            byte[] body = RequestBody(80u, GearSlot.Weapon, DarkSteelSwordId, 0);

            // Act
            _scriptedHandlers.HandleEquipRequest(context, body);
            _scriptedHandlers.HandleEquipRequest(context, body);

            // Assert
            Assert.AreEqual(1, _scripted.Calls.FindAll(call => call == "Equip").Count);
            Assert.AreEqual(2, _scriptedOutbox.Messages.Count);
            CollectionAssert.AreEqual(_scriptedOutbox.Messages[0].Body, _scriptedOutbox.Messages[1].Body);
            DecodedResult result = Decode(_scriptedOutbox.Messages[1]);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(EquipFailReason.StatRequirementNotMet, result.Reason);
            Assert.AreEqual(IronSwordId.RawValue, result.ItemId);
            Assert.AreEqual(SWORD_LEVEL, result.Level);
        }

        [TestCase(EquipResult.InventoryError, EquipFailReason.InventoryError, (byte)6)]
        [TestCase(EquipResult.CriticalFailure, EquipFailReason.CriticalFailure, (byte)255)]
        public void Scripted_ServiceFailure_SentOnTheWireWithServiceSlotEcho(EquipResult serviceResult, EquipFailReason expected, byte expectedByte)
        {
            // Arrange
            _scripted.NextResult = serviceResult;
            _scripted.SlotItem = IronHelmetId;
            _scripted.SlotLevel = SWORD_LEVEL;

            // Act
            _scriptedHandlers.HandleEquipRequest(ScriptedContext(CLIENT), RequestBody(81u, GearSlot.Helmet, IronHelmetId, 2));

            // Assert
            Assert.AreEqual(1, _scriptedOutbox.Messages.Count);
            Assert.AreEqual(expectedByte, _scriptedOutbox.Messages[0].Body[5]);
            DecodedResult result = Decode(_scriptedOutbox.Messages[0]);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(expected, result.Reason);
            Assert.AreEqual((byte)GearSlot.Helmet, result.Slot);
            Assert.AreEqual(IronHelmetId.RawValue, result.ItemId);
            Assert.AreEqual(SWORD_LEVEL, result.Level);
        }

        [Test]
        public void Scripted_UndefinedServiceResult_ThrowsAndKeepsTheStoredResult()
        {
            // Arrange: request 82 stored, then the service returns a value with no wire mapping.
            var context = ScriptedContext(CLIENT);
            _scriptedHandlers.HandleEquipRequest(context, RequestBody(82u, GearSlot.Weapon, IronSwordId, 0));
            byte[] stored = _scriptedOutbox.Messages[0].Body;
            _scripted.NextResult = (EquipResult)200;

            // Act
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _scriptedHandlers.HandleEquipRequest(context, RequestBody(83u, GearSlot.Weapon, IronSwordId, 0)));
            _scriptedHandlers.HandleEquipRequest(context, RequestBody(82u, GearSlot.Weapon, IronSwordId, 0));

            // Assert: 82 is still the stored request, answered with its own bytes; no reply was sent for 83.
            Assert.AreEqual(2, _scriptedOutbox.Messages.Count);
            CollectionAssert.AreEqual(stored, _scriptedOutbox.Messages[1].Body);
            Assert.AreEqual(2, _scripted.Calls.FindAll(call => call == "Equip").Count, "82 was answered from the store.");
        }

        [Test]
        public void Scripted_TwoCharacters_StoresAreIndependent()
        {
            // Arrange
            var first = ScriptedContext(CLIENT);
            var second = ScriptedContext(OTHER_CLIENT);
            byte[] body = RequestBody(1u, GearSlot.Weapon, IronSwordId, 0);

            // Act
            _scriptedHandlers.HandleEquipRequest(first, body);
            _scriptedHandlers.HandleEquipRequest(second, body);
            _scriptedHandlers.ClearSession(first.CharacterId);
            _scriptedHandlers.HandleEquipRequest(second, body);

            // Assert: both processed once each; the second character's retransmit still comes from its store.
            Assert.AreEqual(2, _scripted.Calls.FindAll(call => call == "Equip").Count);
            Assert.AreEqual(1, _scriptedHandlers.TrackedSessionCount);
            Assert.AreEqual(1, _scriptedOutbox.ForClient(CLIENT).Count);
            Assert.AreEqual(2, _scriptedOutbox.ForClient(OTHER_CLIENT).Count);
        }

        [Test]
        public void Duplicate_NewerRequestId_IsProcessed()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);
            SendEquip(40u, GearSlot.Weapon, IronSwordId, bagSlot);

            // Act
            SendUnequip(41u, GearSlot.Weapon);

            // Assert
            List<RecordedClientMessage> results = Results();
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(41u, Decode(results[1]).RequestId);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
        }

        [Test]
        public void ClearSession_ForgetsStoredRequest_SameIdIsProcessedAgain()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);
            SendEquip(50u, GearSlot.Weapon, IronSwordId, bagSlot);
            Assert.AreEqual(1, _handlers.TrackedSessionCount);

            // Act
            _handlers.ClearSession(Player);
            SendUnequip(50u, GearSlot.Weapon);

            // Assert
            Assert.AreEqual(1, _handlers.TrackedSessionCount);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player), "The unequip ran: not a retransmit.");
        }

        // ── Held during an irreversible write ──────────────────────────────────────────────

        [Test]
        public void Held_GateClosed_RequestsWaitThenRunInArrivalOrderWhenOpened()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);
            _harness.Gate.Close(Player);
            Accept(RequestBody(60u, GearSlot.Weapon, IronSwordId, bagSlot));
            Accept(RequestBody(61u, GearSlot.Weapon, ItemID.Invalid, EquipRequest.UnusedInventorySlot));
            _harness.Dispatcher.DispatchTick(_tick++);
            Assert.AreEqual(0, _outbox.Messages.Count, "Nothing runs while the gate is closed.");
            Assert.AreEqual(2, _harness.Dispatcher.HeldCount(Player));

            // Act
            _harness.Gate.Open(Player);
            _harness.Dispatcher.DispatchTick(_tick++);

            // Assert: equip then unequip, in arrival order.
            List<RecordedClientMessage> results = Results();
            Assert.AreEqual(2, results.Count);
            DecodedResult first = Decode(results[0]);
            DecodedResult second = Decode(results[1]);
            Assert.AreEqual(60u, first.RequestId);
            Assert.AreEqual(IronSwordId.RawValue, first.ItemId);
            Assert.AreEqual(61u, second.RequestId);
            Assert.AreEqual(0u, second.ItemId);
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
        }

        // ── Malformed bodies ───────────────────────────────────────────────────────────────

        [Test]
        public void Malformed_ShortBody_DroppedByHandlerWithWarningNoServiceCall()
        {
            // Arrange
            int bagSlot = PutInBag(IronSwordId);
            byte[] full = RequestBody(70u, GearSlot.Weapon, IronSwordId, bagSlot);
            var shortBody = new byte[EquipRequest.BodySize - 1];
            Array.Copy(full, shortBody, shortBody.Length);
            using var capture = new InboundLogCapture(HANDLER_LOG_PREFIX);

            // Act
            Send(shortBody);

            // Assert
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.AreEqual(1, capture.CountWarnings(MALFORMED_WARNING));
            Assert.AreEqual(ItemID.Invalid, _equipment.GetEquippedWeaponItemID(Player));
            Assert.IsTrue(_inventory.HasItem(Player, IronSwordId));
        }

        [Test]
        public void Malformed_LongBody_DroppedAtIntake()
        {
            // Arrange
            byte[] body = InboundMessageBuilder.Body(EquipRequest.BodySize + 1, InboundTestIds.SeedOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            // Act
            bool accepted = _harness.SendRequest(CLIENT, EquipRequest.MessageTypeId, _sequence++, body);
            _harness.Dispatcher.DispatchTick(_tick++);

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(0, _outbox.Messages.Count);
            Assert.AreEqual(1, capture.CountWarnings(MALFORMED_WARNING));
        }

        // ── Registration and construction ──────────────────────────────────────────────────

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
        public void Register_EquipRequest_HeldWithTenByteBoundAndOwnUnlimitedTag()
        {
            // Arrange
            var recorder = new RecordingDispatcher();

            // Act
            EquipmentInboundRegistration.Register(recorder, _handlers);

            // Assert
            Assert.AreEqual(1, recorder.Descriptors.Count);
            InboundRequestDescriptor descriptor = recorder.Descriptors[0];
            Assert.AreEqual(EquipRequest.MessageTypeId, descriptor.MessageTypeId);
            Assert.AreEqual(RpcTypeTag.EquipRequest, descriptor.RpcTypeTag);
            Assert.AreEqual(8, (byte)RpcTypeTag.EquipRequest);
            Assert.IsTrue(descriptor.HeldDuringIrreversibleWrite);
            Assert.AreEqual(10, descriptor.MaxBodyBytes);
            Assert.IsFalse(CrossCuttingRpcGuardChain.IsRateLimited(RpcTypeTag.EquipRequest));
        }

        [Test]
        public void Register_NullArguments_Throw()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => EquipmentInboundRegistration.Register(null, _handlers));
            Assert.Throws<ArgumentNullException>(() => EquipmentInboundRegistration.Register(new RecordingDispatcher(), null));
        }

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new EquipmentClientRequestHandlers(null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new EquipmentClientRequestHandlers(_equipment, null));
        }
    }
}

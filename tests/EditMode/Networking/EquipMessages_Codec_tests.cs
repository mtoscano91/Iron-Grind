using System;
using IronGrind.EquipmentSystem;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Equipment Story 008's wire types: the <see cref="EquipRequest"/> and
    /// <see cref="EquipResultMessage"/> codecs, the <see cref="EquipFailReason"/> byte values, the
    /// <c>EquipResult</c> → wire mapping (OQ-EQS-9 option a) and the two routing rows
    /// (networking-wire-protocol.md <c>EquipRequest</c> / <c>EquipResult</c>; equipment-system.md CR-EQS-13; TR-equip-011).
    /// </summary>
    [TestFixture]
    internal sealed class EquipMessages_Codec_Tests
    {
        private const byte LAST_GEAR_SLOT = 6;
        private const byte LAST_BAG_SLOT = 19;
        private const byte MAX_LEVEL = 10;

        // ── EquipRequest ───────────────────────────────────────────────────────────────────

        [Test]
        public void EquipRequest_MaxValues_RoundTripsInTenBytes()
        {
            // Arrange
            var body = new byte[EquipRequest.BodySize];

            // Act
            int written = EquipRequestCodec.WriteBody(body, uint.MaxValue, LAST_GEAR_SLOT, uint.MaxValue, LAST_BAG_SLOT);
            bool read = EquipRequestCodec.TryReadBody(body, out uint requestId, out byte gearSlot, out uint itemId, out byte inventorySlot);

            // Assert
            Assert.AreEqual(10, EquipRequest.BodySize);
            Assert.AreEqual(10, written);
            Assert.IsTrue(read);
            Assert.AreEqual(uint.MaxValue, requestId);
            Assert.AreEqual(LAST_GEAR_SLOT, gearSlot);
            Assert.AreEqual(uint.MaxValue, itemId);
            Assert.AreEqual(LAST_BAG_SLOT, inventorySlot);
        }

        [Test]
        public void EquipRequest_Unequip_RoundTripsItemZeroAndUnusedSlot()
        {
            // Arrange
            var body = new byte[EquipRequest.BodySize];
            const uint REQUEST_ID = 42u;

            // Act
            EquipRequestCodec.WriteBody(body, REQUEST_ID, 2, EquipRequest.UnequipItemId, EquipRequest.UnusedInventorySlot);
            bool read = EquipRequestCodec.TryReadBody(body, out uint requestId, out byte gearSlot, out uint itemId, out byte inventorySlot);

            // Assert
            Assert.IsTrue(read);
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.AreEqual(2, gearSlot);
            Assert.AreEqual(0u, itemId);
            Assert.AreEqual(0xFF, inventorySlot);
        }

        [Test]
        public void EquipRequest_LittleEndianLayout_MatchesWireOrder()
        {
            // Arrange
            var body = new byte[EquipRequest.BodySize];

            // Act
            EquipRequestCodec.WriteBody(body, 0x04030201u, 5, 0x0A090807u, 0x0B);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x07, 0x08, 0x09, 0x0A, 0x0B }, body);
        }

        [TestCase(EquipRequest.BodySize - 1)]
        [TestCase(EquipRequest.BodySize + 1)]
        [TestCase(0)]
        public void EquipRequest_WrongLengthBody_IsRejected(int length)
        {
            // Act
            bool read = EquipRequestCodec.TryReadBody(new byte[length], out uint requestId, out byte gearSlot, out uint itemId, out byte inventorySlot);

            // Assert
            Assert.IsFalse(read);
            Assert.AreEqual(0u, requestId);
            Assert.AreEqual(0, gearSlot);
            Assert.AreEqual(0u, itemId);
            Assert.AreEqual(0, inventorySlot);
        }

        // ── EquipResultMessage ─────────────────────────────────────────────────────────────

        [Test]
        public void EquipResult_MaxValues_RoundTripsInTwelveBytes()
        {
            // Arrange
            var body = new byte[EquipResultMessage.BodySize];

            // Act
            int written = EquipResultMessageCodec.WriteBody(body, uint.MaxValue, true, EquipFailReason.None, LAST_GEAR_SLOT, uint.MaxValue, MAX_LEVEL);
            bool read = EquipResultMessageCodec.TryReadBody(body, out uint requestId, out bool success, out EquipFailReason reason,
                out byte slot, out uint itemId, out byte level, out bool unknown);

            // Assert
            Assert.AreEqual(12, EquipResultMessage.BodySize);
            Assert.AreEqual(12, written);
            Assert.IsTrue(read);
            Assert.AreEqual(uint.MaxValue, requestId);
            Assert.IsTrue(success);
            Assert.AreEqual(EquipFailReason.None, reason);
            Assert.AreEqual(LAST_GEAR_SLOT, slot);
            Assert.AreEqual(uint.MaxValue, itemId);
            Assert.AreEqual(MAX_LEVEL, level);
            Assert.IsFalse(unknown);
        }

        [TestCase(EquipFailReason.StatRequirementNotMet)]
        [TestCase(EquipFailReason.InventoryFull)]
        [TestCase(EquipFailReason.SlotMismatch)]
        [TestCase(EquipFailReason.ItemLocked)]
        [TestCase(EquipFailReason.ItemNotInInventory)]
        [TestCase(EquipFailReason.InventoryError)]
        [TestCase(EquipFailReason.CriticalFailure)]
        public void EquipResult_Failure_RoundTripsReasonAndSlot(EquipFailReason expected)
        {
            // Arrange
            var body = new byte[EquipResultMessage.BodySize];
            const uint REQUEST_ID = 9u;
            const uint ITEM_ID = 1234u;
            const byte LEVEL = 3;

            // Act
            EquipResultMessageCodec.WriteBody(body, REQUEST_ID, false, expected, 1, ITEM_ID, LEVEL);
            bool read = EquipResultMessageCodec.TryReadBody(body, out uint requestId, out bool success, out EquipFailReason reason,
                out byte slot, out uint itemId, out byte level, out bool unknown);

            // Assert
            Assert.IsTrue(read);
            Assert.AreEqual(REQUEST_ID, requestId);
            Assert.IsFalse(success);
            Assert.AreEqual(0, body[4], "success is written as 0");
            Assert.AreEqual(expected, reason);
            Assert.AreEqual(1, slot);
            Assert.AreEqual(ITEM_ID, itemId);
            Assert.AreEqual(LEVEL, level);
            Assert.IsFalse(unknown);
        }

        [Test]
        public void EquipResult_LittleEndianLayout_MatchesWireOrder()
        {
            // Arrange
            var body = new byte[EquipResultMessage.BodySize];

            // Act
            EquipResultMessageCodec.WriteBody(body, 0x04030201u, true, EquipFailReason.ItemLocked, 6, 0x0A090807u, 0x0B);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x01, 0x04, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B }, body);
        }

        [TestCase(EquipResultMessage.BodySize - 1)]
        [TestCase(EquipResultMessage.BodySize + 1)]
        [TestCase(0)]
        public void EquipResult_WrongLengthBody_IsRejected(int length)
        {
            // Act
            bool read = EquipResultMessageCodec.TryReadBody(new byte[length], out _, out _, out _, out _, out _, out _, out _);

            // Assert
            Assert.IsFalse(read);
        }

        [Test]
        public void EquipResult_NonZeroSuccessByte_ReadsAsTrue()
        {
            // Arrange
            var body = new byte[EquipResultMessage.BodySize];
            EquipResultMessageCodec.WriteBody(body, 1u, false, EquipFailReason.None, 0, 0u, 0);
            body[4] = 2;

            // Act
            bool read = EquipResultMessageCodec.TryReadBody(body, out _, out bool success, out _, out _, out _, out _, out _);

            // Assert
            Assert.IsTrue(read);
            Assert.IsTrue(success);
        }

        [TestCase((byte)7)]
        [TestCase((byte)254)]
        public void EquipResult_UnknownFailReasonByte_DecodesAsFallbackWithOneWarning(byte raw)
        {
            // Arrange
            var body = new byte[EquipResultMessage.BodySize];
            EquipResultMessageCodec.WriteBody(body, 1u, false, EquipFailReason.None, 0, 0u, 0);
            body[5] = raw;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("out-of-range failReason byte " + raw));

            // Act
            bool read = EquipResultMessageCodec.TryReadBody(body, out _, out _, out EquipFailReason reason, out _, out _, out _, out bool unknown);

            // Assert
            Assert.IsTrue(read);
            Assert.IsTrue(unknown);
            Assert.AreEqual(EquipFailReason.CriticalFailure, reason);
            LogAssert.NoUnexpectedReceived();
        }

        // ── EquipFailReason and the mapping ────────────────────────────────────────────────

        [Test]
        public void EquipFailReason_ByteValues_MatchTheWireProtocol()
        {
            // Assert
            Assert.AreEqual(0, (byte)EquipFailReason.None);
            Assert.AreEqual(1, (byte)EquipFailReason.StatRequirementNotMet);
            Assert.AreEqual(2, (byte)EquipFailReason.InventoryFull);
            Assert.AreEqual(3, (byte)EquipFailReason.SlotMismatch);
            Assert.AreEqual(4, (byte)EquipFailReason.ItemLocked);
            Assert.AreEqual(5, (byte)EquipFailReason.ItemNotInInventory);
            Assert.AreEqual(6, (byte)EquipFailReason.InventoryError);
            Assert.AreEqual(255, (byte)EquipFailReason.CriticalFailure);
            Assert.AreEqual(8, Enum.GetValues(typeof(EquipFailReason)).Length);
        }

        [TestCase(EquipResult.Success, true, EquipFailReason.None)]
        [TestCase(EquipResult.NoChange, true, EquipFailReason.None)]
        [TestCase(EquipResult.StatRequirementNotMet, false, EquipFailReason.StatRequirementNotMet)]
        [TestCase(EquipResult.InventoryFull, false, EquipFailReason.InventoryFull)]
        [TestCase(EquipResult.SlotMismatch, false, EquipFailReason.SlotMismatch)]
        [TestCase(EquipResult.ItemLocked, false, EquipFailReason.ItemLocked)]
        [TestCase(EquipResult.ItemNotInInventory, false, EquipFailReason.ItemNotInInventory)]
        [TestCase(EquipResult.InventoryError, false, EquipFailReason.InventoryError)]
        [TestCase(EquipResult.CriticalFailure, false, EquipFailReason.CriticalFailure)]
        public void Mapping_EachEquipResult_MapsToTheGddPair(EquipResult result, bool expectedSuccess, EquipFailReason expectedReason)
        {
            // Act / Assert
            Assert.AreEqual(expectedSuccess, EquipmentClientRequestHandlers.IsSuccess(result));
            Assert.AreEqual(expectedReason, EquipmentClientRequestHandlers.ToWire(result));
        }

        [Test]
        public void Mapping_AllNineEquipResultValues_AreCovered()
        {
            // Assert: a new EquipResult member must get a row above.
            Assert.AreEqual(9, Enum.GetValues(typeof(EquipResult)).Length);
        }

        [Test]
        public void Mapping_UndefinedEquipResult_Throws()
        {
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => EquipmentClientRequestHandlers.ToWire((EquipResult)200));
        }

        // ── Routing rows ───────────────────────────────────────────────────────────────────

        [Test]
        public void Routing_EquipRequestAndResult_ArePriorityPathRodRowsWithTheirDirections()
        {
            // Act
            bool hasRequest = MessageRoutingRegistry.TryGetEntry(0xE140, out MessageRoutingEntry request);
            bool hasResult = MessageRoutingRegistry.TryGetEntry(0xE141, out MessageRoutingEntry result);

            // Assert
            Assert.AreEqual(0xE140, EquipRequest.MessageTypeId);
            Assert.AreEqual(0xE141, EquipResultMessage.MessageTypeId);
            Assert.IsTrue(hasRequest);
            Assert.IsTrue(hasResult);
            Assert.AreEqual(nameof(EquipRequest), request.MessageName);
            Assert.AreEqual(nameof(EquipResultMessage), result.MessageName);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, request.Channel);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, result.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, request.DeliveryContext);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, result.DeliveryContext);
            Assert.AreEqual(MessageDirection.ClientToServer, request.Direction);
            Assert.AreEqual(MessageDirection.ServerToOwningClient, result.Direction);
        }
    }
}

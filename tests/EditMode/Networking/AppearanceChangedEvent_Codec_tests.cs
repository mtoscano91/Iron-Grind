using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Equipment Story 009's wire type: the <see cref="AppearanceChangedEvent"/> codec and its
    /// routing row (networking-wire-protocol.md <c>AppearanceChangedEvent</c>; equipment-system.md CR-EQS-11).
    /// </summary>
    [TestFixture]
    internal sealed class AppearanceChangedEvent_Codec_Tests
    {
        [TestCase(uint.MaxValue, (byte)0xFF)]
        [TestCase(0u, (byte)0x00)]
        [TestCase(5001u, (byte)0xC8)]
        public void test_AppearanceChanged_RoundTrip_PreservesEntityAndFlags(uint entityId, byte flags)
        {
            // Arrange
            var body = new byte[AppearanceChangedEvent.BodySize];

            // Act
            int written = AppearanceChangedEventCodec.WriteBody(body, entityId, flags);
            bool read = AppearanceChangedEventCodec.TryReadBody(body, out uint decodedEntity, out byte decodedFlags);

            // Assert
            Assert.AreEqual(5, written);
            Assert.IsTrue(read);
            Assert.AreEqual(entityId, decodedEntity);
            Assert.AreEqual(flags, decodedFlags);
        }

        [Test]
        public void test_AppearanceChanged_EncodedLength_IsExactlyFiveBytes()
        {
            // Arrange
            var body = new byte[AppearanceChangedEvent.BodySize];

            // Act
            int written = AppearanceChangedEventCodec.WriteBody(body, 1u, 0x40);

            // Assert
            Assert.AreEqual(5, AppearanceChangedEvent.BodySize);
            Assert.AreEqual(5, written);
        }

        [Test]
        public void test_AppearanceChanged_LittleEndianLayout_MatchesWireOrder()
        {
            // Arrange
            var body = new byte[AppearanceChangedEvent.BodySize];

            // Act
            AppearanceChangedEventCodec.WriteBody(body, 0x04030201u, 0xC8);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, 0xC8 }, body);
        }

        [TestCase(AppearanceChangedEvent.BodySize - 1)]
        [TestCase(AppearanceChangedEvent.BodySize + 1)]
        [TestCase(0)]
        public void test_AppearanceChanged_WrongLengthBody_IsRejected(int length)
        {
            // Act
            bool read = AppearanceChangedEventCodec.TryReadBody(new byte[length], out uint entityId, out byte flags);

            // Assert
            Assert.IsFalse(read);
            Assert.AreEqual(0u, entityId);
            Assert.AreEqual(0, flags);
        }

        [Test]
        public void test_AppearanceChanged_MessageTypeId_Is0xE142()
        {
            // Assert
            Assert.AreEqual(0xE142, AppearanceChangedEvent.MessageTypeId);
        }

        [Test]
        public void test_AppearanceChanged_Routing_IsPriorityPathZoneBroadcastRow()
        {
            // Act
            bool has = MessageRoutingRegistry.TryGetEntry(AppearanceChangedEvent.MessageTypeId, out MessageRoutingEntry entry);

            // Assert
            Assert.IsTrue(has);
            Assert.AreEqual(nameof(AppearanceChangedEvent), entry.MessageName);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
            Assert.AreEqual(MessageDirection.ServerToAllZoneClients, entry.Direction);
        }
    }
}

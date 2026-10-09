using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 036 — <see cref="NetworkingCoreInboundRegistration"/>
    /// against the real <see cref="MessageRoutingRegistry"/>: <c>SetTarget</c> as a request and
    /// <c>HeartbeatMessage</c> as a connection-level message, and nothing else.
    /// </summary>
    [TestFixture]
    internal sealed class InboundDispatch_CoreRegistration_Tests
    {
        private const uint TargetEntityRaw = 900u;
        private const uint Sequence = 1u;
        private const uint DispatchTick = 8u;
        private const int ShortBodyLength = SetTarget.BodySize - 1;

        /// <summary>A dispatcher over the real routing registry with both core types registered and sealed.</summary>
        private static InboundDispatchHarness CreateCoreHarness(
            bool isDevelopmentBuild, TargetSlotTracker tracker, uint clientId)
        {
            var h = new InboundDispatchHarness(isDevelopmentBuild, null, MessageRoutingRegistry.TryGetEntry);
            var validZoneEntityIds = new List<EntityID> { new EntityID(TargetEntityRaw) };
            var setTargetHandler = new SetTargetRequestHandler(tracker, validZoneEntityIds);
            NetworkingCoreInboundRegistration.Register(h.Dispatcher, setTargetHandler, h.ConnectionSink);
            h.Dispatcher.Seal();
            h.AddClient(clientId);
            return h;
        }

        [Test]
        public void SetTarget_EndToEndThroughTryAcceptAndDispatchTick_SetsTargetInTracker()
        {
            // Arrange
            var tracker = new TargetSlotTracker();
            var h = CreateCoreHarness(false, tracker, InboundTestIds.ClientOne);
            var message = new byte[SetTarget.WireSize];
            SetTargetCodec.Write(message, Sequence, InboundTestIds.EnvelopeTick,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne).RawValue, TargetEntityRaw);

            // Act
            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.IsTrue(accepted);
            Assert.AreEqual(new EntityID(TargetEntityRaw), tracker.GetTarget(InboundTestIds.ClientOne));
        }

        [Test]
        public void SetTarget_BodyOfThreeBytes_HandlerLogsAnomalyAndTargetIsUnchanged()
        {
            // Arrange
            var tracker = new TargetSlotTracker();
            var h = CreateCoreHarness(false, tracker, InboundTestIds.ClientOne);
            byte[] message = InboundMessageBuilder.Request(SetTarget.MessageTypeId, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne).RawValue,
                InboundMessageBuilder.Body(ShortBodyLength, InboundTestIds.SeedOne));
            using var capture = new InboundLogCapture(InboundTestIds.SetTargetHandlerLogPrefix);

            // Act
            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);
            h.Dispatcher.DispatchTick(DispatchTick);

            // Assert
            Assert.IsTrue(accepted, "The intake only knows the maximum; the handler rejects the short body.");
            Assert.AreEqual(1, capture.CountWarnings("InboundMessageMalformed"));
            Assert.AreEqual(EntityID.Invalid, tracker.GetTarget(InboundTestIds.ClientOne));
        }

        [Test]
        public void Heartbeat_RegisteredByCoreRegistration_SinkGivenToRegisterReceivesIt()
        {
            // Arrange
            var h = CreateCoreHarness(false, new TargetSlotTracker(), InboundTestIds.ClientOne);
            var message = new byte[HeartbeatMessage.WireSize];
            MessageEnvelopeCodec.Write(message, HeartbeatMessage.CreateEnvelope(Sequence, InboundTestIds.EnvelopeTick));

            // Act
            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);

            // Assert
            Assert.IsTrue(accepted);
            Assert.AreEqual(1, h.ConnectionSink.Calls.Count);
            Assert.AreEqual(HeartbeatMessage.MessageTypeId, h.ConnectionSink.Calls[0].MessageTypeId);
            Assert.AreEqual(EntityID.Invalid, h.ConnectionSink.Calls[0].SenderEntityId);
            Assert.AreEqual(0, h.ConnectionSink.Calls[0].Body.Length);
        }

        [Test]
        public void Register_InDevelopmentBuildAgainstTheRealRegistry_ThrowsNothing()
        {
            // Arrange
            var h = new InboundDispatchHarness(true, null, MessageRoutingRegistry.TryGetEntry);
            var setTargetHandler = new SetTargetRequestHandler(new TargetSlotTracker(), new List<EntityID>());

            // Act / Assert — both types have a client-to-server row.
            Assert.DoesNotThrow(
                () => NetworkingCoreInboundRegistration.Register(h.Dispatcher, setTargetHandler, h.ConnectionSink));
        }

        [Test]
        public void Register_DoesNotRegisterDamageEvent_ItsMessagesAreDroppedAsUnknown()
        {
            // Arrange — DamageEvent has a routing row but is not a client-to-server request.
            var h = CreateCoreHarness(false, new TargetSlotTracker(), InboundTestIds.ClientOne);
            byte[] message = InboundMessageBuilder.Request(DamageEvent.MessageTypeId, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne).RawValue,
                InboundMessageBuilder.Body(SetTarget.BodySize, InboundTestIds.SeedOne));

            // Act
            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.ConnectionSink.Calls.Count);
        }
    }
}

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronGrind.Networking;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 036 — the registration half of
    /// <see cref="InboundRequestDispatcher"/> (ADR-014 Decision 3): register then seal, startup
    /// failures, the channel read from the routing table, and AC-MCR-03 for a type with no
    /// client-to-server routing row.
    /// </summary>
    [TestFixture]
    internal sealed class InboundDispatch_Registration_Tests
    {
        private const uint FirstSequence = 1u;
        private const uint SecondSequence = 2u;
        private const uint StaleSequence = 9u;
        private const uint NewerSequence = 10u;

        private static InboundDispatchHarness NewHarness(bool isDevelopmentBuild = false)
        {
            return new InboundDispatchHarness(isDevelopmentBuild);
        }

        private static InboundRequestDescriptor RequestWithBody(ushort messageTypeId, int maxBodyBytes)
        {
            return new InboundRequestDescriptor(messageTypeId, RpcTypeTag.SetTarget, false, (ushort)maxBodyBytes);
        }

        // =========================================================================================
        // Register, then seal.
        // =========================================================================================

        [Test]
        public void Register_AfterSeal_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.Seal();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.Register(InboundTestIds.PlainRod(), h.Handler.Handle));
        }

        [Test]
        public void RegisterConnectionLevel_AfterSeal_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.Seal();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), h.ConnectionSink));
        }

        [Test]
        public void TryAccept_BeforeSeal_ReturnsFalseAndReportsNoActivity()
        {
            // Arrange — types registered and the connection added, but Seal() not called.
            var h = NewHarness();
            h.RegisterStandardTypes();
            h.AddClient(InboundTestIds.ClientOne);

            // Act
            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, FirstSequence,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne));

            // Assert
            Assert.IsFalse(accepted, "TryAccept before Seal() must return false.");
            Assert.AreEqual(0, h.Activity.Calls.Count, "No activity may be reported before Seal().");
        }

        // =========================================================================================
        // Startup failures.
        // =========================================================================================

        [Test]
        public void Register_SameRequestTypeTwice_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.Register(InboundTestIds.PlainRod(), h.Handler.Handle);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.Register(InboundTestIds.PlainRod(), h.Handler.Handle));
        }

        [Test]
        public void RegisterConnectionLevel_SameTypeTwice_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), h.ConnectionSink);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), h.ConnectionSink));
        }

        [Test]
        public void Register_IdAlreadyRegisteredAsConnectionLevel_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), h.ConnectionSink);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.Register(RequestWithBody(InboundTestIds.TypeConnUu, InboundTestIds.UuBodySize), h.Handler.Handle));
        }

        [Test]
        public void RegisterConnectionLevel_IdAlreadyRegisteredAsRequest_ThrowsInvalidOperation()
        {
            // Arrange
            var h = NewHarness();
            h.Dispatcher.Register(InboundTestIds.UuA(), h.Handler.Handle);

            // Act / Assert
            var descriptor = new ConnectionMessageDescriptor(InboundTestIds.TypeUuA, InboundTestIds.ConnUuBodySize, false);
            Assert.Throws<InvalidOperationException>(
                () => h.Dispatcher.RegisterConnectionLevel(descriptor, h.ConnectionSink));
        }

        [Test]
        public void Register_NullHandler_ThrowsArgumentNull()
        {
            // Arrange
            var h = NewHarness();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(
                () => h.Dispatcher.Register(InboundTestIds.PlainRod(), null));
        }

        [Test]
        public void RegisterConnectionLevel_NullSink_ThrowsArgumentNull()
        {
            // Arrange
            var h = NewHarness();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(
                () => h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), null));
        }

        [Test]
        public void Register_BodyBoundOneAboveLimit_ThrowsArgumentException()
        {
            // Arrange
            var h = NewHarness();
            InboundRequestDescriptor descriptor = RequestWithBody(InboundTestIds.TypeChat, InboundTestIds.MaxRequestBodyBytes + 1);

            // Act / Assert — ArgumentException exactly, not a subclass.
            Assert.Throws<ArgumentException>(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void Register_BodyBoundAtLimit_IsAccepted()
        {
            // Arrange
            var h = NewHarness();
            InboundRequestDescriptor descriptor = RequestWithBody(InboundTestIds.TypeChat, InboundTestIds.MaxRequestBodyBytes);

            // Act / Assert
            Assert.DoesNotThrow(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void RegisterConnectionLevel_WithoutSenderBodyOneAboveLimit_ThrowsArgumentException()
        {
            // Arrange — 10-byte envelope: the limit is MAX_INBOUND_MESSAGE_BYTES less 10.
            var h = NewHarness();
            int limit = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ServerMessageEnvelope.WireSize;
            var descriptor = new ConnectionMessageDescriptor(InboundTestIds.TypeConnUu, (ushort)(limit + 1), false);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => h.Dispatcher.RegisterConnectionLevel(descriptor, h.ConnectionSink));
        }

        [Test]
        public void RegisterConnectionLevel_WithoutSenderBodyAtLimit_IsAccepted()
        {
            // Arrange
            var h = NewHarness();
            int limit = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ServerMessageEnvelope.WireSize;
            var descriptor = new ConnectionMessageDescriptor(InboundTestIds.TypeConnUu, (ushort)limit, false);

            // Act / Assert
            Assert.DoesNotThrow(() => h.Dispatcher.RegisterConnectionLevel(descriptor, h.ConnectionSink));
        }

        [Test]
        public void RegisterConnectionLevel_WithSenderBodyOneAboveLimit_ThrowsArgumentException()
        {
            // Arrange — 14-byte envelope: the limit is MAX_INBOUND_MESSAGE_BYTES less 14.
            var h = NewHarness();
            var descriptor = new ConnectionMessageDescriptor(
                InboundTestIds.TypeConnUu, (ushort)(InboundTestIds.MaxRequestBodyBytes + 1), true);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => h.Dispatcher.RegisterConnectionLevel(descriptor, h.ConnectionSink));
        }

        [Test]
        public void Register_HeldTypeOnUuChannel_ThrowsArgumentException()
        {
            // Arrange — UU_A has a U-U routing row.
            var h = NewHarness();
            var descriptor = new InboundRequestDescriptor(InboundTestIds.TypeUuA, RpcTypeTag.SetTarget, true, InboundTestIds.UuBodySize);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void Register_HeldTypeWithRateLimitedTag_ThrowsArgumentException()
        {
            // Arrange
            var h = NewHarness();
            var descriptor = new InboundRequestDescriptor(
                InboundTestIds.TypeHeldRod, RpcTypeTag.AllocateFreePoint, true, InboundTestIds.HeldRodBodySize);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void Register_HeldTypeBodyOneAboveHeldLimit_ThrowsArgumentException()
        {
            // Arrange
            var h = NewHarness();
            var descriptor = new InboundRequestDescriptor(InboundTestIds.TypeHeldRod, RpcTypeTag.SetTarget, true,
                (ushort)(InboundDispatchConstants.MAX_HELD_BODY_BYTES + 1));

            // Act / Assert
            Assert.Throws<ArgumentException>(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void Register_HeldTypeBodyAtHeldLimit_IsAccepted()
        {
            // Arrange
            var h = NewHarness();
            var descriptor = new InboundRequestDescriptor(InboundTestIds.TypeHeldRod, RpcTypeTag.SetTarget, true,
                (ushort)InboundDispatchConstants.MAX_HELD_BODY_BYTES);

            // Act / Assert
            Assert.DoesNotThrow(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        [Test]
        public void Register_UndefinedRpcTypeTag_ThrowsArgumentOutOfRange()
        {
            // Arrange
            const byte undefinedTagValue = 255;
            var h = NewHarness();
            var descriptor = new InboundRequestDescriptor(
                InboundTestIds.TypePlainRod, (RpcTypeTag)undefinedTagValue, false, InboundTestIds.PlainRodBodySize);

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => h.Dispatcher.Register(descriptor, h.Handler.Handle));
        }

        // =========================================================================================
        // Channel from the routing table.
        // =========================================================================================

        [Test]
        public void TryAccept_ChannelReadFromRoutingRow_UuTypeIsStaleCheckedAndRodTypeIsNot()
        {
            // Arrange — UU_A has a U-U row, PLAIN_ROD an R-OD row; neither descriptor names a channel.
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] uuBody = InboundMessageBuilder.Body(InboundTestIds.UuBodySize, InboundTestIds.SeedOne);
            byte[] rodBody = InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne);

            // Act
            bool uuNewer = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, NewerSequence, uuBody);
            bool uuOlder = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, StaleSequence, uuBody);
            bool rodNewer = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, NewerSequence, rodBody);
            bool rodOlder = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, StaleSequence, rodBody);

            // Assert
            Assert.IsTrue(uuNewer);
            Assert.IsFalse(uuOlder, "A U-U row means the stale check applies.");
            Assert.IsTrue(rodNewer);
            Assert.IsTrue(rodOlder, "An R-OD row means the stale check never applies.");
            Assert.AreEqual(1, h.Dispatcher.StaleDropCount);
        }

        // =========================================================================================
        // No routing row (AC-MCR-03).
        // =========================================================================================

        [Test]
        public void Register_NoRoutingRowInDevelopmentBuild_ThrowsPendingSchemaDispatchWithTypeId()
        {
            // Arrange
            var h = NewHarness(isDevelopmentBuild: true);

            // Act
            var exception = Assert.Throws<PendingSchemaDispatchException>(
                () => h.Dispatcher.Register(RequestWithBody(InboundTestIds.TypeNoRow, InboundTestIds.PlainRodBodySize), h.Handler.Handle));

            // Assert
            Assert.AreEqual(InboundTestIds.TypeNoRow, exception.MessageTypeId);
        }

        [Test]
        public void RegisterConnectionLevel_NoRoutingRowInDevelopmentBuild_ThrowsPendingSchemaDispatch()
        {
            // Arrange
            var h = NewHarness(isDevelopmentBuild: true);
            var descriptor = new ConnectionMessageDescriptor(InboundTestIds.TypeNoRow, InboundTestIds.ConnUuBodySize, false);

            // Act
            var exception = Assert.Throws<PendingSchemaDispatchException>(
                () => h.Dispatcher.RegisterConnectionLevel(descriptor, h.ConnectionSink));

            // Assert
            Assert.AreEqual(InboundTestIds.TypeNoRow, exception.MessageTypeId);
        }

        [Test]
        public void Register_NoRoutingRowInReleaseBuild_LogsErrorAndDropsMessagesAsUnknownType()
        {
            // Arrange
            var h = NewHarness(isDevelopmentBuild: false);
            h.RegisterStandardTypes();
            LogAssert.Expect(LogType.Error, InboundTestIds.RegistrationSkippedRegex(InboundTestIds.TypeNoRow));

            // Act
            Assert.DoesNotThrow(
                () => h.Dispatcher.Register(RequestWithBody(InboundTestIds.TypeNoRow, InboundTestIds.PlainRodBodySize), h.Handler.Handle));
            h.Dispatcher.Seal();
            h.AddClient(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);
            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeNoRow, FirstSequence,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne));

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(1, capture.CountWarnings("UnknownInboundMessageType"));
        }

        [Test]
        public void Register_ServerToClientRowInDevelopmentBuild_ThrowsPendingSchemaDispatch()
        {
            // Arrange
            var h = NewHarness(isDevelopmentBuild: true);

            // Act / Assert
            Assert.Throws<PendingSchemaDispatchException>(
                () => h.Dispatcher.Register(RequestWithBody(InboundTestIds.TypeServerToClient, InboundTestIds.PlainRodBodySize), h.Handler.Handle));
        }

        [Test]
        public void Register_ServerToClientRowInReleaseBuild_LogsErrorAndDropsMessagesAsUnknownType()
        {
            // Arrange
            var h = NewHarness(isDevelopmentBuild: false);
            LogAssert.Expect(LogType.Error, InboundTestIds.RegistrationSkippedRegex(InboundTestIds.TypeServerToClient));

            // Act
            Assert.DoesNotThrow(
                () => h.Dispatcher.Register(RequestWithBody(InboundTestIds.TypeServerToClient, InboundTestIds.PlainRodBodySize), h.Handler.Handle));
            h.Dispatcher.Seal();
            h.AddClient(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);
            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeServerToClient, SecondSequence,
                InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne));

            // Assert
            Assert.IsFalse(accepted);
            Assert.AreEqual(1, capture.CountWarnings("UnknownInboundMessageType"));
        }
    }
}

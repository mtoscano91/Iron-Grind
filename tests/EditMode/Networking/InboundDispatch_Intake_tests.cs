using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 036 — <see cref="InboundRequestDispatcher.TryAccept"/>
    /// (ADR-014 Decision 2): activity reporting, unknown / malformed / stale / overflowing drops, the
    /// per-(connection, type) stale check, connection-level messages, inbox bounds and bounded logs.
    /// </summary>
    /// <remarks>
    /// Warnings are counted with <see cref="InboundLogCapture"/> because <c>LogAssert</c> cannot prove that
    /// a warning was suppressed. A U-U body carries its sequence number as its first byte, so a test can
    /// tell which messages reached the handler.
    /// </remarks>
    [TestFixture]
    internal sealed class InboundDispatch_Intake_Tests
    {
        private const uint ArrivalTick = 7u;
        private const uint DispatchTick = 8u;
        private const uint LaterDispatchTick = 9u;
        private const uint Sequence = 1u;
        private const uint SequenceTen = 10u;
        private const uint SequenceNine = 9u;
        private const uint SequenceEleven = 11u;
        private const uint SequenceFive = 5u;
        private const uint SequenceFifteen = 15u;
        private const uint SequenceTwenty = 20u;
        private const uint SequenceForty = 40u;
        private const uint SequenceFifty = 50u;
        private const uint SequenceZero = 0u;
        private const int UnknownTypeMessageCount = 10;
        private const int ChatMessagesThatFit = 10;
        private const int GarbageMessageLength = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES + 1;
        private const int TooShortMessageLength = ServerMessageEnvelope.WireSize - 1;
        private const int ShortForSenderMessageLength = ClientEntityMessageEnvelope.WireSize - 2;
        private const byte GarbageByte = 0xFF;

        private static byte[] PlainBody() =>
            InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize, InboundTestIds.SeedOne);

        private static byte[] UuBody(uint marker) =>
            InboundMessageBuilder.Body(InboundTestIds.UuBodySize, (byte)marker);

        private static void AssertSingleActivity(InboundDispatchHarness h, uint clientId, uint tick)
        {
            Assert.AreEqual(1, h.Activity.Calls.Count, "Exactly one activity call per message.");
            Assert.AreEqual(clientId, h.Activity.Calls[0].ClientId);
            Assert.AreEqual(tick, h.Activity.Calls[0].ServerTick);
        }

        // =========================================================================================
        // Activity matrix.
        // =========================================================================================

        [Test]
        public void TryAccept_AcceptedRequest_ReportsActivityOnceWithTickSourceValue()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            Assert.IsTrue(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        [Test]
        public void TryAccept_ConnectionLevelMessage_ReportsActivityOnce()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.SendConnectionLevel(InboundTestIds.ClientOne, InboundTestIds.TypeConnUu, Sequence,
                InboundMessageBuilder.Body(InboundTestIds.ConnUuBodySize, InboundTestIds.SeedOne));

            Assert.IsTrue(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        [Test]
        public void TryAccept_UnknownType_ReportsActivityOnce()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUnregistered, Sequence, PlainBody());

            Assert.IsFalse(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        [Test]
        public void TryAccept_MalformedMessage_ReportsActivityOnce()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, new byte[TooShortMessageLength]);

            Assert.IsFalse(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        [Test]
        public void TryAccept_StaleMessage_ReportsActivityOnce()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            h.Activity.Calls.Clear();
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceNine, UuBody(SequenceNine));

            Assert.IsFalse(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        [Test]
        public void TryAccept_RequestDroppedByFullInbox_ReportsActivityOnce()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            for (int i = 0; i < InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            }
            h.Activity.Calls.Clear();
            h.TickSource.Tick = ArrivalTick;

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            Assert.IsFalse(accepted);
            AssertSingleActivity(h, InboundTestIds.ClientOne, ArrivalTick);
        }

        // =========================================================================================
        // Known connections only.
        // =========================================================================================

        [Test]
        public void TryAccept_ConnectionNeverAdded_ReturnsFalseWithNoActivityAndNoSinkCall()
        {
            var h = InboundDispatchHarness.CreateReady();

            bool accepted = h.SendConnectionLevel(InboundTestIds.ClientOne, InboundTestIds.TypeConnUu, Sequence,
                InboundMessageBuilder.Body(InboundTestIds.ConnUuBodySize, InboundTestIds.SeedOne));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.Activity.Calls.Count);
            Assert.AreEqual(0, h.ConnectionSink.Calls.Count);
        }

        [Test]
        public void TryAccept_ConnectionAddedThenRemoved_ReturnsFalseWithNoActivityAndNoSinkCall()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);

            bool accepted = h.SendConnectionLevel(InboundTestIds.ClientOne, InboundTestIds.TypeConnUu, Sequence,
                InboundMessageBuilder.Body(InboundTestIds.ConnUuBodySize, InboundTestIds.SeedOne));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.Activity.Calls.Count);
            Assert.AreEqual(0, h.ConnectionSink.Calls.Count);
        }

        // =========================================================================================
        // Malformed.
        // =========================================================================================

        [Test]
        public void TryAccept_TwelveByteMessageOfTypeWithSenderEntityId_ReturnsFalse()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] message = InboundMessageBuilder.Request(InboundTestIds.TypePlainRod, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne).RawValue, Array.Empty<byte>());
            var truncated = new byte[ShortForSenderMessageLength];
            Array.Copy(message, truncated, ShortForSenderMessageLength);

            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, truncated);
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.Handler.Calls.Count);
        }

        [Test]
        public void TryAccept_PlainRodWithThirteenByteBody_ReturnsFalseAndHandlerNeverCalled()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] oversizeBody = InboundMessageBuilder.Body(InboundTestIds.PlainRodBodySize + 1, InboundTestIds.SeedOne);

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, oversizeBody);
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, h.Handler.Calls.Count);
        }

        // =========================================================================================
        // Stale check.
        // =========================================================================================

        [Test]
        public void TryAccept_UuSequencesTenNineTenEleven_DispatchesTenAndElevenAndCountsTwoStaleDrops()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);

            bool first = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            bool older = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceNine, UuBody(SequenceNine));
            bool equal = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            bool newer = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceEleven, UuBody(SequenceEleven));
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.IsTrue(first);
            Assert.IsFalse(older);
            Assert.IsFalse(equal, "An equal sequence number is stale.");
            Assert.IsTrue(newer);
            Assert.AreEqual(2, h.Handler.Calls.Count);
            Assert.AreEqual((byte)SequenceTen, h.Handler.Calls[0].Body[0]);
            Assert.AreEqual((byte)SequenceEleven, h.Handler.Calls[1].Body[0]);
            Assert.AreEqual(2, h.Dispatcher.StaleDropCount);
        }

        [Test]
        public void TryAccept_StaleDrop_IsCountedAndNotLogged()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceNine, UuBody(SequenceNine));

            Assert.AreEqual(1, h.Dispatcher.StaleDropCount);
            Assert.AreEqual(0, capture.CountWarnings());
        }

        [Test]
        public void TryAccept_FirstUuMessageWithSequenceZero_IsAccepted()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);

            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceZero, UuBody(SequenceZero));

            Assert.IsTrue(accepted);
        }

        [Test]
        public void TryAccept_UuSequenceWrapsFromMaxToZero_BothAccepted()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);

            bool atMax = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, uint.MaxValue, UuBody(SequenceTen));
            bool wrapped = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceZero, UuBody(SequenceEleven));

            Assert.IsTrue(atMax);
            Assert.IsTrue(wrapped);
        }

        [Test]
        public void TryAccept_StaleStateIsPerConnection_LowerSequenceFromOtherClientAccepted()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);

            bool fromOne = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            bool fromTwo = h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypeUuA, SequenceFive, UuBody(SequenceFive));

            Assert.IsTrue(fromOne);
            Assert.IsTrue(fromTwo);
        }

        [Test]
        public void TryAccept_StaleStateIsClearedWithTheSlot_NewConnectionMaySendLowerSequence()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTen, UuBody(SequenceTen));
            h.Dispatcher.RemoveConnection(InboundTestIds.ClientOne);
            h.Dispatcher.DispatchTick(DispatchTick);
            h.AddClient(InboundTestIds.ClientNew);

            bool accepted = h.SendRequest(InboundTestIds.ClientNew, InboundTestIds.TypeUuA, SequenceFive, UuBody(SequenceFive));

            Assert.IsTrue(accepted, "The slot's stale state must be cleared when it is given to a new connection.");
        }

        [Test]
        public void TryAccept_RodRequestAfterUuWithHigherSequence_BothDispatched()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);

            bool uu = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceFifty, UuBody(SequenceFifty));
            bool rod = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, SequenceForty, PlainBody());
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.IsTrue(uu);
            Assert.IsTrue(rod, "An R-OD request is never stale.");
            Assert.AreEqual(2, h.Handler.Calls.Count);
        }

        [Test]
        public void TryAccept_UuTypeAfterOtherUuTypeWithHigherSequence_BothDispatched()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);

            bool first = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceFifty, UuBody(SequenceFifty));
            bool second = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuB, SequenceForty, UuBody(SequenceForty));
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.IsTrue(first);
            Assert.IsTrue(second, "The highest-seen value is kept per U-U type, not per connection.");
            Assert.AreEqual(2, h.Handler.Calls.Count);
        }

        [Test]
        public void TryAccept_DroppedMessageDoesNotAdvanceHighestSeen()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] oversizeBody = InboundMessageBuilder.Body(InboundTestIds.UuBodySize + 1, InboundTestIds.SeedOne);

            bool dropped = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTwenty, oversizeBody);
            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceFifteen, UuBody(SequenceFifteen));

            Assert.IsFalse(dropped);
            Assert.IsTrue(accepted);
        }

        [Test]
        public void TryAccept_UuRequestDroppedByFullInbox_DoesNotAdvanceHighestSeen()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            for (int i = 0; i < InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            }

            bool dropped = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceTwenty, UuBody(SequenceTwenty));
            h.Dispatcher.DispatchTick(DispatchTick);
            bool accepted = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUuA, SequenceFifteen, UuBody(SequenceFifteen));

            Assert.IsFalse(dropped);
            Assert.IsTrue(accepted);
        }

        // =========================================================================================
        // Connection-level messages.
        // =========================================================================================

        [Test]
        public void TryAccept_ConnectionSinkThrows_ReturnsFalseAndLogsOneErrorWithoutThrowing()
        {
            var h = new InboundDispatchHarness();
            h.Dispatcher.RegisterConnectionLevel(InboundTestIds.ConnUu(), new InboundThrowingConnectionSink());
            h.Dispatcher.Seal();
            h.AddClient(InboundTestIds.ClientOne);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.ConnUuBodySize, InboundTestIds.SeedOne);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                InboundTestIds.TryAcceptFailedRegex(InboundTestIds.ClientOne));

            bool accepted = true;
            Assert.DoesNotThrow(() =>
                accepted = h.SendConnectionLevel(InboundTestIds.ClientOne, InboundTestIds.TypeConnUu, Sequence, body));

            Assert.IsFalse(accepted);
        }

        [Test]
        public void TryAccept_HeartbeatEnvelope_ReachesSinkWithInvalidEntityAndEmptyBody()
        {
            var h = new InboundDispatchHarness();
            h.RegisterStandardTypes();
            h.Dispatcher.RegisterConnectionLevel(
                new ConnectionMessageDescriptor(HeartbeatMessage.MessageTypeId, 0, false), h.ConnectionSink);
            h.Dispatcher.Seal();
            h.AddClient(InboundTestIds.ClientOne);
            var message = new byte[HeartbeatMessage.WireSize];
            MessageEnvelopeCodec.Write(message, HeartbeatMessage.CreateEnvelope(Sequence, InboundTestIds.EnvelopeTick));

            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, h.ConnectionSink.Calls.Count);
            Assert.AreEqual(InboundTestIds.ClientOne, h.ConnectionSink.Calls[0].ClientId);
            Assert.AreEqual(HeartbeatMessage.MessageTypeId, h.ConnectionSink.Calls[0].MessageTypeId);
            Assert.AreEqual(EntityID.Invalid, h.ConnectionSink.Calls[0].SenderEntityId);
            Assert.AreEqual(0, h.ConnectionSink.Calls[0].Body.Length);
            Assert.AreEqual(0, h.Dispatcher.PendingRequestCount(InboundTestIds.ClientOne), "A connection-level message never enters the inbox.");
        }

        [Test]
        public void TryAccept_ConnectionLevelMessageBeforeSessionReady_SinkReceivesIt()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            h.GuardChain.ClearSessionReady(InboundTestIds.ClientOne);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.ConnUuBodySize, InboundTestIds.SeedTwo);

            bool accepted = h.SendConnectionLevel(InboundTestIds.ClientOne, InboundTestIds.TypeConnUu, Sequence, body);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, h.ConnectionSink.Calls.Count);
            CollectionAssert.AreEqual(body, h.ConnectionSink.Calls[0].Body);
        }

        // =========================================================================================
        // Inbox bounds.
        // =========================================================================================

        [Test]
        public void TryAccept_SixtyFifthRequestFromOneClient_DroppedWithOneOverflowWarningAndOtherClientUnaffected()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne, InboundTestIds.ClientTwo);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            for (int i = 0; i < InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION; i++)
            {
                Assert.IsTrue(h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody()),
                    $"Request {i} is within the count bound.");
            }
            bool overflow = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            bool otherClient = h.SendRequest(InboundTestIds.ClientTwo, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            Assert.IsFalse(overflow);
            Assert.IsTrue(otherClient);
            Assert.AreEqual(InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION,
                h.Dispatcher.PendingRequestCount(InboundTestIds.ClientOne));
            Assert.AreEqual(1, capture.CountWarnings("InboundInboxOverflow"));
        }

        [Test]
        public void TryAccept_EleventhChatMessageFromOneClient_DroppedByByteBound()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.ChatBodySize, InboundTestIds.SeedOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            for (int i = 0; i < ChatMessagesThatFit; i++)
            {
                Assert.IsTrue(h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeChat, Sequence, body),
                    $"Chat message {i} fits in the connection's byte bound.");
            }
            bool overflow = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeChat, Sequence, body);

            Assert.IsFalse(overflow);
            Assert.AreEqual(1, capture.CountWarnings("InboundInboxOverflow"));
        }

        [Test]
        public void TryAccept_AfterDispatchTickFollowingCountBound_ClientCanSendAgainAndWarningCanRepeat()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);
            for (int i = 0; i <= InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());
            }
            Assert.AreEqual(1, capture.CountWarnings("InboundInboxOverflow"));
            h.Dispatcher.DispatchTick(DispatchTick);

            for (int i = 0; i < InboundDispatchConstants.MAX_INBOX_REQUESTS_PER_CONNECTION; i++)
            {
                Assert.IsTrue(h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody()),
                    $"Request {i} after the tick must be accepted again.");
            }
            bool overflowAgain = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypePlainRod, Sequence, PlainBody());

            Assert.IsFalse(overflowAgain);
            Assert.AreEqual(2, capture.CountWarnings("InboundInboxOverflow"), "The warning flag is cleared by DispatchTick.");
        }

        [Test]
        public void TryAccept_AfterDispatchTickFollowingByteBound_ClientCanSendAgainAndWarningCanRepeat()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.ChatBodySize, InboundTestIds.SeedOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);
            for (int i = 0; i <= ChatMessagesThatFit; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeChat, Sequence, body);
            }
            h.Dispatcher.DispatchTick(DispatchTick);

            for (int i = 0; i < ChatMessagesThatFit; i++)
            {
                Assert.IsTrue(h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeChat, Sequence, body));
            }
            bool overflowAgain = h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeChat, Sequence, body);

            Assert.IsFalse(overflowAgain);
            Assert.AreEqual(2, capture.CountWarnings("InboundInboxOverflow"));
        }

        [Test]
        public void TryAccept_LargestMessage_HandlerReceivesTheSame386Bytes()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] body = InboundMessageBuilder.Body(InboundTestIds.ChatBodySize, InboundTestIds.SeedThree);
            byte[] message = InboundMessageBuilder.Request(InboundTestIds.TypeChat, Sequence,
                InboundDispatchHarness.EntityOf(InboundTestIds.ClientOne).RawValue, body);

            bool accepted = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, message);
            h.Dispatcher.DispatchTick(DispatchTick);

            Assert.AreEqual(InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES, message.Length);
            Assert.IsTrue(accepted);
            Assert.AreEqual(1, h.Handler.Calls.Count);
            CollectionAssert.AreEqual(body, h.Handler.Calls[0].Body);
        }

        // =========================================================================================
        // Bounded logs and never throws.
        // =========================================================================================

        [Test]
        public void TryAccept_TenUnknownTypeMessagesInOneTickInterval_LogsOneWarningAndCountsTheRest()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            for (int i = 0; i < UnknownTypeMessageCount; i++)
            {
                h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUnregistered, Sequence, PlainBody());
            }

            Assert.AreEqual(1, capture.CountWarnings("UnknownInboundMessageType"));
            Assert.AreEqual(UnknownTypeMessageCount - 1, h.Dispatcher.SuppressedAnomalyCount);
        }

        [Test]
        public void TryAccept_TenMalformedMessagesInOneTickInterval_LogsOneWarning()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);

            for (int i = 0; i < UnknownTypeMessageCount; i++)
            {
                h.Dispatcher.TryAccept(InboundTestIds.ClientOne, new byte[TooShortMessageLength]);
            }

            Assert.AreEqual(1, capture.CountWarnings("InboundMessageMalformed"));
        }

        [Test]
        public void TryAccept_UnknownTypeWarningInNextTickInterval_IsLoggedAgain()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            using var capture = new InboundLogCapture(InboundTestIds.DispatcherLogPrefix);
            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUnregistered, Sequence, PlainBody());
            h.Dispatcher.DispatchTick(DispatchTick);

            h.SendRequest(InboundTestIds.ClientOne, InboundTestIds.TypeUnregistered, Sequence, PlainBody());

            Assert.AreEqual(2, capture.CountWarnings("UnknownInboundMessageType"));
        }

        [Test]
        public void TryAccept_EmptyNineByteAndGarbageSpans_ReturnFalseWithoutThrowing()
        {
            var h = InboundDispatchHarness.CreateReady(InboundTestIds.ClientOne);
            byte[] empty = Array.Empty<byte>();
            var nineBytes = new byte[TooShortMessageLength];
            var garbage = new byte[GarbageMessageLength];
            Array.Fill(garbage, GarbageByte);
            bool emptyResult = true;
            bool nineResult = true;
            bool garbageResult = true;

            Assert.DoesNotThrow(() => emptyResult = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, empty));
            Assert.DoesNotThrow(() => nineResult = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, nineBytes));
            Assert.DoesNotThrow(() => garbageResult = h.Dispatcher.TryAccept(InboundTestIds.ClientOne, garbage));

            Assert.IsFalse(emptyResult);
            Assert.IsFalse(nineResult);
            Assert.IsFalse(garbageResult);
        }
    }
}

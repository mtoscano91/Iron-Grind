using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode tests for Networking Core Story 040 — the game side of the cross-zone relay (ADR-015): broadcaster,
    /// zone-wide send on the outbox, envelope codec, allowlist, inbox, receiving check, origin and tick step 1a, with
    /// several zones connected through <see cref="InMemoryCrossZoneRelayBus"/>. Deterministic: the one threading test
    /// joins its thread and asserts counts, never timing.
    /// </summary>
    [TestFixture]
    internal sealed class CrossZoneRelay_Tests
    {
        private const ushort Broadcast9 = ServerBroadcast_Enhancement9.MessageTypeId;
        private const ushort UnlistedType = 0xE104;
        private const ushort FillerType = GoldSyncEventForcedDelivery.MessageTypeId;
        private const int Envelope = ServerMessageEnvelope.WireSize;
        private const int Cap = PriorityPathQueue<int>.PRIORITY_PATH_CAP;
        private const int MaxBody = ServerBroadcast_Enhancement9.MaxBodySize;
        private const int MinBody = ServerBroadcast_Enhancement9.MinBodySize;
        private const uint TickSeven = 7u;
        private const float DeltaTime = 0.05f;
        private const int AllocationIterations = 1000;
        private const int WarmUpIterations = 20;
        private const string BroadcasterPrefix = "[ServerWideBroadcaster]";
        private const string ReceiverPrefix = "[CrossZoneRelayReceiver]";
        private const string InboxPrefix = "[CrossZoneRelayInbox]";
        private const string ValidTail = ":0123456789abcdef:AAAAAAA=";
        private const ulong OtherOrigin = 0x1111222233334444ul;

        private static readonly string[] StepParameterNames =
            { "completionQueue", "relayInbox", "relayHandler", "dispatcher", "simulation", "outbound", "tickSource" };

        // ---- helpers ----------------------------------------------------------------------------

        private static byte[] Body(int length, int seed)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }
            return bytes;
        }

        private static Regex LogLine(string text) => new Regex(Regex.Escape(text));

        private static ServerMessageEnvelope EnvelopeOf(OutboxRecordedSend send)
        {
            Assert.IsTrue(MessageEnvelopeCodec.TryRead(send.Bytes, out ServerMessageEnvelope envelope));
            return envelope;
        }

        private static CrossZoneRelayReceiver NewReceiver(out CrossZoneRelayInbox inbox, int capacity = 64, ulong originValue = 1ul)
        {
            CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();
            inbox = new CrossZoneRelayInbox(allowlist.LargestMaxBodyBytes, capacity);
            return new CrossZoneRelayReceiver(allowlist, inbox, new CrossZoneRelayOrigin(originValue));
        }

        private static string RepoRoot() => Directory.GetParent(Application.dataPath).FullName;

        private static List<string> NewLog() => new List<string>();

        private static ZoneTickPipeline NewPipeline(List<string> log, ICrossZoneRelayInbox inbox, ICrossZoneRelayHandler handler,
            InboundSettableTickSource tickSource)
        {
            return new ZoneTickPipeline(
                new RelayRecordingCompletionQueue(log), inbox, handler, new RelayRecordingDispatcher(log),
                new RelayRecordingSimulation(log), new RelayRecordingOutbound(log), tickSource);
        }

        // ---- two zones, three zones, echo, body --------------------------------------------------

        [Test]
        public void test_two_zones_local_clients_get_it_in_the_same_tick_and_other_zone_in_its_first_tick_exactly_once()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64, 1u, 2u);
            var b = new RelayTestZone(bus, 64, 11u);
            byte[] body = Body(MinBody, 5);

            bool accepted = a.Broadcaster.Broadcast(Broadcast9, body);
            a.Tick(1u);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, a.SendCount(1u));
            Assert.AreEqual(1, a.SendCount(2u));
            Assert.AreEqual(1u, EnvelopeOf(a.Seam.Sends[0]).ServerTickNumber, "Sent by the flush of the producing tick.");
            Assert.AreEqual(0, b.SendCount(11u), "Zone B has not ticked yet.");

            b.Tick(5u);

            Assert.AreEqual(1, b.SendCount(11u));
            Assert.AreEqual(5u, EnvelopeOf(b.Seam.Sends[0]).ServerTickNumber, "Step 1a enqueues and step 4 of the same tick sends.");
            for (uint tick = 6u; tick < 9u; tick++)
            {
                a.Tick(tick);
                b.Tick(tick);
            }
            Assert.AreEqual(1, a.SendCount(1u));
            Assert.AreEqual(1, a.SendCount(2u));
            Assert.AreEqual(1, b.SendCount(11u));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_three_zones_each_client_receives_exactly_one()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64, 1u);
            var b = new RelayTestZone(bus, 64, 11u);
            var c = new RelayTestZone(bus, 64, 21u);

            a.Broadcaster.Broadcast(Broadcast9, Body(MinBody, 1));
            for (uint tick = 1u; tick <= 3u; tick++)
            {
                a.Tick(tick);
                b.Tick(tick);
                c.Tick(tick);
            }

            Assert.AreEqual(1, a.SendCount(1u));
            Assert.AreEqual(1, b.SendCount(11u));
            Assert.AreEqual(1, c.SendCount(21u));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_echo_own_envelope_is_dropped_by_origin_and_no_client_receives_twice()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64, 1u);
            var b = new RelayTestZone(bus, 64, 11u);

            a.Broadcaster.Broadcast(Broadcast9, Body(MinBody, 1));

            Assert.AreEqual(1, a.Receiver.OwnOriginDropCount);
            Assert.AreEqual(0, b.Receiver.OwnOriginDropCount);
            Assert.AreEqual(0, a.Inbox.Count);
            Assert.AreEqual(1, b.Inbox.Count);
            for (uint tick = 1u; tick <= 3u; tick++)
            {
                a.Tick(tick);
            }
            Assert.AreEqual(1, a.SendCount(1u));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(MinBody)]
        [TestCase(MaxBody)]
        public void test_body_arrives_byte_for_byte_locally_and_in_the_other_zone(int length)
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64, 1u);
            var b = new RelayTestZone(bus, 64, 11u);
            byte[] body = Body(length, 200);

            a.Broadcaster.Broadcast(Broadcast9, body);
            a.Tick(1u);
            b.Tick(1u);

            CollectionAssert.AreEqual(body, a.Bodies(1u)[0]);
            CollectionAssert.AreEqual(body, b.Bodies(11u)[0]);
            Assert.AreEqual(Broadcast9, EnvelopeOf(b.Seam.Sends[0]).MessageTypeId);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- broadcaster ------------------------------------------------------------------------

        [Test]
        public void test_broadcast_enqueues_locally_not_cap_exempt_before_it_publishes()
        {
            var log = NewLog();
            var sink = new RelayRecordingSink(log);
            var publisher = new RelayRecordingPublisher(log);
            var broadcaster = new ServerWideBroadcaster(sink, publisher, CrossZoneRelayAllowlist.CreateProduction());

            bool accepted = broadcaster.Broadcast(Broadcast9, Body(MinBody, 1));

            Assert.IsTrue(accepted);
            CollectionAssert.AreEqual(new[] { "EnqueueToAll(0xE103, exempt=False, length=4)", "Publish(0xE103)" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_type_not_on_the_allowlist_returns_false_logs_one_error_and_sends_and_publishes_nothing()
        {
            var log = NewLog();
            var sink = new RelayRecordingSink(log);
            var publisher = new RelayRecordingPublisher(log);
            var broadcaster = new ServerWideBroadcaster(sink, publisher, CrossZoneRelayAllowlist.CreateProduction());
            LogAssert.Expect(LogType.Error, LogLine($"{BroadcasterPrefix} TypeNotAllowlisted: type=0xE104"));

            bool accepted = false;
            Assert.DoesNotThrow(() => accepted = broadcaster.Broadcast(UnlistedType, Body(MinBody, 1)));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, sink.Calls);
            Assert.AreEqual(0, publisher.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_body_over_the_types_max_is_refused_like_an_unlisted_type()
        {
            var log = NewLog();
            var sink = new RelayRecordingSink(log);
            var publisher = new RelayRecordingPublisher(log);
            var broadcaster = new ServerWideBroadcaster(sink, publisher, CrossZoneRelayAllowlist.CreateProduction());
            LogAssert.Expect(LogType.Error, LogLine($"{BroadcasterPrefix} BodyTooLong: type=0xE103"));

            bool accepted = broadcaster.Broadcast(Broadcast9, Body(MaxBody + 1, 1));

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, sink.Calls);
            Assert.AreEqual(0, publisher.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_publisher_that_throws_leaves_local_delivery_and_the_result_unchanged_and_logs_one_warning()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            var publisher = new RelayRecordingPublisher(null) { Throw = true };
            var broadcaster = new ServerWideBroadcaster(outbox, publisher, CrossZoneRelayAllowlist.CreateProduction());
            LogAssert.Expect(LogType.Warning, LogLine($"{BroadcasterPrefix} PublishFailed: type=0xE103"));

            bool accepted = false;
            Assert.DoesNotThrow(() => accepted = broadcaster.Broadcast(Broadcast9, Body(MinBody, 1)));
            outbox.Flush(1u);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, publisher.Calls);
            Assert.AreEqual(2, seam.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcast_sink_that_throws_does_not_throw_logs_one_error_still_publishes_and_returns_true()
        {
            var log = NewLog();
            var sink = new RelayRecordingSink(log) { Throw = true };
            var publisher = new RelayRecordingPublisher(log);
            var broadcaster = new ServerWideBroadcaster(sink, publisher, CrossZoneRelayAllowlist.CreateProduction());
            LogAssert.Expect(LogType.Error, LogLine($"{BroadcasterPrefix} LocalDeliveryThrew: type=0xE103 - InvalidOperationException: sink fake"));

            bool accepted = false;
            Assert.DoesNotThrow(() => accepted = broadcaster.Broadcast(Broadcast9, Body(MinBody, 1)));

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, sink.Calls);
            Assert.AreEqual(1, publisher.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_broadcaster_constructor_null_argument_throws_naming_it()
        {
            var sink = new RelayRecordingSink(NewLog());
            var publisher = new RelayNoOpPublisher();
            CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();

            Assert.AreEqual("sink", Assert.Throws<ArgumentNullException>(() => new ServerWideBroadcaster(null, publisher, allowlist)).ParamName);
            Assert.AreEqual("publisher", Assert.Throws<ArgumentNullException>(() => new ServerWideBroadcaster(sink, null, allowlist)).ParamName);
            Assert.AreEqual("allowlist", Assert.Throws<ArgumentNullException>(() => new ServerWideBroadcaster(sink, publisher, null)).ParamName);
        }

        // ---- receiving check --------------------------------------------------------------------

        [Test]
        public void test_receive_valid_payload_from_another_origin_is_queued_in_the_inbox()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            byte[] body = Body(MaxBody, 3);

            bool queued = receiver.OnPayload(RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, body));
            var handler = new RelayRecordingHandler();
            inbox.Drain(1u, handler);

            Assert.IsTrue(queued);
            Assert.AreEqual(1, handler.Received.Count);
            Assert.AreEqual(Broadcast9, handler.Received[0].MessageTypeId);
            CollectionAssert.AreEqual(body, handler.Received[0].Body);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_type_not_on_the_allowlist_is_dropped_and_logged_and_nothing_reaches_the_inbox()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=NotAllowlisted "));

            bool queued = receiver.OnPayload(RelayEnvelopeCodec.Encode(UnlistedType, OtherOrigin, Body(MinBody, 1)));

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            Assert.AreEqual(1, receiver.RefusedPayloadCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_body_longer_than_the_types_max_is_dropped_and_logged_and_nothing_reaches_the_inbox()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=BodyTooLong "));

            bool queued = receiver.OnPayload(RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, Body(MaxBody + 1, 1)));

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase("v2:57603" + ValidTail, "UnknownVersion")]
        [TestCase("v1:57603:abc", "Malformed")]
        [TestCase("v1:x" + ValidTail, "Malformed")]
        [TestCase("v1:99999" + ValidTail, "Malformed")]
        [TestCase("v1:-1" + ValidTail, "Malformed")]
        [TestCase("v1:57603:0123456789abcde:AAAAAAA=", "BadOrigin")]
        [TestCase("v1:57603:0123456789abcdeg:AAAAAAA=", "BadOrigin")]
        [TestCase("v1:57603:0123456789abcdef:@@@", "BadBase64")]
        [TestCase("v1:57603:0123456789abcdef:AA AA", "BadBase64")]
        [TestCase("", "UnknownVersion")]
        public void test_receive_malformed_payload_is_dropped_logged_and_never_throws(string payload, string reason)
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason={reason} "));

            bool queued = true;
            Assert.DoesNotThrow(() => queued = receiver.OnPayload(payload));

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_payload_above_the_longest_valid_envelope_is_dropped_before_decoding()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            string payload = "v1:" + new string('A', 197);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=TooLong length=200"));

            bool queued = true;
            Assert.DoesNotThrow(() => queued = receiver.OnPayload(payload));

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_null_payload_is_dropped_and_never_throws()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=Malformed "));

            bool queued = true;
            Assert.DoesNotThrow(() => queued = receiver.OnPayload(null));

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_own_origin_is_dropped_counted_and_not_logged()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox, 64, OtherOrigin);

            bool queued = receiver.OnPayload(RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, Body(MinBody, 1)));

            Assert.IsFalse(queued);
            Assert.AreEqual(1, receiver.OwnOriginDropCount);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_inbox_that_throws_in_try_add_returns_false_logs_one_error_and_counts_the_refusal()
        {
            CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();
            var inbox = new RelayRecordingInbox(null) { ThrowOnAdd = true };
            var receiver = new CrossZoneRelayReceiver(allowlist, inbox, new CrossZoneRelayOrigin(1ul));
            LogAssert.Expect(LogType.Error, LogLine($"{ReceiverPrefix} ReceiverThrew - InvalidOperationException: inbox add fake"));

            bool queued = true;
            Assert.DoesNotThrow(() => queued = receiver.OnPayload(RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, Body(MinBody, 1))));

            Assert.IsFalse(queued);
            Assert.AreEqual(1, receiver.RefusedPayloadCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_body_of_54_bytes_fills_the_decode_buffer_and_is_refused_after_decoding_as_body_too_long()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            string payload = RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, Body(54, 1));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=BodyTooLong length=98"));

            bool queued = receiver.OnPayload(payload);

            Assert.AreEqual(98, payload.Length, "The longest envelope the length check lets through.");
            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_body_of_55_bytes_makes_the_envelope_too_long_and_is_refused_before_decoding()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            string payload = RelayEnvelopeCodec.Encode(Broadcast9, OtherOrigin, Body(55, 1));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=TooLong length=102"));

            bool queued = receiver.OnPayload(payload);

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_body_over_the_decode_buffer_with_a_short_type_field_is_refused_by_the_codec_as_body_too_long()
        {
            // A one-digit type leaves room for a 57-byte body (76 base64 characters) inside the 98-character limit,
            // so the length check passes and the refusal comes from the codec, before any decoding (the largest
            // body a one-digit type can carry; 60 bytes would exceed the envelope limit and be reported TooLong).
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            string payload = RelayEnvelopeCodec.Encode(1, OtherOrigin, Body(57, 1));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=BodyTooLong length=98"));

            bool queued = receiver.OnPayload(payload);

            Assert.IsFalse(queued);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_throttle_250_refusals_of_one_reason_log_three_lines_and_all_are_counted()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out CrossZoneRelayInbox inbox);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=UnknownVersion length=3"));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadsDropped: reason=UnknownVersion count=100 (logging every 100th)"));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadsDropped: reason=UnknownVersion count=200 (logging every 100th)"));

            for (int i = 0; i < 250; i++)
            {
                Assert.IsFalse(receiver.OnPayload("v9:"));
            }

            Assert.AreEqual(250, receiver.RefusedPayloadCount);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receive_throttle_two_different_reasons_each_log_their_own_first_full_line()
        {
            CrossZoneRelayReceiver receiver = NewReceiver(out _);
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=UnknownVersion length=3"));
            LogAssert.Expect(LogType.Warning, LogLine($"{ReceiverPrefix} PayloadDropped: reason=Malformed length=0"));

            receiver.OnPayload("v9:");
            receiver.OnPayload("v9:");
            receiver.OnPayload(null);
            receiver.OnPayload(null);

            Assert.AreEqual(4, receiver.RefusedPayloadCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_receiver_constructor_null_argument_throws_naming_it()
        {
            CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();
            var inbox = new CrossZoneRelayInbox(MaxBody);
            var origin = new CrossZoneRelayOrigin();

            Assert.AreEqual("allowlist", Assert.Throws<ArgumentNullException>(() => new CrossZoneRelayReceiver(null, inbox, origin)).ParamName);
            Assert.AreEqual("inbox", Assert.Throws<ArgumentNullException>(() => new CrossZoneRelayReceiver(allowlist, null, origin)).ParamName);
            Assert.AreEqual("origin", Assert.Throws<ArgumentNullException>(() => new CrossZoneRelayReceiver(allowlist, inbox, null)).ParamName);
        }

        // ---- inbox ------------------------------------------------------------------------------

        [Test]
        public void test_inbox_full_drops_and_counts_and_the_drain_logs_one_line_with_the_count()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 4);
            var handler = new RelayRecordingHandler();
            byte[] body = Body(MinBody, 1);

            var results = new List<bool>();
            for (int i = 0; i < 6; i++)
            {
                results.Add(inbox.TryAdd(Broadcast9, body));
            }
            LogAssert.Expect(LogType.Warning, LogLine($"{InboxPrefix} EntriesDropped: count=2 "));
            int delivered = inbox.Drain(1u, handler);
            int deliveredAgain = inbox.Drain(2u, handler);

            CollectionAssert.AreEqual(new[] { true, true, true, true, false, false }, results);
            Assert.AreEqual(4, delivered);
            Assert.AreEqual(4, handler.Received.Count);
            Assert.AreEqual(0, deliveredAgain);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_inbox_full_in_a_zone_entries_already_waiting_are_delivered_by_the_next_step_1a()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64);
            var b = new RelayTestZone(bus, 4, 11u);
            for (int i = 0; i < 6; i++)
            {
                a.Broadcaster.Broadcast(Broadcast9, Body(MinBody, i));
            }
            LogAssert.Expect(LogType.Warning, LogLine($"{InboxPrefix} EntriesDropped: count=2 tick=1"));

            b.Tick(1u);
            b.Tick(2u);

            Assert.AreEqual(4, b.SendCount(11u));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_inbox_body_longer_than_the_slot_is_dropped_and_counted()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 4);

            bool added = inbox.TryAdd(Broadcast9, Body(MaxBody + 1, 1));
            LogAssert.Expect(LogType.Warning, LogLine($"{InboxPrefix} EntriesDropped: count=1 "));
            inbox.Drain(1u, new RelayCountingHandler());

            Assert.IsFalse(added);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_inbox_delivers_in_arrival_order_across_a_ring_wrap()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 3);
            var handler = new RelayRecordingHandler();

            inbox.TryAdd(Broadcast9, Body(MinBody, 1));
            inbox.TryAdd(Broadcast9, Body(MinBody, 2));
            inbox.Drain(1u, handler);
            inbox.TryAdd(Broadcast9, Body(MinBody, 3));
            inbox.TryAdd(Broadcast9, Body(MinBody, 4));
            inbox.TryAdd(Broadcast9, Body(MinBody, 5));
            inbox.Drain(2u, handler);

            var firstBytes = new List<int>();
            foreach (RelayReceived received in handler.Received)
            {
                firstBytes.Add(received.Body[0]);
            }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, firstBytes);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_inbox_constructor_rejects_a_non_positive_capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrossZoneRelayInbox(MaxBody, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrossZoneRelayInbox(-1, 4));
            Assert.AreEqual(64, CrossZoneRelayInbox.RELAY_INBOX_CAPACITY);
        }

        [Test]
        public void test_inbox_failing_handler_does_not_stop_the_rest_and_logs_one_error()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 8);
            var handler = new RelayRecordingHandler { ThrowOnCall = 1 };
            for (int i = 0; i < 3; i++)
            {
                inbox.TryAdd(Broadcast9, Body(MinBody, i));
            }
            LogAssert.Expect(LogType.Error, LogLine($"{InboxPrefix} HandlerThrew: type=0xE103 tick=4 - InvalidOperationException: relay handler fake"));

            int removed = inbox.Drain(4u, handler);

            Assert.AreEqual(3, removed);
            Assert.AreEqual(3, handler.Received.Count);
            Assert.AreEqual(0, inbox.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_inbox_full_try_add_from_inside_the_handler_is_refused_counted_and_logged_on_the_next_drain()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 2);
            bool? addedInsideHandler = null;
            var handler = new RelayRecordingHandler();
            handler.Hook = call =>
            {
                if (call == 0)
                {
                    addedInsideHandler = inbox.TryAdd(Broadcast9, Body(MinBody, 9));
                }
            };
            inbox.TryAdd(Broadcast9, Body(MinBody, 1));
            inbox.TryAdd(Broadcast9, Body(MinBody, 2));

            int first = inbox.Drain(1u, handler);
            LogAssert.Expect(LogType.Warning, LogLine($"{InboxPrefix} EntriesDropped: count=1 tick=2"));
            int second = inbox.Drain(2u, handler);

            Assert.AreEqual(false, addedInsideHandler, "Both slots are still held while the handler runs.");
            Assert.AreEqual(2, first);
            Assert.AreEqual(0, second);
            Assert.AreEqual(2, handler.Received.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- tick pipeline (step 1a) ------------------------------------------------------------

        [Test]
        public void test_pipeline_order_is_completion_relay_requests_simulation_outbound()
        {
            var log = NewLog();
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var pipeline = NewPipeline(log, new RelayRecordingInbox(log), new RelayCountingHandler(), tickSource);

            pipeline.Tick(DeltaTime);

            CollectionAssert.AreEqual(
                new[] { "Completion(7)", "Relay(7)", "Requests(7)", "Simulation(7)", "Outbound(7)" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pipeline_relay_step_that_throws_logs_step_failed_and_the_later_steps_still_run()
        {
            var log = NewLog();
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var inbox = new RelayRecordingInbox(log) { ThrowOnDrain = true };
            var pipeline = NewPipeline(log, inbox, new RelayCountingHandler(), tickSource);
            LogAssert.Expect(LogType.Error, LogLine("[ZoneTickPipeline] StepFailed: step=Relay tick=7 - InvalidOperationException: boom"));

            Assert.DoesNotThrow(() => pipeline.Tick(DeltaTime));

            CollectionAssert.AreEqual(
                new[] { "Completion(7)", "Relay(7)", "Requests(7)", "Simulation(7)", "Outbound(7)" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pipeline_failing_handler_in_step_1a_leaves_the_other_entries_and_the_later_steps_running()
        {
            var log = NewLog();
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var inbox = new CrossZoneRelayInbox(MaxBody, 8);
            var handler = new RelayRecordingHandler { ThrowOnCall = 1, Log = log };
            var pipeline = NewPipeline(log, inbox, handler, tickSource);
            for (int i = 0; i < 3; i++)
            {
                inbox.TryAdd(Broadcast9, Body(MinBody, i));
            }
            LogAssert.Expect(LogType.Error, LogLine($"{InboxPrefix} HandlerThrew: type=0xE103 tick=7 "));

            Assert.DoesNotThrow(() => pipeline.Tick(DeltaTime));

            Assert.AreEqual(3, handler.Received.Count);
            CollectionAssert.AreEqual(
                new[] { "Completion(7)", "Handler(0)", "Handler(1)", "Handler(2)", "Requests(7)", "Simulation(7)", "Outbound(7)" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pipeline_drain_bound_entry_added_during_the_drain_waits_for_the_next_tick()
        {
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var inbox = new CrossZoneRelayInbox(MaxBody, 8);
            var handler = new RelayRecordingHandler();
            handler.Hook = call =>
            {
                if (call == 0)
                {
                    inbox.TryAdd(Broadcast9, Body(MinBody, 9));
                }
            };
            var pipeline = NewPipeline(null, inbox, handler, tickSource);
            inbox.TryAdd(Broadcast9, Body(MinBody, 1));

            pipeline.Tick(DeltaTime);
            int afterFirstTick = handler.Received.Count;
            tickSource.Tick = TickSeven + 1u;
            pipeline.Tick(DeltaTime);

            Assert.AreEqual(1, afterFirstTick);
            Assert.AreEqual(2, handler.Received.Count);
            Assert.AreEqual(9, handler.Received[1].Body[0]);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_pipeline_five_argument_constructor_runs_four_steps_and_an_empty_relay_step()
        {
            var log = NewLog();
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var pipeline = new ZoneTickPipeline(new RelayRecordingCompletionQueue(log), new RelayRecordingDispatcher(log),
                new RelayRecordingSimulation(log), new RelayRecordingOutbound(log), tickSource);

            pipeline.Tick(DeltaTime);

            CollectionAssert.AreEqual(new[] { "Completion(7)", "Requests(7)", "Simulation(7)", "Outbound(7)" }, log);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void test_pipeline_seven_argument_constructor_null_argument_throws_naming_it(int nullIndex)
        {
            var tickSource = new InboundSettableTickSource();
            object[] arguments =
            {
                new RelayRecordingCompletionQueue(null), new CrossZoneRelayInbox(MaxBody), new RelayCountingHandler(),
                new RelayRecordingDispatcher(null), new RelayRecordingSimulation(null), new RelayRecordingOutbound(null), tickSource,
            };
            arguments[nullIndex] = null;

            ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(() => new ZoneTickPipeline(
                (ITickCompletionQueue)arguments[0], (ICrossZoneRelayInbox)arguments[1], (ICrossZoneRelayHandler)arguments[2],
                (IInboundRequestDispatcher)arguments[3], (IZoneSimulationStep)arguments[4], (IZoneOutboundStep)arguments[5],
                (IServerTickSource)arguments[6]));

            Assert.AreEqual(StepParameterNames[nullIndex], thrown.ParamName);
        }

        // ---- zone-wide send on the outbox -------------------------------------------------------

        [Test]
        public void test_sink_clientmessageoutbox_implements_the_zone_client_broadcast_sink()
        {
            Assert.IsInstanceOf<IZoneClientBroadcastSink>(new ClientMessageOutbox(new OutboxRecordingSendSeam()));
        }

        [Test]
        public void test_sink_no_clients_does_nothing_and_logs_nothing()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);

            Assert.DoesNotThrow(() => outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 1), false));
            outbox.Flush(1u);

            Assert.AreEqual(0, seam.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sink_three_clients_one_removed_enqueues_for_the_two_that_remain()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            outbox.AddClient(3u);
            outbox.RemoveClient(2u);

            outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 1), false);
            outbox.Flush(1u);

            Assert.AreEqual(2, seam.Sends.Count);
            Assert.AreEqual(1u, seam.Sends[0].ClientId);
            Assert.AreEqual(3u, seam.Sends[1].ClientId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sink_passes_the_cap_exempt_flag_through()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(1u);
            for (int i = 0; i < Cap; i++)
            {
                outbox.Enqueue(1u, FillerType, Body(MinBody, i), false);
            }

            outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 1), true);
            outbox.Flush(1u);

            Assert.AreEqual(Cap, seam.Sends.Count);
            Assert.AreEqual(Broadcast9, EnvelopeOf(seam.Sends[0]).MessageTypeId, "An exempt message leads the flush.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sink_removal_during_the_zone_wide_send_still_reaches_the_other_clients_and_removes_the_client_after()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            var requester = new OutboxRecordingDisconnectRequester { RemoveFrom = outbox };
            outbox.SetDisconnectRequester(requester);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            outbox.AddClient(3u);
            for (int i = 0; i < ClientMessageOutbox.OUTBOX_MAX_QUEUED_MESSAGES; i++)
            {
                outbox.Enqueue(2u, FillerType, Body(MinBody, i), false);
            }
            LogAssert.Expect(LogType.Error, LogLine("[ClientMessageOutbox] QueueFull: clientId=2 "));

            Assert.DoesNotThrow(() => outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 1), false));
            Assert.DoesNotThrow(() => outbox.Flush(1u));

            CollectionAssert.AreEqual(new[] { 2u }, requester.Requests);
            int toClient1 = 0, toClient2 = 0, toClient3 = 0;
            foreach (OutboxRecordedSend send in seam.Sends)
            {
                if (send.ClientId == 1u) { toClient1++; }
                if (send.ClientId == 2u) { toClient2++; }
                if (send.ClientId == 3u) { toClient3++; }
            }
            Assert.AreEqual(1, toClient1);
            Assert.AreEqual(0, toClient2, "Client 2 was removed when the call returned; its queue went with it.");
            Assert.AreEqual(1, toClient3);
            Assert.IsFalse(outbox.TryGetSequenceCounter(2u, out _));
            LogAssert.Expect(LogType.Warning, LogLine("[ClientMessageOutbox] UnknownClient: clientId=2 "));
            outbox.Enqueue(2u, Broadcast9, Body(MinBody, 1), false);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sink_add_client_from_inside_the_zone_wide_send_does_not_throw_and_the_new_client_gets_no_copy()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.SetDisconnectRequester(new RelayAddClientRequester { Outbox = outbox, ClientToAdd = 4u });
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            outbox.AddClient(3u);
            for (int i = 0; i < ClientMessageOutbox.OUTBOX_MAX_QUEUED_MESSAGES; i++)
            {
                outbox.Enqueue(2u, FillerType, Body(MinBody, i), false);
            }
            LogAssert.Expect(LogType.Error, LogLine("[ClientMessageOutbox] QueueFull: clientId=2 "));

            Assert.DoesNotThrow(() => outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 1), false));
            Assert.DoesNotThrow(() => outbox.Flush(1u));

            int toClient1 = 0, toClient3 = 0, toClient4 = 0;
            foreach (OutboxRecordedSend send in seam.Sends)
            {
                if (send.ClientId == 1u) { toClient1++; }
                if (send.ClientId == 3u) { toClient3++; }
                if (send.ClientId == 4u) { toClient4++; }
            }
            Assert.AreEqual(1, toClient1);
            Assert.AreEqual(1, toClient3);
            Assert.AreEqual(0, toClient4, "Client 4 joined during the send and was not part of that broadcast.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sink_zone_wide_send_from_inside_the_send_seam_does_not_drop_the_removal_early()
        {
            var seam = new OutboxRecordingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            outbox.Enqueue(1u, FillerType, Body(MinBody, 1), false);
            bool fired = false;
            seam.AfterSend = id =>
            {
                if (!fired)
                {
                    fired = true;
                    outbox.EnqueueToAllClients(Broadcast9, Body(MinBody, 2), false);
                    outbox.RemoveClient(2u);
                }
            };

            Assert.DoesNotThrow(() => outbox.Flush(1u));
            seam.AfterSend = null;
            seam.Sends.Clear();
            outbox.Flush(2u);

            Assert.AreEqual(1, seam.Sends.Count, "Only client 1 still has its zone-wide message; client 2 was removed.");
            Assert.AreEqual(1u, seam.Sends[0].ClientId);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- not cap-exempt (AC-NC-47) ----------------------------------------------------------

        [Test]
        public void test_cap_local_broadcast_is_not_exempt_it_waits_for_the_next_flush_behind_eight_queued()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64, 1u);
            for (int i = 0; i < Cap; i++)
            {
                a.Outbox.Enqueue(1u, FillerType, Body(MinBody, i), false);
            }

            a.Broadcaster.Broadcast(Broadcast9, Body(MinBody, 1));
            a.Tick(1u);
            int firstFlush = a.Seam.Sends.Count;
            ushort lastTypeFirstFlush = EnvelopeOf(a.Seam.Sends[Cap - 1]).MessageTypeId;
            a.Tick(2u);

            Assert.AreEqual(Cap, firstFlush);
            Assert.AreEqual(FillerType, lastTypeFirstFlush);
            Assert.AreEqual(Cap + 1, a.Seam.Sends.Count);
            Assert.AreEqual(Broadcast9, EnvelopeOf(a.Seam.Sends[Cap]).MessageTypeId);
            Assert.AreEqual(2u, EnvelopeOf(a.Seam.Sends[Cap]).ServerTickNumber);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cap_relayed_broadcast_is_not_exempt_it_waits_for_the_next_flush_behind_eight_queued()
        {
            var bus = new InMemoryCrossZoneRelayBus();
            var a = new RelayTestZone(bus, 64);
            var b = new RelayTestZone(bus, 64, 11u);
            for (int i = 0; i < Cap; i++)
            {
                b.Outbox.Enqueue(11u, FillerType, Body(MinBody, i), false);
            }

            a.Broadcaster.Broadcast(Broadcast9, Body(MinBody, 1));
            b.Tick(1u);
            int firstFlush = b.Seam.Sends.Count;
            ushort lastTypeFirstFlush = EnvelopeOf(b.Seam.Sends[Cap - 1]).MessageTypeId;
            b.Tick(2u);

            Assert.AreEqual(Cap, firstFlush);
            Assert.AreEqual(FillerType, lastTypeFirstFlush);
            Assert.AreEqual(Cap + 1, b.Seam.Sends.Count);
            Assert.AreEqual(Broadcast9, EnvelopeOf(b.Seam.Sends[Cap]).MessageTypeId);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- envelope codec ---------------------------------------------------------------------

        [Test]
        public void test_codec_round_trip_for_types_0_0xE103_65535_and_bodies_of_0_4_52_bytes()
        {
            ushort[] types = { 0, Broadcast9, 65535 };
            int[] lengths = { 0, MinBody, MaxBody };
            const ulong origin = 0x0123456789ABCDEFul;
            var buffer = new byte[RelayEnvelopeCodec.DecodeBufferSize(MaxBody)];

            foreach (ushort type in types)
            {
                foreach (int length in lengths)
                {
                    byte[] body = Body(length, 17);
                    string payload = RelayEnvelopeCodec.Encode(type, origin, body);

                    RelayEnvelopeDecodeResult result = RelayEnvelopeCodec.TryDecode(
                        payload, buffer, out ushort decodedType, out ulong decodedOrigin, out int decodedLength);

                    Assert.AreEqual(RelayEnvelopeDecodeResult.Ok, result, payload);
                    StringAssert.StartsWith($"v1:{type}:0123456789abcdef:", payload);
                    Assert.AreEqual(type, decodedType);
                    Assert.AreEqual(origin, decodedOrigin);
                    Assert.AreEqual(length, decodedLength);
                    CollectionAssert.AreEqual(body, new ArraySegment<byte>(buffer, 0, decodedLength).ToArray());
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_codec_a_52_byte_body_gives_a_payload_under_100_characters_and_the_documented_maximum()
        {
            string payload = RelayEnvelopeCodec.Encode(Broadcast9, ulong.MaxValue, Body(MaxBody, 1));

            Assert.Less(payload.Length, 100);
            Assert.AreEqual(98, RelayEnvelopeCodec.MaxEnvelopeLength(MaxBody));
            Assert.LessOrEqual(payload.Length, RelayEnvelopeCodec.MaxEnvelopeLength(MaxBody));
            Assert.AreEqual(54, RelayEnvelopeCodec.DecodeBufferSize(MaxBody));
        }

        [Test]
        public void test_codec_origin_is_16_lowercase_hex_digits_even_for_a_small_value()
        {
            string payload = RelayEnvelopeCodec.Encode(1, 0xABul, Body(0, 0));

            Assert.AreEqual("v1:1:00000000000000ab:", payload);
        }

        [Test]
        public void test_codec_decode_into_a_buffer_that_is_too_small_is_refused_as_body_too_long_not_thrown()
        {
            string payload = RelayEnvelopeCodec.Encode(Broadcast9, 1ul, Body(MaxBody, 1));

            RelayEnvelopeDecodeResult result = RelayEnvelopeCodec.TryDecode(payload, new byte[4], out _, out _, out _);

            Assert.AreEqual(RelayEnvelopeDecodeResult.BodyTooLong, result);
        }

        [Test]
        public void test_codec_decoded_length_is_checked_from_the_length_and_padding_before_decoding()
        {
            var buffer = new byte[MaxBody];

            RelayEnvelopeDecodeResult exactFit = RelayEnvelopeCodec.TryDecode(
                RelayEnvelopeCodec.Encode(1, 1ul, Body(MaxBody, 1)), buffer, out _, out _, out int exactLength);
            RelayEnvelopeDecodeResult onePadded = RelayEnvelopeCodec.TryDecode(
                RelayEnvelopeCodec.Encode(1, 1ul, Body(MaxBody + 1, 1)), buffer, out _, out _, out _);
            RelayEnvelopeDecodeResult notMultipleOfFour = RelayEnvelopeCodec.TryDecode(
                "v1:1:0123456789abcdef:AAAAA", new byte[1], out _, out _, out _);

            Assert.AreEqual(RelayEnvelopeDecodeResult.Ok, exactFit);
            Assert.AreEqual(MaxBody, exactLength);
            Assert.AreEqual(RelayEnvelopeDecodeResult.BodyTooLong, onePadded);
            Assert.AreEqual(RelayEnvelopeDecodeResult.BadBase64, notMultipleOfFour);
        }

        // ---- origin -----------------------------------------------------------------------------

        [Test]
        public void test_origin_two_instances_differ_and_the_hex_form_is_16_lowercase_digits()
        {
            var first = new CrossZoneRelayOrigin();
            var second = new CrossZoneRelayOrigin();

            Assert.AreNotEqual(first.Value, second.Value);
            StringAssert.IsMatch("^[0-9a-f]{16}$", first.Hex);
            Assert.AreEqual(first.Value.ToString("x16"), first.Hex);
        }

        [Test]
        public void test_origin_the_relay_folder_never_uses_the_game_random_provider()
        {
            string folder = Path.Combine(RepoRoot(), "src", "ServerLogic", "Networking", "CrossZoneRelay");
            string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);

            string[] expectedFiles =
            {
                "IServerWideBroadcaster.cs", "IZoneClientBroadcastSink.cs", "ICrossZoneRelayPublisher.cs",
                "ICrossZoneRelayInbox.cs", "ICrossZoneRelayHandler.cs", "ServerWideBroadcaster.cs",
                "CrossZoneRelayInbox.cs", "CrossZoneRelayAllowlist.cs", "RelayEnvelopeCodec.cs",
                "CrossZoneRelayReceiver.cs", "CrossZoneRelayOrigin.cs", "ZoneClientRelayHandler.cs",
            };
            var names = new List<string>();
            foreach (string path in files)
            {
                names.Add(Path.GetFileName(path));
            }
            foreach (string expected in expectedFiles)
            {
                CollectionAssert.Contains(names, expected);
            }

            foreach (string path in files)
            {
                StringAssert.DoesNotContain("IRandomProvider", File.ReadAllText(path), path);
            }

            string originSource = File.ReadAllText(Path.Combine(folder, "CrossZoneRelayOrigin.cs"));
            StringAssert.Contains("RandomNumberGenerator", originSource);
            StringAssert.DoesNotContain("System.Random", originSource);
            StringAssert.DoesNotContain("UnityEngine.Random", originSource);
            StringAssert.DoesNotContain("new Random(", originSource);
        }

        // ---- allowlist --------------------------------------------------------------------------

        [Test]
        public void test_allowlist_production_holds_one_row_enhancement9_with_52_bytes()
        {
            CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();

            Assert.AreEqual(1, allowlist.Count);
            Assert.IsTrue(allowlist.TryGetMaxBodyBytes(Broadcast9, out int max));
            Assert.AreEqual(52, max);
            Assert.AreEqual(52, allowlist.LargestMaxBodyBytes);
            Assert.IsFalse(allowlist.TryGetMaxBodyBytes(UnlistedType, out _));
        }

        [Test]
        public void test_allowlist_is_sealed_after_construction_a_changed_source_list_has_no_effect_and_no_public_mutator_exists()
        {
            var rows = new List<CrossZoneRelayAllowlistRow> { new CrossZoneRelayAllowlistRow(10, 8) };
            var allowlist = new CrossZoneRelayAllowlist(rows);

            rows.Add(new CrossZoneRelayAllowlistRow(11, 8));

            Assert.AreEqual(1, allowlist.Count);
            Assert.IsFalse(allowlist.TryGetMaxBodyBytes(11, out _));
            foreach (MethodInfo method in typeof(CrossZoneRelayAllowlist).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                StringAssert.DoesNotStartWith("Add", method.Name);
                StringAssert.DoesNotStartWith("Remove", method.Name);
                StringAssert.DoesNotStartWith("Set", method.Name);
                StringAssert.DoesNotStartWith("Clear", method.Name);
            }
            foreach (PropertyInfo property in typeof(CrossZoneRelayAllowlist).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(property.CanWrite, property.Name);
            }
        }

        [Test]
        public void test_allowlist_constructor_rejects_null_empty_and_negative_rows()
        {
            Assert.Throws<ArgumentNullException>(() => new CrossZoneRelayAllowlist(null));
            Assert.Throws<ArgumentException>(() => new CrossZoneRelayAllowlist(new CrossZoneRelayAllowlistRow[0]));
            Assert.Throws<ArgumentException>(() => new CrossZoneRelayAllowlist(new[] { new CrossZoneRelayAllowlistRow(1, -1) }));
        }

        // ---- production handler -----------------------------------------------------------------

        [Test]
        public void test_relay_handler_enqueues_to_all_clients_not_cap_exempt()
        {
            var log = NewLog();
            var handler = new ZoneClientRelayHandler(new RelayRecordingSink(log));

            handler.OnRelayed(Broadcast9, Body(MinBody, 1));

            CollectionAssert.AreEqual(new[] { "EnqueueToAll(0xE103, exempt=False, length=4)" }, log);
            Assert.AreEqual("sink", Assert.Throws<ArgumentNullException>(() => new ZoneClientRelayHandler(null)).ParamName);
        }

        // ---- allocation -------------------------------------------------------------------------

        [Test]
        public void test_allocation_broadcast_local_half_allocates_nothing()
        {
            var counting = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(counting);
            outbox.AddClient(1u);
            outbox.AddClient(2u);
            var broadcaster = new ServerWideBroadcaster(outbox, new RelayNoOpPublisher(), CrossZoneRelayAllowlist.CreateProduction());
            byte[] body = Body(MaxBody, 1);
            for (uint tick = 1u; tick <= WarmUpIterations; tick++)
            {
                broadcaster.Broadcast(Broadcast9, body);
                outbox.Flush(tick);
            }
            int warmUpCount = counting.Count;

            Assert.That(() =>
            {
                for (uint tick = WarmUpIterations + 1u; tick <= WarmUpIterations + AllocationIterations; tick++)
                {
                    broadcaster.Broadcast(Broadcast9, body);
                    outbox.Flush(tick);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(warmUpCount + 2 * AllocationIterations, counting.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_allocation_try_add_and_a_drain_of_8_entries_allocate_nothing()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 64);
            var handler = new RelayCountingHandler();
            byte[] body = Body(MaxBody, 1);
            for (int i = 0; i < WarmUpIterations; i++)
            {
                for (int k = 0; k < 8; k++)
                {
                    inbox.TryAdd(Broadcast9, body);
                }
                inbox.Drain(1u, handler);
            }
            int warmUpCount = handler.Count;

            Assert.That(() =>
            {
                for (int i = 0; i < AllocationIterations; i++)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        inbox.TryAdd(Broadcast9, body);
                    }
                    inbox.Drain(1u, handler);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(warmUpCount + 8 * AllocationIterations, handler.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_allocation_an_empty_drain_allocates_nothing()
        {
            var inbox = new CrossZoneRelayInbox(MaxBody);
            var handler = new RelayCountingHandler();
            for (int i = 0; i < WarmUpIterations; i++)
            {
                inbox.Drain(1u, handler);
            }

            Assert.That(() =>
            {
                for (int i = 0; i < AllocationIterations; i++)
                {
                    inbox.Drain(1u, handler);
                }
            }, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_allocation_an_empty_step_1a_through_the_pipeline_allocates_nothing()
        {
            var tickSource = new InboundSettableTickSource { Tick = TickSeven };
            var pipeline = new ZoneTickPipeline(
                new RelayRecordingCompletionQueue(null), new CrossZoneRelayInbox(MaxBody), new RelayCountingHandler(),
                new RelayRecordingDispatcher(null), new RelayRecordingSimulation(null), new RelayRecordingOutbound(null), tickSource);
            for (int i = 0; i < WarmUpIterations; i++)
            {
                pipeline.Tick(DeltaTime);
            }

            Assert.That(() =>
            {
                for (int i = 0; i < AllocationIterations; i++)
                {
                    pipeline.Tick(DeltaTime);
                }
            }, Is.Not.AllocatingGCMemory());
            LogAssert.NoUnexpectedReceived();
        }

        // ---- threading --------------------------------------------------------------------------

        private sealed class RelayIndexHandler : ICrossZoneRelayHandler
        {
            public readonly bool[] Seen;
            public int Delivered;
            public bool Duplicate;
            public bool OutOfOrder;
            public int LastIndex = -1;

            public RelayIndexHandler(int count)
            {
                Seen = new bool[count];
            }

            public void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                int index = body[0] | (body[1] << 8) | (body[2] << 16) | (body[3] << 24);
                if (Seen[index])
                {
                    Duplicate = true;
                }
                Seen[index] = true;
                if (index <= LastIndex)
                {
                    OutOfOrder = true;
                }
                LastIndex = index;
                Delivered++;
            }
        }

        [Test]
        [Timeout(30000)]
        public void test_threading_ten_thousand_entries_from_a_second_thread_are_each_delivered_once_or_counted_as_dropped()
        {
            const int total = 10000;
            // The full inbox makes the drain log many "EntriesDropped" warnings; they are expected noise here.
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                RunThreadingScenario(total);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        private static void RunThreadingScenario(int total)
        {
            var inbox = new CrossZoneRelayInbox(MaxBody, 64);
            var handler = new RelayIndexHandler(total);
            int dropped = 0;
            var writer = new Thread(() =>
            {
                var body = new byte[4];
                for (int i = 0; i < total; i++)
                {
                    body[0] = (byte)i;
                    body[1] = (byte)(i >> 8);
                    body[2] = (byte)(i >> 16);
                    body[3] = (byte)(i >> 24);
                    if (!inbox.TryAdd(Broadcast9, body))
                    {
                        dropped++;
                    }
                }
            });

            writer.Start();
            while (writer.IsAlive)
            {
                inbox.Drain(1u, handler);
            }
            writer.Join();
            inbox.Drain(1u, handler);

            Assert.Greater(handler.Delivered, 0, "Nothing was delivered.");
            Assert.AreEqual(total, handler.Delivered + dropped);
            Assert.IsFalse(handler.Duplicate, "An entry was delivered twice.");
            Assert.IsFalse(handler.OutOfOrder, "Delivered entry indices must strictly increase.");
            Assert.AreEqual(0, inbox.Count);
        }

        // ---- assembly ---------------------------------------------------------------------------

        [Test]
        public void test_assembly_every_relay_type_is_in_the_server_logic_assembly()
        {
            Assembly serverLogic = typeof(ClientMessageOutbox).Assembly;
            Type[] relayTypes =
            {
                typeof(IServerWideBroadcaster), typeof(IZoneClientBroadcastSink), typeof(ICrossZoneRelayPublisher),
                typeof(ICrossZoneRelayInbox), typeof(ICrossZoneRelayHandler), typeof(ServerWideBroadcaster),
                typeof(CrossZoneRelayInbox), typeof(CrossZoneRelayAllowlist), typeof(RelayEnvelopeCodec),
                typeof(CrossZoneRelayReceiver), typeof(CrossZoneRelayOrigin), typeof(ZoneClientRelayHandler),
            };

            foreach (Type type in relayTypes)
            {
                Assert.AreSame(serverLogic, type.Assembly, type.Name);
            }
            Assert.AreEqual("IronGrind.ServerLogic", serverLogic.GetName().Name);
        }
    }
}

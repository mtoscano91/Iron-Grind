using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>One send seen by <see cref="OutboxRecordingSendSeam"/>: the client, a copy of the bytes, the channel.</summary>
    internal sealed class OutboxRecordedSend
    {
        public readonly uint ClientId;
        public readonly byte[] Bytes;
        public readonly NetworkChannel Channel;

        public OutboxRecordedSend(uint clientId, byte[] bytes, NetworkChannel channel)
        {
            ClientId = clientId;
            Bytes = bytes;
            Channel = channel;
        }
    }

    /// <summary>A send seam that copies every message; an optional hook runs after each send.</summary>
    internal sealed class OutboxRecordingSendSeam : IClientMessageSendSeam
    {
        public readonly List<OutboxRecordedSend> Sends = new List<OutboxRecordedSend>();

        public Action<uint> AfterSend;

        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
        {
            Sends.Add(new OutboxRecordedSend(clientId, message.ToArray(), channel));
            AfterSend?.Invoke(clientId);
        }
    }

    /// <summary>
    /// A send seam that copies the message, runs a hook while the message span is still live, then checks
    /// that the span still holds the same bytes (the outbox must not reuse its send buffer re-entrantly).
    /// </summary>
    internal sealed class OutboxReentrantSendSeam : IClientMessageSendSeam
    {
        public readonly List<OutboxRecordedSend> Sends = new List<OutboxRecordedSend>();

        public Action DuringSend;
        public bool MessageChangedDuringHook;

        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
        {
            byte[] copy = message.ToArray();
            Sends.Add(new OutboxRecordedSend(clientId, copy, channel));
            DuringSend?.Invoke();
            if (!message.SequenceEqual(new ReadOnlySpan<byte>(copy)))
            {
                MessageChangedDuringHook = true;
            }
        }
    }

    /// <summary>A send seam that only counts, so it allocates nothing.</summary>
    internal sealed class OutboxCountingSendSeam : IClientMessageSendSeam
    {
        public int Count;
        public long ByteCount;

        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
        {
            Count++;
            ByteCount += message.Length;
        }
    }

    /// <summary>A send seam that throws for one client and records the sends of every other.</summary>
    internal sealed class OutboxThrowingSendSeam : IClientMessageSendSeam
    {
        public readonly List<OutboxRecordedSend> Sends = new List<OutboxRecordedSend>();

        public uint ThrowFor;
        public int ThrowAttempts;

        public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
        {
            if (clientId == ThrowFor)
            {
                ThrowAttempts++;
                throw new InvalidOperationException("send seam fake");
            }
            Sends.Add(new OutboxRecordedSend(clientId, message.ToArray(), channel));
        }
    }

    /// <summary>A disconnect requester that records requests and can remove the client from the outbox inside the call.</summary>
    internal sealed class OutboxRecordingDisconnectRequester : IClientDisconnectRequester
    {
        public readonly List<uint> Requests = new List<uint>();

        public ClientMessageOutbox RemoveFrom;

        public void Disconnect(uint clientId)
        {
            Requests.Add(clientId);
            if (RemoveFrom != null)
            {
                RemoveFrom.RemoveClient(clientId);
            }
        }
    }

    /// <summary>A disconnect requester that records the request and then throws.</summary>
    internal sealed class OutboxThrowingDisconnectRequester : IClientDisconnectRequester
    {
        public int Requests;

        public void Disconnect(uint clientId)
        {
            Requests++;
            throw new InvalidOperationException("disconnect requester fake");
        }
    }

    /// <summary>
    /// A fake routing lookup (the <see cref="MessageRoutingLookup"/> shape): rows for the ids set on it, or a row
    /// for every id when <see cref="KnowsEveryId"/>; throws when <see cref="Throw"/>.
    /// </summary>
    internal sealed class OutboxFakeRouting
    {
        private readonly Dictionary<ushort, NetworkChannel> _channels = new Dictionary<ushort, NetworkChannel>();

        public bool KnowsEveryId;
        public bool Throw;
        public NetworkChannel DefaultChannel = NetworkChannel.ReliableOrdered;
        public int Calls;

        public void Set(ushort messageTypeId, NetworkChannel channel)
        {
            _channels[messageTypeId] = channel;
        }

        public bool Lookup(ushort messageTypeId, out MessageRoutingEntry entry)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("routing lookup fake");
            }

            NetworkChannel channel;
            bool known = _channels.TryGetValue(messageTypeId, out channel);
            if (!known && !KnowsEveryId)
            {
                entry = default;
                return false;
            }

            if (!known)
            {
                channel = DefaultChannel;
            }

            entry = new MessageRoutingEntry(messageTypeId, "FakeRow", DesignPillar.EarnedPower, channel,
                MessageDirection.ServerToOwningClient, MessageDeliveryContext.ReliableUnorderedBatch, false, "test fake");
            return true;
        }
    }

    /// <summary>
    /// EditMode unit tests for Networking Core Story 039 — the plain C# <see cref="ClientMessageOutbox"/>
    /// (copy, order, envelope, cap and exemption, delivery, queue bound, size, unknown clients, never
    /// throws, allocation, source search). The adapter listener and the queue overload have their tests in
    /// <c>InboundTransportAdapter_tests.cs</c> and <c>WireProtocol_PriorityPathCap_tests.cs</c>; the channel-to-delivery
    /// table has its tests in <c>NgoClientMessageSender_tests.cs</c>.
    /// </summary>
    [TestFixture]
    internal sealed class ClientMessageOutbox_Tests
    {
        private const uint ClientA = 1u;
        private const uint ClientB = 2u;
        private const ushort TypeA = GoldSyncEventForcedDelivery.MessageTypeId;
        private const ushort TypeB = SelfDamageEvent.MessageTypeId;
        private const ushort TypeC = SetTarget.MessageTypeId;
        private const ushort TypeOutsideRange = DamageEvent.MessageTypeId;
        private const ushort TypeNoRow = 0xE0F0;
        private const ushort FakeIdOrdered = 0xE100;
        private const ushort FakeIdUnordered = 0xE101;
        private const ushort FakeIdUnreliable = 0xE102;
        private const int Envelope = ServerMessageEnvelope.WireSize;
        private const int MaxBody = RUBatchWriter.MAX_MESSAGE_BODY_BYTES;
        private const int SmallBody = 4;
        private const int Cap = PriorityPathQueue<int>.PRIORITY_PATH_CAP;
        private const int MaxQueued = ClientMessageOutbox.OUTBOX_MAX_QUEUED_MESSAGES;
        private const int ArenaBytes = ClientMessageOutbox.OUTBOX_ARENA_BYTES;
        private const int AllocationTicks = 125;
        private const int WarmUpTicks = 20;
        private const int DrainTicks = 20;
        private const int BacklogBodyBytes = 400;
        private const int BacklogBurstTicks = 5;
        private const int BacklogNonExemptPerBurstTick = 12;
        private const int BacklogDrainTicks = 6;
        private const int BacklogDrainNonExemptPerTick = 2;
        private const int BacklogWarmUpCycles = 2;
        private const int BacklogMeasuredCycles = 10;
        private const int TrackedWarningLimit = 64; // the outbox's MAX_TRACKED_WARNINGS, a private constant
        private const string OutboxPrefix = "[ClientMessageOutbox]";
        private const string SourceRoot = "src";
        private const string SenderFile = "NgoClientMessageSender.cs";

        private static readonly string[] SenderOnlyNames = { "SendUnnamedMessage", "FastBufferWriter" };

        private OutboxRecordingSendSeam _sink;
        private OutboxRecordingDisconnectRequester _requester;
        private ClientMessageOutbox _outbox;

        [SetUp]
        public void SetUp()
        {
            _sink = new OutboxRecordingSendSeam();
            _requester = new OutboxRecordingDisconnectRequester();
            _outbox = new ClientMessageOutbox(_sink);
            _outbox.SetDisconnectRequester(_requester);
            _outbox.AddClient(ClientA);
        }

        // ---- helpers ----------------------------------------------------------------------------

        private static Regex Log(string eventName, uint clientId) =>
            new Regex(Regex.Escape($"{OutboxPrefix} {eventName}: clientId={clientId} "));

        private static byte[] Tag(int tag, int length = SmallBody)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)tag;
            }
            return bytes;
        }

        private static int TagOf(OutboxRecordedSend send) => send.Bytes[Envelope];

        private static ServerMessageEnvelope EnvelopeOf(OutboxRecordedSend send)
        {
            Assert.IsTrue(MessageEnvelopeCodec.TryRead(send.Bytes, out ServerMessageEnvelope envelope));
            return envelope;
        }

        private List<int> TagsFor(uint clientId)
        {
            var tags = new List<int>();
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                if (send.ClientId == clientId)
                {
                    tags.Add(TagOf(send));
                }
            }
            return tags;
        }

        private List<uint> SequencesFor(uint clientId)
        {
            var sequences = new List<uint>();
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                if (send.ClientId == clientId)
                {
                    sequences.Add(EnvelopeOf(send).SequenceNumber);
                }
            }
            return sequences;
        }

        private void EnqueueTags(uint clientId, int from, int to, bool exempt = false, int length = SmallBody)
        {
            for (int tag = from; tag <= to; tag++)
            {
                _outbox.Enqueue(clientId, TypeA, Tag(tag, length), exempt);
            }
        }

        private void FlushTicks(uint firstTick, int count)
        {
            for (uint tick = firstTick; tick < firstTick + (uint)count; tick++)
            {
                _outbox.Flush(tick);
            }
        }

        private static int[] Range(int from, int to)
        {
            var values = new int[to - from + 1];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = from + i;
            }
            return values;
        }

        private ClientMessageOutbox NewOutbox(OutboxFakeRouting routing)
        {
            var outbox = new ClientMessageOutbox(_sink, routing.Lookup);
            outbox.SetDisconnectRequester(_requester);
            outbox.AddClient(ClientA);
            return outbox;
        }

        // One burst-and-drain cycle: a backlog builds (13 enqueued per tick, 8 sent), then drains to empty.
        // 400-byte bodies, so the arena tail reaches the end and a compaction runs inside the cycle.
        private static uint RunBacklogCycle(ClientMessageOutbox outbox, uint clientId, byte[] body, uint tick)
        {
            for (int t = 0; t < BacklogBurstTicks; t++)
            {
                for (int i = 0; i < BacklogNonExemptPerBurstTick; i++)
                {
                    outbox.Enqueue(clientId, TypeA, body, false);
                }
                outbox.Enqueue(clientId, TypeB, body, true);
                outbox.Flush(tick);
                tick++;
            }
            for (int t = 0; t < BacklogDrainTicks; t++)
            {
                for (int i = 0; i < BacklogDrainNonExemptPerTick; i++)
                {
                    outbox.Enqueue(clientId, TypeA, body, false);
                }
                outbox.Flush(tick);
                tick++;
            }
            return tick;
        }

        private static int MessagesPerBacklogCycle =>
            BacklogBurstTicks * (BacklogNonExemptPerBurstTick + 1) + BacklogDrainTicks * BacklogDrainNonExemptPerTick;

        // ---- constants and construction ---------------------------------------------------------

        [Test]
        public void test_outbox_constants_match_the_story()
        {
            Assert.AreEqual(128, ClientMessageOutbox.OUTBOX_MAX_QUEUED_MESSAGES);
            Assert.AreEqual(16384, ClientMessageOutbox.OUTBOX_ARENA_BYTES);
            Assert.AreEqual(0xE000, ClientMessageOutbox.PRIORITY_RANGE_FIRST);
            Assert.AreEqual(0xEFFF, ClientMessageOutbox.PRIORITY_RANGE_LAST);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_outbox_constructor_null_send_seam_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ClientMessageOutbox(null));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- copy, order, envelope --------------------------------------------------------------

        [Test]
        public void test_enqueue_copies_the_body_before_returning()
        {
            byte[] source = { 1, 2, 3, 4, 5 };

            _outbox.Enqueue(ClientA, TypeA, source, false);
            Array.Fill(source, (byte)0xFF);
            _outbox.Flush(1u);

            Assert.AreEqual(1, _sink.Sends.Count);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 },
                _sink.Sends[0].Bytes.AsSpan(Envelope, 5).ToArray());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_flush_sends_in_enqueue_order_with_envelope_type_tick_and_sequence()
        {
            ushort[] types = { TypeA, TypeB, TypeC };
            for (int i = 0; i < types.Length; i++)
            {
                _outbox.Enqueue(ClientA, types[i], Tag(i + 1), false);
            }
            Assert.AreEqual(0, _sink.Sends.Count, "Nothing is sent before the flush.");

            _outbox.Flush(77u);

            Assert.AreEqual(3, _sink.Sends.Count);
            for (int i = 0; i < types.Length; i++)
            {
                ServerMessageEnvelope envelope = EnvelopeOf(_sink.Sends[i]);
                Assert.AreEqual(types[i], envelope.MessageTypeId);
                Assert.AreEqual(77u, envelope.ServerTickNumber);
                Assert.AreEqual((uint)(i + 1), envelope.SequenceNumber);
                Assert.AreEqual(Envelope + SmallBody, _sink.Sends[i].Bytes.Length, "Envelope plus body, one message.");
                Assert.AreEqual(i + 1, TagOf(_sink.Sends[i]));
                Assert.AreEqual(ClientA, _sink.Sends[i].ClientId);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_two_clients_are_independent_and_deferred_message_is_numbered_when_sent()
        {
            _outbox.AddClient(ClientB);
            EnqueueTags(ClientA, 1, Cap + 1);
            EnqueueTags(ClientB, 1, 1);

            _outbox.Flush(1u);
            int sentAfterFirst = _sink.Sends.Count;
            _outbox.Flush(2u);

            uint firstOfB = 0;
            var sequencesOfA = new List<uint>();
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                if (send.ClientId == ClientB)
                {
                    firstOfB = EnvelopeOf(send).SequenceNumber;
                }
                else
                {
                    sequencesOfA.Add(EnvelopeOf(send).SequenceNumber);
                }
            }
            Assert.AreEqual(1u, firstOfB, "Client B starts at 1 whatever client A sent.");
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, sequencesOfA);
            Assert.AreEqual(Cap + 1, sentAfterFirst, "Eight of A and one of B.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_is_issued_at_the_flush_in_send_order_exempt_enqueued_last_carries_one()
        {
            EnqueueTags(ClientA, 1, Cap);
            _outbox.Enqueue(ClientA, TypeA, Tag(99), true);

            _outbox.Flush(1u);
            var firstFlushSequences = SequencesFor(ClientA);
            var firstFlushTags = TagsFor(ClientA);
            _sink.Sends.Clear();
            _outbox.Flush(2u);

            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 }, firstFlushSequences,
                "Numbers rise in send order on the first flush.");
            Assert.AreEqual(99, firstFlushTags[0], "The exempt message, enqueued last, is sent first.");
            CollectionAssert.AreEqual(new uint[] { 9 }, SequencesFor(ClientA), "The displaced message is numbered when it is sent.");
            CollectionAssert.AreEqual(new[] { 1 }, TagsFor(ClientA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_refused_message_between_two_accepted_ones_leaves_no_gap()
        {
            LogAssert.Expect(LogType.Error, Log("BodyTooLarge", ClientA));

            _outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            _outbox.Enqueue(ClientA, TypeA, Tag(2, MaxBody + 1), false);
            _outbox.Enqueue(ClientA, TypeA, Tag(3), false);
            _outbox.Flush(1u);

            CollectionAssert.AreEqual(new[] { 1, 3 }, TagsFor(ClientA));
            CollectionAssert.AreEqual(new uint[] { 1, 2 }, SequencesFor(ClientA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_removed_and_added_again_starts_at_one_with_an_empty_queue()
        {
            EnqueueTags(ClientA, 1, 2);
            _outbox.Flush(1u);
            EnqueueTags(ClientA, 3, 4);

            _outbox.RemoveClient(ClientA);
            _outbox.AddClient(ClientA);
            _outbox.Enqueue(ClientA, TypeA, Tag(9), false);
            _outbox.Flush(2u);

            Assert.AreEqual(3, _sink.Sends.Count, "Two before the removal and one after; the discarded pair never leaves.");
            Assert.AreEqual(9, TagOf(_sink.Sends[2]));
            Assert.AreEqual(1u, EnvelopeOf(_sink.Sends[2]).SequenceNumber);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_counter_accessor_returns_the_counter_the_flush_uses()
        {
            _outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            _outbox.Flush(1u);

            bool found = _outbox.TryGetSequenceCounter(ClientA, out ConnectionSequenceCounter counter);

            Assert.IsTrue(found);
            Assert.AreEqual(2u, counter.IssueNext(), "The flush issued 1 from this same instance.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_sequence_counter_accessor_unknown_client_returns_false()
        {
            bool found = _outbox.TryGetSequenceCounter(99u, out ConnectionSequenceCounter counter);

            Assert.IsFalse(found);
            Assert.IsNull(counter);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- cap and exemption ------------------------------------------------------------------

        [Test]
        public void test_cap_eleven_non_exempt_sends_eight_then_three_in_order()
        {
            EnqueueTags(ClientA, 1, 11);

            _outbox.Flush(1u);
            Assert.AreEqual(Cap, _sink.Sends.Count);
            _outbox.Flush(2u);

            CollectionAssert.AreEqual(Range(1, 11), TagsFor(ClientA));
            Assert.AreEqual(1u, EnvelopeOf(_sink.Sends[0]).ServerTickNumber);
            Assert.AreEqual(2u, EnvelopeOf(_sink.Sends[Cap]).ServerTickNumber, "The three go out at the next flush.");
            Assert.AreEqual(11, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_cap_is_per_client_two_clients_send_sixteen_in_one_tick()
        {
            _outbox.AddClient(ClientB);
            EnqueueTags(ClientA, 1, Cap);
            EnqueueTags(ClientB, 1, Cap);

            _outbox.Flush(1u);

            Assert.AreEqual(2 * Cap, _sink.Sends.Count);
            Assert.AreEqual(Cap, TagsFor(ClientA).Count);
            Assert.AreEqual(Cap, TagsFor(ClientB).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_exempt_before_the_flush_is_sent_first_and_displaces_the_oldest()
        {
            EnqueueTags(ClientA, 1, Cap);
            _outbox.Enqueue(ClientA, TypeA, Tag(99), true);

            _outbox.Flush(1u);
            var firstTick = TagsFor(ClientA);
            _sink.Sends.Clear();
            _outbox.Flush(2u);

            var expected = new List<int> { 99 };
            expected.AddRange(Range(2, Cap));
            CollectionAssert.AreEqual(expected, firstTick);
            Assert.AreEqual(Cap, firstTick.Count, "The tick sends 8, not 9.");
            CollectionAssert.AreEqual(new[] { 1 }, TagsFor(ClientA), "The first non-exempt message leads the next flush.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_exempt_after_the_flush_is_first_in_the_next_flush()
        {
            EnqueueTags(ClientA, 1, 10);
            _outbox.Flush(1u);
            _sink.Sends.Clear();

            _outbox.Enqueue(ClientA, TypeA, Tag(99), true);
            _outbox.Flush(2u);

            CollectionAssert.AreEqual(new[] { 99, 9, 10 }, TagsFor(ClientA));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_exempt_more_than_cap_are_all_sent_in_one_tick()
        {
            EnqueueTags(ClientA, 1, Cap + 2, exempt: true);

            _outbox.Flush(1u);

            CollectionAssert.AreEqual(Range(1, Cap + 2), TagsFor(ClientA));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- delivery ---------------------------------------------------------------------------

        [Test]
        public void test_delivery_channel_comes_from_the_routing_row_for_each_of_the_three_channels()
        {
            var routing = new OutboxFakeRouting();
            routing.Set(FakeIdOrdered, NetworkChannel.ReliableOrdered);
            routing.Set(FakeIdUnordered, NetworkChannel.ReliableUnordered);
            routing.Set(FakeIdUnreliable, NetworkChannel.Unreliable);
            ClientMessageOutbox outbox = NewOutbox(routing);

            outbox.Enqueue(ClientA, FakeIdOrdered, Tag(1), false);
            outbox.Enqueue(ClientA, FakeIdUnordered, Tag(2), false);
            outbox.Enqueue(ClientA, FakeIdUnreliable, Tag(3), false);
            outbox.Flush(1u);

            Assert.AreEqual(3, _sink.Sends.Count);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, _sink.Sends[0].Channel);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, _sink.Sends[1].Channel);
            Assert.AreEqual(NetworkChannel.Unreliable, _sink.Sends[2].Channel);
            Assert.AreEqual(3, routing.Calls, "One lookup per accepted message.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_delivery_default_lookup_uses_the_real_registry_and_a_registered_id_is_sent_reliable_ordered()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(TypeA, out MessageRoutingEntry row), "Precondition: the id has a row.");
            Assert.AreEqual(NetworkChannel.ReliableOrdered, row.Channel, "Precondition: the row is reliable ordered.");

            _outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            _outbox.Flush(1u);

            Assert.AreEqual(1, _sink.Sends.Count);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, _sink.Sends[0].Channel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_delivery_lookup_that_throws_logs_one_error_sends_nothing_and_does_not_throw()
        {
            var routing = new OutboxFakeRouting { Throw = true, KnowsEveryId = true };
            ClientMessageOutbox outbox = NewOutbox(routing);
            LogAssert.Expect(LogType.Error, Log("EnqueueThrew", ClientA));

            Assert.DoesNotThrow(() => outbox.Enqueue(ClientA, FakeIdOrdered, Tag(1), false));
            Assert.DoesNotThrow(() => outbox.Flush(1u));

            Assert.AreEqual(0, _sink.Sends.Count);
            Assert.AreEqual(1, routing.Calls);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_delivery_type_outside_the_priority_range_is_not_sent_and_logs_one_error()
        {
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(TypeOutsideRange, out _), "Precondition: the id has a row.");
            LogAssert.Expect(LogType.Error, Log("MessageTypeRejected", ClientA));

            Assert.DoesNotThrow(() => _outbox.Enqueue(ClientA, TypeOutsideRange, Tag(1), false));
            _outbox.Flush(1u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_delivery_type_in_range_without_a_routing_row_is_not_sent_and_logs_one_error()
        {
            Assert.IsFalse(MessageRoutingRegistry.TryGetEntry(TypeNoRow, out _), "Precondition: the id has no row.");
            LogAssert.Expect(LogType.Error, Log("MessageTypeRejected", ClientA));

            Assert.DoesNotThrow(() => _outbox.Enqueue(ClientA, TypeNoRow, Tag(1), false));
            _outbox.Flush(1u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_delivery_range_boundaries_only_0xE000_to_0xEFFF_are_sent_even_when_a_row_exists_for_every_id()
        {
            var routing = new OutboxFakeRouting { KnowsEveryId = true };
            ClientMessageOutbox outbox = NewOutbox(routing);
            LogAssert.Expect(LogType.Error, Log("MessageTypeRejected", ClientA));
            LogAssert.Expect(LogType.Error, Log("MessageTypeRejected", ClientA));

            outbox.Enqueue(ClientA, 0xDFFF, Tag(1), false);
            outbox.Enqueue(ClientA, 0xE000, Tag(2), false);
            outbox.Enqueue(ClientA, 0xEFFF, Tag(3), false);
            outbox.Enqueue(ClientA, 0xF000, Tag(4), false);
            outbox.Flush(1u);

            Assert.AreEqual(2, _sink.Sends.Count);
            Assert.AreEqual(0xE000, EnvelopeOf(_sink.Sends[0]).MessageTypeId);
            Assert.AreEqual(0xEFFF, EnvelopeOf(_sink.Sends[1]).MessageTypeId);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- size -------------------------------------------------------------------------------

        [Test]
        public void test_size_body_of_exactly_512_bytes_is_sent()
        {
            _outbox.Enqueue(ClientA, TypeA, Tag(7, MaxBody), false);

            _outbox.Flush(1u);

            Assert.AreEqual(1, _sink.Sends.Count);
            Assert.AreEqual(Envelope + MaxBody, _sink.Sends[0].Bytes.Length);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_size_body_of_513_bytes_is_not_sent_and_logs_one_error()
        {
            LogAssert.Expect(LogType.Error, Log("BodyTooLarge", ClientA));

            Assert.DoesNotThrow(() => _outbox.Enqueue(ClientA, TypeA, Tag(7, MaxBody + 1), false));
            _outbox.Flush(1u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_size_zero_length_body_is_queued_and_sent_as_exactly_the_ten_byte_envelope()
        {
            _outbox.Enqueue(ClientA, TypeB, ReadOnlySpan<byte>.Empty, false);

            _outbox.Flush(1u);

            Assert.AreEqual(1, _sink.Sends.Count);
            Assert.AreEqual(Envelope, _sink.Sends[0].Bytes.Length);
            Assert.AreEqual(10, _sink.Sends[0].Bytes.Length);
            Assert.AreEqual(TypeB, EnvelopeOf(_sink.Sends[0]).MessageTypeId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_size_zero_length_body_is_accepted_when_the_tail_is_exactly_at_the_arena_end()
        {
            int fits = ArenaBytes / MaxBody;
            EnqueueTags(ClientA, 1, fits, false, MaxBody);

            _outbox.Enqueue(ClientA, TypeB, ReadOnlySpan<byte>.Empty, false);
            FlushTicks(1u, DrainTicks);

            Assert.AreEqual(fits + 1, _sink.Sends.Count, "The arena is full to the byte and a message slot is free.");
            OutboxRecordedSend last = _sink.Sends[fits];
            Assert.AreEqual(Envelope, last.Bytes.Length);
            Assert.AreEqual(TypeB, EnvelopeOf(last).MessageTypeId);
            Assert.AreEqual(0, _requester.Requests.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- queue full -------------------------------------------------------------------------

        [Test]
        public void test_queue_full_129th_message_is_refused_with_one_error_and_one_disconnect_request()
        {
            _outbox.AddClient(ClientB);
            EnqueueTags(ClientA, 1, MaxQueued);
            LogAssert.Expect(LogType.Error, Log("QueueFull", ClientA));

            _outbox.Enqueue(ClientA, TypeA, Tag(200), false);

            CollectionAssert.AreEqual(new[] { ClientA }, _requester.Requests);
            LogAssert.NoUnexpectedReceived();

            // A second refused message logs nothing and requests no second disconnect.
            _outbox.Enqueue(ClientA, TypeA, Tag(201), false);
            Assert.AreEqual(1, _requester.Requests.Count);
            LogAssert.NoUnexpectedReceived();

            // Another client is not affected.
            _outbox.Enqueue(ClientB, TypeA, Tag(5), false);
            FlushTicks(1u, DrainTicks);

            CollectionAssert.AreEqual(new[] { 5 }, TagsFor(ClientB));
            CollectionAssert.AreEqual(Range(1, MaxQueued), TagsFor(ClientA), "Exactly the 128 accepted messages were sent.");
            CollectionAssert.DoesNotContain(TagsFor(ClientA), 200);
            CollectionAssert.DoesNotContain(TagsFor(ClientA), 201);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_body_that_does_not_fit_the_remaining_bytes_is_refused_the_same_way()
        {
            int fits = ArenaBytes / MaxBody;
            EnqueueTags(ClientA, 1, fits, false, MaxBody);
            LogAssert.Expect(LogType.Error, Log("QueueFull", ClientA));

            _outbox.Enqueue(ClientA, TypeA, Tag(200, 1), false);

            CollectionAssert.AreEqual(new[] { ClientA }, _requester.Requests);
            LogAssert.NoUnexpectedReceived();

            FlushTicks(1u, DrainTicks);

            CollectionAssert.AreEqual(Range(1, fits), TagsFor(ClientA), "Exactly the accepted messages were sent.");
            CollectionAssert.DoesNotContain(TagsFor(ClientA), 200);
            Assert.AreEqual(fits, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_without_a_requester_logs_one_error_that_says_so_and_does_not_throw()
        {
            var outbox = new ClientMessageOutbox(_sink);
            outbox.AddClient(ClientA);
            for (int i = 0; i < MaxQueued; i++)
            {
                outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            }
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("QueueFull: clientId=1 ") + ".*no disconnect requester"));

            Assert.DoesNotThrow(() => outbox.Enqueue(ClientA, TypeA, Tag(2), false));
            LogAssert.NoUnexpectedReceived();

            // The second refusal logs nothing: one QueueFull per client, requester or not.
            Assert.DoesNotThrow(() => outbox.Enqueue(ClientA, TypeA, Tag(3), false));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_exempt_message_with_128_queued_is_refused_like_any_other_current_behaviour()
        {
            EnqueueTags(ClientA, 1, MaxQueued);
            LogAssert.Expect(LogType.Error, Log("QueueFull", ClientA));

            _outbox.Enqueue(ClientA, TypeA, Tag(200), true);

            CollectionAssert.AreEqual(new[] { ClientA }, _requester.Requests, "Current behaviour: the exemption is not a reserve slot.");
            FlushTicks(1u, DrainTicks);
            CollectionAssert.DoesNotContain(TagsFor(ClientA), 200);
            Assert.AreEqual(MaxQueued, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_requester_that_throws_logs_the_overflow_then_one_disconnect_error_and_does_not_throw()
        {
            var requester = new OutboxThrowingDisconnectRequester();
            _outbox.SetDisconnectRequester(requester);
            EnqueueTags(ClientA, 1, MaxQueued);
            LogAssert.Expect(LogType.Error, Log("QueueFull", ClientA));
            LogAssert.Expect(LogType.Error, Log("DisconnectRequestThrew", ClientA));

            Assert.DoesNotThrow(() => _outbox.Enqueue(ClientA, TypeA, Tag(200), false));

            Assert.AreEqual(1, requester.Requests);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_space_is_free_again_after_sending_49_messages_over_7_ticks()
        {
            EnqueueTags(ClientA, 1, 49);
            for (uint tick = 1; tick <= 7; tick++)
            {
                _outbox.Flush(tick);
            }
            Assert.AreEqual(49, _sink.Sends.Count);
            CollectionAssert.AreEqual(Range(1, 49), TagsFor(ClientA));

            EnqueueTags(ClientA, 1, MaxQueued);

            Assert.AreEqual(0, _requester.Requests.Count);
            LogAssert.NoUnexpectedReceived();
            for (uint tick = 8; tick < 8 + 16; tick++)
            {
                _outbox.Flush(tick);
            }
            Assert.AreEqual(49 + MaxQueued, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_arena_is_compacted_so_sent_bytes_are_reused_and_order_is_kept()
        {
            int fits = ArenaBytes / MaxBody;
            EnqueueTags(ClientA, 1, fits, false, MaxBody);
            _outbox.Flush(1u);

            // The tail is at the end of the arena, but eight bodies were given back.
            _outbox.Enqueue(ClientA, TypeA, Tag(fits + 1, MaxBody), false);
            LogAssert.NoUnexpectedReceived();
            for (uint tick = 2; tick <= 6; tick++)
            {
                _outbox.Flush(tick);
            }

            CollectionAssert.AreEqual(Range(1, fits + 1), TagsFor(ClientA));
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                Assert.AreEqual(Envelope + MaxBody, send.Bytes.Length);
                for (int i = Envelope; i < send.Bytes.Length; i++)
                {
                    Assert.AreEqual(send.Bytes[Envelope], send.Bytes[i], "Body bytes survived the compaction.");
                }
            }
            Assert.AreEqual(0, _requester.Requests.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_queue_full_compaction_with_a_middle_hole_and_reused_indices_keeps_every_body_and_the_order()
        {
            const int body = MaxBody;
            // Tick 1: 28 non-exempt (1..28) and one exempt (29), 512 bytes each, tail at 14,848.
            // The flush sends 29 then 2..8; message 1 (displaced) and 9..28 stay: a hole opens in the middle.
            EnqueueTags(ClientA, 1, 28, false, body);
            _outbox.Enqueue(ClientA, TypeA, Tag(29, body), true);
            _outbox.Flush(1u);
            Assert.AreEqual(Cap, _sink.Sends.Count);

            // Freed entry indices are reused by 30..32, whose offsets rise while their indices fall.
            EnqueueTags(ClientA, 30, 32, false, body);
            // Tail is now 16,384 of 16,384 with 24 messages (12,288 bytes) live: this one needs a compaction.
            EnqueueTags(ClientA, 33, 33, false, body);
            Assert.AreEqual(0, _requester.Requests.Count, "The compaction made room; nothing was refused.");
            FlushTicks(2u, 4);

            var expected = new List<int> { 29, 2, 3, 4, 5, 6, 7, 8, 1 };
            for (int tag = 9; tag <= 28; tag++)
            {
                expected.Add(tag);
            }
            for (int tag = 30; tag <= 33; tag++)
            {
                expected.Add(tag);
            }
            CollectionAssert.AreEqual(expected, TagsFor(ClientA));
            foreach (OutboxRecordedSend send in _sink.Sends)
            {
                Assert.AreEqual(Envelope + body, send.Bytes.Length);
                for (int i = Envelope; i < send.Bytes.Length; i++)
                {
                    Assert.AreEqual(TagOf(send), send.Bytes[i], "Message " + TagOf(send) + " arrived with its own bytes.");
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_disconnect_reentry_remove_client_inside_the_overflowing_enqueue_does_not_throw_and_discards_the_queue()
        {
            _requester.RemoveFrom = _outbox;
            EnqueueTags(ClientA, 1, MaxQueued);
            LogAssert.Expect(LogType.Error, Log("QueueFull", ClientA));

            Assert.DoesNotThrow(() => _outbox.Enqueue(ClientA, TypeA, Tag(200), false));
            Assert.DoesNotThrow(() => _outbox.Flush(1u));

            CollectionAssert.AreEqual(new[] { ClientA }, _requester.Requests);
            Assert.AreEqual(0, _sink.Sends.Count, "The queue was discarded with the client.");
            Assert.IsFalse(_outbox.TryGetSequenceCounter(ClientA, out _));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- unknown or removed client ----------------------------------------------------------

        [Test]
        public void test_unknown_client_sends_nothing_throws_nothing_and_warns_once_per_tick()
        {
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", 50u));

            Assert.DoesNotThrow(() => _outbox.Enqueue(50u, TypeA, Tag(1), false));
            Assert.DoesNotThrow(() => _outbox.Enqueue(50u, TypeA, Tag(2), false));
            _outbox.Flush(1u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();

            // The next tick may warn again.
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", 50u));
            _outbox.Enqueue(50u, TypeA, Tag(3), false);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unknown_client_two_different_ids_in_one_tick_warn_once_each()
        {
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", 50u));
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", 51u));

            _outbox.Enqueue(50u, TypeA, Tag(1), false);
            _outbox.Enqueue(50u, TypeA, Tag(2), false);
            _outbox.Enqueue(51u, TypeA, Tag(3), false);
            _outbox.Enqueue(51u, TypeA, Tag(4), false);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_unknown_client_the_65th_distinct_id_in_one_tick_logs_no_warning()
        {
            int warnings = 0;
            Application.LogCallback capture = (string text, string stack, LogType type) =>
            {
                if (type == LogType.Warning && text.Contains(OutboxPrefix + " UnknownClient:"))
                {
                    warnings++;
                }
            };
            // The first 64 ids warn; a warning for the 65th would be unexpected and fail the test.
            for (uint id = 1000u; id < 1000u + TrackedWarningLimit; id++)
            {
                LogAssert.Expect(LogType.Warning, Log("UnknownClient", id));
            }

            Application.logMessageReceived += capture;
            try
            {
                for (uint id = 1000u; id < 1000u + TrackedWarningLimit + 1u; id++)
                {
                    _outbox.Enqueue(id, TypeA, Tag(1), false);
                }
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            Assert.AreEqual(TrackedWarningLimit, warnings, "64 distinct ids warn; the 65th logs nothing.");
            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_removed_client_discards_its_queued_messages_and_later_enqueues_warn()
        {
            _outbox.AddClient(ClientB);
            EnqueueTags(ClientA, 1, 3);
            EnqueueTags(ClientB, 1, 1);

            _outbox.RemoveClient(ClientA);
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", ClientA));
            _outbox.Enqueue(ClientA, TypeA, Tag(9), false);
            _outbox.Flush(1u);

            Assert.AreEqual(0, TagsFor(ClientA).Count);
            Assert.AreEqual(1, TagsFor(ClientB).Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_remove_unknown_client_does_nothing_and_add_known_client_keeps_its_queue()
        {
            EnqueueTags(ClientA, 1, 2);

            Assert.DoesNotThrow(() => _outbox.RemoveClient(77u));
            _outbox.AddClient(ClientA);
            _outbox.Flush(1u);

            CollectionAssert.AreEqual(new[] { 1, 2 }, TagsFor(ClientA));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- never throws -----------------------------------------------------------------------

        [Test]
        public void test_throwing_send_seam_does_not_stop_other_clients_and_logs_one_error_per_client()
        {
            var seam = new OutboxThrowingSendSeam { ThrowFor = ClientA };
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(ClientA);
            outbox.AddClient(ClientB);
            for (int i = 1; i <= 3; i++)
            {
                outbox.Enqueue(ClientA, TypeA, Tag(i), false);
            }
            outbox.Enqueue(ClientB, TypeA, Tag(1), false);
            outbox.Enqueue(ClientB, TypeA, Tag(2), false);
            LogAssert.Expect(LogType.Error, Log("SendThrew", ClientA));

            Assert.DoesNotThrow(() => outbox.Flush(1u));

            Assert.AreEqual(3, seam.ThrowAttempts, "The rest of the client's messages are still attempted.");
            Assert.AreEqual(2, seam.Sends.Count);
            Assert.AreEqual(ClientB, seam.Sends[0].ClientId);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_remove_client_inside_the_send_seam_does_not_throw_and_other_clients_are_still_sent()
        {
            _outbox.AddClient(ClientB);
            EnqueueTags(ClientA, 1, 3);
            EnqueueTags(ClientB, 1, 2);
            _sink.AfterSend = id =>
            {
                if (id == ClientA)
                {
                    _outbox.RemoveClient(ClientA);
                }
            };

            Assert.DoesNotThrow(() => _outbox.Flush(1u));
            _sink.AfterSend = null;
            LogAssert.Expect(LogType.Warning, Log("UnknownClient", ClientA));
            _outbox.Enqueue(ClientA, TypeA, Tag(9), false);
            _outbox.Flush(2u);

            CollectionAssert.AreEqual(new[] { 1 }, TagsFor(ClientA), "The rest of the removed client's queue is discarded.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, TagsFor(ClientB));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_enqueue_inside_the_send_seam_is_sent_at_the_next_flush_and_the_message_being_sent_is_intact()
        {
            var seam = new OutboxReentrantSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(ClientA);
            bool fired = false;
            seam.DuringSend = () =>
            {
                if (!fired)
                {
                    fired = true;
                    outbox.Enqueue(ClientA, TypeA, Tag(50), false);
                }
            };
            outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            outbox.Enqueue(ClientA, TypeA, Tag(2), false);

            Assert.DoesNotThrow(() => outbox.Flush(1u));
            int sentInFirstFlush = seam.Sends.Count;
            Assert.DoesNotThrow(() => outbox.Flush(2u));

            Assert.AreEqual(2, sentInFirstFlush, "The message enqueued from inside the seam waits for the next flush.");
            Assert.AreEqual(3, seam.Sends.Count);
            Assert.AreEqual(1, TagOf(seam.Sends[0]));
            Assert.AreEqual(2, TagOf(seam.Sends[1]));
            Assert.AreEqual(50, TagOf(seam.Sends[2]));
            Assert.AreEqual(2u, EnvelopeOf(seam.Sends[2]).ServerTickNumber);
            Assert.IsFalse(seam.MessageChangedDuringHook, "The message being sent was not corrupted by the re-entrant Enqueue.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_nested_flush_inside_the_send_seam_sends_nothing_extra()
        {
            var seam = new OutboxReentrantSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(ClientA);
            seam.DuringSend = () => outbox.Flush(99u);
            outbox.Enqueue(ClientA, TypeA, Tag(1), false);
            outbox.Enqueue(ClientA, TypeA, Tag(2), false);

            Assert.DoesNotThrow(() => outbox.Flush(1u));

            Assert.AreEqual(2, seam.Sends.Count);
            Assert.AreEqual(1u, EnvelopeOf(seam.Sends[0]).ServerTickNumber);
            Assert.AreEqual(1u, EnvelopeOf(seam.Sends[1]).ServerTickNumber);
            Assert.IsFalse(seam.MessageChangedDuringHook);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- allocation -------------------------------------------------------------------------

        [Test]
        public void test_enqueue_and_flush_allocate_nothing_over_1000_messages()
        {
            var seam = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(ClientA);
            byte[] body = Tag(1, 100);

            // Seven normal and one exempt message per tick: eight sent, nothing deferred.
            for (uint tick = 1; tick <= WarmUpTicks; tick++)
            {
                for (int i = 0; i < Cap - 1; i++)
                {
                    outbox.Enqueue(ClientA, TypeA, body, false);
                }
                outbox.Enqueue(ClientA, TypeB, body, true);
                outbox.Flush(tick);
            }
            int warmUpCount = seam.Count;

            Assert.That(() =>
            {
                for (uint tick = 1; tick <= AllocationTicks; tick++)
                {
                    for (int i = 0; i < Cap - 1; i++)
                    {
                        outbox.Enqueue(ClientA, TypeA, body, false);
                    }
                    outbox.Enqueue(ClientA, TypeB, body, true);
                    outbox.Flush(tick);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(warmUpCount + AllocationTicks * Cap, seam.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_enqueue_and_flush_allocate_nothing_with_a_backlog_deferral_displacement_and_compaction()
        {
            var seam = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            outbox.AddClient(ClientA);
            byte[] body = Tag(1, BacklogBodyBytes);
            uint tick = 1u;
            for (int cycle = 0; cycle < BacklogWarmUpCycles; cycle++)
            {
                tick = RunBacklogCycle(outbox, ClientA, body, tick);
            }
            int warmUpCount = seam.Count;
            uint measuredStart = tick;
            uint endTick = tick;

            Assert.That(() =>
            {
                uint t = measuredStart;
                for (int cycle = 0; cycle < BacklogMeasuredCycles; cycle++)
                {
                    t = RunBacklogCycle(outbox, ClientA, body, t);
                }
                endTick = t;
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(warmUpCount + BacklogMeasuredCycles * MessagesPerBacklogCycle, seam.Count,
                "Every message of every cycle was sent; each cycle drains to empty.");
            Assert.Greater(endTick, measuredStart);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_add_client_reserves_everything_a_fresh_client_allocates_nothing_without_its_own_warm_up()
        {
            const uint WarmClient = 9u;
            const uint FreshClient = 10u;
            var seam = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(seam);
            byte[] body = Tag(1, BacklogBodyBytes);

            // Warm the JIT on a different client; the fresh client below never ran.
            outbox.AddClient(WarmClient);
            uint tick = 1u;
            for (int cycle = 0; cycle < BacklogWarmUpCycles; cycle++)
            {
                tick = RunBacklogCycle(outbox, WarmClient, body, tick);
            }
            outbox.AddClient(FreshClient);
            int before = seam.Count;
            uint measuredStart = tick;

            Assert.That(() =>
            {
                uint t = measuredStart;
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    t = RunBacklogCycle(outbox, FreshClient, body, t);
                }
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(before + 2 * MessagesPerBacklogCycle, seam.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ---- source search ----------------------------------------------------------------------

        [Test]
        public void test_source_send_unnamed_message_and_fast_buffer_writer_appear_only_in_the_ngo_sender_file()
        {
            string srcRoot = Path.Combine(OutboxRepoRoot(), SourceRoot);
            string senderPath = null;
            foreach (string path in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileName(path), SenderFile, StringComparison.OrdinalIgnoreCase))
                {
                    senderPath = Path.GetFullPath(path);
                }
            }
            Assert.IsNotNull(senderPath, SenderFile + " must exist under src/.");
            string senderText = File.ReadAllText(senderPath);
            foreach (string name in SenderOnlyNames)
            {
                StringAssert.Contains(name, senderText, "The sender file must use " + name);
            }

            foreach (string path in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(path), senderPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string text = File.ReadAllText(path);
                foreach (string name in SenderOnlyNames)
                {
                    StringAssert.DoesNotContain(name, text, path);
                }
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static string OutboxRepoRoot()
        {
            // Application.dataPath is <repo>/Assets; the sources live in <repo>/src.
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}

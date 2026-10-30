using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode tests for Hit Detection Story 003: <see cref="RUBatchOutboundStep"/> (the per-tick R-U batch step)
    /// and the allocation-free <see cref="RUBatchWriter.Write"/> path. The flush order outbox, R-U, positions and the
    /// teardown behaviour are covered at composition level in <c>MovementSystem_PipelineWiring_tests.cs</c>.
    /// </summary>
    [TestFixture]
    internal sealed class RUBatch_OutboundStep_Tests
    {
        private const uint ClientA = 1u;
        private const uint ClientB = 2u;
        private const uint ClientC = 3u;
        private const uint Tick = 500u;
        private const int MaxClients = 3;

        private const ushort TypeHealth = 0x0A01;
        private const ushort TypeCooldown = 0x0A02;
        private const ushort TypeLoot = 0x0A03;

        private sealed class Sent
        {
            public readonly uint ClientId;
            public readonly byte[] Data;
            public readonly NetworkChannel Channel;

            public Sent(uint clientId, byte[] data, NetworkChannel channel)
            {
                ClientId = clientId;
                Data = data;
                Channel = channel;
            }
        }

        private sealed class FakeSeam : IClientMessageSendSeam
        {
            public readonly List<Sent> Packets = new List<Sent>();
            public bool Record = true;
            public int Count;
            public uint ThrowForClient = uint.MaxValue;
            public Action OnSend;

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                OnSend?.Invoke();
                if (clientId == ThrowForClient)
                {
                    throw new InvalidOperationException("boom");
                }

                Count++;
                if (Record)
                {
                    Packets.Add(new Sent(clientId, message.ToArray(), channel));
                }
            }
        }

        private FakeSeam _seam;
        private ClientMessageOutbox _outbox;
        private RUBatchOutboundStep _step;

        [SetUp]
        public void SetUp()
        {
            _seam = new FakeSeam();
            _outbox = new ClientMessageOutbox(_seam);
            _outbox.AddClient(ClientA);
            _outbox.AddClient(ClientB);
            _outbox.AddClient(ClientC);
            _step = new RUBatchOutboundStep(_seam, _outbox, MaxClients);
        }

        private static HitCheckResult Sample(HitResult result = HitResult.Hit)
            => new HitCheckResult(new EntityID(7u), new EntityID(51u), result, 1000u);

        private static byte[] HitBody(in HitCheckResult value)
        {
            byte[] body = new byte[HitCheckResult.BodySize];
            BatchSubMessageCodec.WriteHitCheckResultBody(body, in value);
            return body;
        }

        private void EnqueueHit(uint client, HitResult result = HitResult.Hit)
        {
            HitCheckResult value = Sample(result);
            _step.Enqueue(client, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, HitBody(in value));
        }

        private static List<ushort> TypeIds(byte[] packet)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out _, out ushort count));
            var ids = new List<ushort>();
            int offset = BatchHeaderCodec.HeaderSize;
            for (int i = 0; i < count; i++)
            {
                Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(new ReadOnlySpan<byte>(packet, offset, packet.Length - offset), out ushort bodySize, out ushort typeId));
                ids.Add(typeId);
                offset += BatchSubMessageFraming.SubMessageHeaderSize + bodySize;
            }

            Assert.AreEqual(packet.Length, offset, "the packet holds exactly the counted sub-messages");
            return ids;
        }

        private static ushort SubMessageCount(byte[] packet)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out _, out ushort count));
            return count;
        }

        private static uint SequenceOf(byte[] packet)
        {
            Assert.IsTrue(BatchHeaderCodec.TryRead(packet, out ServerMessageEnvelope envelope, out _));
            return envelope.SequenceNumber;
        }

        // One health message and 127 loot messages: 128 entries, over the byte budget so loot is evicted on write.
        private void FillEntries(uint client)
        {
            _step.Enqueue(client, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[1]);
            for (int i = 0; i < RUBatchOutboundStep.RU_MAX_PENDING_MESSAGES - 1; i++)
            {
                _step.Enqueue(client, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[1]);
            }
        }

        private static void ExpectWarning(string pattern) => LogAssert.Expect(LogType.Warning, new Regex(pattern));

        private static void ExpectError(string pattern) => LogAssert.Expect(LogType.Error, new Regex(pattern));

        [Test]
        public void test_ru_batch_single_client_gets_one_packet_that_decodes_back()
        {
            EnqueueHit(ClientA, HitResult.MissFacing);

            _step.Flush(Tick);

            Assert.AreEqual(1, _seam.Packets.Count);
            Sent sent = _seam.Packets[0];
            Assert.AreEqual(ClientA, sent.ClientId);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, sent.Channel);
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + HitCheckResult.BatchSize, sent.Data.Length);
            Assert.IsTrue(BatchHeaderCodec.TryRead(sent.Data, out ServerMessageEnvelope envelope, out ushort count));
            Assert.AreEqual(RUBatchWriter.TICK_BATCH_RU, envelope.MessageTypeId);
            Assert.AreEqual(Tick, envelope.ServerTickNumber);
            Assert.AreEqual(1, count);
            byte[] sub = new byte[sent.Data.Length - BatchHeaderCodec.HeaderSize];
            Array.Copy(sent.Data, BatchHeaderCodec.HeaderSize, sub, 0, sub.Length);
            Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(sub, out HitCheckResult decoded, out int bytesRead));
            Assert.AreEqual(HitCheckResult.BatchSize, bytesRead);
            Assert.AreEqual(Sample(HitResult.MissFacing), decoded);
        }

        [Test]
        public void test_ru_batch_messages_for_one_client_never_reach_another()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            _step.Enqueue(ClientB, RUBatchCategory.SkillCooldownUpdate, TypeCooldown, new byte[3]);

            _step.Flush(Tick);

            Assert.AreEqual(2, _seam.Packets.Count);
            Sent a = _seam.Packets.Find(p => p.ClientId == ClientA);
            Sent b = _seam.Packets.Find(p => p.ClientId == ClientB);
            CollectionAssert.AreEqual(new ushort[] { TypeHealth }, TypeIds(a.Data));
            CollectionAssert.AreEqual(new ushort[] { TypeCooldown }, TypeIds(b.Data));
            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == ClientC));
        }

        [Test]
        public void test_ru_batch_empty_tick_sends_nothing_and_leaves_the_sequence_counter_alone()
        {
            Assert.IsTrue(_outbox.TryGetSequenceCounter(ClientA, out ConnectionSequenceCounter counter));
            uint before = counter.IssueNext();

            _step.Flush(Tick);
            uint after = counter.IssueNext();

            Assert.AreEqual(0, _seam.Count);
            Assert.AreEqual(before + 1u, after);
        }

        [Test]
        public void test_ru_batch_second_flush_without_new_messages_sends_nothing()
        {
            EnqueueHit(ClientA);
            _step.Flush(Tick);
            Assert.AreEqual(1, _seam.Count);

            _step.Flush(Tick + 1u);

            Assert.AreEqual(1, _seam.Count);
        }

        [Test]
        public void test_ru_batch_overflow_across_categories_drops_loot_first_and_fits()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[200]);
            _step.Enqueue(ClientA, RUBatchCategory.SkillCooldownUpdate, TypeCooldown, new byte[100]);
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[200]);
            ExpectWarning("BatchOverflow");

            _step.Flush(Tick);

            Assert.AreEqual(1, _seam.Packets.Count);
            byte[] packet = _seam.Packets[0].Data;
            Assert.LessOrEqual(packet.Length, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);
            CollectionAssert.AreEqual(new ushort[] { TypeHealth, TypeCooldown }, TypeIds(packet));
        }

        [Test]
        public void test_ru_batch_lone_category_overflow_sends_a_twelve_byte_empty_batch()
        {
            // 29 x 17 + 12 = 505 fits in 512; 30 does not.
            for (int i = 0; i < 30; i++)
            {
                EnqueueHit(ClientA);
            }

            ExpectWarning("BatchOverflow");
            _step.Flush(Tick);

            Assert.AreEqual(1, _seam.Packets.Count);
            byte[] packet = _seam.Packets[0].Data;
            Assert.AreEqual(BatchHeaderCodec.HeaderSize, packet.Length);
            Assert.AreEqual(0, SubMessageCount(packet));
        }

        [Test]
        public void test_ru_batch_twenty_nine_hit_check_results_fit_without_a_log()
        {
            for (int i = 0; i < 29; i++)
            {
                EnqueueHit(ClientA);
            }

            _step.Flush(Tick);

            byte[] packet = _seam.Packets[0].Data;
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + 29 * HitCheckResult.BatchSize, packet.Length);
            Assert.AreEqual(29, SubMessageCount(packet));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ru_batch_damage_event_category_is_refused_with_an_error()
        {
            ExpectError("MessageRefused");

            _step.Enqueue(ClientA, RUBatchCategory.DamageEvent, 0x0301, new byte[4]);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
        }

        [Test]
        public void test_ru_batch_gold_sync_event_category_is_refused_with_an_error()
        {
            ExpectError("MessageRefused");

            _step.Enqueue(ClientA, RUBatchCategory.GoldSyncEvent, 0x0302, new byte[4]);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
        }

        [Test]
        public void test_ru_batch_category_outside_the_enum_range_is_refused_with_an_error()
        {
            ExpectError("MessageRefused");

            _step.Enqueue(ClientA, (RUBatchCategory)200, TypeHealth, new byte[4]);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
        }

        [Test]
        public void test_ru_batch_body_of_497_bytes_is_refused_with_an_error()
        {
            ExpectError("MessageRefused");

            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth,
                new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes + 1]);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
        }

        [Test]
        public void test_ru_batch_body_of_496_bytes_is_accepted_and_sent()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth,
                new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes]);

            _step.Flush(Tick);

            Assert.AreEqual(RUBatchWriter.MAX_MESSAGE_BODY_BYTES, _seam.Packets[0].Data.Length);
            CollectionAssert.AreEqual(new ushort[] { TypeHealth }, TypeIds(_seam.Packets[0].Data));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ru_batch_129th_entry_is_dropped_with_one_warning_and_earlier_messages_are_sent()
        {
            FillEntries(ClientA);
            ExpectWarning("BatchFull");

            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[1]);
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[1]);
            LogAssert.NoUnexpectedReceived();
            ExpectWarning("BatchOverflow");
            _step.Flush(Tick);

            Assert.AreEqual(1, _seam.Packets.Count);
            CollectionAssert.AreEqual(new ushort[] { TypeHealth }, TypeIds(_seam.Packets[0].Data));
        }

        [Test]
        public void test_ru_batch_body_past_the_4096_byte_arena_is_dropped_with_one_warning()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[1]);
            int fitting = (RUBatchOutboundStep.RU_ARENA_BYTES - 1) / RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes;
            for (int i = 0; i < fitting; i++)
            {
                _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes]);
            }

            ExpectWarning("BatchFull");
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes]);
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes]);
            LogAssert.NoUnexpectedReceived();
            ExpectWarning("BatchOverflow");
            _step.Flush(Tick);

            CollectionAssert.AreEqual(new ushort[] { TypeHealth }, TypeIds(_seam.Packets[0].Data));
        }

        [Test]
        public void test_ru_batch_the_full_warning_fires_again_on_the_next_tick()
        {
            FillEntries(ClientA);
            ExpectWarning("BatchFull");
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[1]);
            ExpectWarning("BatchOverflow");
            _step.Flush(Tick);

            FillEntries(ClientA);
            ExpectWarning("BatchFull");
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[1]);
            ExpectWarning("BatchOverflow");
            _step.Flush(Tick + 1u);

            Assert.AreEqual(2, _seam.Packets.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ru_batch_more_clients_than_states_drops_the_extra_clients_message_with_a_warning()
        {
            var step = new RUBatchOutboundStep(_seam, _outbox, 2);
            step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            step.Enqueue(ClientB, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            ExpectWarning("BatchFull");

            Assert.DoesNotThrow(() => step.Enqueue(ClientC, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]));
            step.Flush(Tick);

            Assert.AreEqual(2, _seam.Packets.Count);
            Assert.IsNull(_seam.Packets.Find(p => p.ClientId == ClientC));
        }

        [Test]
        public void test_ru_batch_client_removed_before_flush_gets_nothing_and_others_are_unaffected()
        {
            EnqueueHit(ClientA);
            EnqueueHit(ClientB);
            _outbox.RemoveClient(ClientA);

            Assert.DoesNotThrow(() => _step.Flush(Tick));

            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(ClientB, _seam.Packets[0].ClientId);
        }

        [Test]
        public void test_ru_batch_message_for_a_client_removed_from_the_outbox_is_dropped_without_a_log()
        {
            _outbox.RemoveClient(ClientA);

            EnqueueHit(ClientA);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ru_batch_unknown_client_is_dropped_silently()
        {
            _step.Enqueue(99u, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            _step.Flush(Tick);

            Assert.AreEqual(0, _seam.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_ru_batch_send_throwing_for_one_client_is_logged_and_the_other_is_still_sent()
        {
            _seam.ThrowForClient = ClientA;
            EnqueueHit(ClientA);
            EnqueueHit(ClientB);
            ExpectError("SendThrew");

            _step.Flush(Tick);
            _seam.ThrowForClient = uint.MaxValue;
            _step.Flush(Tick + 1u);

            Assert.AreEqual(1, _seam.Packets.Count);
            Assert.AreEqual(ClientB, _seam.Packets[0].ClientId);
        }

        [Test]
        public void test_ru_batch_sequence_numbers_are_consecutive_on_the_outbox_counter()
        {
            EnqueueHit(ClientA);
            _step.Flush(Tick);
            EnqueueHit(ClientA);
            _step.Flush(Tick + 1u);

            uint first = SequenceOf(_seam.Packets[0].Data);
            uint second = SequenceOf(_seam.Packets[1].Data);
            Assert.AreEqual(first + 1u, second);
            Assert.IsTrue(_outbox.TryGetSequenceCounter(ClientA, out ConnectionSequenceCounter counter));
            Assert.AreEqual(second + 1u, counter.IssueNext());
        }

        [Test]
        public void test_ru_batch_mixed_categories_are_written_in_canonical_order_keeping_input_order_within_a_category()
        {
            _step.Enqueue(ClientA, RUBatchCategory.LootBidUpdate, TypeLoot, new byte[2]);
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, 0x0A11, new byte[2]);
            _step.Enqueue(ClientA, RUBatchCategory.SkillCooldownUpdate, TypeCooldown, new byte[2]);
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, 0x0A12, new byte[2]);

            _step.Flush(Tick);

            CollectionAssert.AreEqual(new ushort[] { 0x0A11, 0x0A12, TypeCooldown, TypeLoot }, TypeIds(_seam.Packets[0].Data));
        }

        [Test]
        public void test_ru_batch_enqueue_and_flush_allocate_nothing()
        {
            _seam.Record = false;
            byte[] body = HitBody(Sample());
            _step.Enqueue(ClientA, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
            _step.Enqueue(ClientB, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
            _step.Flush(Tick);

            Assert.That(() =>
            {
                _step.Enqueue(ClientA, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
                _step.Enqueue(ClientB, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body);
                _step.Flush(Tick + 1u);
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(4, _seam.Count);
        }

        [Test]
        public void test_ru_batch_writer_allocates_nothing_without_overflow()
        {
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            byte[] body = HitBody(Sample());
            var pending = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.LootBidUpdate, TypeLoot, body),
                new PendingSubMessage(RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body),
                new PendingSubMessage(RUBatchCategory.EntityHealthUpdate, TypeHealth, body),
            };
            RUBatchWriter.Write(buffer, 1u, Tick, null, null, pending, ClientA);
            int written = 0;

            Assert.That(() => { written = RUBatchWriter.Write(buffer, 2u, Tick, null, null, pending, ClientA); },
                Is.Not.AllocatingGCMemory());
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + 3 * (BatchSubMessageFraming.SubMessageHeaderSize + body.Length), written);
        }

        [Test]
        public void test_ru_batch_enqueue_from_inside_the_send_seam_is_refused_with_an_error()
        {
            EnqueueHit(ClientA);
            _seam.OnSend = () =>
            {
                _seam.OnSend = null;
                _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            };
            ExpectError("EnqueueDuringFlush");

            _step.Flush(Tick);
            _step.Flush(Tick + 1u);

            Assert.AreEqual(1, _seam.Packets.Count);
        }

        [Test]
        public void test_ru_batch_zero_length_body_is_sent_as_a_header_only_sub_message()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, ReadOnlySpan<byte>.Empty);

            _step.Flush(Tick);

            byte[] packet = _seam.Packets[0].Data;
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + BatchSubMessageFraming.SubMessageHeaderSize, packet.Length);
            CollectionAssert.AreEqual(new ushort[] { TypeHealth }, TypeIds(packet));
        }

        [Test]
        public void test_ru_batch_recycled_state_holds_nothing_of_the_previous_client()
        {
            _step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[] { 1, 2, 3 });
            _step.Flush(Tick);

            _step.Enqueue(ClientB, RUBatchCategory.SkillCooldownUpdate, TypeCooldown, new byte[] { 9 });
            _step.Flush(Tick + 1u);

            Assert.AreEqual(2, _seam.Packets.Count);
            byte[] packet = _seam.Packets[1].Data;
            Assert.AreEqual(ClientB, _seam.Packets[1].ClientId);
            CollectionAssert.AreEqual(new ushort[] { TypeCooldown }, TypeIds(packet));
            Assert.AreEqual((byte)9, packet[packet.Length - 1]);
        }

        [Test]
        public void test_ru_batch_exhausted_pool_recovers_on_the_next_tick()
        {
            var step = new RUBatchOutboundStep(_seam, _outbox, 1);
            step.Enqueue(ClientA, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            ExpectWarning("BatchFull");
            step.Enqueue(ClientB, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            step.Flush(Tick);

            step.Enqueue(ClientB, RUBatchCategory.EntityHealthUpdate, TypeHealth, new byte[2]);
            step.Flush(Tick + 1u);

            Assert.AreEqual(2, _seam.Packets.Count);
            Assert.AreEqual(ClientB, _seam.Packets[1].ClientId);
        }

        [Test]
        public void test_ru_batch_largest_tick_number_is_written_unchanged()
        {
            EnqueueHit(ClientA);

            _step.Flush(uint.MaxValue);

            Assert.IsTrue(BatchHeaderCodec.TryRead(_seam.Packets[0].Data, out ServerMessageEnvelope envelope, out _));
            Assert.AreEqual(uint.MaxValue, envelope.ServerTickNumber);
        }

        [Test]
        public void test_ru_batch_constructor_null_seam_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RUBatchOutboundStep(null, _outbox, 1));
        }

        [Test]
        public void test_ru_batch_constructor_null_outbox_throws()
        {
            Assert.Throws<ArgumentNullException>(() => new RUBatchOutboundStep(_seam, null, 1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void test_ru_batch_constructor_max_clients_below_one_throws(int maxClients)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RUBatchOutboundStep(_seam, _outbox, maxClients));
        }
    }
}

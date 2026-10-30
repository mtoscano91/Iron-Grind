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
    /// EditMode tests for Hit Detection Story 002: the <see cref="HitCheckResult"/> wire type, its
    /// <see cref="BatchSubMessageCodec"/> entries, <see cref="WireEnumCodec.DecodeHitResult"/>, the
    /// routing row, and the <see cref="RUBatchWriter"/> write and drop order.
    /// </summary>
    [TestFixture]
    internal sealed class HitCheckResult_Codec_Tests
    {
        private const uint AttackerRaw = 7u;
        private const uint TargetRaw = 51u;
        private const uint Tick = 1000u;
        private const int ResultByteOffsetInBatch = BatchSubMessageFraming.SubMessageHeaderSize + 8;

        private const ushort TypeIdCast = 0x0A10;
        private const ushort TypeIdCooldown = 0x0A11;
        private const ushort TypeIdLoot = 0x0A12;

        private static HitCheckResult Sample(HitResult result = HitResult.MissFacing)
            => new HitCheckResult(new EntityID(AttackerRaw), new EntityID(TargetRaw), result, Tick);

        private static byte[] WriteBatch(in HitCheckResult value)
        {
            byte[] buffer = new byte[HitCheckResult.BatchSize];
            BatchSubMessageCodec.WriteHitCheckResult(buffer, in value);
            return buffer;
        }

        [Test]
        public void test_hit_check_result_write_then_read_round_trips_all_fields()
        {
            // Arrange
            HitCheckResult original = Sample();
            byte[] buffer = new byte[HitCheckResult.BatchSize];

            // Act
            int written = BatchSubMessageCodec.WriteHitCheckResult(buffer, in original);
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(buffer, out HitCheckResult decoded, out int bytesRead);

            // Assert
            Assert.AreEqual(HitCheckResult.BatchSize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(HitCheckResult.BatchSize, bytesRead);
            Assert.AreEqual(original, decoded);
            Assert.AreEqual(AttackerRaw, decoded.AttackerEntityId.RawValue);
            Assert.AreEqual(TargetRaw, decoded.TargetEntityId.RawValue);
            Assert.AreEqual(HitResult.MissFacing, decoded.Result);
            Assert.AreEqual(Tick, decoded.ServerTick);
        }

        [Test]
        public void test_hit_check_result_max_server_tick_round_trips()
        {
            // Arrange
            var original = new HitCheckResult(new EntityID(AttackerRaw), new EntityID(TargetRaw), HitResult.Hit, uint.MaxValue);
            byte[] buffer = WriteBatch(in original);

            // Act
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(buffer, out HitCheckResult decoded, out _);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(uint.MaxValue, decoded.ServerTick);
        }

        [Test]
        public void test_hit_check_result_empty_input_returns_false()
        {
            // Arrange / Act
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(ReadOnlySpan<byte>.Empty, out HitCheckResult decoded, out int bytesRead);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(default(HitCheckResult), decoded);
            Assert.AreEqual(0, bytesRead);
        }

        [Test]
        public void test_hit_check_result_sizes_and_type_id_match_the_wire_spec()
        {
            // Arrange / Act / Assert: the numbers are the point.
            Assert.AreEqual(13, HitCheckResult.BodySize);
            Assert.AreEqual(17, HitCheckResult.BatchSize);
            Assert.AreEqual(0x0306, HitCheckResult.MessageTypeId);
        }

        [Test]
        public void test_hit_check_result_write_produces_the_expected_little_endian_bytes()
        {
            // Arrange
            HitCheckResult value = Sample();
            byte[] expected =
            {
                0x0F, 0x00, 0x06, 0x03, // header: length field 2 + 13 = 15, type 0x0306
                0x07, 0x00, 0x00, 0x00, // attacker 7
                0x33, 0x00, 0x00, 0x00, // target 51
                0x02,                   // MissFacing
                0xE8, 0x03, 0x00, 0x00, // tick 1000
            };

            // Act
            byte[] actual = WriteBatch(in value);

            // Assert
            Assert.AreEqual(17, actual.Length);
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void test_hit_check_result_body_writer_matches_the_bytes_after_the_header()
        {
            // Arrange
            HitCheckResult value = Sample();
            byte[] full = WriteBatch(in value);
            byte[] body = new byte[HitCheckResult.BodySize];

            // Act
            int written = BatchSubMessageCodec.WriteHitCheckResultBody(body, in value);

            // Assert
            Assert.AreEqual(HitCheckResult.BodySize, written);
            byte[] fullBody = new byte[HitCheckResult.BodySize];
            Array.Copy(full, BatchSubMessageFraming.SubMessageHeaderSize, fullBody, 0, HitCheckResult.BodySize);
            CollectionAssert.AreEqual(fullBody, body);
        }

        [Test]
        public void test_hit_check_result_truncated_input_returns_false()
        {
            // Arrange
            byte[] full = WriteBatch(Sample());
            byte[] truncated = new byte[HitCheckResult.BatchSize - 1];
            Array.Copy(full, truncated, truncated.Length);

            // Act
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(truncated, out HitCheckResult decoded, out int bytesRead);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(default(HitCheckResult), decoded);
            Assert.AreEqual(0, bytesRead);
        }

        [TestCase((byte)3)]
        [TestCase((byte)255)]
        public void test_hit_check_result_bad_result_byte_reads_as_miss_out_of_range_and_logs_once(byte badByte)
        {
            // Arrange
            byte[] buffer = WriteBatch(Sample(HitResult.Hit));
            buffer[ResultByteOffsetInBatch] = badByte;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeHitResult.*" + badByte));

            // Act
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(buffer, out HitCheckResult decoded, out _);

            // Assert: the message is still read, with a miss and the other fields intact.
            Assert.IsTrue(ok);
            Assert.AreEqual(HitResult.MissOutOfRange, decoded.Result);
            Assert.AreEqual(AttackerRaw, decoded.AttackerEntityId.RawValue);
            Assert.AreEqual(Tick, decoded.ServerTick);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase((byte)0, HitResult.Hit)]
        [TestCase((byte)1, HitResult.MissOutOfRange)]
        [TestCase((byte)2, HitResult.MissFacing)]
        public void test_hit_check_result_valid_result_bytes_read_unchanged_with_no_anomaly(byte validByte, HitResult expected)
        {
            // Arrange
            byte[] buffer = WriteBatch(Sample());
            buffer[ResultByteOffsetInBatch] = validByte;

            // Act
            bool ok = BatchSubMessageCodec.TryReadHitCheckResult(buffer, out HitCheckResult decoded, out _);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(expected, decoded.Result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_hit_check_result_routing_registry_has_exactly_one_row_with_the_expected_routing()
        {
            // Arrange
            int rowCount = 0;
            foreach (MessageRoutingEntry row in MessageRoutingRegistry.AllEntries)
            {
                if (row.MessageTypeId == HitCheckResult.MessageTypeId)
                {
                    rowCount++;
                }
            }

            // Act
            bool found = MessageRoutingRegistry.TryGetEntry(HitCheckResult.MessageTypeId, out MessageRoutingEntry entry);

            // Assert
            Assert.AreEqual(1, rowCount);
            Assert.IsTrue(found);
            Assert.AreEqual(nameof(HitCheckResult), entry.MessageName);
            Assert.AreEqual(DesignPillar.RhythmMastery, entry.Pillars);
            Assert.AreEqual(NetworkChannel.ReliableUnordered, entry.Channel);
            Assert.AreEqual(MessageDirection.ServerToOwningClient, entry.Direction);
            Assert.AreEqual(MessageDeliveryContext.ReliableUnorderedBatch, entry.DeliveryContext);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void test_hit_check_result_category_values_are_stable()
        {
            // Arrange / Act / Assert: the numbers are the point.
            Assert.AreEqual((byte)8, (byte)RUBatchCategory.ConnectionQualityUpdate);
            Assert.AreEqual((byte)9, (byte)RUBatchCategory.HitCheckResult);
        }

        [Test]
        public void test_hit_check_result_opaque_path_goes_through_the_batch_writer_and_reads_back_equal()
        {
            // Arrange
            HitCheckResult original = Sample(HitResult.Hit);
            byte[] body = new byte[HitCheckResult.BodySize];
            BatchSubMessageCodec.WriteHitCheckResultBody(body, in original);
            var pending = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, body),
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            int written = RUBatchWriter.Write(
                buffer, sequenceNumber: 1u, tickNumber: Tick,
                damageEvents: Array.Empty<DamageEvent>(), goldSyncEvents: Array.Empty<GoldSyncEvent>(),
                otherSubMessages: pending, clientIdForLogging: 1u);

            // Assert
            Assert.AreEqual(BatchHeaderCodec.HeaderSize + HitCheckResult.BatchSize, written);
            Assert.IsTrue(BatchHeaderCodec.TryRead(buffer, out _, out ushort count));
            Assert.AreEqual(1, count);
            Assert.IsTrue(BatchSubMessageCodec.TryReadHitCheckResult(buffer.AsSpan(BatchHeaderCodec.HeaderSize), out HitCheckResult decoded, out _));
            Assert.AreEqual(original, decoded);
        }

        [Test]
        public void test_hit_check_result_serialization_order_is_cooldown_then_hit_check_then_loot_bid()
        {
            // Arrange: supplied in reverse of the expected write order.
            byte[] hitBody = new byte[HitCheckResult.BodySize];
            HitCheckResult value = Sample();
            BatchSubMessageCodec.WriteHitCheckResultBody(hitBody, in value);
            var pending = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.LootBidUpdate, TypeIdLoot, new byte[4]),
                new PendingSubMessage(RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, hitBody),
                new PendingSubMessage(RUBatchCategory.SkillCooldownUpdate, TypeIdCooldown, new byte[4]),
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];

            // Act
            int written = RUBatchWriter.Write(
                buffer, 1u, Tick, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(), pending, clientIdForLogging: 1u);

            // Assert
            ushort[] actual = ReadTypeIds(buffer, written);
            CollectionAssert.AreEqual(new[] { TypeIdCooldown, HitCheckResult.MessageTypeId, TypeIdLoot }, actual);
        }

        // Sizes: cast + cooldown + hit + loot. Budget is 512 including the 12-byte header, each
        // sub-message adds 4 header bytes. Loot is always 120 bytes, cooldown and hit 100 bytes.
        [TestCase(196, new[] { TypeIdCast, TypeIdCooldown, HitCheckResult.MessageTypeId }, TestName = "test_hit_check_result_overflow_drops_loot_bid_first")]
        [TestCase(316, new[] { TypeIdCast, TypeIdCooldown }, TestName = "test_hit_check_result_overflow_drops_hit_check_second")]
        [TestCase(416, new[] { TypeIdCast }, TestName = "test_hit_check_result_overflow_drops_skill_cooldown_third")]
        public void test_hit_check_result_overflow_order(int castPayloadBytes, ushort[] expectedSurvivors)
        {
            // Arrange
            var pending = new List<PendingSubMessage>
            {
                new PendingSubMessage(RUBatchCategory.SkillCastResult, TypeIdCast, new byte[castPayloadBytes]),
                new PendingSubMessage(RUBatchCategory.SkillCooldownUpdate, TypeIdCooldown, new byte[96]),
                new PendingSubMessage(RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, new byte[96]),
                new PendingSubMessage(RUBatchCategory.LootBidUpdate, TypeIdLoot, new byte[116]),
            };
            byte[] buffer = new byte[ClientBufferSet.BufferSize];
            LogAssert.Expect(LogType.Warning, new Regex(@"\[RUBatchWriter\] BatchOverflow"));

            // Act
            int written = RUBatchWriter.Write(
                buffer, 1u, Tick, Array.Empty<DamageEvent>(), Array.Empty<GoldSyncEvent>(), pending, clientIdForLogging: 1u);

            // Assert
            Assert.LessOrEqual(written, RUBatchWriter.MAX_MESSAGE_BODY_BYTES);
            CollectionAssert.AreEqual(expectedSurvivors, ReadTypeIds(buffer, written));
        }

        [Test]
        public void test_hit_check_result_write_and_read_do_not_allocate()
        {
            // Arrange
            HitCheckResult value = Sample();
            byte[] buffer = new byte[HitCheckResult.BatchSize];
            BatchSubMessageCodec.WriteHitCheckResult(buffer, in value); // warm-up
            BatchSubMessageCodec.TryReadHitCheckResult(buffer, out _, out _);

            // Act / Assert
            Assert.That(() =>
            {
                BatchSubMessageCodec.WriteHitCheckResult(buffer, in value);
                BatchSubMessageCodec.TryReadHitCheckResult(buffer, out _, out _);
            }, Is.Not.AllocatingGCMemory());
        }

        private static ushort[] ReadTypeIds(byte[] buffer, int written)
        {
            var ids = new List<ushort>();
            int offset = BatchHeaderCodec.HeaderSize;
            while (offset < written)
            {
                Assert.IsTrue(BatchSubMessageFraming.TryReadHeader(buffer.AsSpan(offset), out ushort bodySize, out ushort typeId));
                ids.Add(typeId);
                offset += BatchSubMessageFraming.SubMessageHeaderSize + bodySize;
            }

            Assert.AreEqual(written, offset);
            return ids.ToArray();
        }
    }
}

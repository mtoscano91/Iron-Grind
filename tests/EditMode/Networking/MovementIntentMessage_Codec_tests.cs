using System;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Movement System Story 002: <see cref="MovementIntentMessage"/> and
    /// <see cref="MovementIntentMessageCodec"/> (CR-MOV-10; networking-wire-protocol.md Movement System Messages).
    /// </summary>
    [TestFixture]
    internal sealed class MovementIntentMessage_Codec_Tests
    {
        private const float DIR_TOLERANCE = 1f / 32767f;

        // ── Round trip / layout ────────────────────────────────────────────────────────────

        [Test]
        public void test_movement_intent_round_trip_preserves_all_fields()
        {
            // Arrange
            (short x, short z) = MovementIntentMessageCodec.EncodeDirection(new Vector2(0.6f, -0.8f), out bool wasNonUnit);
            short facing = MovementIntentMessageCodec.EncodeFacing(123.4f);
            var message = new MovementIntentMessage(x, z, facing, 1000u);
            var body = new byte[MovementIntentMessage.BodySize];

            // Act
            int written = MovementIntentMessageCodec.WriteBody(body, in message);
            bool read = MovementIntentMessageCodec.TryReadBody(body, out MovementIntentMessage result);
            Vector2 decoded = MovementIntentMessageCodec.DecodeDirection(result.DirX, result.DirZ);

            // Assert
            Assert.AreEqual(10, written);
            Assert.IsTrue(read);
            Assert.IsFalse(wasNonUnit);
            Assert.AreEqual(19660, result.DirX);
            Assert.AreEqual(-26214, result.DirZ);
            Assert.AreEqual(1234, result.FacingAngle);
            Assert.AreEqual(1000u, result.TickNumber);
            Assert.AreEqual(0.6f, decoded.x, DIR_TOLERANCE);
            Assert.AreEqual(-0.8f, decoded.y, DIR_TOLERANCE);
        }

        [Test]
        public void test_movement_intent_write_body_uses_little_endian_field_order()
        {
            // Arrange
            var message = new MovementIntentMessage(0x0102, unchecked((short)0xFF03), 0x0405, 0x0A0B0C0Du);
            var body = new byte[MovementIntentMessage.BodySize];

            // Act
            MovementIntentMessageCodec.WriteBody(body, in message);

            // Assert
            CollectionAssert.AreEqual(
                new byte[] { 0x02, 0x01, 0x03, 0xFF, 0x05, 0x04, 0x0D, 0x0C, 0x0B, 0x0A },
                body);
        }

        [Test]
        public void test_movement_intent_wire_size_is_envelope_plus_body()
        {
            // Arrange
            int expectedWireSize = ClientEntityMessageEnvelope.WireSize + MovementIntentMessage.BodySize;

            // Act
            int wireSize = MovementIntentMessage.WireSize;

            // Assert
            Assert.AreEqual(10, MovementIntentMessage.BodySize);
            Assert.AreEqual(expectedWireSize, wireSize);
            Assert.AreEqual(24, wireSize);
            Assert.AreEqual(0x0211, MovementIntentMessage.MessageTypeId);
        }

        [Test]
        public void test_movement_intent_full_message_round_trips_through_envelope()
        {
            // Arrange
            var message = new MovementIntentMessage(-32767, 32767, 3599, uint.MaxValue);
            var buffer = new byte[MovementIntentMessage.WireSize];

            // Act
            int written = MovementIntentMessageCodec.Write(buffer, 7u, 1000u, 501u, in message);
            bool read = MovementIntentMessageCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out MovementIntentMessage result);

            // Assert
            Assert.AreEqual(MovementIntentMessage.WireSize, written);
            Assert.IsTrue(read);
            Assert.AreEqual(MovementIntentMessage.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(-32767, result.DirX);
            Assert.AreEqual(32767, result.DirZ);
            Assert.AreEqual(3599, result.FacingAngle);
            Assert.AreEqual(uint.MaxValue, result.TickNumber);
        }

        [Test]
        public void test_movement_intent_full_message_with_wrong_type_id_returns_false()
        {
            // Arrange
            var buffer = new byte[MovementIntentMessage.WireSize];
            var wrongEnvelope = new ClientEntityMessageEnvelope(HeartbeatMessage.MessageTypeId, 1u, 1u, 1u);
            MessageEnvelopeCodec.Write(buffer, in wrongEnvelope);

            // Act
            bool read = MovementIntentMessageCodec.TryRead(buffer, out _, out _);

            // Assert
            Assert.IsFalse(read);
        }

        [TestCase(0)]
        [TestCase(ClientEntityMessageEnvelope.WireSize - 1)]
        [TestCase(MovementIntentMessage.WireSize - 1)]
        [TestCase(MovementIntentMessage.WireSize + 1)]
        public void test_movement_intent_full_message_with_wrong_length_returns_false(int length)
        {
            // Arrange: a valid message written into a larger buffer, then cut to the test length
            var message = new MovementIntentMessage(32767, 0, 900, 1000u);
            var buffer = new byte[MovementIntentMessage.WireSize + 1];
            MovementIntentMessageCodec.Write(buffer, 7u, 1000u, 501u, in message);

            // Act
            bool read = MovementIntentMessageCodec.TryRead(
                new ReadOnlySpan<byte>(buffer, 0, length), out _, out _);

            // Assert
            Assert.IsFalse(read);
        }

        // ── Exact length ───────────────────────────────────────────────────────────────────

        [TestCase(0, false)]
        [TestCase(9, false)]
        [TestCase(10, true)]
        [TestCase(11, false)]
        public void test_movement_intent_try_read_body_requires_exact_length(int length, bool expected)
        {
            // Arrange
            var body = new byte[length];

            // Act
            bool read = MovementIntentMessageCodec.TryReadBody(body, out _);

            // Assert
            Assert.AreEqual(expected, read);
        }

        // ── Direction ──────────────────────────────────────────────────────────────────────

        [TestCase(1f, 0f, 32767, 0)]
        [TestCase(-1f, 0f, -32767, 0)]
        [TestCase(0f, 0f, 0, 0)]
        [TestCase(1.00001f, 0f, 32767, 0)]
        [TestCase(-1.00001f, 0f, -32767, 0)]
        [TestCase(0.6f, -0.8f, 19660, -26214)]
        // Over-unit single-axis inputs clamp to a unit vector and are not flagged (clamp before the length check).
        [TestCase(2f, 0f, 32767, 0)]
        [TestCase(1e30f, 0f, 32767, 0)]
        // Tolerance boundary: an encoded length 100 below 32,767 is still accepted.
        [TestCase(32667f / 32767f, 0f, 32667, 0)]
        public void test_movement_intent_encode_direction_unit_inputs_are_not_flagged(float x, float z, int expectedX, int expectedZ)
        {
            // Act
            (short ex, short ez) = MovementIntentMessageCodec.EncodeDirection(new Vector2(x, z), out bool wasNonUnit);

            // Assert
            Assert.AreEqual(expectedX, ex);
            Assert.AreEqual(expectedZ, ez);
            Assert.IsFalse(wasNonUnit);
        }

        [TestCase(0.5f, 0f)]
        [TestCase(0f, 0.5f)]
        [TestCase(0.9f, 0.9f)]
        [TestCase(-1e30f, 1e30f)]
        // Tolerance boundary: an encoded length 101 below 32,767 is rejected.
        [TestCase(32666f / 32767f, 0f)]
        // Non-finite components (float-to-int conversion differs between x64 and ARM64).
        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.NaN)]
        [TestCase(float.NaN, 1f)]
        [TestCase(float.PositiveInfinity, 0f)]
        [TestCase(float.NegativeInfinity, 0f)]
        [TestCase(0f, float.NegativeInfinity)]
        public void test_movement_intent_encode_direction_non_unit_input_falls_back_and_flags(float x, float z)
        {
            // Act
            (short ex, short ez) = MovementIntentMessageCodec.EncodeDirection(new Vector2(x, z), out bool wasNonUnit);

            // Assert
            Assert.AreEqual(32767, ex);
            Assert.AreEqual(0, ez);
            Assert.IsTrue(wasNonUnit);
        }

        // ── Facing ─────────────────────────────────────────────────────────────────────────

        [TestCase(0f, 0)]
        [TestCase(359.94f, 3599)]
        [TestCase(359.96f, 0)]
        [TestCase(-0.1f, 3599)]
        [TestCase(123.4f, 1234)]
        [TestCase(360f, 0)]
        [TestCase(720.5f, 5)]
        // Non-finite input encodes to 0; huge finite input is clamped to ±1e6° (1e7 % 3600 = 2800).
        [TestCase(float.NaN, 0)]
        [TestCase(float.PositiveInfinity, 0)]
        [TestCase(float.NegativeInfinity, 0)]
        [TestCase(1e10f, 2800)]
        [TestCase(-1e10f, 800)]
        public void test_movement_intent_encode_facing_wraps_into_valid_range(float degrees, int expected)
        {
            // Act
            short encoded = MovementIntentMessageCodec.EncodeFacing(degrees);

            // Assert
            Assert.AreEqual(expected, encoded);
        }

        [Test]
        public void test_movement_intent_decode_facing_divides_by_ten()
        {
            // Arrange
            const short facing = 1234;

            // Act
            float degrees = MovementIntentMessageCodec.DecodeFacing(facing);

            // Assert
            Assert.AreEqual(123.4f, degrees, 1e-4f);
        }

        [TestCase(3600, 0)]
        [TestCase(3599, 3599)]
        [TestCase(0, 0)]
        [TestCase(-1, 3599)]
        [TestCase(7200, 0)]
        [TestCase(-32768, 3232)]
        [TestCase(32767, 367)]
        public void test_movement_intent_normalize_facing_wraps_every_short(int input, int expected)
        {
            // Act
            short normalized = MovementIntentMessageCodec.NormalizeFacing((short)input);

            // Assert
            Assert.AreEqual(expected, normalized);
        }

        [Test]
        public void test_movement_intent_try_read_body_does_not_range_check_facing()
        {
            // Arrange
            var message = new MovementIntentMessage(0, 0, -5, 1u);
            var body = new byte[MovementIntentMessage.BodySize];
            MovementIntentMessageCodec.WriteBody(body, in message);

            // Act
            bool read = MovementIntentMessageCodec.TryReadBody(body, out MovementIntentMessage result);

            // Assert
            Assert.IsTrue(read);
            Assert.AreEqual(-5, result.FacingAngle);
        }

        // ── Routing ────────────────────────────────────────────────────────────────────────

        [Test]
        public void test_movement_intent_registry_has_exactly_one_unreliable_client_to_server_row()
        {
            // Act
            int matches = 0;
            foreach (MessageRoutingEntry entry in MessageRoutingRegistry.AllEntries)
            {
                if (entry.MessageTypeId == MovementIntentMessage.MessageTypeId)
                {
                    matches++;
                }
            }
            bool found = MessageRoutingRegistry.TryGetEntry(MovementIntentMessage.MessageTypeId, out MessageRoutingEntry row);

            // Assert
            Assert.AreEqual(1, matches);
            Assert.IsTrue(found);
            Assert.AreEqual(NetworkChannel.Unreliable, row.Channel);
            Assert.AreEqual(MessageDirection.ClientToServer, row.Direction);
            Assert.AreEqual(MessageDeliveryContext.Standalone, row.DeliveryContext);
            Assert.AreEqual(nameof(MovementIntentMessage), row.MessageName);
            StringAssert.Contains("self-corrects next tick", row.Rationale);
            StringAssert.Contains("MCR-2", row.Rationale);
        }

        // ── Allocation ─────────────────────────────────────────────────────────────────────

        [Test]
        public void test_movement_intent_write_and_read_do_not_allocate()
        {
            // Arrange
            var body = new byte[MovementIntentMessage.BodySize];
            var buffer = new byte[MovementIntentMessage.WireSize];
            var message = new MovementIntentMessage(19660, -26214, 1234, 1000u);
            var direction = new Vector2(0.6f, -0.8f);

            // Pre-warm so JIT/first-call costs are not counted.
            MovementIntentMessageCodec.Write(buffer, 7u, 1000u, 501u, in message);
            MovementIntentMessageCodec.TryRead(buffer, out _, out _);
            MovementIntentMessageCodec.WriteBody(body, in message);
            MovementIntentMessageCodec.TryReadBody(body, out _);
            MovementIntentMessageCodec.EncodeDirection(direction, out _);
            MovementIntentMessageCodec.EncodeFacing(123.4f);

            // Act & Assert
            Assert.That(() =>
            {
                MovementIntentMessageCodec.Write(buffer, 7u, 1000u, 501u, in message);
                MovementIntentMessageCodec.TryRead(buffer, out _, out _);
                MovementIntentMessageCodec.WriteBody(body, in message);
                MovementIntentMessageCodec.TryReadBody(body, out MovementIntentMessage read);
                MovementIntentMessageCodec.EncodeDirection(direction, out _);
                MovementIntentMessageCodec.DecodeDirection(read.DirX, read.DirZ);
                MovementIntentMessageCodec.EncodeFacing(123.4f);
                MovementIntentMessageCodec.NormalizeFacing(read.FacingAngle);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}

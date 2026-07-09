using System;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 003 — the CR-NET-7.1 message envelope
    /// (<see cref="ServerMessageEnvelope"/>, <see cref="ClientEntityMessageEnvelope"/>,
    /// <see cref="MessageEnvelopeCodec"/>) and the CR-NET-7.2 fixed-point primitive encoders
    /// (<see cref="WireFixedPointCodec"/>). Covers AC-WP-1 (envelope byte layout), AC-NC-28
    /// (fixed-point round-trip accuracy and guards), and AC-NC-03 (bit-for-bit int passthrough).
    /// </summary>
    [TestFixture]
    internal sealed class WireProtocol_Envelope_Serialization_Tests
    {
        // -----------------------------------------------------------------------
        // AC-WP-1: envelope byte length + documented offsets, and round-trip.
        // -----------------------------------------------------------------------

        [Test]
        public void ServerMessageEnvelope_Write_ProducesExactly10BytesAtDocumentedOffsets()
        {
            // Arrange
            var envelope = new ServerMessageEnvelope(messageTypeId: 0x0201, sequenceNumber: 42u, serverTickNumber: 100000u);
            Span<byte> buffer = new byte[ServerMessageEnvelope.WireSize];

            // Act
            MessageEnvelopeCodec.Write(buffer, in envelope);

            // Assert — exact wire size
            Assert.AreEqual(10, ServerMessageEnvelope.WireSize, "Server envelope wire size must be exactly 10 bytes per CR-NET-7.1.");

            // Assert — documented offsets, read independently of MessageEnvelopeCodec.TryRead
            Assert.AreEqual(0x0201, BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0, 2)), "MessageTypeID must be at offset 0 (2 bytes).");
            Assert.AreEqual(42u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(2, 4)), "SequenceNumber must be at offset 2 (4 bytes).");
            Assert.AreEqual(100000u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(6, 4)), "ServerTickNumber must be at offset 6 (4 bytes).");
        }

        [Test]
        public void ClientEntityMessageEnvelope_Write_ProducesExactly14BytesAtDocumentedOffsets()
        {
            // Arrange
            var envelope = new ClientEntityMessageEnvelope(messageTypeId: 0xE010, sequenceNumber: 7u, serverTickNumber: 1000u, senderEntityId: 501u);
            Span<byte> buffer = new byte[ClientEntityMessageEnvelope.WireSize];

            // Act
            MessageEnvelopeCodec.Write(buffer, in envelope);

            // Assert — exact wire size
            Assert.AreEqual(14, ClientEntityMessageEnvelope.WireSize, "Client entity-referencing envelope wire size must be exactly 14 bytes per CR-NET-7.1.");

            // Assert — documented offsets, read independently of MessageEnvelopeCodec.TryRead
            Assert.AreEqual(0xE010, BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0, 2)), "MessageTypeID must be at offset 0 (2 bytes).");
            Assert.AreEqual(7u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(2, 4)), "SequenceNumber must be at offset 2 (4 bytes).");
            Assert.AreEqual(1000u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(6, 4)), "ServerTickNumber must be at offset 6 (4 bytes).");
            Assert.AreEqual(501u, BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(10, 4)), "SenderEntityID must be at offset 10 (4 bytes).");
        }

        [Test]
        public void ServerMessageEnvelope_WriteThenTryRead_RoundTrips()
        {
            // Arrange
            var envelope = new ServerMessageEnvelope(0xABCD, 4000000000u, 123456u);
            Span<byte> buffer = new byte[ServerMessageEnvelope.WireSize];
            MessageEnvelopeCodec.Write(buffer, in envelope);

            // Act
            bool ok = MessageEnvelopeCodec.TryRead(buffer, out ServerMessageEnvelope decoded);

            // Assert
            Assert.IsTrue(ok, "TryRead must succeed on a correctly-sized buffer.");
            Assert.AreEqual(envelope, decoded, "Decoded envelope must equal the original.");
        }

        [Test]
        public void ClientEntityMessageEnvelope_WriteThenTryRead_RoundTrips()
        {
            // Arrange
            var envelope = new ClientEntityMessageEnvelope(0x0001, 1u, 999999u, 4000000000u);
            Span<byte> buffer = new byte[ClientEntityMessageEnvelope.WireSize];
            MessageEnvelopeCodec.Write(buffer, in envelope);

            // Act
            bool ok = MessageEnvelopeCodec.TryRead(buffer, out ClientEntityMessageEnvelope decoded);

            // Assert
            Assert.IsTrue(ok, "TryRead must succeed on a correctly-sized buffer.");
            Assert.AreEqual(envelope, decoded, "Decoded envelope must equal the original.");
        }

        [Test]
        public void MessageEnvelopeCodec_TryReadServerEnvelope_TooShortSource_ReturnsFalse()
        {
            // Arrange — one byte short of the 10-byte wire size
            ReadOnlySpan<byte> tooShort = new byte[9];

            // Act
            bool ok = MessageEnvelopeCodec.TryRead(tooShort, out ServerMessageEnvelope decoded);

            // Assert
            Assert.IsFalse(ok, "TryRead must return false for a source shorter than ServerMessageEnvelope.WireSize.");
            Assert.AreEqual(default(ServerMessageEnvelope), decoded, "Out envelope must be default when decoding fails.");
        }

        [Test]
        public void MessageEnvelopeCodec_TryReadClientEntityEnvelope_TooShortSource_ReturnsFalse()
        {
            // Arrange — one byte short of the 14-byte wire size
            ReadOnlySpan<byte> tooShort = new byte[13];

            // Act
            bool ok = MessageEnvelopeCodec.TryRead(tooShort, out ClientEntityMessageEnvelope decoded);

            // Assert
            Assert.IsFalse(ok, "TryRead must return false for a source shorter than ClientEntityMessageEnvelope.WireSize.");
            Assert.AreEqual(default(ClientEntityMessageEnvelope), decoded, "Out envelope must be default when decoding fails.");
        }

        // -----------------------------------------------------------------------
        // AC-NC-28: fixed-point round-trip accuracy for the three worked values,
        // plus the position boundary case and the degenerate-quaternion guard.
        // -----------------------------------------------------------------------

        [Test]
        public void EncodePosition_WorkedValue_RoundTripsWithinAxisTolerance()
        {
            // Arrange
            var original = new Vector3(12.75f, 0.00f, -85.23f);
            Span<byte> buffer = new byte[WireFixedPointCodec.PositionWireSize];

            // Act
            WireFixedPointCodec.EncodePosition(buffer, original);
            Vector3 decoded = WireFixedPointCodec.DecodePosition(buffer);

            // Assert — within ±0.01m per axis (AC-NC-28)
            Assert.AreEqual(original.x, decoded.x, 0.01f, "posX must round-trip within ±0.01m.");
            Assert.AreEqual(original.y, decoded.y, 0.01f, "posY must round-trip within ±0.01m.");
            Assert.AreEqual(original.z, decoded.z, 0.01f, "posZ must round-trip within ±0.01m.");
        }

        [Test]
        public void EncodePosition_BoundaryValue_DoesNotOverflowOrThrow()
        {
            // Arrange
            var positiveBoundary = new Vector3(327.67f, 0f, 0f);
            var negativeBoundary = new Vector3(-327.67f, 0f, 0f);
            Span<byte> buffer = new byte[WireFixedPointCodec.PositionWireSize];

            // Act & Assert — positive boundary: 327.67 x 100 = 32767 = short.MaxValue exactly
            Assert.DoesNotThrow(() => WireFixedPointCodec.EncodePosition(buffer, positiveBoundary),
                "Encoding the exact boundary value must not throw or overflow.");
            Vector3 decodedPositive = WireFixedPointCodec.DecodePosition(buffer);
            Assert.AreEqual(327.67f, decodedPositive.x, 0.01f);
            Assert.AreEqual(0f, decodedPositive.y, 0.01f);
            Assert.AreEqual(0f, decodedPositive.z, 0.01f);

            // Act & Assert — negative boundary
            Assert.DoesNotThrow(() => WireFixedPointCodec.EncodePosition(buffer, negativeBoundary),
                "Encoding the exact negative boundary value must not throw or overflow.");
            Vector3 decodedNegative = WireFixedPointCodec.DecodePosition(buffer);
            Assert.AreEqual(-327.67f, decodedNegative.x, 0.01f);
        }

        [Test]
        public void EncodePosition_OutOfRangeAxes_ClampsAndLogsAggregatedAnomaly()
        {
            // Arrange — X and Z exceed ±327.67m; Y is within range.
            // Expect — a single aggregated warning naming the clamped axes (not one log per axis).
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodePosition.*X.*Z"));
            var outOfRange = new Vector3(400f, 0f, -400f);
            Span<byte> buffer = new byte[WireFixedPointCodec.PositionWireSize];

            // Act
            WireFixedPointCodec.EncodePosition(buffer, outOfRange);
            Vector3 decoded = WireFixedPointCodec.DecodePosition(buffer);

            // Assert — clamped to the boundary before encoding
            Assert.AreEqual(327.67f, decoded.x, 0.01f, "X must clamp to +327.67m.");
            Assert.AreEqual(0f, decoded.y, 0.01f, "Y was within range and must be unaffected.");
            Assert.AreEqual(-327.67f, decoded.z, 0.01f, "Z must clamp to -327.67m.");
        }

        [Test]
        public void EncodeRotation_WorkedValue_RoundTripsWithinDotProductTolerance()
        {
            // Arrange
            var raw = new Quaternion(0.707f, 0.0f, 0.707f, 0.0f);
            Quaternion normalizedOriginal = raw.normalized;
            Span<byte> buffer = new byte[WireFixedPointCodec.RotationWireSize];

            // Act
            WireFixedPointCodec.EncodeRotation(buffer, raw);
            Quaternion decoded = WireFixedPointCodec.DecodeRotation(buffer);

            // Assert — dot product with the original (normalized) input >= 0.9999997 (AC-NC-28)
            float dot = Mathf.Abs(Quaternion.Dot(normalizedOriginal, decoded));
            Assert.GreaterOrEqual(dot, 0.9999997f, $"Dot product between decoded and original normalized quaternion must be >= 0.9999997 (was {dot}).");
        }

        [Test]
        public void EncodeRotation_DegenerateZeroQuaternion_SubstitutesIdentityAndLogsAnomaly()
        {
            // Arrange — expect the anomaly declared before the call that logs it.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodeRotation.*identity"));
            var degenerate = new Quaternion(0f, 0f, 0f, 0f);
            Span<byte> buffer = new byte[WireFixedPointCodec.RotationWireSize];

            // Act
            WireFixedPointCodec.EncodeRotation(buffer, degenerate);
            Quaternion decoded = WireFixedPointCodec.DecodeRotation(buffer);

            // Assert — decodes to identity (0,0,0,1)
            Assert.AreEqual(0f, decoded.x, 0.0001f);
            Assert.AreEqual(0f, decoded.y, 0.0001f);
            Assert.AreEqual(0f, decoded.z, 0.0001f);
            Assert.AreEqual(1f, decoded.w, 0.0001f);
        }

        [Test]
        public void EncodeDirection_WorkedValue_RoundTripsWithinTolerance()
        {
            // Arrange — a non-zero, non-axis-aligned direction (normalized internally by the encoder).
            var original = new Vector3(3f, 4f, 0f); // magnitude 5 — normalizes to (0.6, 0.8, 0.0)
            Span<byte> buffer = new byte[WireFixedPointCodec.DirectionWireSize];

            // Act
            WireFixedPointCodec.EncodeDirection(buffer, original);
            Vector3 decoded = WireFixedPointCodec.DecodeDirection(buffer);

            // Assert — round-trips within the ×32,767 fixed-point resolution
            Assert.AreEqual(0.6f, decoded.x, 0.001f, "dirX must round-trip within fixed-point resolution.");
            Assert.AreEqual(0.8f, decoded.y, 0.001f, "dirY must round-trip within fixed-point resolution.");
            Assert.AreEqual(0f, decoded.z, 0.001f, "dirZ must round-trip within fixed-point resolution.");
        }

        [Test]
        public void EncodeDirection_DegenerateZeroVector_SubstitutesUnitXAndLogsAnomaly()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodeDirection.*\(1,0,0\)"));
            Span<byte> buffer = new byte[WireFixedPointCodec.DirectionWireSize];

            // Act
            WireFixedPointCodec.EncodeDirection(buffer, Vector3.zero);
            Vector3 decoded = WireFixedPointCodec.DecodeDirection(buffer);

            // Assert
            Assert.AreEqual(1f, decoded.x, 0.0001f);
            Assert.AreEqual(0f, decoded.y, 0.0001f);
            Assert.AreEqual(0f, decoded.z, 0.0001f);
        }

        [Test]
        public void EncodeCycleTimer_WorkedValue_RoundTripsWithinFractionTolerance()
        {
            // Arrange
            const float cycleDuration = 5.0f;
            float cycleTimer = 0.37f * cycleDuration;
            Span<byte> buffer = new byte[WireFixedPointCodec.CycleTimerWireSize];

            // Act
            WireFixedPointCodec.EncodeCycleTimer(buffer, cycleTimer, cycleDuration);
            float fraction = WireFixedPointCodec.DecodeCycleTimerFraction(buffer);

            // Assert — decoded fraction is 0.37 ± 0.0001 (AC-NC-28)
            Assert.AreEqual(0.37f, fraction, 0.0001f);
        }

        [Test]
        public void EncodeCycleTimer_NonPositiveCycleDuration_LogsAnomalyAndEncodesZero()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodeCycleTimer.*cycleDuration"));
            Span<byte> buffer = new byte[WireFixedPointCodec.CycleTimerWireSize];

            // Act
            WireFixedPointCodec.EncodeCycleTimer(buffer, cycleTimer: 1.0f, cycleDuration: 0f);
            float fraction = WireFixedPointCodec.DecodeCycleTimerFraction(buffer);

            // Assert — safe fallback of 0
            Assert.AreEqual(0f, fraction, 0.0001f);
        }

        [Test]
        public void EncodeCycleTimer_ValueAboveCycleDuration_ClampsToMaxAndLogsAnomaly()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireFixedPointCodec\] EncodeCycleTimer.*clamped"));
            Span<byte> buffer = new byte[WireFixedPointCodec.CycleTimerWireSize];

            // Act
            WireFixedPointCodec.EncodeCycleTimer(buffer, cycleTimer: 6.0f, cycleDuration: 5.0f);
            float fraction = WireFixedPointCodec.DecodeCycleTimerFraction(buffer);

            // Assert — clamped to CycleDuration, encoded value ends up 10,000 (fraction 1.0)
            Assert.AreEqual(1.0f, fraction, 0.0001f);
        }

        // -----------------------------------------------------------------------
        // Supplementary coverage (not gated by a named AC): critChance and
        // attackSpeedMultiplier are part of this story's CR-NET-7.2 encoder set
        // and are exercised here for basic round-trip sanity.
        // -----------------------------------------------------------------------

        [Test]
        public void EncodeCritChance_RoundTripsWithinScaleResolution()
        {
            // Arrange — 5% crit chance, within the documented 0.0-0.75 domain range.
            const float critChance = 0.05f;
            Span<byte> buffer = new byte[WireFixedPointCodec.CritChanceWireSize];

            // Act
            WireFixedPointCodec.EncodeCritChance(buffer, critChance);
            float decoded = WireFixedPointCodec.DecodeCritChance(buffer);

            // Assert — resolution is 1/10,000 = 0.0001
            Assert.AreEqual(critChance, decoded, 0.0001f);
        }

        [Test]
        public void EncodeAttackSpeedMultiplier_RoundTripsWithinScaleResolution()
        {
            // Arrange — within the documented 0.5-2.0 domain range.
            const float attackSpeedMultiplier = 1.25f;
            Span<byte> buffer = new byte[WireFixedPointCodec.AttackSpeedMultiplierWireSize];

            // Act
            WireFixedPointCodec.EncodeAttackSpeedMultiplier(buffer, attackSpeedMultiplier);
            float decoded = WireFixedPointCodec.DecodeAttackSpeedMultiplier(buffer);

            // Assert — resolution is 1/1,000 = 0.001
            Assert.AreEqual(attackSpeedMultiplier, decoded, 0.001f);
        }

        // -----------------------------------------------------------------------
        // AC-NC-03: server-computed finalDamage (int) round-trips bit-for-bit
        // identical through the envelope+body serialize/deserialize path.
        // -----------------------------------------------------------------------

        [Test]
        public void WriteInt32ReadInt32_FinalDamage_RoundTripsBitForBitIdentical()
        {
            // Arrange
            int[] values = { 0, 1, -1, 42, -42, int.MaxValue, int.MinValue };
            Span<byte> buffer = new byte[WireFixedPointCodec.Int32WireSize];

            foreach (int value in values)
            {
                // Act
                WireFixedPointCodec.WriteInt32(buffer, value);
                int decoded = WireFixedPointCodec.ReadInt32(buffer);

                // Assert
                Assert.AreEqual(value, decoded, $"finalDamage value {value} must round-trip bit-for-bit identical.");
            }
        }

        [Test]
        public void WriteUInt32ReadUInt32_RoundTripsBitForBitIdentical()
        {
            // Arrange
            uint[] values = { 0u, 1u, 2147483648u, 4294967295u };
            Span<byte> buffer = new byte[WireFixedPointCodec.UInt32WireSize];

            foreach (uint value in values)
            {
                // Act
                WireFixedPointCodec.WriteUInt32(buffer, value);
                uint decoded = WireFixedPointCodec.ReadUInt32(buffer);

                // Assert
                Assert.AreEqual(value, decoded, $"Value {value} must round-trip bit-for-bit identical.");
            }
        }

        [Test]
        public void EnvelopeAndFinalDamageBody_SerializeThenDeserialize_DamageValueMatchesServerComputedValue()
        {
            // Arrange — a server-computed DamageEvent-like wire payload: envelope (10B) + finalDamage (4B).
            // Proves AC-NC-03 at the serialization/deserialization boundary: the value "client B" decodes
            // matches exactly what the server computed and wrote — no gameplay logic involved.
            const int serverComputedFinalDamage = 287;
            var envelope = new ServerMessageEnvelope(messageTypeId: 0x0301, sequenceNumber: 7u, serverTickNumber: 500u);

            Span<byte> wireBuffer = new byte[ServerMessageEnvelope.WireSize + WireFixedPointCodec.Int32WireSize];
            MessageEnvelopeCodec.Write(wireBuffer, in envelope);
            WireFixedPointCodec.WriteInt32(wireBuffer.Slice(ServerMessageEnvelope.WireSize, WireFixedPointCodec.Int32WireSize), serverComputedFinalDamage);

            // Act — "client B" decodes the exact same bytes the server produced.
            bool envelopeOk = MessageEnvelopeCodec.TryRead(wireBuffer, out ServerMessageEnvelope decodedEnvelope);
            int decodedFinalDamage = WireFixedPointCodec.ReadInt32(wireBuffer.Slice(ServerMessageEnvelope.WireSize, WireFixedPointCodec.Int32WireSize));

            // Assert
            Assert.IsTrue(envelopeOk);
            Assert.AreEqual(envelope, decodedEnvelope);
            Assert.AreEqual(serverComputedFinalDamage, decodedFinalDamage,
                "Client B's decoded finalDamage must be bit-for-bit identical to the server-computed value (AC-NC-03).");
        }
    }
}

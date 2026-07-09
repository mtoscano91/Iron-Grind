using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for Networking Core Story 004 — the CR-NET-7.3 ID zero-write guard
    /// (<see cref="WireIdCodec"/>) and the CR-NET-7.4/7.9 enum range-check guards
    /// (<see cref="WireEnumCodec"/>). Covers AC-NC-31 (ID zero-guard), AC-NC-32 (enum
    /// substitute-and-continue), AC-NC-18 (StatID reject-and-drop), and AC-NC-17 (nonzero-ID
    /// volume invariant).
    /// </summary>
    /// <remarks>
    /// Buffers referenced inside an <c>Assert.Throws</c>/<c>Assert.DoesNotThrow</c> lambda are
    /// declared as <see cref="byte"/><c>[]</c>, not <c>Span&lt;byte&gt;</c> — a <c>Span&lt;byte&gt;</c>
    /// local (a ref struct) cannot be captured by a lambda closure (CS8175-class compile error).
    /// The implicit <c>byte[]</c> → <c>Span&lt;byte&gt;</c> conversion at the call site inside the
    /// lambda body is legal because the resulting <c>Span&lt;byte&gt;</c> is never itself captured
    /// or stored — only the backing array (an ordinary reference type) is.
    /// </remarks>
    [TestFixture]
    internal sealed class WireProtocol_EntityIdEnumGuards_Tests
    {
        private const ushort SampleMessageTypeId = 0x0301;

        // -----------------------------------------------------------------------
        // AC-NC-31: ID zero-write guard — EntityID, ItemID, CharacterID.
        // -----------------------------------------------------------------------

        [Test]
        public void SerializeEntityId_ZeroId_ThrowsLogsAnomalyAndLeavesBufferUntouched()
        {
            // Arrange — byte[], not Span<byte>, because it is referenced inside the Assert.Throws lambda.
            byte[] buffer = new byte[WireIdCodec.EntityIdWireSize];
            FillSentinel(buffer);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireIdCodec\] SerializeEntityId.*InvalidIdZeroWrite.*attackerEntityId"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                WireIdCodec.SerializeEntityId(buffer, EntityID.Invalid, SampleMessageTypeId, "attackerEntityId"),
                "Encoding a zero-valued EntityID must throw before writing any byte.");

            // Assert — buffer is provably untouched (still the sentinel pattern)
            AssertSentinelUnchanged(buffer);
        }

        [Test]
        public void SerializeEntityId_NonzeroId_EncodesAndDecodesCorrectly()
        {
            // Arrange — no lambda involved, Span<byte> is fine here.
            var original = new EntityID(501u);
            Span<byte> buffer = new byte[WireIdCodec.EntityIdWireSize];

            // Act
            WireIdCodec.SerializeEntityId(buffer, original, SampleMessageTypeId, "attackerEntityId");
            EntityID decoded = WireIdCodec.DeserializeEntityId(buffer);

            // Assert
            Assert.AreEqual(501u, BinaryPrimitives.ReadUInt32LittleEndian(buffer), "Wire bytes must be the 4-byte little-endian raw value.");
            Assert.AreEqual(original, decoded, "Decoded EntityID must equal the original.");
        }

        [Test]
        public void SerializeItemId_ZeroId_ThrowsLogsAnomalyAndLeavesBufferUntouched()
        {
            // Arrange
            byte[] buffer = new byte[WireIdCodec.ItemIdWireSize];
            FillSentinel(buffer);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireIdCodec\] SerializeItemId.*InvalidIdZeroWrite.*itemId"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                WireIdCodec.SerializeItemId(buffer, ItemID.Invalid, SampleMessageTypeId, "itemId"),
                "Encoding a zero-valued ItemID must throw before writing any byte.");

            // Assert
            AssertSentinelUnchanged(buffer);
        }

        [Test]
        public void SerializeItemId_NonzeroId_EncodesAndDecodesCorrectly()
        {
            // Arrange
            var original = new ItemID(4200u);
            Span<byte> buffer = new byte[WireIdCodec.ItemIdWireSize];

            // Act
            WireIdCodec.SerializeItemId(buffer, original, SampleMessageTypeId, "itemId");
            ItemID decoded = WireIdCodec.DeserializeItemId(buffer);

            // Assert
            Assert.AreEqual(4200u, BinaryPrimitives.ReadUInt32LittleEndian(buffer), "Wire bytes must be the 4-byte little-endian raw value.");
            Assert.AreEqual(original, decoded, "Decoded ItemID must equal the original.");
        }

        [Test]
        public void SerializeCharacterId_ZeroId_ThrowsLogsAnomalyAndLeavesBufferUntouched()
        {
            // Arrange
            byte[] buffer = new byte[WireIdCodec.CharacterIdWireSize];
            FillSentinel(buffer);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireIdCodec\] SerializeCharacterId.*InvalidIdZeroWrite.*characterId"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                WireIdCodec.SerializeCharacterId(buffer, CharacterID.Invalid, SampleMessageTypeId, "characterId"),
                "Encoding a zero-valued CharacterID must throw before writing any byte.");

            // Assert
            AssertSentinelUnchanged(buffer);
        }

        [Test]
        public void SerializeCharacterId_NonzeroId_EncodesAndDecodesCorrectly()
        {
            // Arrange
            var original = new CharacterID(77u);
            Span<byte> buffer = new byte[WireIdCodec.CharacterIdWireSize];

            // Act
            WireIdCodec.SerializeCharacterId(buffer, original, SampleMessageTypeId, "characterId");
            CharacterID decoded = WireIdCodec.DeserializeCharacterId(buffer);

            // Assert
            Assert.AreEqual(77u, BinaryPrimitives.ReadUInt32LittleEndian(buffer), "Wire bytes must be the 4-byte little-endian raw value.");
            Assert.AreEqual(original, decoded, "Decoded CharacterID must equal the original.");
        }

        // -----------------------------------------------------------------------
        // AC-NC-32: enum substitute-and-continue guards — DamageType, DisconnectType,
        // DisconnectReason (non-contiguous valid set).
        // -----------------------------------------------------------------------

        [Test]
        public void DecodeDamageType_OutOfRangeByte_SubstitutesPhysicalAndLogsAnomaly()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDamageType.*100"));

            // Act
            DamageType result = WireEnumCodec.DecodeDamageType(100, SampleMessageTypeId);

            // Assert — substituted, not dropped: the function returns a usable value.
            Assert.AreEqual(DamageType.Physical, result, "Out-of-range DamageType byte must substitute Physical (0).");
        }

        [Test]
        public void DecodeDamageType_ValidByte_ReturnsCorrectValueWithoutLogging()
        {
            // Act
            DamageType result = WireEnumCodec.DecodeDamageType((byte)DamageType.Magical, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DamageType.Magical, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DecodeDisconnectType_OutOfRangeByte_SubstitutesTimeoutAndLogsAnomaly()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectType.*50"));

            // Act
            DisconnectType result = WireEnumCodec.DecodeDisconnectType(50, SampleMessageTypeId);

            // Assert — substituted; per AC-NC-32 the entity is still despawned (guard proven here
            // returns the correct fallback — the despawn logic itself does not exist yet).
            Assert.AreEqual(DisconnectType.Timeout, result, "Out-of-range DisconnectType byte must substitute Timeout (1).");
        }

        [Test]
        public void DecodeDisconnectType_ValidByte_ReturnsCorrectValueWithoutLogging()
        {
            // Act
            DisconnectType result = WireEnumCodec.DecodeDisconnectType((byte)DisconnectType.ZoneTransfer, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DisconnectType.ZoneTransfer, result);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DecodeDamageType_AdjacentOutOfRangeByte3_SubstitutesPhysicalAndLogsAnomaly()
        {
            // Arrange — 3 is one past DamageType's max declared value (True = 2); proves the
            // boundary check is exact, not off-by-one.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDamageType.*3"));

            // Act
            DamageType result = WireEnumCodec.DecodeDamageType(3, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DamageType.Physical, result, "Byte 3 is one past DamageType's max (2) and must substitute Physical.");
        }

        [Test]
        public void DecodeDisconnectType_AdjacentOutOfRangeByte3_SubstitutesTimeoutAndLogsAnomaly()
        {
            // Arrange — 3 is one past DisconnectType's max declared value (ZoneTransfer = 2).
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectType.*3"));

            // Act
            DisconnectType result = WireEnumCodec.DecodeDisconnectType(3, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DisconnectType.Timeout, result, "Byte 3 is one past DisconnectType's max (2) and must substitute Timeout.");
        }

        [Test]
        public void DecodeDisconnectReason_AdjacentGapByte4_SubstitutesOtherAndLogsAnomaly()
        {
            // Arrange — 4 is immediately adjacent to the declared member GhostDeath (3); proves
            // the switch doesn't accidentally accept a neighbor of a valid value.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectReason.*4"));

            // Act
            DisconnectReason result = WireEnumCodec.DecodeDisconnectReason(4, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DisconnectReason.Other, result, "Byte 4 (adjacent to GhostDeath=3) is not a declared member and must substitute Other.");
        }

        [Test]
        public void DecodeDisconnectReason_AdjacentGapByte254_SubstitutesOtherAndLogsAnomaly()
        {
            // Arrange — 254 is immediately adjacent to the declared outlier Other (255); proves
            // the switch doesn't accidentally accept a neighbor of the outlier value.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectReason.*254"));

            // Act
            DisconnectReason result = WireEnumCodec.DecodeDisconnectReason(254, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DisconnectReason.Other, result, "Byte 254 (adjacent to Other=255) is not a declared member and must substitute Other.");
        }

        [Test]
        public void DecodeDisconnectReason_GapValue_SubstitutesOtherAndLogsAnomaly()
        {
            // Arrange — 100 falls in the gap between the declared set {0,1,2,3,255}.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectReason.*100"));

            // Act
            DisconnectReason result = WireEnumCodec.DecodeDisconnectReason(100, SampleMessageTypeId);

            // Assert
            Assert.AreEqual(DisconnectReason.Other, result, "A byte in the gap of the non-contiguous valid set must substitute Other (255).");
        }

        [Test]
        public void DecodeDisconnectReason_255_IsAcceptedAsValidOtherWithoutSubstitutionLog()
        {
            // Act — 255 is itself a declared member (Other), not an out-of-range outlier.
            DisconnectReason result = WireEnumCodec.DecodeDisconnectReason(255, SampleMessageTypeId);

            // Assert — accepted as valid; no anomaly logged for a legitimately-received Other.
            Assert.AreEqual(DisconnectReason.Other, result);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // AC-NC-18: StatID reject-and-drop guard — a different policy from AC-NC-32's
        // substitute-and-continue. Returns false, never a substituted value.
        // -----------------------------------------------------------------------

        [Test]
        public void TryValidateStatID_OutOfRangeByte0xFF_ReturnsFalseAndLogsAnomaly()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] TryValidateStatID.*255"));

            // Act
            bool isValid = WireEnumCodec.TryValidateStatID(0xFF, SampleMessageTypeId, out StatID validated);

            // Assert — rejected, not substituted: caller must drop the message, apply no stat change.
            Assert.IsFalse(isValid, "0xFF is outside StatID's declared range (0-16) and must be rejected, not substituted.");
        }

        [Test]
        public void TryValidateStatID_ValidMaxBoundaryByte16_ReturnsTrueWithMovementSpeed()
        {
            // Act — 16 (MovementSpeed) is the highest declared StatID member.
            bool isValid = WireEnumCodec.TryValidateStatID(16, SampleMessageTypeId, out StatID validated);

            // Assert
            Assert.IsTrue(isValid, "16 (MovementSpeed) is the max valid StatID member and must be accepted.");
            Assert.AreEqual(StatID.MovementSpeed, validated);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TryValidateStatID_AdjacentOutOfRangeByte17_ReturnsFalseAndLogsAnomaly()
        {
            // Arrange — one past the max declared value (16); proves the boundary check is exact.
            LogAssert.Expect(LogType.Warning, new Regex(@"\[WireEnumCodec\] TryValidateStatID.*17"));

            // Act
            bool isValid = WireEnumCodec.TryValidateStatID(17, SampleMessageTypeId, out StatID validated);

            // Assert
            Assert.IsFalse(isValid, "17 is one past the max declared StatID value (16) and must be rejected.");
        }

        // -----------------------------------------------------------------------
        // AC-NC-17: deterministic volume/invariant test — 500 iterations x 10 synthetic
        // entities (5,000 total), legitimate nonzero EntityIDs never corrupt to zero.
        // -----------------------------------------------------------------------

        [Test]
        public void SerializeEntityId_500IterationsTimes10Entities_NeverProducesAZeroValuedIdField()
        {
            // Arrange
            const int iterationCount = 500;
            const int entityCountPerIteration = 10;
            var capturedMessages = new List<byte[]>(iterationCount * entityCountPerIteration);

            // Act — deterministic fixture: entity IDs 1-10 cycling every iteration, all nonzero.
            for (int iteration = 0; iteration < iterationCount; iteration++)
            {
                for (int entityIndex = 0; entityIndex < entityCountPerIteration; entityIndex++)
                {
                    uint rawEntityId = (uint)(entityIndex + 1); // cycles 1..10 — always nonzero
                    var entityId = new EntityID(rawEntityId);

                    Span<byte> buffer = new byte[WireIdCodec.EntityIdWireSize];
                    WireIdCodec.SerializeEntityId(buffer, entityId, SampleMessageTypeId, "attackerEntityId");

                    capturedMessages.Add(buffer.ToArray());
                }
            }

            // Assert — at least 5,000 captured messages, and none decodes to a zero-valued ID.
            Assert.GreaterOrEqual(capturedMessages.Count, 5000, "Fixture must capture at least 5,000 messages (500 x 10).");
            foreach (byte[] captured in capturedMessages)
            {
                EntityID decoded = WireIdCodec.DeserializeEntityId(captured);
                Assert.AreNotEqual(EntityID.Invalid, decoded, "No captured EntityID field may be zero-valued.");
            }
        }

        // -----------------------------------------------------------------------
        // Test helpers
        // -----------------------------------------------------------------------

        private static void FillSentinel(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0xAA;
            }
        }

        private static void AssertSentinelUnchanged(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.AreEqual(0xAA, buffer[i], $"Buffer byte at offset {i} must be untouched (still the 0xAA sentinel) after a thrown zero-guard.");
            }
        }
    }
}

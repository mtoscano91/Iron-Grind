using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IronGrind.EnhancementSystem;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// EditMode unit tests for the Enhancement and NPC interaction message codecs (Enhancement Story 010):
    /// AC-NC-40 (fixed-size codecs, without <c>InventoryFullSync</c>), AC-NC-41 (broadcast half), AC-NC-42
    /// (result-code half), the NPC interaction codecs, and the twelve routing rows.
    /// </summary>
    [TestFixture]
    internal sealed class WireProtocol_EnhancementMessages_Tests
    {
        private const int BufferSize = 64;

        // -----------------------------------------------------------------------
        // AC-NC-40: body length and round trip, one case per fixed-size message.
        // -----------------------------------------------------------------------

        [TestCase(1u, (byte)0, (byte)0)]
        [TestCase(uint.MaxValue, (byte)19, (byte)255)]
        [TestCase(7u, (byte)255, (byte)19)]
        public void EnhancementAttemptRequest_WriteThenRead_BodyIs6BytesAndRoundTrips(uint requestId, byte itemSlot, byte scrollSlot)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementAttemptRequestCodec.WriteBody(buffer, requestId, itemSlot, scrollSlot);
            bool ok = EnhancementAttemptRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out uint readRequestId, out byte readItem, out byte readScroll);

            // Assert
            Assert.AreEqual(6, written);
            Assert.AreEqual(6, EnhancementAttemptRequest.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(requestId, readRequestId);
            Assert.AreEqual(itemSlot, readItem);
            Assert.AreEqual(scrollSlot, readScroll);
        }

        [TestCase(1u, (byte)0)]
        [TestCase(uint.MaxValue, (byte)255)]
        [TestCase(7u, (byte)19)]
        public void EnhancementRequestReceived_WriteThenRead_BodyIs5BytesAndRoundTrips(uint requestId, byte itemSlot)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementRequestReceivedCodec.WriteBody(buffer, requestId, itemSlot);
            bool ok = EnhancementRequestReceivedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out uint readRequestId, out byte readItem);

            // Assert
            Assert.AreEqual(5, written);
            Assert.AreEqual(5, EnhancementRequestReceived.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(requestId, readRequestId);
            Assert.AreEqual(itemSlot, readItem);
        }

        [TestCase(1u, EnhancementResultCode.Success, (byte)9)]
        [TestCase(uint.MaxValue, EnhancementResultCode.Destruction, (byte)0)]
        [TestCase(7u, EnhancementResultCode.RejectedTierMismatch, (byte)0)]
        [TestCase(8u, EnhancementResultCode.RejectedNotUpgradeable, (byte)0)]
        public void EnhancementAttemptResultMessage_WriteThenRead_BodyIs6BytesAndRoundTrips(uint requestId, EnhancementResultCode code, byte newLevel)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementAttemptResultMessageCodec.WriteBody(buffer, requestId, code, newLevel);
            bool ok = EnhancementAttemptResultMessageCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out uint readRequestId, out EnhancementResultCode readCode, out byte readLevel, out bool isUnknown);

            // Assert
            Assert.AreEqual(6, written);
            Assert.AreEqual(6, EnhancementAttemptResultMessage.BodySize);
            Assert.IsTrue(ok);
            Assert.IsFalse(isUnknown);
            Assert.AreEqual(requestId, readRequestId);
            Assert.AreEqual(code, readCode);
            Assert.AreEqual(newLevel, readLevel);
            Assert.AreEqual((byte)code, buffer[4], "resultCode is one raw byte at offset 4; outcome is not serialized.");
        }

        [Test]
        public void EnhancementAttemptResultMessage_IsRejection_IsTrueOnlyForRejectedCodes()
        {
            Assert.IsFalse(EnhancementAttemptResultMessage.IsRejection(EnhancementResultCode.Success));
            Assert.IsFalse(EnhancementAttemptResultMessage.IsRejection(EnhancementResultCode.Destruction));
            Assert.IsTrue(EnhancementAttemptResultMessage.IsRejection(EnhancementResultCode.RejectedAtMaxLevel));
            Assert.IsTrue(EnhancementAttemptResultMessage.IsRejection(EnhancementResultCode.RejectedNotUpgradeable));
        }

        [Test]
        public void CancelEnhancement_ZeroBody_WritesNothingAndReadsTrue()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act & Assert
            Assert.AreEqual(0, CancelEnhancement.BodySize);
            Assert.AreEqual(0, CancelEnhancementCodec.WriteBody(buffer));
            Assert.IsTrue(CancelEnhancementCodec.TryReadBody(ReadOnlySpan<byte>.Empty));
        }

        [TestCase((byte)0, (byte)0)]
        [TestCase((byte)19, (byte)255)]
        [TestCase((byte)255, (byte)19)]
        public void EnhancementPreviewRequest_WriteThenRead_BodyIs2BytesAndRoundTrips(byte itemSlot, byte scrollSlot)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementPreviewRequestCodec.WriteBody(buffer, itemSlot, scrollSlot);
            bool ok = EnhancementPreviewRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out byte readItem, out byte readScroll);

            // Assert
            Assert.AreEqual(2, written);
            Assert.AreEqual(2, EnhancementPreviewRequest.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(itemSlot, readItem);
            Assert.AreEqual(scrollSlot, readScroll);
        }

        [TestCase((byte)0, (byte)0, (byte)0)]
        [TestCase((byte)19, (byte)255, (byte)8)]
        [TestCase((byte)255, (byte)19, (byte)4)]
        public void EnhancementStateUpdate_WriteThenRead_BodyIs7BytesAndRoundTrips(byte itemSlot, byte scrollSlot, byte level)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementStateUpdateCodec.WriteBody(buffer, itemSlot, scrollSlot, level, 0.65f, 0.35f);
            bool ok = EnhancementStateUpdateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out byte readItem, out byte readScroll, out byte readLevel, out float pSuccess, out float pDestruction);

            // Assert
            Assert.AreEqual(7, written);
            Assert.AreEqual(7, EnhancementStateUpdate.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(itemSlot, readItem);
            Assert.AreEqual(scrollSlot, readScroll);
            Assert.AreEqual(level, readLevel);
            Assert.AreEqual(0.65f, pSuccess, 1e-6f);
            Assert.AreEqual(0.35f, pDestruction, 1e-6f);
        }

        // Fixed-point rule (CR-NET-7.2): probability x 10,000 as a ushort.
        [TestCase(0.65f, 0.35f, 6500, 3500)]
        [TestCase(1.0f, 0f, 10000, 0)]
        [TestCase(0f, 1.0f, 0, 10000)]
        [TestCase(0.85f, 0.15f, 8500, 1500)]
        public void EnhancementStateUpdate_Probabilities_EncodeAsFixedPointTimes10000(float pSuccess, float pDestruction, int expectedSuccess, int expectedDestruction)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            EnhancementStateUpdateCodec.WriteBody(buffer, 0, 1, 4, pSuccess, pDestruction);

            // Assert
            Assert.AreEqual(expectedSuccess, (int)BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(3, 2)));
            Assert.AreEqual(expectedDestruction, (int)BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(5, 2)));
        }

        [TestCase((byte)0, (byte)0, EnhancementResultCode.RejectedAtMaxLevel)]
        [TestCase((byte)19, (byte)255, EnhancementResultCode.RejectedTierMismatch)]
        [TestCase((byte)255, (byte)19, EnhancementResultCode.RejectedNotUpgradeable)]
        public void EnhancementPreviewRejected_WriteThenRead_BodyIs3BytesAndRoundTrips(byte itemSlot, byte scrollSlot, EnhancementResultCode code)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EnhancementPreviewRejectedCodec.WriteBody(buffer, itemSlot, scrollSlot, code);
            bool ok = EnhancementPreviewRejectedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out byte readItem, out byte readScroll, out EnhancementResultCode readCode, out bool isUnknown);

            // Assert
            Assert.AreEqual(3, written);
            Assert.AreEqual(3, EnhancementPreviewRejected.BodySize);
            Assert.IsTrue(ok);
            Assert.IsFalse(isUnknown);
            Assert.AreEqual(itemSlot, readItem);
            Assert.AreEqual(scrollSlot, readScroll);
            Assert.AreEqual(code, readCode);
        }

        // -----------------------------------------------------------------------
        // AC-NC-41 (broadcast half): ServerBroadcast_Enhancement9 body lengths 4, 52, 52, 50.
        // -----------------------------------------------------------------------

        [Test]
        public void ServerBroadcastEnhancement9_TwoEmptyStrings_BodyIs4BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, string.Empty, string.Empty);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual(4, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, player);
            Assert.AreEqual(string.Empty, item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_Two24ByteAsciiStrings_BodyIs52BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            string playerName = new string('a', 24);
            string itemName = new string('b', 24);

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, playerName, itemName);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual(52, written);
            Assert.AreEqual(ServerBroadcast_Enhancement9.MaxBodySize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(playerName, player);
            Assert.AreEqual(itemName, item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_ItemNameOf30AsciiBytes_IsCutToFirst24Bytes()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            string playerName = new string('a', 24);
            string itemName = new string('b', 30);

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, playerName, itemName);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual(52, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(playerName, player);
            Assert.AreEqual(new string('b', 24), item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_ItemNameWithThreeByteCharsAcrossTheLimit_IsCutOnACharacterBoundary()
        {
            // Arrange — 22 ASCII bytes + two 3-byte characters (U+20AC): the second character would end at byte 28,
            // the first at 25 > 24, so the cut keeps exactly the 22 ASCII bytes.
            byte[] buffer = new byte[BufferSize];
            string playerName = new string('a', 24);
            string itemName = new string('c', 22) + "€€";

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, playerName, itemName);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual(50, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(playerName, player);
            Assert.AreEqual(new string('c', 22), item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_TryReadBody_ShortOrOversizedPrefix_ReturnsFalse()
        {
            // Arrange — a body shorter than the 4-byte minimum, and a length prefix of 25 (> 24).
            byte[] tooShort = new byte[3];
            byte[] oversizedPrefix = new byte[BufferSize];
            BinaryPrimitives.WriteUInt16LittleEndian(oversizedPrefix.AsSpan(0, 2), 25);

            // Act & Assert
            Assert.IsFalse(ServerBroadcast_Enhancement9Codec.TryReadBody(tooShort, out _, out _));
            Assert.IsFalse(ServerBroadcast_Enhancement9Codec.TryReadBody(oversizedPrefix, out _, out _));
        }

        [Test]
        public void ServerBroadcastEnhancement9_FourByteCharacterEndingExactlyAtTheLimit_IsKeptWhole()
        {
            // Arrange — 20 ASCII bytes + one surrogate pair (U+1F600, 4 bytes) = exactly 24 bytes.
            byte[] buffer = new byte[BufferSize];
            string itemName = new string('c', 20) + "\U0001F600";

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, "p", itemName);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual((2 + 1) + (2 + ServerBroadcast_Enhancement9.MaxStringBytes), written);
            Assert.IsTrue(ok);
            Assert.AreEqual("p", player);
            Assert.AreEqual(itemName, item);
            Assert.AreEqual(ServerBroadcast_Enhancement9.MaxStringBytes, Encoding.UTF8.GetByteCount(item));
        }

        [Test]
        public void ServerBroadcastEnhancement9_FourByteCharacterAcrossTheLimit_IsCutBeforeTheWholeCharacter()
        {
            // Arrange — 23 ASCII bytes + a 4-byte character would end at byte 27 > 24, so only the 23 ASCII bytes stay.
            byte[] buffer = new byte[BufferSize];
            string itemName = new string('c', 23) + "\U0001F600";

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, "p", itemName);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual((2 + 1) + (2 + 23), written);
            Assert.IsTrue(ok);
            Assert.AreEqual("p", player);
            Assert.AreEqual(new string('c', 23), item);
            foreach (char c in item)
            {
                Assert.IsFalse(char.IsSurrogate(c), "A surrogate pair must never be split.");
            }
        }

        [Test]
        public void ServerBroadcastEnhancement9_NullStrings_AreWrittenAsTwoEmptyStrings()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = ServerBroadcast_Enhancement9Codec.WriteBody(buffer, null, null);
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out string player, out string item);

            // Assert
            Assert.AreEqual(ServerBroadcast_Enhancement9.MinBodySize, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, player);
            Assert.AreEqual(string.Empty, item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_TryReadBody_FirstPrefixLargerThanRemainingBytes_ReturnsFalseWithEmptyStrings()
        {
            // Arrange — the first prefix claims 10 bytes but only 5 follow.
            byte[] body = { 10, 0, 1, 2, 3, 4, 5 };

            // Act
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(body, out string player, out string item);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(string.Empty, player);
            Assert.AreEqual(string.Empty, item);
        }

        [Test]
        public void ServerBroadcastEnhancement9_TryReadBody_SecondPrefixLargerThanRemainingBytes_ReturnsFalseWithEmptyStrings()
        {
            // Arrange — a valid first string "ab", then a second prefix of 20 (<= 24) with only 3 bytes following.
            byte[] body = { 2, 0, (byte)'a', (byte)'b', 20, 0, 1, 2, 3 };

            // Act
            bool ok = ServerBroadcast_Enhancement9Codec.TryReadBody(body, out string player, out string item);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(string.Empty, player);
            Assert.AreEqual(string.Empty, item);
        }

        // -----------------------------------------------------------------------
        // AC-NC-42 (result-code half): unknown resultCode 10 and 255.
        // -----------------------------------------------------------------------

        [TestCase((byte)10)]
        [TestCase((byte)255)]
        public void EnhancementAttemptResultMessage_UnknownResultCode_DecodesAsRejectionFlagsItAndLogsOneAnomaly(byte rawCode)
        {
            // Arrange
            byte[] body = { 1, 0, 0, 0, rawCode, 5 };
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex(@"\[EnhancementAttemptResultMessageCodec\].*out-of-range resultCode byte " + rawCode));

            // Act
            bool ok = EnhancementAttemptResultMessageCodec.TryReadBody(body,
                out uint requestId, out EnhancementResultCode code, out byte newLevel, out bool isUnknown);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsTrue(isUnknown);
            Assert.AreEqual(1u, requestId);
            Assert.IsTrue(EnhancementAttemptResultMessage.IsRejection(code), "An unknown code must decode as a rejection.");
            Assert.AreEqual(EnhancementAttemptResultMessage.UnknownResultCodeFallback, code);
            Assert.AreEqual(0, newLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EnhancementAttemptResultMessage_MaxKnownResultCode_DecodesAsKnownWithNoWarning()
        {
            // Arrange
            byte[] body = { 1, 0, 0, 0, EnhancementAttemptResultMessage.MaxKnownResultCode, 0 };

            // Act
            bool ok = EnhancementAttemptResultMessageCodec.TryReadBody(body,
                out uint _, out EnhancementResultCode code, out byte _, out bool isUnknown);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsFalse(isUnknown);
            Assert.AreEqual((EnhancementResultCode)EnhancementAttemptResultMessage.MaxKnownResultCode, code);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void EnhancementAttemptResultMessage_OneAboveMaxKnownResultCode_DecodesAsUnknownAndLogsOneAnomaly()
        {
            // Arrange
            byte rawCode = (byte)(EnhancementAttemptResultMessage.MaxKnownResultCode + 1);
            byte[] body = { 1, 0, 0, 0, rawCode, 0 };
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex(@"\[EnhancementAttemptResultMessageCodec\].*out-of-range resultCode byte " + rawCode));

            // Act
            bool ok = EnhancementAttemptResultMessageCodec.TryReadBody(body,
                out uint _, out EnhancementResultCode code, out byte _, out bool isUnknown);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsTrue(isUnknown);
            Assert.AreEqual(EnhancementAttemptResultMessage.UnknownResultCodeFallback, code);
            LogAssert.NoUnexpectedReceived();
        }

        // Bytes 0 (Success) and 1 (Destruction) can never be a preview rejection (EnhancementPreviewRejected.MinKnownResultCode).
        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)10)]
        [TestCase((byte)255)]
        public void EnhancementPreviewRejected_UnknownResultCode_DecodesAsRejectionFlagsItAndLogsOneAnomaly(byte rawCode)
        {
            // Arrange
            byte[] body = { 3, 4, rawCode };
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex(@"\[EnhancementPreviewRejectedCodec\].*out-of-range resultCode byte " + rawCode));

            // Act
            bool ok = EnhancementPreviewRejectedCodec.TryReadBody(body,
                out byte item, out byte scroll, out EnhancementResultCode code, out bool isUnknown);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsTrue(isUnknown);
            Assert.AreEqual(3, item);
            Assert.AreEqual(4, scroll);
            Assert.IsTrue(EnhancementAttemptResultMessage.IsRejection(code), "An unknown code must decode as a rejection.");
            Assert.AreEqual(EnhancementPreviewRejected.UnknownResultCodeFallback, code);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(EnhancementPreviewRejected.MinKnownResultCode)]
        [TestCase(EnhancementPreviewRejected.MaxKnownResultCode)]
        public void EnhancementPreviewRejected_KnownBoundaryResultCode_DecodesAsKnownWithNoWarning(byte rawCode)
        {
            // Arrange
            byte[] body = { 3, 4, rawCode };

            // Act
            bool ok = EnhancementPreviewRejectedCodec.TryReadBody(body,
                out byte _, out byte _, out EnhancementResultCode code, out bool isUnknown);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsFalse(isUnknown);
            Assert.AreEqual((EnhancementResultCode)rawCode, code);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // NPC interaction codecs.
        // -----------------------------------------------------------------------

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void OpenNPCInteraction_WriteThenRead_BodyIs4BytesAndRoundTrips(uint npcId)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = OpenNPCInteractionCodec.WriteBody(buffer, npcId);
            bool ok = OpenNPCInteractionCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out uint readNpcId);

            // Assert
            Assert.AreEqual(4, written);
            Assert.AreEqual(4, OpenNPCInteraction.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(npcId, readNpcId);
        }

        [Test]
        public void NpcZeroBodyMessages_CloseOpenedAndRejected_HaveNoBody()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act & Assert
            Assert.AreEqual(0, CloseNPCInteraction.BodySize);
            Assert.AreEqual(0, CloseNPCInteractionCodec.WriteBody(buffer));
            Assert.IsTrue(CloseNPCInteractionCodec.TryReadBody(ReadOnlySpan<byte>.Empty));

            Assert.AreEqual(0, NPCInteractionOpened.BodySize);
            Assert.AreEqual(0, NPCInteractionOpenedCodec.WriteBody(buffer));
            Assert.IsTrue(NPCInteractionOpenedCodec.TryReadBody(ReadOnlySpan<byte>.Empty));

            Assert.AreEqual(0, RejectedNotInTownHub.BodySize);
            Assert.AreEqual(0, RejectedNotInTownHubCodec.WriteBody(buffer));
            Assert.IsTrue(RejectedNotInTownHubCodec.TryReadBody(ReadOnlySpan<byte>.Empty));
        }

        // -----------------------------------------------------------------------
        // Too-short bodies are rejected by every message with a non-zero body.
        // -----------------------------------------------------------------------

        [TestCase(0)]
        [TestCase(5)]
        public void EnhancementAttemptRequest_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementAttemptRequestCodec.TryReadBody(new byte[length], out _, out _, out _));

        [TestCase(0)]
        [TestCase(4)]
        public void EnhancementRequestReceived_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementRequestReceivedCodec.TryReadBody(new byte[length], out _, out _));

        [TestCase(0)]
        [TestCase(5)]
        public void EnhancementAttemptResultMessage_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementAttemptResultMessageCodec.TryReadBody(new byte[length], out _, out _, out _, out _));

        [TestCase(0)]
        [TestCase(1)]
        public void EnhancementPreviewRequest_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementPreviewRequestCodec.TryReadBody(new byte[length], out _, out _));

        [TestCase(0)]
        [TestCase(6)]
        public void EnhancementStateUpdate_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementStateUpdateCodec.TryReadBody(new byte[length], out _, out _, out _, out _, out _));

        [TestCase(0)]
        [TestCase(2)]
        public void EnhancementPreviewRejected_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(EnhancementPreviewRejectedCodec.TryReadBody(new byte[length], out _, out _, out _, out _));

        [TestCase(0)]
        [TestCase(3)]
        public void OpenNPCInteraction_TryReadBody_ShortBody_ReturnsFalse(int length)
            => Assert.IsFalse(OpenNPCInteractionCodec.TryReadBody(new byte[length], out _));

        // -----------------------------------------------------------------------
        // Routing rows and MessageTypeId uniqueness.
        // -----------------------------------------------------------------------

        [TestCase(EnhancementAttemptRequest.MessageTypeId, nameof(EnhancementAttemptRequest), MessageDirection.ClientToServer)]
        [TestCase(EnhancementRequestReceived.MessageTypeId, nameof(EnhancementRequestReceived), MessageDirection.ServerToOwningClient)]
        [TestCase(EnhancementAttemptResultMessage.MessageTypeId, nameof(EnhancementAttemptResultMessage), MessageDirection.ServerToOwningClient)]
        [TestCase(ServerBroadcast_Enhancement9.MessageTypeId, nameof(ServerBroadcast_Enhancement9), MessageDirection.ServerToAllZoneClients)]
        [TestCase(CancelEnhancement.MessageTypeId, nameof(CancelEnhancement), MessageDirection.ClientToServer)]
        [TestCase(EnhancementPreviewRequest.MessageTypeId, nameof(EnhancementPreviewRequest), MessageDirection.ClientToServer)]
        [TestCase(EnhancementStateUpdate.MessageTypeId, nameof(EnhancementStateUpdate), MessageDirection.ServerToOwningClient)]
        [TestCase(EnhancementPreviewRejected.MessageTypeId, nameof(EnhancementPreviewRejected), MessageDirection.ServerToOwningClient)]
        [TestCase(OpenNPCInteraction.MessageTypeId, nameof(OpenNPCInteraction), MessageDirection.ClientToServer)]
        [TestCase(CloseNPCInteraction.MessageTypeId, nameof(CloseNPCInteraction), MessageDirection.ClientToServer)]
        [TestCase(NPCInteractionOpened.MessageTypeId, nameof(NPCInteractionOpened), MessageDirection.ServerToOwningClient)]
        [TestCase(RejectedNotInTownHub.MessageTypeId, nameof(RejectedNotInTownHub), MessageDirection.ServerToOwningClient)]
        public void Registry_EnhancementAndNpcMessage_HasRowOnReliableOrderedPriorityPathWithExpectedDirection(ushort messageTypeId, string name, MessageDirection direction)
        {
            // Act
            bool found = MessageRoutingRegistry.TryGetEntry(messageTypeId, out MessageRoutingEntry entry);

            // Assert
            Assert.IsTrue(found, $"{name} must have a routing row.");
            Assert.AreEqual(name, entry.MessageName);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel, "All twelve messages are R-OD.");
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
            Assert.AreEqual(direction, entry.Direction);
        }

        [Test]
        public void MessageTypeIds_TheTwelveNewValues_AreDistinctFromEachOtherAndFromExistingOnes()
        {
            // Arrange
            ushort[] newIds =
            {
                EnhancementAttemptRequest.MessageTypeId, EnhancementRequestReceived.MessageTypeId,
                EnhancementAttemptResultMessage.MessageTypeId, ServerBroadcast_Enhancement9.MessageTypeId,
                CancelEnhancement.MessageTypeId, EnhancementPreviewRequest.MessageTypeId,
                EnhancementStateUpdate.MessageTypeId, EnhancementPreviewRejected.MessageTypeId,
                OpenNPCInteraction.MessageTypeId, CloseNPCInteraction.MessageTypeId,
                NPCInteractionOpened.MessageTypeId, RejectedNotInTownHub.MessageTypeId,
            };
            ushort[] existingIds =
            {
                HeartbeatMessage.MessageTypeId, DamageEvent.MessageTypeId, CycleTimerBroadcast.MessageTypeId,
                EntityPositionUpdate.MessageTypeId, GoldSyncEvent.MessageTypeId, EntityHealthUpdate.MessageTypeId,
                PartyMemberHealthUpdate.MessageTypeId, GoldSyncEventForcedDelivery.MessageTypeId,
                SelfDamageEvent.MessageTypeId, SetTarget.MessageTypeId,
            };
            var seen = new HashSet<ushort>(existingIds);

            // Act & Assert
            Assert.AreEqual(existingIds.Length, seen.Count, "The existing ids must themselves be distinct.");
            foreach (ushort id in newIds)
            {
                Assert.IsTrue(seen.Add(id), $"MessageTypeId 0x{id:X4} collides with another message type.");
            }
        }
    }
}

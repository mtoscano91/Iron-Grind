using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for the zone wire messages and codecs (Zone Instancing Story 006): ZoneFullResponse,
    /// ZoneSnapshotRequest, ZoneSessionEnded, PlayerJoinedZone, EntityState, ZoneStateSnapshotFragment, the enum
    /// substitution decoders and the five routing rows.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_WireMessages_Tests
    {
        private const int BufferSize = 256;
        private const uint ZoneIdValue = 0x01020304u;
        private const uint EntityIdValue = 0x0A0B0C0Du;
        private const uint SnapshotVersionValue = 0xDEADBEEFu;
        private const byte RetryAfterSecondsValue = 30;
        private const int GracePeriodSecondsValue = 90;
        private const int LevelValue = 12;
        private const ushort MobTypeIdValue = 7;
        private const int NameMaxBytes = 24;

        private static EntityState MakePlayer(string name)
            => EntityState.CreatePlayer(new EntityID(EntityIdValue), 100, -200, 300, 1, -2, 3, 32767, 450, 500, true, false,
                5000, -1800, name, LevelValue, 80, 100, 0x05, true, 123456u);

        private static EntityState MakeMob()
            => EntityState.CreateMob(new EntityID(EntityIdValue), -100, 200, -300, 0, 0, 0, 32767, 90, 100, false, 0, 450, MobTypeIdValue);

        // -----------------------------------------------------------------------
        // Round trips and body sizes.
        // -----------------------------------------------------------------------

        [TestCase(JoinRejectedReason.ZoneFull)]
        [TestCase(JoinRejectedReason.ZoneDoesNotExist)]
        [TestCase(JoinRejectedReason.LoadCharacterFailed)]
        [TestCase(JoinRejectedReason.Other)]
        public void ZoneFullResponse_WriteThenRead_BodyIs6BytesAndRoundTrips(JoinRejectedReason reason)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new ZoneFullResponse(new ZoneID(ZoneIdValue), reason, RetryAfterSecondsValue);

            // Act
            int written = ZoneFullResponseCodec.WriteBody(buffer, message);
            bool ok = ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out ZoneFullResponse read);

            // Assert
            Assert.AreEqual(6, written);
            Assert.AreEqual(6, ZoneFullResponse.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
            Assert.AreEqual(ZoneIdValue, BinaryPrimitives.ReadUInt32LittleEndian(buffer), "zoneId is little-endian at offset 0.");
        }

        [Test]
        public void ZoneSnapshotRequest_WriteThenRead_BodyIs8BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new ZoneSnapshotRequest(new ZoneID(ZoneIdValue), SnapshotVersionValue);

            // Act
            int written = ZoneSnapshotRequestCodec.WriteBody(buffer, message);
            bool ok = ZoneSnapshotRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out ZoneSnapshotRequest read);

            // Assert
            Assert.AreEqual(8, written);
            Assert.AreEqual(8, ZoneSnapshotRequest.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
        }

        [TestCase(DisconnectReason.ZoneClosed, 0)]
        [TestCase(DisconnectReason.GhostDeath, GracePeriodSecondsValue)]
        [TestCase(DisconnectReason.Other, -1)]
        public void ZoneSessionEnded_WriteThenRead_BodyIs9BytesAndRoundTrips(DisconnectReason reason, int grace)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new ZoneSessionEnded(new ZoneID(ZoneIdValue), reason, grace);

            // Act
            int written = ZoneSessionEndedCodec.WriteBody(buffer, message);
            bool ok = ZoneSessionEndedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out ZoneSessionEnded read);

            // Assert
            Assert.AreEqual(9, written);
            Assert.AreEqual(9, ZoneSessionEnded.BodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
        }

        [Test]
        public void ZoneFixedBodies_TrailingBytes_AreTolerated()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            ZoneFullResponseCodec.WriteBody(buffer, new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, 1));
            ZoneSnapshotRequestCodec.WriteBody(buffer, new ZoneSnapshotRequest(new ZoneID(ZoneIdValue), 1u));
            ZoneSessionEndedCodec.WriteBody(buffer, new ZoneSessionEnded(new ZoneID(ZoneIdValue), DisconnectReason.ZoneClosed, 1));

            // Act & Assert
            Assert.IsTrue(ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 6 + 5), out _));
            Assert.IsTrue(ZoneSnapshotRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 8 + 5), out _));
            Assert.IsTrue(ZoneSessionEndedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 9 + 5), out _));
        }

        [Test]
        public void PlayerJoinedZone_EmptyName_BodyIs16BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), string.Empty, LevelValue, 100, -200, 300);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(16, written);
            Assert.AreEqual(16, PlayerJoinedZone.MinBodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
        }

        [Test]
        public void PlayerJoinedZone_24ByteName_BodyIs40BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), new string('n', NameMaxBytes), LevelValue, short.MinValue, 0, short.MaxValue);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(40, written);
            Assert.AreEqual(40, PlayerJoinedZone.MaxBodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
        }

        [Test]
        public void PlayerJoinedZone_MultiByteName_RoundTripsWithByteLengthPrefix()
        {
            // Arrange — 3 euro signs = 9 UTF-8 bytes.
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), "€€€", LevelValue, 1, 2, 3);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(16 + 9, written);
            Assert.AreEqual(9, BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4, 2)));
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
        }

        [Test]
        public void EntityState_PlayerEmptyName_Is53BytesFirstByteIsTypeAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState state = MakePlayer(string.Empty);

            // Act
            int written = EntityStateCodec.WriteBody(buffer, state);
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out int consumed);

            // Assert
            Assert.AreEqual(53, written);
            Assert.AreEqual(53, EntityState.PlayerMinBodySize);
            Assert.AreEqual((byte)EntityType.Player, buffer[0]);
            Assert.IsTrue(ok);
            Assert.AreEqual(written, consumed);
            Assert.AreEqual(state, read);
        }

        [Test]
        public void EntityState_Player24ByteName_Is77BytesAndRoundTrips()
        {
            // Arrange — 8 euro signs = exactly 24 UTF-8 bytes.
            byte[] buffer = new byte[BufferSize];
            EntityState state = MakePlayer(new string('€', 8));

            // Act
            int written = EntityStateCodec.WriteBody(buffer, state);
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out int consumed);

            // Assert
            Assert.AreEqual(77, written);
            Assert.AreEqual(77, EntityState.PlayerMaxBodySize);
            Assert.IsTrue(ok);
            Assert.AreEqual(written, consumed);
            Assert.AreEqual(state, read);
        }

        [Test]
        public void EntityState_Mob_Is35BytesFirstByteIsTypeAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState state = MakeMob();

            // Act
            int written = EntityStateCodec.WriteBody(buffer, state);
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out int consumed);

            // Assert
            Assert.AreEqual(35, written);
            Assert.AreEqual(35, EntityState.MobBodySize);
            Assert.AreEqual(33, EntityState.HeaderSize);
            Assert.AreEqual((byte)EntityType.Mob, buffer[0]);
            Assert.IsTrue(ok);
            Assert.AreEqual(written, consumed);
            Assert.AreEqual(state, read);
            Assert.AreEqual(MobTypeIdValue, read.MobTypeId);
        }

        [Test]
        public void EntityState_PackedPlayerThenMob_DecodeInSequenceUsingBytesConsumed()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState player = MakePlayer("Aria");
            EntityState mob = MakeMob();
            int first = EntityStateCodec.WriteBody(buffer, player);
            int second = EntityStateCodec.WriteBody(buffer.AsSpan(first), mob);
            int total = first + second;

            // Act
            bool okFirst = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, total), out EntityState readFirst, out int consumedFirst);
            bool okSecond = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, consumedFirst, total - consumedFirst),
                out EntityState readSecond, out int consumedSecond);

            // Assert
            Assert.IsTrue(okFirst);
            Assert.IsTrue(okSecond);
            Assert.AreEqual(first, consumedFirst);
            Assert.AreEqual(second, consumedSecond);
            Assert.AreEqual(player, readFirst);
            Assert.AreEqual(mob, readSecond);
        }

        [TestCase(0, 1, 0)]
        [TestCase(1, 1, 0)]
        [TestCase(5, 5, 4)]
        [TestCase(5, 5, 0)]
        public void ZoneStateSnapshotFragment_EmptyOrSmallPayload_RoundTrips(int unused, int total, int index)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            byte[] payload = { 1, 2, 3 };
            var header = new ZoneStateSnapshotFragment(SnapshotVersionValue, (ushort)total, (ushort)index);

            // Act
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, payload);
            bool ok = ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out ZoneStateSnapshotFragment read, out ReadOnlySpan<byte> readPayload);

            // Assert
            Assert.AreEqual(8 + 3, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(header, read);
            Assert.IsTrue(readPayload.SequenceEqual(payload));
        }

        [Test]
        public void ZoneStateSnapshotFragment_MaxPayload504Bytes_RoundTripsAndBodyIs512()
        {
            // Arrange
            byte[] buffer = new byte[ZoneStateSnapshotFragment.MaxMessageBodyBytes];
            byte[] payload = new byte[504];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)i;
            }

            var header = new ZoneStateSnapshotFragment(SnapshotVersionValue, 3, 2);

            // Act
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, payload);
            bool ok = ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out ZoneStateSnapshotFragment read, out ReadOnlySpan<byte> readPayload);

            // Assert
            Assert.AreEqual(512, written);
            Assert.AreEqual(504, ZoneStateSnapshotFragment.MaxPayloadBytes);
            Assert.IsTrue(ok);
            Assert.AreEqual(header, read);
            Assert.IsTrue(readPayload.SequenceEqual(payload));
        }

        [Test]
        public void ZoneStateSnapshotFragment_PayloadHoldingPackedEntities_WalksWithBytesConsumed()
        {
            // Arrange
            byte[] payload = new byte[ZoneStateSnapshotFragment.MaxPayloadBytes];
            int used = EntityStateCodec.WriteBody(payload, MakePlayer("Bob"));
            used += EntityStateCodec.WriteBody(payload.AsSpan(used), MakeMob());
            byte[] buffer = new byte[ZoneStateSnapshotFragment.MaxMessageBodyBytes];
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, new ZoneStateSnapshotFragment(1u, 1, 0), payload.AsSpan(0, used));

            // Act
            bool ok = ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out _, out ReadOnlySpan<byte> readPayload);
            bool okPlayer = EntityStateCodec.TryReadBody(readPayload, out EntityState player, out int consumed);
            bool okMob = EntityStateCodec.TryReadBody(readPayload.Slice(consumed), out EntityState mob, out int consumedMob);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsTrue(okPlayer);
            Assert.IsTrue(okMob);
            Assert.AreEqual(EntityType.Player, player.Type);
            Assert.AreEqual(EntityType.Mob, mob.Type);
            Assert.AreEqual(used, consumed + consumedMob);
        }

        // -----------------------------------------------------------------------
        // Malformed input: false, never an exception.
        // -----------------------------------------------------------------------

        [Test]
        public void TryReadBody_TruncatedBuffer_ReturnsFalseForEveryMessage()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int full = ZoneFullResponseCodec.WriteBody(buffer, new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, 1));
            int request = ZoneSnapshotRequestCodec.WriteBody(buffer, new ZoneSnapshotRequest(new ZoneID(ZoneIdValue), 1u));
            int ended = ZoneSessionEndedCodec.WriteBody(buffer, new ZoneSessionEnded(new ZoneID(ZoneIdValue), DisconnectReason.ZoneClosed, 1));

            // Act & Assert
            for (int length = 0; length < full; length++)
            {
                Assert.IsFalse(ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"ZoneFullResponse length {length}");
            }

            for (int length = 0; length < request; length++)
            {
                Assert.IsFalse(ZoneSnapshotRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"ZoneSnapshotRequest length {length}");
            }

            for (int length = 0; length < ended; length++)
            {
                Assert.IsFalse(ZoneSessionEndedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"ZoneSessionEnded length {length}");
            }
        }

        [Test]
        public void PlayerJoinedZone_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, new PlayerJoinedZone(new EntityID(EntityIdValue), "Cara", LevelValue, 1, 2, 3));

            // Act & Assert
            for (int length = 0; length < written; length++)
            {
                Assert.IsFalse(PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
            }
        }

        [Test]
        public void EntityState_TruncatedBuffer_ReturnsFalseAtEveryShorterLengthForPlayerAndMob()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int player = EntityStateCodec.WriteBody(buffer, MakePlayer("Dan"));

            // Act & Assert
            for (int length = 0; length < player; length++)
            {
                Assert.IsFalse(EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _, out int consumed), $"player length {length}");
                Assert.AreEqual(0, consumed);
            }

            int mob = EntityStateCodec.WriteBody(buffer, MakeMob());
            for (int length = 0; length < mob; length++)
            {
                Assert.IsFalse(EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _, out _), $"mob length {length}");
            }
        }

        [Test]
        public void ZoneStateSnapshotFragment_TruncatedHeader_ReturnsFalse()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            ZoneStateSnapshotFragmentCodec.WriteBody(buffer, new ZoneStateSnapshotFragment(1u, 2, 1), ReadOnlySpan<byte>.Empty);

            // Act & Assert
            for (int length = 0; length < ZoneStateSnapshotFragment.HeaderSize; length++)
            {
                Assert.IsFalse(ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _, out _), $"length {length}");
            }
        }

        [Test]
        public void PlayerJoinedZone_NameLengthOf25_ReturnsFalse()
        {
            // Arrange — a name prefix of 25 (> 24) with enough bytes behind it.
            byte[] buffer = new byte[BufferSize];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(WireIdCodec.EntityIdWireSize, 2), 25);

            // Act
            bool ok = PlayerJoinedZoneCodec.TryReadBody(buffer, out _);

            // Assert
            Assert.IsFalse(ok);
        }

        [Test]
        public void EntityState_NameLengthOf25_ReturnsFalse()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityStateCodec.WriteBody(buffer, MakePlayer(string.Empty));
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(EntityState.HeaderSize, 2), 25);

            // Act
            bool ok = EntityStateCodec.TryReadBody(buffer, out _, out int consumed);

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(0, consumed);
        }

        [Test]
        public void EntityState_UnknownEntityType2_ReturnsFalseWithoutThrowing()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityStateCodec.WriteBody(buffer, MakeMob());
            buffer[0] = 2;

            // Act
            bool ok = false;
            int consumed = -1;
            Assert.DoesNotThrow(() => ok = EntityStateCodec.TryReadBody(buffer, out _, out consumed));

            // Assert
            Assert.IsFalse(ok);
            Assert.AreEqual(0, consumed);
        }

        [Test]
        public void ZoneStateSnapshotFragment_TotalFragmentsZero_ReturnsFalse()
        {
            // Arrange
            byte[] body = new byte[ZoneStateSnapshotFragment.HeaderSize + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6, 2), 0);

            // Act & Assert
            Assert.IsFalse(ZoneStateSnapshotFragmentCodec.TryReadBody(body, out _, out _));
        }

        [TestCase(3, 3)]
        [TestCase(3, 4)]
        [TestCase(1, 1)]
        [TestCase(1, 65535)]
        public void ZoneStateSnapshotFragment_FragmentIndexNotBelowTotal_ReturnsFalse(int total, int index)
        {
            // Arrange
            byte[] body = new byte[ZoneStateSnapshotFragment.HeaderSize + 4];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4, 2), (ushort)total);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6, 2), (ushort)index);

            // Act & Assert
            Assert.IsFalse(ZoneStateSnapshotFragmentCodec.TryReadBody(body, out _, out _));
        }

        [Test]
        public void ZoneStateSnapshotFragment_Payload505Bytes_ReturnsFalse()
        {
            // Arrange — valid header (1 of 1) followed by 505 payload bytes.
            byte[] body = new byte[ZoneStateSnapshotFragment.HeaderSize + 505];
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6, 2), 0);

            // Act & Assert
            Assert.IsFalse(ZoneStateSnapshotFragmentCodec.TryReadBody(body, out _, out ReadOnlySpan<byte> payload));
            Assert.AreEqual(0, payload.Length);
        }

        // -----------------------------------------------------------------------
        // Enum substitution (CR-NET-7.4 / CR-NET-7.9).
        // -----------------------------------------------------------------------

        [Test]
        public void ZoneFullResponse_UnknownReasonByte3_DecodesAsOtherAndLogsAnomaly()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            ZoneFullResponseCodec.WriteBody(buffer, new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, RetryAfterSecondsValue));
            buffer[4] = 3;
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeJoinRejectedReason.*byte 3"));

            // Act
            bool ok = ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 6), out ZoneFullResponse read);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(JoinRejectedReason.Other, read.Reason);
            Assert.AreEqual(RetryAfterSecondsValue, read.RetryAfterSeconds);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ZoneSessionEnded_UnknownReasonByte4_DecodesAsOtherAndLogsAnomaly()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            ZoneSessionEndedCodec.WriteBody(buffer, new ZoneSessionEnded(new ZoneID(ZoneIdValue), DisconnectReason.ZoneClosed, GracePeriodSecondsValue));
            buffer[4] = 4;
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex(@"\[WireEnumCodec\] DecodeDisconnectReason.*byte 4"));

            // Act
            bool ok = ZoneSessionEndedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 9), out ZoneSessionEnded read);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(DisconnectReason.Other, read.Reason);
            Assert.AreEqual(GracePeriodSecondsValue, read.GracePeriodSeconds);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase((byte)0, JoinRejectedReason.ZoneFull)]
        [TestCase((byte)1, JoinRejectedReason.ZoneDoesNotExist)]
        [TestCase((byte)2, JoinRejectedReason.LoadCharacterFailed)]
        [TestCase((byte)255, JoinRejectedReason.Other)]
        public void WireEnumCodec_DecodeJoinRejectedReason_KnownBytes_DecodeWithoutWarning(byte raw, JoinRejectedReason expected)
        {
            // Act
            JoinRejectedReason decoded = WireEnumCodec.DecodeJoinRejectedReason(raw, ZoneFullResponse.MessageTypeId);

            // Assert
            Assert.AreEqual(expected, decoded);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Encode guards.
        // -----------------------------------------------------------------------

        [Test]
        public void PlayerJoinedZone_Encode30ByteName_IsCutTo24Bytes()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), new string('x', 30), LevelValue, 0, 0, 0);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(40, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(new string('x', 24), read.CharacterName);
        }

        [Test]
        public void PlayerJoinedZone_EncodeMultiByteNameAcrossTheLimit_IsCutOnACharacterBoundary()
        {
            // Arrange — 22 ASCII bytes + two 3-byte characters: the first would end at byte 25 > 24.
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), new string('c', 22) + "€€", LevelValue, 0, 0, 0);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(16 + 22, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(new string('c', 22), read.CharacterName);
        }

        [Test]
        public void EntityState_Encode30ByteAndSurrogatePairNames_AreCutToAtMost24BytesWithoutSplittingCharacters()
        {
            // Arrange — 23 ASCII + a 4-byte surrogate pair would end at 27 > 24.
            byte[] buffer = new byte[BufferSize];

            // Act
            int writtenAscii = EntityStateCodec.WriteBody(buffer, MakePlayer(new string('y', 30)));
            EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, writtenAscii), out EntityState ascii, out _);
            int writtenPair = EntityStateCodec.WriteBody(buffer, MakePlayer(new string('z', 23) + "\U0001F600"));
            EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, writtenPair), out EntityState pair, out _);

            // Assert
            Assert.AreEqual(77, writtenAscii);
            Assert.AreEqual(new string('y', 24), ascii.CharacterName);
            Assert.AreEqual(EntityState.PlayerMinBodySize + 23, writtenPair);
            Assert.AreEqual(new string('z', 23), pair.CharacterName);
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(pair.CharacterName), NameMaxBytes);
        }

        [Test]
        public void ZoneStateSnapshotFragment_Encode505BytePayload_ThrowsArgumentException()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize * 4];
            byte[] payload = new byte[505];
            var header = new ZoneStateSnapshotFragment(1u, 1, 0);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, payload));
        }

        [TestCase(0, 0)]
        [TestCase(2, 2)]
        public void ZoneStateSnapshotFragment_EncodeInvalidTotalOrIndex_ThrowsArgumentException(int total, int index)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var header = new ZoneStateSnapshotFragment(1u, (ushort)total, (ushort)index);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, new byte[1]));
        }

        // -----------------------------------------------------------------------
        // Routing rows.
        // -----------------------------------------------------------------------

        [TestCase(ZoneFullResponse.MessageTypeId, nameof(ZoneFullResponse), DesignPillar.Infrastructure, MessageDirection.ServerToOwningClient)]
        [TestCase(ZoneSnapshotRequest.MessageTypeId, nameof(ZoneSnapshotRequest), DesignPillar.Infrastructure, MessageDirection.ClientToServer)]
        [TestCase(ZoneSessionEnded.MessageTypeId, nameof(ZoneSessionEnded), DesignPillar.Infrastructure, MessageDirection.ServerToOwningClient)]
        [TestCase(PlayerJoinedZone.MessageTypeId, nameof(PlayerJoinedZone), DesignPillar.SocialSignals, MessageDirection.ServerToAllZoneClients)]
        [TestCase(ZoneStateSnapshotFragment.MessageTypeId, nameof(ZoneStateSnapshotFragment), DesignPillar.Infrastructure, MessageDirection.ServerToOwningClient)]
        public void Registry_ZoneMessage_HasRowOnReliableOrderedPriorityPathWithExpectedPillarAndDirection(
            ushort messageTypeId, string name, DesignPillar pillar, MessageDirection direction)
        {
            // Act
            bool found = MessageRoutingRegistry.TryGetEntry(messageTypeId, out MessageRoutingEntry entry);

            // Assert
            Assert.IsTrue(found, $"{name} must have a routing row.");
            Assert.AreEqual(name, entry.MessageName);
            Assert.AreEqual(pillar, entry.Pillars);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
            Assert.AreEqual(direction, entry.Direction);
            Assert.IsFalse(entry.IsMcr3Exception);
        }

        [Test]
        public void MessageTypeIds_ZoneMessages_HaveTheSpecifiedValuesAndAreDistinctFromExistingOnes()
        {
            // Arrange
            ushort[] zoneIds =
            {
                ZoneFullResponse.MessageTypeId, ZoneSnapshotRequest.MessageTypeId, ZoneSessionEnded.MessageTypeId,
                PlayerJoinedZone.MessageTypeId, ZoneStateSnapshotFragment.MessageTypeId,
            };
            var seen = new HashSet<ushort>();
            foreach (MessageRoutingEntry entry in MessageRoutingRegistry.AllEntries)
            {
                if (Array.IndexOf(zoneIds, entry.MessageTypeId) < 0)
                {
                    seen.Add(entry.MessageTypeId);
                }
            }

            // Assert
            Assert.AreEqual(new ushort[] { 0xE130, 0xE131, 0xE132, 0xE133, 0xF000 }, zoneIds);
            foreach (ushort id in zoneIds)
            {
                Assert.IsTrue(seen.Add(id), $"MessageTypeId 0x{id:X4} collides with another message type.");
            }
        }
    }
}

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

        // Distinct field values for the golden-byte tests: every field has its own bytes, so any swap fails.
        private const short GoldPosX = 0x0102;
        private const short GoldPosY = 0x0304;
        private const short GoldPosZ = 0x0506;
        private const short GoldRotX = 0x0708;
        private const short GoldRotY = 0x090A;
        private const short GoldRotZ = 0x0B0C;
        private const short GoldRotW = 0x0D0E;
        private const int GoldCurrentHp = 0x11121314;
        private const int GoldMaxHp = 0x21222324;
        private const ushort GoldCycleTimer = 0x3132;
        private const short GoldFacingAngle = 0x4142;
        private const ushort GoldMobTypeId = 0x5152;
        private const int GoldLevel = 0x61626364;
        private const int GoldCurrentMp = 0x71727374;
        private const int GoldMaxMp = 0x01020304;
        private const byte GoldEquipmentFlags = 0x05;
        private const uint GoldRespawnTicks = 0x91929394u;
        private const ushort GoldFragmentTotal = 0x0203;
        private const ushort GoldFragmentIndex = 0x0102;

        private const string Name24 = "nnnnnnnn" + "nnnnnnnn" + "nnnnnnnn";
        private const string Name30 = Name24 + "nnnnnn";

        private const ushort FragmentWireId = ZoneStateSnapshotFragment.MessageTypeId;

        private static byte[] Written(byte[] buffer, int written) => buffer.AsSpan(0, written).ToArray();

        private static byte[] FilledBuffer(int size)
        {
            byte[] buffer = new byte[size];
            buffer.AsSpan().Fill(0xFF);
            return buffer;
        }

        private static EntityState MakeGoldPlayer(string name)
            => EntityState.CreatePlayer(new EntityID(EntityIdValue), GoldPosX, GoldPosY, GoldPosZ, GoldRotX, GoldRotY, GoldRotZ, GoldRotW,
                GoldCurrentHp, GoldMaxHp, true, true, GoldCycleTimer, GoldFacingAngle, name, GoldLevel, GoldCurrentMp, GoldMaxMp,
                GoldEquipmentFlags, true, GoldRespawnTicks);

        private static EntityState MakeGoldMob()
            => EntityState.CreateMob(new EntityID(EntityIdValue), GoldPosX, GoldPosY, GoldPosZ, GoldRotX, GoldRotY, GoldRotZ, GoldRotW,
                GoldCurrentHp, GoldMaxHp, true, GoldCycleTimer, GoldFacingAngle, GoldMobTypeId);

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

        [TestCase(1, 0)]
        [TestCase(5, 4)]
        [TestCase(5, 0)]
        public void ZoneStateSnapshotFragment_SmallPayload_RoundTrips(int total, int index)
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
        public void ZoneFullResponse_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int full = ZoneFullResponseCodec.WriteBody(buffer, new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, 1));

            // Act & Assert
            for (int length = 0; length < full; length++)
            {
                Assert.IsFalse(ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
            }
        }

        [Test]
        public void ZoneSnapshotRequest_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int request = ZoneSnapshotRequestCodec.WriteBody(buffer, new ZoneSnapshotRequest(new ZoneID(ZoneIdValue), 1u));

            // Act & Assert
            for (int length = 0; length < request; length++)
            {
                Assert.IsFalse(ZoneSnapshotRequestCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
            }
        }

        [Test]
        public void ZoneSessionEnded_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int ended = ZoneSessionEndedCodec.WriteBody(buffer, new ZoneSessionEnded(new ZoneID(ZoneIdValue), DisconnectReason.ZoneClosed, 1));

            // Act & Assert
            for (int length = 0; length < ended; length++)
            {
                Assert.IsFalse(ZoneSessionEndedCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
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
        // Exact-size encode (GetBodySize).
        // -----------------------------------------------------------------------

        [TestCase("", 0)]
        [TestCase("Aria", 4)]
        [TestCase(Name24, 24)]
        [TestCase(Name30, 24)]
        public void PlayerJoinedZone_GetBodySize_EqualsWrittenLength(string name, int expectedNameBytes)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), name, LevelValue, 1, 2, 3);

            // Act
            int size = PlayerJoinedZoneCodec.GetBodySize(message);
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);

            // Assert
            Assert.AreEqual(16 + expectedNameBytes, size);
            Assert.AreEqual(size, written);
        }

        [TestCase("", 0)]
        [TestCase("Aria", 4)]
        [TestCase(Name24, 24)]
        [TestCase(Name30, 24)]
        public void EntityState_PlayerGetBodySize_EqualsWrittenLength(string name, int expectedNameBytes)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState state = MakePlayer(name);

            // Act
            int size = EntityStateCodec.GetBodySize(state);
            int written = EntityStateCodec.WriteBody(buffer, state);

            // Assert
            Assert.AreEqual(53 + expectedNameBytes, size);
            Assert.AreEqual(size, written);
        }

        [Test]
        public void EntityState_MobGetBodySize_Is35AndEqualsWrittenLength()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState state = MakeMob();

            // Act
            int size = EntityStateCodec.GetBodySize(state);
            int written = EntityStateCodec.WriteBody(buffer, state);

            // Assert
            Assert.AreEqual(35, size);
            Assert.AreEqual(size, written);
        }

        [Test]
        public void PlayerJoinedZone_ExactSizeDestination_Succeeds()
        {
            // Arrange
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), "Ann", LevelValue, 1, 2, 3);
            byte[] destination = new byte[PlayerJoinedZoneCodec.GetBodySize(message)];

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(destination, message);

            // Assert
            Assert.AreEqual(destination.Length, written);
            Assert.IsTrue(PlayerJoinedZoneCodec.TryReadBody(destination, out PlayerJoinedZone read));
            Assert.AreEqual(message, read);
        }

        [Test]
        public void PlayerJoinedZone_DestinationOneByteShort_ThrowsArgumentOutOfRange()
        {
            // Arrange
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), "Ann", LevelValue, 1, 2, 3);
            int size = PlayerJoinedZoneCodec.GetBodySize(message);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerJoinedZoneCodec.WriteBody(new byte[size - 1], message));
        }

        [Test]
        public void EntityState_ExactSizeDestination_SucceedsForPlayerAndMob()
        {
            // Arrange
            EntityState player = MakePlayer("Ann");
            EntityState mob = MakeMob();
            byte[] playerDestination = new byte[EntityStateCodec.GetBodySize(player)];
            byte[] mobDestination = new byte[EntityStateCodec.GetBodySize(mob)];

            // Act
            int playerWritten = EntityStateCodec.WriteBody(playerDestination, player);
            int mobWritten = EntityStateCodec.WriteBody(mobDestination, mob);

            // Assert
            Assert.AreEqual(playerDestination.Length, playerWritten);
            Assert.AreEqual(mobDestination.Length, mobWritten);
            Assert.IsTrue(EntityStateCodec.TryReadBody(playerDestination, out EntityState readPlayer, out _));
            Assert.IsTrue(EntityStateCodec.TryReadBody(mobDestination, out EntityState readMob, out _));
            Assert.AreEqual(player, readPlayer);
            Assert.AreEqual(mob, readMob);
        }

        [Test]
        public void EntityState_DestinationOneByteShort_ThrowsArgumentOutOfRange()
        {
            // Arrange
            EntityState player = MakePlayer("Ann");
            EntityState mob = MakeMob();
            int playerSize = EntityStateCodec.GetBodySize(player);
            int mobSize = EntityStateCodec.GetBodySize(mob);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityStateCodec.WriteBody(new byte[playerSize - 1], player));
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityStateCodec.WriteBody(new byte[mobSize - 1], mob));
        }

        [Test]
        public void EntityState_PackingToExactly504Bytes_LastEntryFitsOnlyBecauseOfExactSizing()
        {
            // Arrange — 11 mobs (385) + player with a 7-byte name (60) + player with a 6-byte name (59) = 504.
            byte[] payload = new byte[ZoneStateSnapshotFragment.MaxPayloadBytes];
            int offset = 0;
            for (int i = 0; i < 11; i++)
            {
                offset += EntityStateCodec.WriteBody(payload.AsSpan(offset), MakeMob());
            }

            offset += EntityStateCodec.WriteBody(payload.AsSpan(offset), MakePlayer("Charlie"));
            int remaining = payload.Length - offset;

            // Act
            offset += EntityStateCodec.WriteBody(payload.AsSpan(offset), MakePlayer("Delta1"));

            // Assert
            Assert.AreEqual(59, remaining);
            Assert.Less(remaining, EntityState.PlayerMaxBodySize, "A destination sized to the maximum would have rejected this entry.");
            Assert.AreEqual(ZoneStateSnapshotFragment.MaxPayloadBytes, offset);

            int count = 0;
            ReadOnlySpan<byte> rest = payload;
            while (rest.Length > 0)
            {
                Assert.IsTrue(EntityStateCodec.TryReadBody(rest, out _, out int consumed));
                rest = rest.Slice(consumed);
                count++;
            }

            Assert.AreEqual(13, count);
        }

        // -----------------------------------------------------------------------
        // Golden bytes: every layout asserted whole, little-endian, written by hand from the spec.
        // -----------------------------------------------------------------------

        [Test]
        public void ZoneFullResponse_GoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange
            byte[] buffer = FilledBuffer(BufferSize);
            var message = new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.LoadCharacterFailed, RetryAfterSecondsValue);
            byte[] expected = { 0x04, 0x03, 0x02, 0x01, 0x02, 0x1E };

            // Act
            int written = ZoneFullResponseCodec.WriteBody(buffer, message);

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void ZoneSnapshotRequest_GoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange
            byte[] buffer = FilledBuffer(BufferSize);
            var message = new ZoneSnapshotRequest(new ZoneID(ZoneIdValue), SnapshotVersionValue);
            byte[] expected = { 0x04, 0x03, 0x02, 0x01, 0xEF, 0xBE, 0xAD, 0xDE };

            // Act
            int written = ZoneSnapshotRequestCodec.WriteBody(buffer, message);

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void ZoneSessionEnded_GoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange — grace period -300 = 0xFFFFFED4 (negative, multi-byte).
            byte[] buffer = FilledBuffer(BufferSize);
            var message = new ZoneSessionEnded(new ZoneID(ZoneIdValue), DisconnectReason.GhostDeath, -300);
            byte[] expected = { 0x04, 0x03, 0x02, 0x01, 0x03, 0xD4, 0xFE, 0xFF, 0xFF };

            // Act
            int written = ZoneSessionEndedCodec.WriteBody(buffer, message);

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void PlayerJoinedZone_GoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange — level 300 = 0x012C, positions 100 / -200 / 300.
            byte[] buffer = FilledBuffer(BufferSize);
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), "Ab", 300, 100, -200, 300);
            byte[] expected =
            {
                0x0D, 0x0C, 0x0B, 0x0A,
                0x02, 0x00, 0x41, 0x62,
                0x2C, 0x01, 0x00, 0x00,
                0x64, 0x00,
                0x38, 0xFF,
                0x2C, 0x01,
            };

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void EntityState_MobGoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange
            byte[] buffer = FilledBuffer(BufferSize);
            byte[] expected =
            {
                0x01,
                0x0D, 0x0C, 0x0B, 0x0A,
                0x02, 0x01, 0x04, 0x03, 0x06, 0x05,
                0x08, 0x07, 0x0A, 0x09, 0x0C, 0x0B, 0x0E, 0x0D,
                0x14, 0x13, 0x12, 0x11,
                0x24, 0x23, 0x22, 0x21,
                0x01,
                0x00,
                0x32, 0x31,
                0x42, 0x41,
                0x52, 0x51,
            };

            // Act
            int written = EntityStateCodec.WriteBody(buffer, MakeGoldMob());

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void EntityState_PlayerGoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange
            byte[] buffer = FilledBuffer(BufferSize);
            byte[] expected =
            {
                0x00,
                0x0D, 0x0C, 0x0B, 0x0A,
                0x02, 0x01, 0x04, 0x03, 0x06, 0x05,
                0x08, 0x07, 0x0A, 0x09, 0x0C, 0x0B, 0x0E, 0x0D,
                0x14, 0x13, 0x12, 0x11,
                0x24, 0x23, 0x22, 0x21,
                0x01,
                0x01,
                0x32, 0x31,
                0x42, 0x41,
                0x02, 0x00, 0x48, 0x69,
                0x64, 0x63, 0x62, 0x61,
                0x74, 0x73, 0x72, 0x71,
                0x04, 0x03, 0x02, 0x01,
                0x05,
                0x01,
                0x94, 0x93, 0x92, 0x91,
            };

            // Act
            int written = EntityStateCodec.WriteBody(buffer, MakeGoldPlayer("Hi"));

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        [Test]
        public void ZoneStateSnapshotFragment_GoldenBytes_MatchTheSpecifiedLayout()
        {
            // Arrange — total 0x0203, index 0x0102 (total != index).
            byte[] buffer = FilledBuffer(BufferSize);
            var header = new ZoneStateSnapshotFragment(SnapshotVersionValue, GoldFragmentTotal, GoldFragmentIndex);
            byte[] payload = { 0xAA, 0xBB, 0xCC };
            byte[] expected = { 0xEF, 0xBE, 0xAD, 0xDE, 0x03, 0x02, 0x02, 0x01, 0xAA, 0xBB, 0xCC };

            // Act
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, payload);

            // Assert
            CollectionAssert.AreEqual(expected, Written(buffer, written));
        }

        // -----------------------------------------------------------------------
        // Invalid EntityID on encode (CR-NET-7.3).
        // -----------------------------------------------------------------------

        [Test]
        public void PlayerJoinedZone_EncodeInvalidEntityId_ThrowsInvalidOperationAndWarns()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(EntityID.Invalid, "Ann", LevelValue, 1, 2, 3);
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex($@"\[WireIdCodec\] SerializeEntityId: InvalidIdZeroWrite.*messageTypeId={PlayerJoinedZone.MessageTypeId}\b"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => PlayerJoinedZoneCodec.WriteBody(buffer, message));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EntityState_EncodeInvalidEntityId_ThrowsInvalidOperationWarnsWithFragmentIdAndWritesNothing(bool isPlayer)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            EntityState state = isPlayer
                ? EntityState.CreatePlayer(EntityID.Invalid, 0, 0, 0, 0, 0, 0, 0, 1, 1, false, false, 0, 0, "Ann", 1, 1, 1, 0, false, 0)
                : EntityState.CreateMob(EntityID.Invalid, 0, 0, 0, 0, 0, 0, 0, 1, 1, false, 0, 0, 1);
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new Regex($@"\[WireIdCodec\] SerializeEntityId: InvalidIdZeroWrite.*messageTypeId={FragmentWireId}\b"));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => EntityStateCodec.WriteBody(buffer, state));
            Assert.AreEqual(0, buffer[0], "The id guard runs before the entityType byte is written.");
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // bool wire rules.
        // -----------------------------------------------------------------------

        [Test]
        public void EntityState_MobWireGhostByteSetTo1_ReadsIsGhostFalse()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int written = EntityStateCodec.WriteBody(buffer, MakeMob());
            buffer[28] = 1;

            // Act
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out _);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsFalse(read.IsGhost);
        }

        [Test]
        public void EntityState_MobWrite_LeavesGhostByteAt0()
        {
            // Arrange
            byte[] buffer = FilledBuffer(BufferSize);

            // Act
            EntityStateCodec.WriteBody(buffer, MakeMob());

            // Assert
            Assert.AreEqual(0, buffer[28]);
        }

        [Test]
        public void EntityState_PlayerBoolBytesSetTo2_ReadAsTrue()
        {
            // Arrange — empty name: isInDeadState is at 33 + 2 + 12 + 1 = 48.
            byte[] buffer = new byte[BufferSize];
            EntityState state = EntityState.CreatePlayer(new EntityID(EntityIdValue), 0, 0, 0, 0, 0, 0, 0, 1, 1, false, false, 0, 0,
                string.Empty, 1, 1, 1, 0, false, 0);
            int written = EntityStateCodec.WriteBody(buffer, state);
            buffer[27] = 2;
            buffer[28] = 2;
            buffer[48] = 2;

            // Act
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out _);

            // Assert
            Assert.IsTrue(ok);
            Assert.IsTrue(read.IsInCombat);
            Assert.IsTrue(read.IsGhost);
            Assert.IsTrue(read.IsInDeadState);
        }

        // -----------------------------------------------------------------------
        // Fragment boundaries.
        // -----------------------------------------------------------------------

        [Test]
        public void ZoneStateSnapshotFragment_EmptyPayload_Is8BytesAndRoundTrips()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var header = new ZoneStateSnapshotFragment(SnapshotVersionValue, 2, 1);

            // Act
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, ReadOnlySpan<byte>.Empty);
            bool ok = ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out ZoneStateSnapshotFragment read, out ReadOnlySpan<byte> payload);

            // Assert
            Assert.AreEqual(8, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(header, read);
            Assert.AreEqual(0, payload.Length);
        }

        [TestCase(503)]
        [TestCase(504)]
        public void ZoneStateSnapshotFragment_PayloadAtBoundary_RoundTrips(int payloadLength)
        {
            // Arrange
            byte[] buffer = new byte[ZoneStateSnapshotFragment.MaxMessageBodyBytes];
            byte[] payload = new byte[payloadLength];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i * 7);
            }

            var header = new ZoneStateSnapshotFragment(SnapshotVersionValue, 4, 3);

            // Act
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, header, payload);
            bool ok = ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written),
                out ZoneStateSnapshotFragment read, out ReadOnlySpan<byte> readPayload);

            // Assert
            Assert.AreEqual(8 + payloadLength, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(header, read);
            Assert.IsTrue(readPayload.SequenceEqual(payload));
        }

        [Test]
        public void ZoneStateSnapshotFragment_DecodedPayload_IsAViewOfTheSourceBuffer()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            int written = ZoneStateSnapshotFragmentCodec.WriteBody(buffer, new ZoneStateSnapshotFragment(1u, 1, 0), new byte[] { 1, 2, 3 });
            ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out _, out ReadOnlySpan<byte> payload);

            // Act — mutate the source after decoding.
            buffer[ZoneStateSnapshotFragment.HeaderSize] = 99;

            // Assert
            Assert.AreEqual(99, payload[0]);
        }

        // -----------------------------------------------------------------------
        // Name edge cases and remaining encode/decode rules.
        // -----------------------------------------------------------------------

        [Test]
        public void PlayerJoinedZone_NullName_EncodesAsEmpty()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), null, LevelValue, 1, 2, 3);

            // Act
            int written = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerJoinedZone read);

            // Assert
            Assert.AreEqual(16, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, read.CharacterName);
        }

        [Test]
        public void EntityState_NullName_EncodesAsEmpty()
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];

            // Act
            int written = EntityStateCodec.WriteBody(buffer, MakePlayer(null));
            bool ok = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out EntityState read, out _);

            // Assert
            Assert.AreEqual(53, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(string.Empty, read.CharacterName);
        }

        [TestCase(1, 5)]
        [TestCase(22, 22)]
        public void WireNames_LoneSurrogate_WrittenByteCountMatchesTheEncodedTruncatedString(int leadingAsciiChars, int expectedNameBytes)
        {
            // Arrange — a lone surrogate encodes as U+FFFD (3 bytes); the second case is cut before it (22 + 3 > 24).
            // The name is built here, not passed through [TestCase]: attribute string arguments are stored as UTF-8
            // metadata, which cannot hold a lone surrogate (it arrives as three U+FFFD characters).
            string name = new string('a', leadingAsciiChars) + "\uD800" + "b";
            byte[] buffer = new byte[BufferSize];
            var message = new PlayerJoinedZone(new EntityID(EntityIdValue), name, LevelValue, 1, 2, 3);

            // Act
            int writtenMessage = PlayerJoinedZoneCodec.WriteBody(buffer, message);
            PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, writtenMessage), out PlayerJoinedZone readMessage);
            int writtenState = EntityStateCodec.WriteBody(buffer, MakePlayer(name));
            EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, writtenState), out EntityState readState, out _);

            // Assert
            Assert.AreEqual(16 + expectedNameBytes, writtenMessage);
            Assert.AreEqual(53 + expectedNameBytes, writtenState);
            Assert.AreEqual(expectedNameBytes, Encoding.UTF8.GetByteCount(readMessage.CharacterName));
            Assert.AreEqual(expectedNameBytes, Encoding.UTF8.GetByteCount(readState.CharacterName));
            Assert.LessOrEqual(writtenMessage - 16, NameMaxBytes);
            Assert.LessOrEqual(writtenState - 53, NameMaxBytes);
        }

        [Test]
        public void WireNames_InvalidUtf8OnRead_DecodeToReplacementCharacterAndSucceed()
        {
            // Arrange — pins current behaviour: invalid UTF-8 does not fail the read, it yields U+FFFD.
            byte[] joined = new byte[BufferSize];
            int writtenJoined = PlayerJoinedZoneCodec.WriteBody(joined, new PlayerJoinedZone(new EntityID(EntityIdValue), "ab", LevelValue, 1, 2, 3));
            joined[6] = 0xFF;
            joined[7] = 0xFE;
            byte[] state = new byte[BufferSize];
            int writtenState = EntityStateCodec.WriteBody(state, MakePlayer("ab"));
            state[EntityState.HeaderSize + 2] = 0xFF;
            state[EntityState.HeaderSize + 3] = 0xFE;

            // Act
            bool okJoined = PlayerJoinedZoneCodec.TryReadBody(new ReadOnlySpan<byte>(joined, 0, writtenJoined), out PlayerJoinedZone readJoined);
            bool okState = EntityStateCodec.TryReadBody(new ReadOnlySpan<byte>(state, 0, writtenState), out EntityState readState, out _);

            // Assert
            Assert.IsTrue(okJoined);
            Assert.IsTrue(okState);
            Assert.That(readJoined.CharacterName, Does.Contain("�"));
            Assert.That(readState.CharacterName, Does.Contain("�"));
        }

        [TestCase((byte)0)]
        [TestCase((byte)255)]
        public void ZoneFullResponse_RetryAfterSecondsBoundaries_RoundTrip(byte retryAfter)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            var message = new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, retryAfter);

            // Act
            int written = ZoneFullResponseCodec.WriteBody(buffer, message);
            bool ok = ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out ZoneFullResponse read);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(retryAfter, read.RetryAfterSeconds);
            Assert.AreEqual(message, read);
        }

        [TestCase((byte)0, JoinRejectedReason.ZoneFull)]
        [TestCase((byte)1, JoinRejectedReason.ZoneDoesNotExist)]
        [TestCase((byte)2, JoinRejectedReason.LoadCharacterFailed)]
        public void ZoneFullResponse_KnownReasonBytes_DecodeWithoutLogging(byte raw, JoinRejectedReason expected)
        {
            // Arrange
            byte[] buffer = new byte[BufferSize];
            ZoneFullResponseCodec.WriteBody(buffer, new ZoneFullResponse(new ZoneID(ZoneIdValue), JoinRejectedReason.ZoneFull, 1));
            buffer[4] = raw;

            // Act
            bool ok = ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, 6), out ZoneFullResponse read);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(expected, read.Reason);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Story 011: ZoneEntryAck (0xE134, C->S) and PlayerLeftZone (0xE135, S->C).
        // -----------------------------------------------------------------------

        [Test]
        public void ZoneEntryAck_WriteThenRead_BodyIs8BytesAndRoundTrips()
        {
            // Arrange
            var buffer = new byte[16];
            var message = new ZoneEntryAck(new ZoneID(ZoneIdValue), SnapshotVersionValue);

            // Act
            int written = ZoneEntryAckCodec.WriteBody(buffer, message);
            bool ok = ZoneEntryAckCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out ZoneEntryAck read);

            // Assert
            Assert.AreEqual(8, ZoneEntryAck.BodySize);
            Assert.AreEqual(8, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
            Assert.AreEqual(ZoneIdValue, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
            Assert.AreEqual(SnapshotVersionValue, BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(buffer, 4, 4)));
        }

        [Test]
        public void ZoneEntryAck_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            var buffer = new byte[ZoneEntryAck.BodySize];
            ZoneEntryAckCodec.WriteBody(buffer, new ZoneEntryAck(new ZoneID(ZoneIdValue), 1u));

            for (int length = 0; length < ZoneEntryAck.BodySize; length++)
            {
                Assert.IsFalse(ZoneEntryAckCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
            }

            Assert.IsTrue(ZoneEntryAckCodec.TryReadBody(new ReadOnlySpan<byte>(new byte[ZoneEntryAck.BodySize + 3]), out _), "trailing bytes are ignored");
        }

        [Test]
        public void PlayerLeftZone_WriteThenRead_BodyIs5BytesAndRoundTrips()
        {
            // Arrange
            var buffer = new byte[16];
            var message = new PlayerLeftZone(new EntityID(77u), DisconnectType.ZoneTransfer);

            // Act
            int written = PlayerLeftZoneCodec.WriteBody(buffer, message);
            bool ok = PlayerLeftZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, written), out PlayerLeftZone read);

            // Assert
            Assert.AreEqual(5, PlayerLeftZone.BodySize);
            Assert.AreEqual(5, written);
            Assert.IsTrue(ok);
            Assert.AreEqual(message, read);
            Assert.AreEqual(77u, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
            Assert.AreEqual((byte)DisconnectType.ZoneTransfer, buffer[4]);
        }

        [Test]
        public void PlayerLeftZone_TruncatedBuffer_ReturnsFalseAtEveryShorterLength()
        {
            var buffer = new byte[PlayerLeftZone.BodySize];
            PlayerLeftZoneCodec.WriteBody(buffer, new PlayerLeftZone(new EntityID(1u), DisconnectType.Timeout));

            for (int length = 0; length < PlayerLeftZone.BodySize; length++)
            {
                Assert.IsFalse(PlayerLeftZoneCodec.TryReadBody(new ReadOnlySpan<byte>(buffer, 0, length), out _), $"length {length}");
            }
        }

        [Test]
        public void PlayerLeftZone_UnknownDisconnectType_DecodesAsTimeoutWithAnAnomalyLog()
        {
            // Arrange
            var buffer = new byte[PlayerLeftZone.BodySize];
            PlayerLeftZoneCodec.WriteBody(buffer, new PlayerLeftZone(new EntityID(5u), DisconnectType.Graceful));
            buffer[4] = 50;
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("DecodeDisconnectType"));

            // Act
            bool ok = PlayerLeftZoneCodec.TryReadBody(buffer, out PlayerLeftZone read);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual(DisconnectType.Timeout, read.DisconnectType);
        }

        [Test]
        public void PlayerLeftZone_InvalidEntityId_ThrowsOnWrite()
        {
            var buffer = new byte[PlayerLeftZone.BodySize];
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("InvalidIdZeroWrite"));

            Assert.Throws<InvalidOperationException>(() => PlayerLeftZoneCodec.WriteBody(buffer, new PlayerLeftZone(EntityID.Invalid, DisconnectType.Timeout)));
        }

        [TestCase(ZoneEntryAck.MessageTypeId, nameof(ZoneEntryAck), DesignPillar.Infrastructure, MessageDirection.ClientToServer, (ushort)0xE134)]
        [TestCase(PlayerLeftZone.MessageTypeId, nameof(PlayerLeftZone), DesignPillar.SocialSignals, MessageDirection.ServerToAllZoneClients, (ushort)0xE135)]
        public void Registry_Story011Message_HasTheProvisionalIdAndTheRightDirectionOnThePriorityPath(
            ushort messageTypeId, string name, DesignPillar pillar, MessageDirection direction, ushort expectedId)
        {
            Assert.AreEqual(expectedId, messageTypeId);
            Assert.IsTrue(MessageRoutingRegistry.TryGetEntry(messageTypeId, out MessageRoutingEntry entry), $"{name} must have a routing row.");
            Assert.AreEqual(name, entry.MessageName);
            Assert.AreEqual(pillar, entry.Pillars);
            Assert.AreEqual(direction, entry.Direction);
            Assert.AreEqual(NetworkChannel.ReliableOrdered, entry.Channel);
            Assert.AreEqual(MessageDeliveryContext.PriorityPath, entry.DeliveryContext);
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

using System;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Gateway
{
    /// <summary>
    /// Gateway Story 001: link frame codec (round trip, sizes, partial frames, violations, direction checks).
    /// </summary>
    [TestFixture]
    internal sealed class GatewayLink_Codec_Tests
    {
        private const int MAX_FRAME_BYTES = 65536;
        private const int LENGTH_PREFIX = 4;
        private const int TOKEN_LENGTH = 16;
        private const int MAX_LIVE_ACCOUNTS = (MAX_FRAME_BYTES - 1 - 11) / 4;
        private const uint REQUEST_ID = 42;
        private const uint ACCOUNT_ID = 1001;
        private const uint CHARACTER_ID = 77;
        private const uint ZONE_ID = 5;
        private const byte TEMPLATE_ID = 3;
        private const ushort UDP_PORT = 7777;
        private const ushort CAPACITY = 50;
        private const byte BAD_FLAG = 2;
        private const byte UNKNOWN_TYPE = 0x55;
        private const byte UNDEFINED_REASON = 9;

        // --------------------------------------------------------------- Fixtures

        private static byte[] MakeToken(byte seed = 1)
        {
            var token = new byte[TOKEN_LENGTH];
            for (int i = 0; i < TOKEN_LENGTH; i++)
            {
                token[i] = (byte)(seed + i);
            }

            return token;
        }

        private static AccountID[] MakeAccounts(int count)
        {
            var accounts = new AccountID[count];
            for (int i = 0; i < count; i++)
            {
                accounts[i] = new AccountID((uint)(ACCOUNT_ID + i));
            }

            return accounts;
        }

        private static byte[] Raw(GatewayLinkMessageType type, int bodyLength, byte fill = 0)
        {
            return RawByte((byte)type, bodyLength, fill);
        }

        private static byte[] RawByte(byte type, int bodyLength, byte fill = 0)
        {
            var frame = new byte[LENGTH_PREFIX + 1 + bodyLength];
            BitConverter.GetBytes((uint)(1 + bodyLength)).CopyTo(frame, 0);
            frame[LENGTH_PREFIX] = type;
            for (int i = LENGTH_PREFIX + 1; i < frame.Length; i++)
            {
                frame[i] = fill;
            }

            return frame;
        }

        private static GatewayToZoneMessage ReadToZone(byte[] frame)
        {
            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out int consumed);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status);
            Assert.AreEqual(frame.Length, consumed);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message));
            return message;
        }

        private static ZoneToGatewayMessage ReadToGateway(byte[] frame)
        {
            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out int consumed);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status);
            Assert.AreEqual(frame.Length, consumed);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message));
            return message;
        }

        private static bool ZoneDecodeFails(byte[] frame)
        {
            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out _);
            return status != GatewayLinkFrameStatus.Frame || !GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out _);
        }

        private static bool GatewayDecodeFails(byte[] frame)
        {
            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out _);
            return status != GatewayLinkFrameStatus.Frame || !GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out _);
        }

        private static void AssertLengthField(byte[] frame, GatewayLinkMessageType type, int bodyLength)
        {
            Assert.AreEqual(LENGTH_PREFIX + 1 + bodyLength, frame.Length);
            Assert.AreEqual((uint)(1 + bodyLength), BitConverter.ToUInt32(frame, 0));
            Assert.AreEqual((byte)type, frame[LENGTH_PREFIX]);
        }

        // --------------------------------------------------------------- Round trip: zone to gateway

        [Test]
        public void test_register_zone_round_trips()
        {
            var value = new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, MakeAccounts(3));
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(value));

            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, decoded.Type);
            Assert.AreEqual(ZONE_ID, decoded.RegisterZone.ZoneId.RawValue);
            Assert.AreEqual(TEMPLATE_ID, decoded.RegisterZone.TemplateId);
            Assert.AreEqual(UDP_PORT, decoded.RegisterZone.UdpPort);
            Assert.AreEqual(CAPACITY, decoded.RegisterZone.Capacity);
            CollectionAssert.AreEqual(value.LiveAccounts, decoded.RegisterZone.LiveAccounts);
        }

        [Test]
        public void test_register_zone_boundary_values_round_trip()
        {
            var value = new RegisterZoneMessage(new ZoneID(uint.MaxValue), byte.MaxValue, ushort.MaxValue, ushort.MaxValue, new[] { new AccountID(uint.MaxValue), new AccountID(0) });
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(value)).RegisterZone;

            Assert.AreEqual(uint.MaxValue, decoded.ZoneId.RawValue);
            Assert.AreEqual(byte.MaxValue, decoded.TemplateId);
            Assert.AreEqual(ushort.MaxValue, decoded.UdpPort);
            Assert.AreEqual(ushort.MaxValue, decoded.Capacity);
            CollectionAssert.AreEqual(value.LiveAccounts, decoded.LiveAccounts);
        }

        [Test]
        public void test_register_zone_empty_live_accounts_round_trips()
        {
            var value = new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, new AccountID[0]);
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(value)).RegisterZone;

            Assert.IsNotNull(decoded.LiveAccounts);
            Assert.AreEqual(0, decoded.LiveAccounts.Length);
        }

        [Test]
        public void test_register_zone_max_live_accounts_fits_cap_and_round_trips()
        {
            var value = new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, MakeAccounts(MAX_LIVE_ACCOUNTS));
            byte[] frame = GatewayLinkCodec.Encode(value);

            Assert.AreEqual(16381, MAX_LIVE_ACCOUNTS);
            Assert.AreEqual((uint)MAX_FRAME_BYTES, BitConverter.ToUInt32(frame, 0));
            CollectionAssert.AreEqual(value.LiveAccounts, ReadToGateway(frame).RegisterZone.LiveAccounts);
        }

        [TestCase((ushort)0)]
        [TestCase(ushort.MaxValue)]
        public void test_player_count_round_trips(ushort count)
        {
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new PlayerCountMessage(count)));

            Assert.AreEqual(GatewayLinkMessageType.PlayerCount, decoded.Type);
            Assert.AreEqual(count, decoded.PlayerCount.Count);
        }

        [Test]
        public void test_zone_draining_round_trips()
        {
            var decoded = ReadToGateway(GatewayLinkCodec.EncodeZoneDraining());

            Assert.AreEqual(GatewayLinkMessageType.ZoneDraining, decoded.Type);
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void test_session_released_round_trips(uint accountId)
        {
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new SessionReleasedMessage(new AccountID(accountId))));

            Assert.AreEqual(GatewayLinkMessageType.SessionReleased, decoded.Type);
            Assert.AreEqual(accountId, decoded.SessionReleased.AccountId.RawValue);
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void test_admit_result_accepted_round_trips(uint requestId)
        {
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new AdmitResultMessage(requestId, true, AdmitRejectReason.None)));

            Assert.AreEqual(GatewayLinkMessageType.AdmitResult, decoded.Type);
            Assert.AreEqual(requestId, decoded.AdmitResult.RequestId);
            Assert.IsTrue(decoded.AdmitResult.Accepted);
            Assert.AreEqual(AdmitRejectReason.None, decoded.AdmitResult.Reason);
        }

        [TestCase(AdmitRejectReason.NotActive)]
        [TestCase(AdmitRejectReason.Full)]
        [TestCase(AdmitRejectReason.InvalidToken)]
        public void test_admit_result_rejected_round_trips(AdmitRejectReason reason)
        {
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, false, reason)));

            Assert.AreEqual(REQUEST_ID, decoded.AdmitResult.RequestId);
            Assert.IsFalse(decoded.AdmitResult.Accepted);
            Assert.AreEqual(reason, decoded.AdmitResult.Reason);
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void test_evict_result_round_trips(uint requestId)
        {
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new EvictResultMessage(requestId)));

            Assert.AreEqual(GatewayLinkMessageType.EvictResult, decoded.Type);
            Assert.AreEqual(requestId, decoded.EvictResult.RequestId);
        }

        [Test]
        public void test_reconnect_result_accepted_round_trips()
        {
            byte[] token = MakeToken();
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new ReconnectResultMessage(uint.MaxValue, true, token)));

            Assert.AreEqual(GatewayLinkMessageType.ReconnectResult, decoded.Type);
            Assert.AreEqual(uint.MaxValue, decoded.ReconnectResult.RequestId);
            Assert.IsTrue(decoded.ReconnectResult.Accepted);
            CollectionAssert.AreEqual(token, decoded.ReconnectResult.NewToken);
        }

        [Test]
        public void test_reconnect_result_rejected_all_zero_token_round_trips()
        {
            byte[] zeros = new byte[TOKEN_LENGTH];
            var decoded = ReadToGateway(GatewayLinkCodec.Encode(new ReconnectResultMessage(0, false, zeros)));

            Assert.IsFalse(decoded.ReconnectResult.Accepted);
            Assert.AreEqual(0u, decoded.ReconnectResult.RequestId);
            CollectionAssert.AreEqual(zeros, decoded.ReconnectResult.NewToken);
        }

        // --------------------------------------------------------------- Round trip: gateway to zone

        [Test]
        public void test_admit_round_trips()
        {
            byte[] token = MakeToken();
            var decoded = ReadToZone(GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), new CharacterID(CHARACTER_ID), token)));

            Assert.AreEqual(GatewayLinkMessageType.Admit, decoded.Type);
            Assert.AreEqual(REQUEST_ID, decoded.Admit.RequestId);
            Assert.AreEqual(ACCOUNT_ID, decoded.Admit.AccountId.RawValue);
            Assert.AreEqual(CHARACTER_ID, decoded.Admit.CharacterId.RawValue);
            CollectionAssert.AreEqual(token, decoded.Admit.Token);
        }

        [Test]
        public void test_admit_boundary_values_and_zero_token_round_trip()
        {
            byte[] zeros = new byte[TOKEN_LENGTH];
            var decoded = ReadToZone(GatewayLinkCodec.Encode(new AdmitMessage(uint.MaxValue, new AccountID(uint.MaxValue), new CharacterID(0), zeros))).Admit;

            Assert.AreEqual(uint.MaxValue, decoded.RequestId);
            Assert.AreEqual(uint.MaxValue, decoded.AccountId.RawValue);
            Assert.AreEqual(0u, decoded.CharacterId.RawValue);
            CollectionAssert.AreEqual(zeros, decoded.Token);
        }

        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        public void test_evict_round_trips(uint requestId, uint accountId)
        {
            var decoded = ReadToZone(GatewayLinkCodec.Encode(new EvictMessage(requestId, new AccountID(accountId))));

            Assert.AreEqual(GatewayLinkMessageType.Evict, decoded.Type);
            Assert.AreEqual(requestId, decoded.Evict.RequestId);
            Assert.AreEqual(accountId, decoded.Evict.AccountId.RawValue);
        }

        [Test]
        public void test_reconnect_round_trips()
        {
            byte[] token = MakeToken(9);
            var decoded = ReadToZone(GatewayLinkCodec.Encode(new ReconnectMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), token)));

            Assert.AreEqual(GatewayLinkMessageType.Reconnect, decoded.Type);
            Assert.AreEqual(REQUEST_ID, decoded.Reconnect.RequestId);
            Assert.AreEqual(ACCOUNT_ID, decoded.Reconnect.AccountId.RawValue);
            CollectionAssert.AreEqual(token, decoded.Reconnect.Token);
        }

        [Test]
        public void test_decoded_tokens_are_independent_copies()
        {
            byte[] frame = GatewayLinkCodec.Encode(new ReconnectMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), MakeToken()));
            var decoded = ReadToZone(frame);
            frame[frame.Length - 1] ^= 0xFF;

            CollectionAssert.AreEqual(MakeToken(), decoded.Reconnect.Token);
        }

        // --------------------------------------------------------------- Sizes

        [Test]
        public void test_encoded_body_sizes_match_table()
        {
            byte[] token = MakeToken();
            var account = new AccountID(ACCOUNT_ID);

            AssertLengthField(GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, MakeAccounts(0))), GatewayLinkMessageType.RegisterZone, 11);
            AssertLengthField(GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, MakeAccounts(5))), GatewayLinkMessageType.RegisterZone, 11 + 4 * 5);
            AssertLengthField(GatewayLinkCodec.Encode(new PlayerCountMessage(1)), GatewayLinkMessageType.PlayerCount, 2);
            AssertLengthField(GatewayLinkCodec.EncodeZoneDraining(), GatewayLinkMessageType.ZoneDraining, 0);
            AssertLengthField(GatewayLinkCodec.Encode(new SessionReleasedMessage(account)), GatewayLinkMessageType.SessionReleased, 4);
            AssertLengthField(GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, true, AdmitRejectReason.None)), GatewayLinkMessageType.AdmitResult, 6);
            AssertLengthField(GatewayLinkCodec.Encode(new EvictResultMessage(REQUEST_ID)), GatewayLinkMessageType.EvictResult, 4);
            AssertLengthField(GatewayLinkCodec.Encode(new ReconnectResultMessage(REQUEST_ID, true, token)), GatewayLinkMessageType.ReconnectResult, 21);
            AssertLengthField(GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, account, new CharacterID(CHARACTER_ID), token)), GatewayLinkMessageType.Admit, 28);
            AssertLengthField(GatewayLinkCodec.Encode(new EvictMessage(REQUEST_ID, account)), GatewayLinkMessageType.Evict, 8);
            AssertLengthField(GatewayLinkCodec.Encode(new ReconnectMessage(REQUEST_ID, account, token)), GatewayLinkMessageType.Reconnect, 24);
        }

        [Test]
        public void test_message_type_values_match_table()
        {
            Assert.AreEqual(0x01, (byte)GatewayLinkMessageType.RegisterZone);
            Assert.AreEqual(0x02, (byte)GatewayLinkMessageType.PlayerCount);
            Assert.AreEqual(0x03, (byte)GatewayLinkMessageType.ZoneDraining);
            Assert.AreEqual(0x04, (byte)GatewayLinkMessageType.SessionReleased);
            Assert.AreEqual(0x05, (byte)GatewayLinkMessageType.AdmitResult);
            Assert.AreEqual(0x06, (byte)GatewayLinkMessageType.EvictResult);
            Assert.AreEqual(0x07, (byte)GatewayLinkMessageType.ReconnectResult);
            Assert.AreEqual(0x81, (byte)GatewayLinkMessageType.Admit);
            Assert.AreEqual(0x82, (byte)GatewayLinkMessageType.Evict);
            Assert.AreEqual(0x83, (byte)GatewayLinkMessageType.Reconnect);
        }

        // --------------------------------------------------------------- Partial frames

        [Test]
        public void test_frame_split_at_every_offset_needs_more_until_last_byte()
        {
            byte[] frame = GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), new CharacterID(CHARACTER_ID), MakeToken()));

            for (int length = 0; length < frame.Length; length++)
            {
                var partial = new ReadOnlySpan<byte>(frame, 0, length);
                var status = GatewayLinkCodec.TryReadFrame(partial, MAX_FRAME_BYTES, out _, out ReadOnlySpan<byte> body, out int consumed);
                Assert.AreEqual(GatewayLinkFrameStatus.NeedMoreBytes, status, "length " + length);
                Assert.AreEqual(0, consumed);
                Assert.AreEqual(0, body.Length);
            }

            var full = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> fullBody, out int fullConsumed);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, full);
            Assert.AreEqual((byte)GatewayLinkMessageType.Admit, type);
            Assert.AreEqual(frame.Length, fullConsumed);
            Assert.AreEqual(GatewayLinkCodec.ADMIT_BODY_BYTES, fullBody.Length);
        }

        [Test]
        public void test_empty_body_frame_split_at_every_offset_needs_more_until_last_byte()
        {
            byte[] frame = GatewayLinkCodec.EncodeZoneDraining();

            for (int length = 0; length < frame.Length; length++)
            {
                var status = GatewayLinkCodec.TryReadFrame(new ReadOnlySpan<byte>(frame, 0, length), MAX_FRAME_BYTES, out _, out _, out _);
                Assert.AreEqual(GatewayLinkFrameStatus.NeedMoreBytes, status, "length " + length);
            }

            Assert.AreEqual(GatewayLinkFrameStatus.Frame, GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out _, out _, out _));
        }

        [Test]
        public void test_two_concatenated_frames_yield_two_frames_in_order()
        {
            byte[] first = GatewayLinkCodec.Encode(new EvictMessage(1, new AccountID(ACCOUNT_ID)));
            byte[] second = GatewayLinkCodec.Encode(new ReconnectMessage(2, new AccountID(ACCOUNT_ID), MakeToken()));
            var both = new byte[first.Length + second.Length];
            first.CopyTo(both, 0);
            second.CopyTo(both, first.Length);

            var status1 = GatewayLinkCodec.TryReadFrame(both, MAX_FRAME_BYTES, out byte type1, out ReadOnlySpan<byte> body1, out int consumed1);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status1);
            Assert.AreEqual(first.Length, consumed1);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeGatewayToZone(type1, body1, out GatewayToZoneMessage m1));
            Assert.AreEqual(GatewayLinkMessageType.Evict, m1.Type);

            var rest = new ReadOnlySpan<byte>(both, consumed1, both.Length - consumed1);
            var status2 = GatewayLinkCodec.TryReadFrame(rest, MAX_FRAME_BYTES, out byte type2, out ReadOnlySpan<byte> body2, out int consumed2);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status2);
            Assert.AreEqual(second.Length, consumed2);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeGatewayToZone(type2, body2, out GatewayToZoneMessage m2));
            Assert.AreEqual(GatewayLinkMessageType.Reconnect, m2.Type);
        }

        [Test]
        public void test_frame_with_trailing_partial_header_consumes_only_first_frame()
        {
            byte[] first = GatewayLinkCodec.EncodeZoneDraining();
            var buffer = new byte[first.Length + 2];
            first.CopyTo(buffer, 0);

            var status = GatewayLinkCodec.TryReadFrame(buffer, MAX_FRAME_BYTES, out _, out _, out int consumed);

            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status);
            Assert.AreEqual(first.Length, consumed);
        }

        // --------------------------------------------------------------- Frame violations

        [Test]
        public void test_length_zero_is_violation()
        {
            var buffer = new byte[LENGTH_PREFIX];

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(buffer, MAX_FRAME_BYTES, out _, out _, out _));
        }

        [Test]
        public void test_length_equal_to_cap_is_not_a_violation_and_cap_plus_one_is()
        {
            byte[] atCap = BitConverter.GetBytes((uint)MAX_FRAME_BYTES);
            byte[] overCap = BitConverter.GetBytes((uint)MAX_FRAME_BYTES + 1);

            Assert.AreEqual(GatewayLinkFrameStatus.NeedMoreBytes, GatewayLinkCodec.TryReadFrame(atCap, MAX_FRAME_BYTES, out _, out _, out _));
            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(overCap, MAX_FRAME_BYTES, out _, out _, out _));
        }

        [Test]
        public void test_length_uint_max_is_violation()
        {
            byte[] buffer = BitConverter.GetBytes(uint.MaxValue);

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(buffer, MAX_FRAME_BYTES, out _, out _, out _));
        }

        [Test]
        public void test_cap_is_a_parameter_not_a_constant()
        {
            byte[] frame = GatewayLinkCodec.Encode(new PlayerCountMessage(1));
            int smallCap = frame.Length - LENGTH_PREFIX - 1;

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(frame, smallCap, out _, out _, out _));
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, GatewayLinkCodec.TryReadFrame(frame, smallCap + 1, out _, out _, out _));
        }

        [Test]
        public void test_unknown_type_is_violation()
        {
            byte[] frame = RawByte(UNKNOWN_TYPE, 2);

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out _, out _, out _));
            Assert.IsTrue(ZoneDecodeFails(frame));
            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_unknown_type_decoders_reject_directly()
        {
            Assert.IsFalse(GatewayLinkCodec.TryDecodeGatewayToZone(UNKNOWN_TYPE, new byte[0], out _));
            Assert.IsFalse(GatewayLinkCodec.TryDecodeZoneToGateway(UNKNOWN_TYPE, new byte[0], out _));
        }

        [Test]
        public void test_player_count_with_three_byte_body_is_violation()
        {
            Assert.IsTrue(GatewayDecodeFails(Raw(GatewayLinkMessageType.PlayerCount, 3)));
        }

        [Test]
        public void test_admit_with_fifteen_byte_token_is_violation()
        {
            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Admit, GatewayLinkCodec.ADMIT_BODY_BYTES - 1)));
        }

        [Test]
        public void test_every_wrong_body_length_is_violation()
        {
            var types = new[]
            {
                GatewayLinkMessageType.PlayerCount, GatewayLinkMessageType.ZoneDraining, GatewayLinkMessageType.SessionReleased,
                GatewayLinkMessageType.AdmitResult, GatewayLinkMessageType.EvictResult, GatewayLinkMessageType.ReconnectResult,
            };
            var sizes = new[]
            {
                GatewayLinkCodec.PLAYER_COUNT_BODY_BYTES, 0, GatewayLinkCodec.SESSION_RELEASED_BODY_BYTES,
                GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES, GatewayLinkCodec.EVICT_RESULT_BODY_BYTES, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES,
            };

            for (int i = 0; i < types.Length; i++)
            {
                Assert.IsTrue(GatewayDecodeFails(Raw(types[i], sizes[i] + 1)), types[i] + " one byte long");
                if (sizes[i] > 0)
                {
                    Assert.IsTrue(GatewayDecodeFails(Raw(types[i], sizes[i] - 1)), types[i] + " one byte short");
                }
            }

            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Admit, GatewayLinkCodec.ADMIT_BODY_BYTES + 1)));
            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Evict, GatewayLinkCodec.EVICT_BODY_BYTES - 1)));
            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Evict, GatewayLinkCodec.EVICT_BODY_BYTES + 1)));
            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Reconnect, GatewayLinkCodec.RECONNECT_BODY_BYTES - 1)));
            Assert.IsTrue(ZoneDecodeFails(Raw(GatewayLinkMessageType.Reconnect, GatewayLinkCodec.RECONNECT_BODY_BYTES + 1)));
        }

        [Test]
        public void test_register_zone_with_bad_count_or_length_is_violation()
        {
            Assert.IsTrue(GatewayDecodeFails(Raw(GatewayLinkMessageType.RegisterZone, 10)));

            // Declared liveCount = 2 but only one account's bytes present.
            byte[] shortFrame = Raw(GatewayLinkMessageType.RegisterZone, 11 + 4);
            shortFrame[LENGTH_PREFIX + 1 + 9] = 2;
            Assert.IsTrue(GatewayDecodeFails(shortFrame));

            // Declared liveCount = 0 but trailing account bytes present.
            Assert.IsTrue(GatewayDecodeFails(Raw(GatewayLinkMessageType.RegisterZone, 11 + 4)));

            // Body length not a multiple of the account size.
            Assert.IsTrue(GatewayDecodeFails(Raw(GatewayLinkMessageType.RegisterZone, 11 + 2)));
        }

        // --------------------------------------------------------------- Direction and flag checks

        [Test]
        public void test_zone_decoder_rejects_zone_to_gateway_types()
        {
            Assert.IsTrue(ZoneDecodeFails(GatewayLinkCodec.Encode(new PlayerCountMessage(1))));
            Assert.IsTrue(ZoneDecodeFails(GatewayLinkCodec.EncodeZoneDraining()));
            Assert.IsTrue(ZoneDecodeFails(GatewayLinkCodec.Encode(new EvictResultMessage(REQUEST_ID))));
            Assert.IsTrue(ZoneDecodeFails(GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, true, AdmitRejectReason.None))));
        }

        [Test]
        public void test_gateway_decoder_rejects_gateway_to_zone_types()
        {
            Assert.IsTrue(GatewayDecodeFails(GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), new CharacterID(CHARACTER_ID), MakeToken()))));
            Assert.IsTrue(GatewayDecodeFails(GatewayLinkCodec.Encode(new EvictMessage(REQUEST_ID, new AccountID(ACCOUNT_ID)))));
            Assert.IsTrue(GatewayDecodeFails(GatewayLinkCodec.Encode(new ReconnectMessage(REQUEST_ID, new AccountID(ACCOUNT_ID), MakeToken()))));
        }

        [Test]
        public void test_admit_result_accepted_byte_two_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = BAD_FLAG;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_admit_result_accepted_with_reason_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = 1;
            frame[LENGTH_PREFIX + 1 + 5] = (byte)AdmitRejectReason.Full;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_admit_result_rejected_with_none_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = 0;
            frame[LENGTH_PREFIX + 1 + 5] = (byte)AdmitRejectReason.None;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_reconnect_result_accepted_byte_two_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = BAD_FLAG;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_reconnect_result_rejected_with_nonzero_token_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = 0;
            frame[frame.Length - 1] = 1;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_reconnect_result_accepted_with_zero_token_decodes()
        {
            byte[] frame = Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = 1;

            Assert.IsFalse(GatewayDecodeFails(frame));
        }

        // --------------------------------------------------------------- Encoder argument checks

        [Test]
        public void test_encode_with_wrong_length_token_throws()
        {
            var account = new AccountID(ACCOUNT_ID);

            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, account, new CharacterID(CHARACTER_ID), new byte[TOKEN_LENGTH - 1])));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitMessage(REQUEST_ID, account, new CharacterID(CHARACTER_ID), null)));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new ReconnectMessage(REQUEST_ID, account, new byte[TOKEN_LENGTH + 1])));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new ReconnectResultMessage(REQUEST_ID, true, new byte[TOKEN_LENGTH - 1])));
        }

        [Test]
        public void test_encode_inconsistent_results_throws()
        {
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, true, AdmitRejectReason.Full)));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, false, AdmitRejectReason.None)));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new ReconnectResultMessage(REQUEST_ID, false, MakeToken())));
        }

        [Test]
        public void test_encode_register_zone_with_too_many_accounts_or_null_throws()
        {
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, MakeAccounts(ushort.MaxValue + 1))));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, null)));
        }

        // --------------------------------------------------------------- No exceptions on malformed input

        [Test]
        public void test_decoders_never_throw_for_any_type_and_body_length()
        {
            for (int type = 0; type <= byte.MaxValue; type++)
            {
                for (int length = 0; length <= 40; length++)
                {
                    foreach (byte fill in new byte[] { 0, 1, 0xFF })
                    {
                        var body = new byte[length];
                        for (int i = 0; i < length; i++)
                        {
                            body[i] = fill;
                        }

                        Assert.DoesNotThrow(() =>
                        {
                            GatewayLinkCodec.TryDecodeGatewayToZone((byte)type, body, out _);
                            GatewayLinkCodec.TryDecodeZoneToGateway((byte)type, body, out _);
                        });
                    }
                }
            }
        }

        [Test]
        public void test_failed_decode_leaves_default_message_on_every_failure_path()
        {
            foreach (byte[] frame in FailingZoneToGatewayFrames())
            {
                GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out _);
                Assert.IsFalse(GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message), "type " + type);
                Assert.AreEqual(default(GatewayLinkMessageType), message.Type);
                Assert.IsNull(message.RegisterZone.LiveAccounts);
                Assert.IsNull(message.ReconnectResult.NewToken);
            }

            foreach (byte[] frame in FailingGatewayToZoneFrames())
            {
                GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out _);
                Assert.IsFalse(GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message), "type " + type);
                Assert.AreEqual(default(GatewayLinkMessageType), message.Type);
                Assert.IsNull(message.Admit.Token);
                Assert.IsNull(message.Reconnect.Token);
            }
        }

        private static byte[][] FailingZoneToGatewayFrames()
        {
            byte[] badFlag = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            badFlag[LENGTH_PREFIX + 1 + 4] = BAD_FLAG;
            byte[] acceptedWithReason = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            acceptedWithReason[LENGTH_PREFIX + 1 + 4] = 1;
            acceptedWithReason[LENGTH_PREFIX + 1 + 5] = (byte)AdmitRejectReason.Full;
            byte[] rejectedWithNone = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            byte[] undefinedReason = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            undefinedReason[LENGTH_PREFIX + 1 + 5] = UNDEFINED_REASON;
            byte[] reconnectBadFlag = Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES);
            reconnectBadFlag[LENGTH_PREFIX + 1 + 4] = BAD_FLAG;
            byte[] rejectedNonZeroToken = Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES);
            rejectedNonZeroToken[rejectedNonZeroToken.Length - 1] = 1;
            byte[] registerCountMismatch = Raw(GatewayLinkMessageType.RegisterZone, GatewayLinkCodec.REGISTER_ZONE_FIXED_BODY_BYTES);
            registerCountMismatch[LENGTH_PREFIX + 1 + 9] = 1;

            return new[]
            {
                Raw(GatewayLinkMessageType.RegisterZone, GatewayLinkCodec.REGISTER_ZONE_FIXED_BODY_BYTES - 1),
                registerCountMismatch,
                Raw(GatewayLinkMessageType.PlayerCount, GatewayLinkCodec.PLAYER_COUNT_BODY_BYTES + 1),
                Raw(GatewayLinkMessageType.ZoneDraining, 1),
                Raw(GatewayLinkMessageType.SessionReleased, GatewayLinkCodec.SESSION_RELEASED_BODY_BYTES - 1),
                Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES + 1),
                badFlag,
                acceptedWithReason,
                rejectedWithNone,
                undefinedReason,
                Raw(GatewayLinkMessageType.EvictResult, GatewayLinkCodec.EVICT_RESULT_BODY_BYTES + 1),
                Raw(GatewayLinkMessageType.ReconnectResult, GatewayLinkCodec.RECONNECT_RESULT_BODY_BYTES - 1),
                reconnectBadFlag,
                rejectedNonZeroToken,
                Raw(GatewayLinkMessageType.Admit, GatewayLinkCodec.ADMIT_BODY_BYTES),
            };
        }

        private static byte[][] FailingGatewayToZoneFrames()
        {
            return new[]
            {
                Raw(GatewayLinkMessageType.Admit, GatewayLinkCodec.ADMIT_BODY_BYTES - 1),
                Raw(GatewayLinkMessageType.Evict, GatewayLinkCodec.EVICT_BODY_BYTES + 1),
                Raw(GatewayLinkMessageType.Reconnect, GatewayLinkCodec.RECONNECT_BODY_BYTES - 1),
                Raw(GatewayLinkMessageType.PlayerCount, GatewayLinkCodec.PLAYER_COUNT_BODY_BYTES),
            };
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void test_non_positive_cap_rejects_even_a_valid_frame(int cap)
        {
            byte[] frame = GatewayLinkCodec.Encode(new PlayerCountMessage(1));

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(frame, cap, out _, out _, out _));
            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(new byte[1], cap, out _, out _, out _));
        }

        [TestCase((byte)4)]
        [TestCase(UNDEFINED_REASON)]
        [TestCase(byte.MaxValue)]
        public void test_admit_result_undefined_reason_is_violation(byte reason)
        {
            byte[] frame = Raw(GatewayLinkMessageType.AdmitResult, GatewayLinkCodec.ADMIT_RESULT_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 4] = 0;
            frame[LENGTH_PREFIX + 1 + 5] = reason;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_encode_admit_result_with_undefined_reason_throws()
        {
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, false, (AdmitRejectReason)UNDEFINED_REASON)));
            Assert.Throws<ArgumentException>(() => GatewayLinkCodec.Encode(new AdmitResultMessage(REQUEST_ID, false, (AdmitRejectReason)byte.MaxValue)));
        }

        [Test]
        public void test_unknown_type_in_partial_frame_is_violation()
        {
            byte[] frame = RawByte(UNKNOWN_TYPE, 10);
            var partial = new ReadOnlySpan<byte>(frame, 0, LENGTH_PREFIX + 1);

            Assert.AreEqual(GatewayLinkFrameStatus.ProtocolViolation, GatewayLinkCodec.TryReadFrame(partial, MAX_FRAME_BYTES, out _, out _, out _));
        }

        [Test]
        public void test_complete_frame_with_length_equal_to_cap_is_a_frame()
        {
            byte[] frame = Raw(GatewayLinkMessageType.RegisterZone, MAX_FRAME_BYTES - 1);

            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out _, out ReadOnlySpan<byte> body, out int consumed);

            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status);
            Assert.AreEqual(LENGTH_PREFIX + MAX_FRAME_BYTES, consumed);
            Assert.AreEqual(MAX_FRAME_BYTES - 1, body.Length);
        }

        [Test]
        public void test_register_zone_declaring_max_count_with_short_body_is_violation()
        {
            byte[] frame = Raw(GatewayLinkMessageType.RegisterZone, GatewayLinkCodec.REGISTER_ZONE_FIXED_BODY_BYTES);
            frame[LENGTH_PREFIX + 1 + 9] = 0xFF;
            frame[LENGTH_PREFIX + 1 + 10] = 0xFF;

            Assert.IsTrue(GatewayDecodeFails(frame));
        }

        [Test]
        public void test_reconnect_with_all_zero_token_round_trips()
        {
            byte[] zeros = new byte[TOKEN_LENGTH];
            var decoded = ReadToZone(GatewayLinkCodec.Encode(new ReconnectMessage(uint.MaxValue, new AccountID(0), zeros))).Reconnect;

            Assert.AreEqual(uint.MaxValue, decoded.RequestId);
            Assert.AreEqual(0u, decoded.AccountId.RawValue);
            CollectionAssert.AreEqual(zeros, decoded.Token);
        }

        [Test]
        public void test_read_frame_never_throws_for_every_prefix_of_arbitrary_bytes()
        {
            var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x00, 0x81, 0x05, 0x00, 0x00, 0x00, 0x02 };

            for (int length = 0; length <= bytes.Length; length++)
            {
                int n = length;
                Assert.DoesNotThrow(() => GatewayLinkCodec.TryReadFrame(new ReadOnlySpan<byte>(bytes, 0, n), MAX_FRAME_BYTES, out _, out _, out _));
            }
        }
    }
}

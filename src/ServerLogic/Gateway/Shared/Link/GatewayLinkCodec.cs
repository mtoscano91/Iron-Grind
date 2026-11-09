using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.Gateway.Link
{
    /// <summary>
    /// Pure byte encode/decode of the gateway link frame (Gateway Story 001, ADR-016 Decision 3). A frame is a
    /// <c>uint32</c> little-endian length, a <c>byte</c> message type, then the payload. The length field counts
    /// the type byte plus the payload (so it is always 1 + payload length, and never 0). Integers are
    /// little-endian; ids are their raw <c>uint32</c>. Encoders throw <see cref="ArgumentException"/> on invalid
    /// arguments (our own code); readers and decoders never throw and report malformed input as a violation.
    /// The frame size cap is a parameter (config value <c>GATEWAY_LINK_MAX_FRAME_BYTES</c>), never hardcoded here.
    /// </summary>
    public static class GatewayLinkCodec
    {
        /// <summary>Bytes in the length prefix.</summary>
        public const int LENGTH_PREFIX_BYTES = 4;

        /// <summary>Exact length of every token field.</summary>
        public const int TOKEN_LENGTH = 16;

        /// <summary>Body bytes of a RegisterZone with no live accounts.</summary>
        public const int REGISTER_ZONE_FIXED_BODY_BYTES = 11;

        /// <summary>Body bytes per live account in RegisterZone.</summary>
        public const int REGISTER_ZONE_ACCOUNT_BYTES = 4;

        /// <summary>Body bytes of PlayerCount.</summary>
        public const int PLAYER_COUNT_BODY_BYTES = 2;

        /// <summary>Body bytes of SessionReleased.</summary>
        public const int SESSION_RELEASED_BODY_BYTES = 4;

        /// <summary>Body bytes of AdmitResult.</summary>
        public const int ADMIT_RESULT_BODY_BYTES = 6;

        /// <summary>Body bytes of EvictResult.</summary>
        public const int EVICT_RESULT_BODY_BYTES = 4;

        /// <summary>Body bytes of ReconnectResult.</summary>
        public const int RECONNECT_RESULT_BODY_BYTES = 21;

        /// <summary>Body bytes of Admit.</summary>
        public const int ADMIT_BODY_BYTES = 28;

        /// <summary>Body bytes of Evict.</summary>
        public const int EVICT_BODY_BYTES = 8;

        /// <summary>Body bytes of Reconnect.</summary>
        public const int RECONNECT_BODY_BYTES = 24;

        // ---------------------------------------------------------------- Encode

        /// <summary>
        /// Encodes a full RegisterZone frame (length prefix, type, body). More than 16 381 accounts gives a frame over
        /// the 65 536 GATEWAY_LINK_MAX_FRAME_BYTES cap, which the peer rejects (the encoder does not know the cap).
        /// </summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        /// <exception cref="ArgumentException">LiveAccounts is null or holds more than 65535 accounts.</exception>
        public static byte[] Encode(RegisterZoneMessage value)
        {
            AccountID[] accounts = value.LiveAccounts;
            if (accounts == null || accounts.Length > ushort.MaxValue)
            {
                throw new ArgumentException("LiveAccounts must be non-null and at most 65535 entries.", nameof(value));
            }

            byte[] frame = NewFrame(
                GatewayLinkMessageType.RegisterZone,
                REGISTER_ZONE_FIXED_BODY_BYTES + REGISTER_ZONE_ACCOUNT_BYTES * accounts.Length);
            int p = LENGTH_PREFIX_BYTES + 1;
            p = Put32(frame, p, value.ZoneId.RawValue);
            frame[p++] = value.TemplateId;
            p = Put16(frame, p, value.UdpPort);
            p = Put16(frame, p, value.Capacity);
            p = Put16(frame, p, (ushort)accounts.Length);
            for (int i = 0; i < accounts.Length; i++)
            {
                p = Put32(frame, p, accounts[i].RawValue);
            }

            return frame;
        }

        /// <summary>Encodes a full PlayerCount frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        public static byte[] Encode(PlayerCountMessage value)
        {
            byte[] frame = NewFrame(GatewayLinkMessageType.PlayerCount, PLAYER_COUNT_BODY_BYTES);
            Put16(frame, LENGTH_PREFIX_BYTES + 1, value.Count);
            return frame;
        }

        /// <summary>Encodes a full ZoneDraining frame (empty body).</summary>
        /// <returns>The frame bytes.</returns>
        public static byte[] EncodeZoneDraining()
        {
            return NewFrame(GatewayLinkMessageType.ZoneDraining, 0);
        }

        /// <summary>Encodes a full SessionReleased frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        public static byte[] Encode(SessionReleasedMessage value)
        {
            byte[] frame = NewFrame(GatewayLinkMessageType.SessionReleased, SESSION_RELEASED_BODY_BYTES);
            Put32(frame, LENGTH_PREFIX_BYTES + 1, value.AccountId.RawValue);
            return frame;
        }

        /// <summary>Encodes a full AdmitResult frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        /// <exception cref="ArgumentException">The reason is not None if and only if accepted.</exception>
        public static byte[] Encode(AdmitResultMessage value)
        {
            if (value.Accepted != (value.Reason == AdmitRejectReason.None) || !IsDefinedReason((byte)value.Reason))
            {
                throw new ArgumentException("Reason must be a defined AdmitRejectReason, None if and only if accepted.", nameof(value));
            }

            byte[] frame = NewFrame(GatewayLinkMessageType.AdmitResult, ADMIT_RESULT_BODY_BYTES);
            int p = Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            frame[p++] = value.Accepted ? (byte)1 : (byte)0;
            frame[p] = (byte)value.Reason;
            return frame;
        }

        /// <summary>Encodes a full EvictResult frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        public static byte[] Encode(EvictResultMessage value)
        {
            byte[] frame = NewFrame(GatewayLinkMessageType.EvictResult, EVICT_RESULT_BODY_BYTES);
            Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            return frame;
        }

        /// <summary>Encodes a full ReconnectResult frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        /// <exception cref="ArgumentException">The token is not 16 bytes, or is non-zero while rejected.</exception>
        public static byte[] Encode(ReconnectResultMessage value)
        {
            RequireToken(value.NewToken, nameof(value));
            if (!value.Accepted && !IsAllZero(value.NewToken))
            {
                throw new ArgumentException("A rejected ReconnectResult must carry an all-zero token.", nameof(value));
            }

            byte[] frame = NewFrame(GatewayLinkMessageType.ReconnectResult, RECONNECT_RESULT_BODY_BYTES);
            int p = Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            frame[p++] = value.Accepted ? (byte)1 : (byte)0;
            Buffer.BlockCopy(value.NewToken, 0, frame, p, TOKEN_LENGTH);
            return frame;
        }

        /// <summary>Encodes a full Admit frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        /// <exception cref="ArgumentException">The token is not 16 bytes.</exception>
        public static byte[] Encode(AdmitMessage value)
        {
            RequireToken(value.Token, nameof(value));
            byte[] frame = NewFrame(GatewayLinkMessageType.Admit, ADMIT_BODY_BYTES);
            int p = Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            p = Put32(frame, p, value.AccountId.RawValue);
            p = Put32(frame, p, value.CharacterId.RawValue);
            Buffer.BlockCopy(value.Token, 0, frame, p, TOKEN_LENGTH);
            return frame;
        }

        /// <summary>Encodes a full Evict frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        public static byte[] Encode(EvictMessage value)
        {
            byte[] frame = NewFrame(GatewayLinkMessageType.Evict, EVICT_BODY_BYTES);
            int p = Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            Put32(frame, p, value.AccountId.RawValue);
            return frame;
        }

        /// <summary>Encodes a full Reconnect frame.</summary>
        /// <param name="value">The message.</param>
        /// <returns>The frame bytes.</returns>
        /// <exception cref="ArgumentException">The token is not 16 bytes.</exception>
        public static byte[] Encode(ReconnectMessage value)
        {
            RequireToken(value.Token, nameof(value));
            byte[] frame = NewFrame(GatewayLinkMessageType.Reconnect, RECONNECT_BODY_BYTES);
            int p = Put32(frame, LENGTH_PREFIX_BYTES + 1, value.RequestId);
            p = Put32(frame, p, value.AccountId.RawValue);
            Buffer.BlockCopy(value.Token, 0, frame, p, TOKEN_LENGTH);
            return frame;
        }

        // ---------------------------------------------------------------- Frame reading

        /// <summary>
        /// Reads one frame from the start of <paramref name="buffer"/>. The declared length counts the type byte
        /// plus the payload. A length of 0 or above <paramref name="maxFrameBytes"/>, or an unknown type byte,
        /// is a <see cref="GatewayLinkFrameStatus.ProtocolViolation"/> (reported as soon as the bytes that
        /// prove it are present). A <paramref name="maxFrameBytes"/> below 1 (misconfiguration) rejects every
        /// buffer as a violation. Never throws. Wrong-direction types are not rejected here; the direction
        /// decoders do that.
        /// </summary>
        /// <param name="buffer">Bytes received so far.</param>
        /// <param name="maxFrameBytes">Largest accepted length field (config GATEWAY_LINK_MAX_FRAME_BYTES).</param>
        /// <param name="messageType">The raw type byte when a frame was read, otherwise 0.</param>
        /// <param name="body">The payload (a slice of <paramref name="buffer"/>) when a frame was read, otherwise empty.</param>
        /// <param name="bytesConsumed">Bytes of <paramref name="buffer"/> the frame occupied (4 + length), otherwise 0.</param>
        /// <returns>The read status.</returns>
        public static GatewayLinkFrameStatus TryReadFrame(
            ReadOnlySpan<byte> buffer,
            int maxFrameBytes,
            out byte messageType,
            out ReadOnlySpan<byte> body,
            out int bytesConsumed)
        {
            messageType = 0;
            body = ReadOnlySpan<byte>.Empty;
            bytesConsumed = 0;

            if (maxFrameBytes < 1)
            {
                return GatewayLinkFrameStatus.ProtocolViolation;
            }

            if (buffer.Length < LENGTH_PREFIX_BYTES)
            {
                return GatewayLinkFrameStatus.NeedMoreBytes;
            }

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            if (length == 0 || length > (uint)maxFrameBytes)
            {
                return GatewayLinkFrameStatus.ProtocolViolation;
            }

            if (buffer.Length > LENGTH_PREFIX_BYTES && !IsKnownType(buffer[LENGTH_PREFIX_BYTES]))
            {
                return GatewayLinkFrameStatus.ProtocolViolation;
            }

            long total = (long)LENGTH_PREFIX_BYTES + length;
            if (buffer.Length < total)
            {
                return GatewayLinkFrameStatus.NeedMoreBytes;
            }

            messageType = buffer[LENGTH_PREFIX_BYTES];
            body = buffer.Slice(LENGTH_PREFIX_BYTES + 1, (int)length - 1);
            bytesConsumed = (int)total;
            return GatewayLinkFrameStatus.Frame;
        }

        // ---------------------------------------------------------------- Decode

        /// <summary>
        /// Zone-side decoder: accepts only gateway to zone types 0x81-0x83. Validates the exact body length.
        /// Never throws; any other type or a malformed body returns false.
        /// </summary>
        /// <param name="messageType">The raw type byte from <see cref="TryReadFrame"/>.</param>
        /// <param name="body">The payload from <see cref="TryReadFrame"/>.</param>
        /// <param name="message">The decoded message (tokens are copied), or default on failure.</param>
        /// <returns>True if the message is valid; false is a protocol violation.</returns>
        public static bool TryDecodeGatewayToZone(byte messageType, ReadOnlySpan<byte> body, out GatewayToZoneMessage message)
        {
            switch ((GatewayLinkMessageType)messageType)
            {
                case GatewayLinkMessageType.Admit:
                    return TryDecodeAdmit(body, out message);
                case GatewayLinkMessageType.Evict:
                    return TryDecodeEvict(body, out message);
                case GatewayLinkMessageType.Reconnect:
                    return TryDecodeReconnect(body, out message);
                default:
                    message = default;
                    return false;
            }
        }

        /// <summary>
        /// Gateway-side decoder: accepts only zone to gateway types 0x01-0x07. Validates the exact body length,
        /// that flag bytes are 0 or 1, that an AdmitResult reason is a defined <see cref="AdmitRejectReason"/>
        /// that is None if and only if accepted, and that a rejected ReconnectResult carries an all-zero token.
        /// Never throws; failures return false.
        /// </summary>
        /// <param name="messageType">The raw type byte from <see cref="TryReadFrame"/>.</param>
        /// <param name="body">The payload from <see cref="TryReadFrame"/>.</param>
        /// <param name="message">The decoded message (tokens and account lists are copied), or default on failure.</param>
        /// <returns>True if the message is valid; false is a protocol violation.</returns>
        public static bool TryDecodeZoneToGateway(byte messageType, ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            switch ((GatewayLinkMessageType)messageType)
            {
                case GatewayLinkMessageType.RegisterZone:
                    return TryDecodeRegisterZone(body, out message);
                case GatewayLinkMessageType.PlayerCount:
                    return TryDecodePlayerCount(body, out message);
                case GatewayLinkMessageType.ZoneDraining:
                    return TryDecodeZoneDraining(body, out message);
                case GatewayLinkMessageType.SessionReleased:
                    return TryDecodeSessionReleased(body, out message);
                case GatewayLinkMessageType.AdmitResult:
                    return TryDecodeAdmitResult(body, out message);
                case GatewayLinkMessageType.EvictResult:
                    return TryDecodeEvictResult(body, out message);
                case GatewayLinkMessageType.ReconnectResult:
                    return TryDecodeReconnectResult(body, out message);
                default:
                    message = default;
                    return false;
            }
        }

        private static bool TryDecodeAdmit(ReadOnlySpan<byte> body, out GatewayToZoneMessage message)
        {
            message = default;
            if (body.Length != ADMIT_BODY_BYTES)
            {
                return false;
            }

            var admit = new AdmitMessage(
                Get32(body, 0),
                new AccountID(Get32(body, 4)),
                new CharacterID(Get32(body, 8)),
                body.Slice(12, TOKEN_LENGTH).ToArray());
            message = new GatewayToZoneMessage(GatewayLinkMessageType.Admit, admit, default, default);
            return true;
        }

        private static bool TryDecodeEvict(ReadOnlySpan<byte> body, out GatewayToZoneMessage message)
        {
            message = default;
            if (body.Length != EVICT_BODY_BYTES)
            {
                return false;
            }

            var evict = new EvictMessage(Get32(body, 0), new AccountID(Get32(body, 4)));
            message = new GatewayToZoneMessage(GatewayLinkMessageType.Evict, default, evict, default);
            return true;
        }

        private static bool TryDecodeReconnect(ReadOnlySpan<byte> body, out GatewayToZoneMessage message)
        {
            message = default;
            if (body.Length != RECONNECT_BODY_BYTES)
            {
                return false;
            }

            var reconnect = new ReconnectMessage(
                Get32(body, 0),
                new AccountID(Get32(body, 4)),
                body.Slice(8, TOKEN_LENGTH).ToArray());
            message = new GatewayToZoneMessage(GatewayLinkMessageType.Reconnect, default, default, reconnect);
            return true;
        }

        private static bool TryDecodeRegisterZone(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length < REGISTER_ZONE_FIXED_BODY_BYTES)
            {
                return false;
            }

            int count = Get16(body, 9);
            if (body.Length != REGISTER_ZONE_FIXED_BODY_BYTES + REGISTER_ZONE_ACCOUNT_BYTES * count)
            {
                return false;
            }

            var accounts = new AccountID[count];
            for (int i = 0; i < count; i++)
            {
                accounts[i] = new AccountID(Get32(body, REGISTER_ZONE_FIXED_BODY_BYTES + REGISTER_ZONE_ACCOUNT_BYTES * i));
            }

            message = Wrap(
                GatewayLinkMessageType.RegisterZone,
                registerZone: new RegisterZoneMessage(new ZoneID(Get32(body, 0)), body[4], Get16(body, 5), Get16(body, 7), accounts));
            return true;
        }

        private static bool TryDecodePlayerCount(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != PLAYER_COUNT_BODY_BYTES)
            {
                return false;
            }

            message = Wrap(GatewayLinkMessageType.PlayerCount, playerCount: new PlayerCountMessage(Get16(body, 0)));
            return true;
        }

        private static bool TryDecodeZoneDraining(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != 0)
            {
                return false;
            }

            message = Wrap(GatewayLinkMessageType.ZoneDraining);
            return true;
        }

        private static bool TryDecodeSessionReleased(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != SESSION_RELEASED_BODY_BYTES)
            {
                return false;
            }

            message = Wrap(
                GatewayLinkMessageType.SessionReleased,
                sessionReleased: new SessionReleasedMessage(new AccountID(Get32(body, 0))));
            return true;
        }

        private static bool TryDecodeAdmitResult(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != ADMIT_RESULT_BODY_BYTES || body[4] > 1 || !IsDefinedReason(body[5]))
            {
                return false;
            }

            bool accepted = body[4] == 1;
            var reason = (AdmitRejectReason)body[5];
            if (accepted != (reason == AdmitRejectReason.None))
            {
                return false;
            }

            message = Wrap(GatewayLinkMessageType.AdmitResult, admitResult: new AdmitResultMessage(Get32(body, 0), accepted, reason));
            return true;
        }

        private static bool TryDecodeEvictResult(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != EVICT_RESULT_BODY_BYTES)
            {
                return false;
            }

            message = Wrap(GatewayLinkMessageType.EvictResult, evictResult: new EvictResultMessage(Get32(body, 0)));
            return true;
        }

        private static bool TryDecodeReconnectResult(ReadOnlySpan<byte> body, out ZoneToGatewayMessage message)
        {
            message = default;
            if (body.Length != RECONNECT_RESULT_BODY_BYTES || body[4] > 1)
            {
                return false;
            }

            bool accepted = body[4] == 1;
            byte[] token = body.Slice(5, TOKEN_LENGTH).ToArray();
            if (!accepted && !IsAllZero(token))
            {
                return false;
            }

            message = Wrap(
                GatewayLinkMessageType.ReconnectResult,
                reconnectResult: new ReconnectResultMessage(Get32(body, 0), accepted, token));
            return true;
        }

        private static bool IsDefinedReason(byte value)
        {
            return value <= (byte)AdmitRejectReason.InvalidToken;
        }

        // ---------------------------------------------------------------- Helpers

        private static ZoneToGatewayMessage Wrap(
            GatewayLinkMessageType type,
            RegisterZoneMessage registerZone = default,
            PlayerCountMessage playerCount = default,
            SessionReleasedMessage sessionReleased = default,
            AdmitResultMessage admitResult = default,
            EvictResultMessage evictResult = default,
            ReconnectResultMessage reconnectResult = default)
        {
            return new ZoneToGatewayMessage(type, registerZone, playerCount, sessionReleased, admitResult, evictResult, reconnectResult);
        }

        private static bool IsKnownType(byte value)
        {
            switch ((GatewayLinkMessageType)value)
            {
                case GatewayLinkMessageType.RegisterZone:
                case GatewayLinkMessageType.PlayerCount:
                case GatewayLinkMessageType.ZoneDraining:
                case GatewayLinkMessageType.SessionReleased:
                case GatewayLinkMessageType.AdmitResult:
                case GatewayLinkMessageType.EvictResult:
                case GatewayLinkMessageType.ReconnectResult:
                case GatewayLinkMessageType.Admit:
                case GatewayLinkMessageType.Evict:
                case GatewayLinkMessageType.Reconnect:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAllZero(byte[] token)
        {
            for (int i = 0; i < token.Length; i++)
            {
                if (token[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void RequireToken(byte[] token, string paramName)
        {
            if (token == null || token.Length != TOKEN_LENGTH)
            {
                throw new ArgumentException("Token must be exactly " + TOKEN_LENGTH + " bytes.", paramName);
            }
        }

        private static byte[] NewFrame(GatewayLinkMessageType type, int bodyBytes)
        {
            var frame = new byte[LENGTH_PREFIX_BYTES + 1 + bodyBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(1 + bodyBytes));
            frame[LENGTH_PREFIX_BYTES] = (byte)type;
            return frame;
        }

        private static int Put16(byte[] target, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(target, offset, 2), value);
            return offset + 2;
        }

        private static int Put32(byte[] target, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(target, offset, 4), value);
            return offset + 4;
        }

        private static ushort Get16(ReadOnlySpan<byte> data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        }

        private static uint Get32(ReadOnlySpan<byte> data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
        }
    }
}

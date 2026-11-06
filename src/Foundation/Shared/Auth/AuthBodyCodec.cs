using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Authentication
{
    /// <summary>
    /// Encodes and decodes the four authentication HTTPS bodies (auth-wire-messages.md,
    /// ADR-016 Decision 2). Little-endian integers, bool as one byte (0/1), strings as a
    /// ushort byte count plus UTF-8 bytes (max 255 bytes). No header or message id.
    /// Encoders throw on invalid arguments; decoders never throw and return false on malformed input.
    /// Enum bytes (error codes) are decoded as-is with no range check, except that a
    /// <see cref="LoginResponse"/> must pair success with <see cref="AuthFailureReason.None"/> and
    /// failure with any other reason (auth-wire-messages.md: "None=0 on success").
    /// </summary>
    public static class AuthBodyCodec
    {
        /// <summary>Exact length of the token field and session token.</summary>
        public const int TOKEN_LENGTH = 16;

        /// <summary>Maximum UTF-8 byte length of an encoded string.</summary>
        public const int MAX_STRING_BYTES = 255;

        /// <summary>Encoded length of a successful <see cref="LoginResponse"/>.</summary>
        public const int LOGIN_SUCCESS_LENGTH = 28;

        // ---------------------------------------------------------------- Encode

        /// <summary>Encodes a <see cref="ConnectionRequest"/>.</summary>
        /// <param name="value">The request.</param>
        /// <returns>The body bytes.</returns>
        /// <exception cref="ArgumentNullException">A string is null.</exception>
        /// <exception cref="ArgumentException">A string exceeds 255 UTF-8 bytes, or the token is not exactly 16 bytes.</exception>
        public static byte[] Encode(ConnectionRequest value)
        {
            var buffer = new List<byte>();
            WriteString(buffer, value.Username, nameof(value.Username));
            WriteString(buffer, value.Password, nameof(value.Password));
            WriteToken(buffer, value.TokenField, nameof(value.TokenField));
            return buffer.ToArray();
        }

        /// <summary>Encodes a <see cref="RegistrationRequest"/>.</summary>
        /// <param name="value">The request.</param>
        /// <returns>The body bytes.</returns>
        /// <exception cref="ArgumentNullException">A string is null.</exception>
        /// <exception cref="ArgumentException">A string exceeds 255 UTF-8 bytes.</exception>
        public static byte[] Encode(RegistrationRequest value)
        {
            var buffer = new List<byte>();
            WriteString(buffer, value.Username, nameof(value.Username));
            WriteString(buffer, value.Password, nameof(value.Password));
            WriteString(buffer, value.CharacterName, nameof(value.CharacterName));
            buffer.Add(value.ClassType);
            return buffer.ToArray();
        }

        /// <summary>
        /// Encodes a <see cref="LoginResponse"/>: 2 bytes on failure, 28 bytes on success.
        /// On failure the success-only fields are ignored.
        /// </summary>
        /// <param name="value">The response.</param>
        /// <returns>The body bytes.</returns>
        /// <exception cref="ArgumentException">
        /// Success with a session token that is not exactly 16 bytes, success with a reason other than
        /// <see cref="AuthFailureReason.None"/>, or failure with <see cref="AuthFailureReason.None"/>.
        /// </exception>
        public static byte[] Encode(LoginResponse value)
        {
            if (!IsConsistent(value.Success, value.ErrorCode))
            {
                throw new ArgumentException(
                    "Success must carry AuthFailureReason.None and failure must carry another reason.",
                    nameof(value));
            }

            var buffer = new List<byte>();
            buffer.Add(value.Success ? (byte)1 : (byte)0);
            buffer.Add((byte)value.ErrorCode);
            if (value.Success)
            {
                WriteToken(buffer, value.SessionToken, nameof(value.SessionToken));
                WriteUInt32(buffer, value.CharacterId.RawValue);
                WriteUInt32(buffer, value.ZoneId.RawValue);
                WriteUInt16(buffer, value.ZoneServerPort);
            }

            return buffer.ToArray();
        }

        /// <summary>Encodes a <see cref="RegistrationResult"/> (2 bytes).</summary>
        /// <param name="value">The result.</param>
        /// <returns>The body bytes.</returns>
        public static byte[] Encode(RegistrationResult value)
        {
            return new[] { value.Success ? (byte)1 : (byte)0, (byte)value.ErrorCode };
        }

        // ---------------------------------------------------------------- Decode

        /// <summary>Decodes a <see cref="ConnectionRequest"/>. Never throws.</summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="value">The decoded value, or default on failure.</param>
        /// <returns>True if the body is well-formed.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> body, out ConnectionRequest value)
        {
            value = default;
            var reader = new Reader(body);
            if (!reader.TryReadString(out string username)
                || !reader.TryReadString(out string password)
                || !reader.TryReadBytes(TOKEN_LENGTH, out byte[] token)
                || !reader.AtEnd)
            {
                return false;
            }

            value = new ConnectionRequest(username, password, token);
            return true;
        }

        /// <summary>Decodes a <see cref="RegistrationRequest"/>. Never throws.</summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="value">The decoded value, or default on failure.</param>
        /// <returns>True if the body is well-formed.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> body, out RegistrationRequest value)
        {
            value = default;
            var reader = new Reader(body);
            if (!reader.TryReadString(out string username)
                || !reader.TryReadString(out string password)
                || !reader.TryReadString(out string characterName)
                || !reader.TryReadByte(out byte classType)
                || !reader.AtEnd)
            {
                return false;
            }

            value = new RegistrationRequest(username, password, characterName, classType);
            return true;
        }

        /// <summary>
        /// Decodes a <see cref="LoginResponse"/>. Never throws. A failure body with extra bytes is
        /// malformed, and so is success with a reason other than <see cref="AuthFailureReason.None"/> or
        /// failure with <see cref="AuthFailureReason.None"/>. On a failure body the success-only fields are
        /// default (token is null).
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="value">The decoded value, or default on failure.</param>
        /// <returns>True if the body is well-formed.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> body, out LoginResponse value)
        {
            value = default;
            var reader = new Reader(body);
            if (!reader.TryReadBool(out bool success)
                || !reader.TryReadByte(out byte errorCode)
                || !IsConsistent(success, (AuthFailureReason)errorCode))
            {
                return false;
            }

            if (!success)
            {
                if (!reader.AtEnd)
                {
                    return false;
                }

                value = LoginResponse.ForFailure((AuthFailureReason)errorCode);
                return true;
            }

            if (!reader.TryReadBytes(TOKEN_LENGTH, out byte[] token)
                || !reader.TryReadUInt32(out uint characterId)
                || !reader.TryReadUInt32(out uint zoneId)
                || !reader.TryReadUInt16(out ushort port)
                || !reader.AtEnd)
            {
                return false;
            }

            value = new LoginResponse(
                true,
                (AuthFailureReason)errorCode,
                token,
                new CharacterID(characterId),
                new ZoneID(zoneId),
                port);
            return true;
        }

        /// <summary>Decodes a <see cref="RegistrationResult"/>. Never throws.</summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="value">The decoded value, or default on failure.</param>
        /// <returns>True if the body is well-formed.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> body, out RegistrationResult value)
        {
            value = default;
            var reader = new Reader(body);
            if (!reader.TryReadBool(out bool success)
                || !reader.TryReadByte(out byte errorCode)
                || !reader.AtEnd)
            {
                return false;
            }

            value = new RegistrationResult(success, (RegistrationError)errorCode);
            return true;
        }

        // ---------------------------------------------------------------- Helpers

        private static bool IsConsistent(bool success, AuthFailureReason errorCode)
        {
            return success == (errorCode == AuthFailureReason.None);
        }

        private static void WriteString(List<byte> buffer, string text, string paramName)
        {
            if (text == null)
            {
                throw new ArgumentNullException(paramName);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > MAX_STRING_BYTES)
            {
                throw new ArgumentException(
                    "String exceeds " + MAX_STRING_BYTES + " UTF-8 bytes (was " + bytes.Length + ").", paramName);
            }

            WriteUInt16(buffer, (ushort)bytes.Length);
            buffer.AddRange(bytes);
        }

        private static void WriteToken(List<byte> buffer, byte[] token, string paramName)
        {
            if (token == null || token.Length != TOKEN_LENGTH)
            {
                throw new ArgumentException("Token must be exactly " + TOKEN_LENGTH + " bytes.", paramName);
            }

            buffer.AddRange(token);
        }

        private static void WriteUInt16(List<byte> buffer, ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            buffer.AddRange(bytes);
        }

        private static void WriteUInt32(List<byte> buffer, uint value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            buffer.AddRange(bytes);
        }

        /// <summary>Bounds-checked forward-only reader; every read returns false instead of throwing.</summary>
        private ref struct Reader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _position;

            public Reader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _position = 0;
            }

            public bool AtEnd => _position == _data.Length;

            private int Remaining => _data.Length - _position;

            public bool TryReadByte(out byte value)
            {
                value = 0;
                if (Remaining < 1)
                {
                    return false;
                }

                value = _data[_position];
                _position += 1;
                return true;
            }

            public bool TryReadBool(out bool value)
            {
                value = false;
                if (!TryReadByte(out byte raw) || raw > 1)
                {
                    return false;
                }

                value = raw == 1;
                return true;
            }

            public bool TryReadUInt16(out ushort value)
            {
                value = 0;
                if (Remaining < 2)
                {
                    return false;
                }

                value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
                _position += 2;
                return true;
            }

            public bool TryReadUInt32(out uint value)
            {
                value = 0;
                if (Remaining < 4)
                {
                    return false;
                }

                value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
                _position += 4;
                return true;
            }

            public bool TryReadBytes(int count, out byte[] value)
            {
                value = null;
                if (Remaining < count)
                {
                    return false;
                }

                value = _data.Slice(_position, count).ToArray();
                _position += count;
                return true;
            }

            public bool TryReadString(out string value)
            {
                value = null;
                if (!TryReadUInt16(out ushort length) || length > MAX_STRING_BYTES || Remaining < length)
                {
                    return false;
                }

                // Invalid UTF-8 is decoded permissively (replacement characters); it never throws.
                value = Encoding.UTF8.GetString(_data.Slice(_position, length));
                _position += length;
                return true;
            }
        }
    }
}

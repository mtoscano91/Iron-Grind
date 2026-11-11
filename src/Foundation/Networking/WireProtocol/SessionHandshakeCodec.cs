using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="SessionHandshake"/> (24-byte body, little-endian). The decode allocates only the
    /// 16-byte token array (the zone request keeps it; the receive buffer is not valid after the callback). It does not
    /// judge the values: <c>characterId == 0</c> and an all-zero token are the intake's malformed rules.
    /// </summary>
    /// <example>
    /// <code>
    /// if (SessionHandshakeCodec.TryReadBody(body, out SessionHandshake handshake)) { /* use handshake.SessionToken */ }
    /// </code>
    /// </example>
    public static class SessionHandshakeCodec
    {
        /// <summary>
        /// Writes the 24-byte body at offset 0 and returns 24. Throws <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="SessionHandshake.BodySize"/> or the token is not
        /// exactly <see cref="SessionHandshake.TOKEN_LENGTH"/> bytes.
        /// </summary>
        /// <param name="destination">The buffer to write into.</param>
        /// <param name="message">The message.</param>
        /// <returns>The body size, 24.</returns>
        public static int WriteBody(Span<byte> destination, in SessionHandshake message)
        {
            if (message.SessionToken == null || message.SessionToken.Length != SessionHandshake.TOKEN_LENGTH)
            {
                throw new ArgumentOutOfRangeException(nameof(message), "SessionToken must be exactly 16 bytes.");
            }

            destination = destination.Slice(0, SessionHandshake.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, message.CharacterId.RawValue);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), message.TargetZoneId.RawValue);
            message.SessionToken.AsSpan().CopyTo(destination.Slice(8, SessionHandshake.TOKEN_LENGTH));
            return SessionHandshake.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> (and a default message) if <paramref name="body"/> is not
        /// exactly <see cref="SessionHandshake.BodySize"/> bytes. Never throws.
        /// </summary>
        /// <param name="body">The body after the envelope.</param>
        /// <param name="message">The decoded message; the token is a new array.</param>
        /// <returns>True when the length is exactly 24.</returns>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SessionHandshake message)
        {
            if (body.Length != SessionHandshake.BodySize)
            {
                message = default;
                return false;
            }

            byte[] token = new byte[SessionHandshake.TOKEN_LENGTH];
            body.Slice(8, SessionHandshake.TOKEN_LENGTH).CopyTo(token);
            message = new SessionHandshake(
                new CharacterID(BinaryPrimitives.ReadUInt32LittleEndian(body)),
                new ZoneID(BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4))),
                token);
            return true;
        }
    }
}

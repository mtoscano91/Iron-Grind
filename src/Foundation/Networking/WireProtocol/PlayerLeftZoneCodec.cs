using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="PlayerLeftZone"/> (5-byte body, little-endian). No allocation.
    /// Zone Instancing Story 011.
    /// </summary>
    public static class PlayerLeftZoneCodec
    {
        /// <summary>
        /// Writes the 5-byte body at offset 0 and returns 5. Throws <see cref="InvalidOperationException"/> (via
        /// <see cref="WireIdCodec"/>, CR-NET-7.3) for <c>EntityID.Invalid</c>, and
        /// <see cref="ArgumentOutOfRangeException"/> if <paramref name="destination"/> is shorter than
        /// <see cref="PlayerLeftZone.BodySize"/>.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in PlayerLeftZone message)
        {
            destination = destination.Slice(0, PlayerLeftZone.BodySize);
            WireIdCodec.SerializeEntityId(destination, message.EntityId, PlayerLeftZone.MessageTypeId, nameof(message.EntityId));
            destination[4] = (byte)message.DisconnectType;
            return PlayerLeftZone.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (and a default message) if
        /// <paramref name="body"/> is shorter than 5 bytes; trailing bytes are ignored. An unknown
        /// <see cref="DisconnectType"/> byte decodes as <see cref="DisconnectType.Timeout"/> with an anomaly log
        /// and still returns <see langword="true"/>. An EntityID of 0 is not rejected here.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out PlayerLeftZone message)
        {
            if (body.Length < PlayerLeftZone.BodySize)
            {
                message = default;
                return false;
            }

            message = new PlayerLeftZone(
                WireIdCodec.DeserializeEntityId(body),
                WireEnumCodec.DecodeDisconnectType(body[4], PlayerLeftZone.MessageTypeId));
            return true;
        }
    }
}

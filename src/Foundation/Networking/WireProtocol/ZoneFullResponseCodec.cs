using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ZoneFullResponse"/> (6-byte body, little-endian). No allocation.
    /// Zone Instancing Story 006.
    /// </summary>
    public static class ZoneFullResponseCodec
    {
        /// <summary>
        /// Writes the 6-byte body at offset 0 and returns 6. Throws <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="ZoneFullResponse.BodySize"/>.
        /// There is no <c>ZoneID.Invalid</c> guard on encode (no WireIdCodec ZoneID serializer exists); the sender must not
        /// send an invalid zone id.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in ZoneFullResponse message)
        {
            destination = destination.Slice(0, ZoneFullResponse.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, message.ZoneId.RawValue);
            destination[4] = (byte)message.Reason;
            destination[5] = message.RetryAfterSeconds;
            return ZoneFullResponse.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (and a default message) if
        /// <paramref name="body"/> is shorter than 6 bytes; trailing bytes are ignored. An unknown reason byte decodes as
        /// <see cref="JoinRejectedReason.Other"/> with an anomaly log (CR-NET-7.4) and still returns <see langword="true"/>.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out ZoneFullResponse message)
        {
            if (body.Length < ZoneFullResponse.BodySize)
            {
                message = default;
                return false;
            }

            message = new ZoneFullResponse(
                new ZoneID(BinaryPrimitives.ReadUInt32LittleEndian(body)),
                WireEnumCodec.DecodeJoinRejectedReason(body[4], ZoneFullResponse.MessageTypeId),
                body[5]);
            return true;
        }
    }
}

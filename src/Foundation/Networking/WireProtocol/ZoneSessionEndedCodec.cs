using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ZoneSessionEnded"/> (9-byte body, little-endian). No allocation.
    /// Zone Instancing Story 006.
    /// </summary>
    public static class ZoneSessionEndedCodec
    {
        /// <summary>
        /// Writes the 9-byte body at offset 0 and returns 9. Throws <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="ZoneSessionEnded.BodySize"/>.
        /// There is no <c>ZoneID.Invalid</c> guard on encode (no WireIdCodec ZoneID serializer exists); the sender must not
        /// send an invalid zone id.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in ZoneSessionEnded message)
        {
            destination = destination.Slice(0, ZoneSessionEnded.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, message.ZoneId.RawValue);
            destination[4] = (byte)message.Reason;
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(5, 4), message.GracePeriodSeconds);
            return ZoneSessionEnded.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (and a default message) if
        /// <paramref name="body"/> is shorter than 9 bytes; trailing bytes are ignored. An unknown reason byte decodes as
        /// <see cref="DisconnectReason.Other"/> with an anomaly log (CR-NET-7.9) and still returns <see langword="true"/>.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out ZoneSessionEnded message)
        {
            if (body.Length < ZoneSessionEnded.BodySize)
            {
                message = default;
                return false;
            }

            message = new ZoneSessionEnded(
                new ZoneID(BinaryPrimitives.ReadUInt32LittleEndian(body)),
                WireEnumCodec.DecodeDisconnectReason(body[4], ZoneSessionEnded.MessageTypeId),
                BinaryPrimitives.ReadInt32LittleEndian(body.Slice(5, 4)));
            return true;
        }
    }
}

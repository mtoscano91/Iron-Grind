using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ZoneEntryAck"/> (8-byte body, little-endian). No allocation.
    /// Zone Instancing Story 011.
    /// </summary>
    public static class ZoneEntryAckCodec
    {
        /// <summary>
        /// Writes the 8-byte body at offset 0 and returns 8. Throws <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="ZoneEntryAck.BodySize"/>.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in ZoneEntryAck message)
        {
            destination = destination.Slice(0, ZoneEntryAck.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, message.ZoneId.RawValue);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), message.SnapshotVersion);
            return ZoneEntryAck.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (and a default message) if
        /// <paramref name="body"/> is shorter than 8 bytes; trailing bytes are ignored. Never throws.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out ZoneEntryAck message)
        {
            if (body.Length < ZoneEntryAck.BodySize)
            {
                message = default;
                return false;
            }

            message = new ZoneEntryAck(
                new ZoneID(BinaryPrimitives.ReadUInt32LittleEndian(body)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)));
            return true;
        }
    }
}

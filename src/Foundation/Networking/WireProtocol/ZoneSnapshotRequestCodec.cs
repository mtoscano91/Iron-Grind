using System;
using System.Buffers.Binary;
using IronGrind.CharacterPersistence;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ZoneSnapshotRequest"/> (8-byte body, little-endian). No allocation.
    /// Zone Instancing Story 006.
    /// </summary>
    public static class ZoneSnapshotRequestCodec
    {
        /// <summary>
        /// Writes the 8-byte body at offset 0 and returns 8. Throws <see cref="ArgumentOutOfRangeException"/> if
        /// <paramref name="destination"/> is shorter than <see cref="ZoneSnapshotRequest.BodySize"/>.
        /// There is no <c>ZoneID.Invalid</c> guard on encode (no WireIdCodec ZoneID serializer exists); the sender must not
        /// send an invalid zone id.
        /// </summary>
        public static int WriteBody(Span<byte> destination, in ZoneSnapshotRequest message)
        {
            destination = destination.Slice(0, ZoneSnapshotRequest.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, message.ZoneId.RawValue);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), message.SnapshotVersion);
            return ZoneSnapshotRequest.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (and a default message) if
        /// <paramref name="body"/> is shorter than 8 bytes; trailing bytes are ignored.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out ZoneSnapshotRequest message)
        {
            if (body.Length < ZoneSnapshotRequest.BodySize)
            {
                message = default;
                return false;
            }

            message = new ZoneSnapshotRequest(
                new ZoneID(BinaryPrimitives.ReadUInt32LittleEndian(body)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)));
            return true;
        }
    }
}

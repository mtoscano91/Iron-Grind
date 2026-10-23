using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ZoneStateSnapshotFragment"/>: 8-byte header followed by the payload, which runs to the
    /// end of the body. No allocation: decode returns the payload as a slice of the input span. Zone Instancing Story 006.
    /// </summary>
    public static class ZoneStateSnapshotFragmentCodec
    {
        /// <summary>
        /// Writes header and payload at offset 0 and returns <c>8 + payload.Length</c>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="payload"/> is longer than
        /// <see cref="ZoneStateSnapshotFragment.MaxPayloadBytes"/>, <c>TotalFragments</c> is 0, or <c>FragmentIndex</c> is not
        /// below <c>TotalFragments</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than the body.</exception>
        public static int WriteBody(Span<byte> destination, in ZoneStateSnapshotFragment header, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > ZoneStateSnapshotFragment.MaxPayloadBytes)
            {
                throw new ArgumentException(
                    $"Fragment payload of {payload.Length} bytes exceeds the {ZoneStateSnapshotFragment.MaxPayloadBytes}-byte maximum.",
                    nameof(payload));
            }

            if (header.TotalFragments == 0 || header.FragmentIndex >= header.TotalFragments)
            {
                throw new ArgumentException(
                    $"Fragment index {header.FragmentIndex} of {header.TotalFragments} is invalid (total must be >= 1, index < total).",
                    nameof(header));
            }

            int size = ZoneStateSnapshotFragment.HeaderSize + payload.Length;
            destination = destination.Slice(0, size);
            BinaryPrimitives.WriteUInt32LittleEndian(destination, header.SnapshotVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), header.TotalFragments);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), header.FragmentIndex);
            payload.CopyTo(destination.Slice(ZoneStateSnapshotFragment.HeaderSize));
            return size;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (default header, empty payload) if the body is shorter
        /// than 8 bytes, <c>totalFragments</c> is 0, <c>fragmentIndex</c> is not below <c>totalFragments</c>, or the payload
        /// exceeds 504 bytes; never throws. On success <paramref name="payload"/> is the part of
        /// <paramref name="body"/> after the header (a view, not a copy; it is empty for a header-only body).
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out ZoneStateSnapshotFragment header, out ReadOnlySpan<byte> payload)
        {
            header = default;
            payload = ReadOnlySpan<byte>.Empty;

            if (body.Length < ZoneStateSnapshotFragment.HeaderSize
                || body.Length - ZoneStateSnapshotFragment.HeaderSize > ZoneStateSnapshotFragment.MaxPayloadBytes)
            {
                return false;
            }

            ushort total = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2));
            ushort index = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2));
            if (total == 0 || index >= total)
            {
                return false;
            }

            header = new ZoneStateSnapshotFragment(BinaryPrimitives.ReadUInt32LittleEndian(body), total, index);
            payload = body.Slice(ZoneStateSnapshotFragment.HeaderSize);
            return true;
        }
    }
}

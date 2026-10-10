using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EnhancementRequestReceived"/> (5-byte body: <c>uint requestId</c>,
    /// <c>byte itemSlotIndex</c>). No allocation.
    /// </summary>
    public static class EnhancementRequestReceivedCodec
    {
        /// <summary>Writes the 5-byte body at offset 0 of <paramref name="destination"/>; returns 5.</summary>
        /// <param name="destination">At least <see cref="EnhancementRequestReceived.BodySize"/> bytes.</param>
        /// <param name="requestId">Echo of the acknowledged request's id.</param>
        /// <param name="itemSlotIndex">The bag slot now locked for the attempt.</param>
        public static int WriteBody(Span<byte> destination, uint requestId, byte itemSlotIndex)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), requestId);
            destination[4] = itemSlotIndex;
            return EnhancementRequestReceived.BodySize;
        }

        /// <summary>Attempts to decode the body; <see langword="false"/> if shorter than 5 bytes. Extra bytes ignored.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint requestId, out byte itemSlotIndex)
        {
            if (body.Length < EnhancementRequestReceived.BodySize)
            {
                requestId = 0;
                itemSlotIndex = 0;
                return false;
            }

            requestId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            itemSlotIndex = body[4];
            return true;
        }
    }
}

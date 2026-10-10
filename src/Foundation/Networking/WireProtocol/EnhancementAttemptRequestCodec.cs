using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EnhancementAttemptRequest"/> (6-byte body: <c>uint requestId</c>,
    /// <c>byte itemSlotIndex</c>, <c>byte scrollSlotIndex</c>, little-endian). No allocation. Slot indices are not
    /// range-checked here; <c>requestId</c> is a raw counter and is not zero-guarded.
    /// </summary>
    public static class EnhancementAttemptRequestCodec
    {
        /// <summary>
        /// Writes the 6-byte body at offset 0 of <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">At least <see cref="EnhancementAttemptRequest.BodySize"/> bytes.</param>
        /// <param name="requestId">Client-generated monotonically increasing request id.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance (not range-checked).</param>
        /// <param name="scrollSlotIndex">Bag slot of the Enhancement Scroll (not range-checked).</param>
        /// <returns><see cref="EnhancementAttemptRequest.BodySize"/> (6), always.</returns>
        public static int WriteBody(Span<byte> destination, uint requestId, byte itemSlotIndex, byte scrollSlotIndex)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), requestId);
            destination[4] = itemSlotIndex;
            destination[5] = scrollSlotIndex;
            return EnhancementAttemptRequest.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> if <paramref name="body"/> is shorter than
        /// <see cref="EnhancementAttemptRequest.BodySize"/>; extra bytes are ignored.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint requestId, out byte itemSlotIndex, out byte scrollSlotIndex)
        {
            if (body.Length < EnhancementAttemptRequest.BodySize)
            {
                requestId = 0;
                itemSlotIndex = 0;
                scrollSlotIndex = 0;
                return false;
            }

            requestId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            itemSlotIndex = body[4];
            scrollSlotIndex = body[5];
            return true;
        }
    }
}

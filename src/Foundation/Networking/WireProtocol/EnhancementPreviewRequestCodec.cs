using System;

namespace IronGrind.Networking
{
    /// <summary>Body-level codec for <see cref="EnhancementPreviewRequest"/> (2-byte body). No allocation.</summary>
    public static class EnhancementPreviewRequestCodec
    {
        /// <summary>Writes the 2-byte body at offset 0 of <paramref name="destination"/>; returns 2.</summary>
        /// <param name="destination">At least <see cref="EnhancementPreviewRequest.BodySize"/> bytes.</param>
        /// <param name="itemSlotIndex">Bag slot of the selected item (not range-checked).</param>
        /// <param name="scrollSlotIndex">Bag slot of the selected scroll (not range-checked).</param>
        public static int WriteBody(Span<byte> destination, byte itemSlotIndex, byte scrollSlotIndex)
        {
            destination[0] = itemSlotIndex;
            destination[1] = scrollSlotIndex;
            return EnhancementPreviewRequest.BodySize;
        }

        /// <summary>Attempts to decode the body; <see langword="false"/> if shorter than 2 bytes. Extra bytes ignored.</summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out byte itemSlotIndex, out byte scrollSlotIndex)
        {
            if (body.Length < EnhancementPreviewRequest.BodySize)
            {
                itemSlotIndex = 0;
                scrollSlotIndex = 0;
                return false;
            }

            itemSlotIndex = body[0];
            scrollSlotIndex = body[1];
            return true;
        }
    }
}

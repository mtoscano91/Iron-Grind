using System;
using IronGrind.EnhancementSystem;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EnhancementPreviewRejected"/> (3-byte body). No allocation. Does not use
    /// <see cref="WireEnumCodec"/> (no decoder for this enum there); an unknown <c>resultCode</c> never throws (AC-NC-42).
    /// </summary>
    public static class EnhancementPreviewRejectedCodec
    {
        /// <summary>Writes the 3-byte body at offset 0 of <paramref name="destination"/>; returns 3.</summary>
        /// <param name="destination">At least <see cref="EnhancementPreviewRejected.BodySize"/> bytes.</param>
        /// <param name="itemSlotIndex">Echo of the request's item slot.</param>
        /// <param name="scrollSlotIndex">Echo of the request's scroll slot.</param>
        /// <param name="resultCode">A <c>Rejected*</c> code.</param>
        public static int WriteBody(Span<byte> destination, byte itemSlotIndex, byte scrollSlotIndex, EnhancementResultCode resultCode)
        {
            destination[0] = itemSlotIndex;
            destination[1] = scrollSlotIndex;
            destination[2] = (byte)resultCode;
            return EnhancementPreviewRejected.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> only if <paramref name="body"/> is shorter than
        /// 3 bytes. A <c>resultCode</c> byte that is not a <c>Rejected*</c> code (below
        /// <see cref="EnhancementPreviewRejected.MinKnownResultCode"/>, so <c>Success</c> or <c>Destruction</c>, or above
        /// <see cref="EnhancementPreviewRejected.MaxKnownResultCode"/>) does not
        /// fail and does not throw (AC-NC-42): <paramref name="isUnknownResultCode"/> is set,
        /// <paramref name="resultCode"/> is <see cref="EnhancementPreviewRejected.UnknownResultCodeFallback"/>, and exactly
        /// one <see cref="Debug.LogWarning(object)"/> anomaly is logged.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out byte itemSlotIndex, out byte scrollSlotIndex,
            out EnhancementResultCode resultCode, out bool isUnknownResultCode)
        {
            if (body.Length < EnhancementPreviewRejected.BodySize)
            {
                itemSlotIndex = 0;
                scrollSlotIndex = 0;
                resultCode = default;
                isUnknownResultCode = false;
                return false;
            }

            itemSlotIndex = body[0];
            scrollSlotIndex = body[1];
            byte rawCode = body[2];

            if (rawCode < EnhancementPreviewRejected.MinKnownResultCode || rawCode > EnhancementPreviewRejected.MaxKnownResultCode)
            {
                // Out of range = not a Rejected* code: 0 (Success) and 1 (Destruction) can never be a preview rejection.
                Debug.LogWarning($"[EnhancementPreviewRejectedCodec] TryReadBody: received out-of-range resultCode byte {rawCode} " +
                    $"for messageTypeId={EnhancementPreviewRejected.MessageTypeId} — decoding as a rejection (AC-NC-42, CR-NET-7.4).");
                resultCode = EnhancementPreviewRejected.UnknownResultCodeFallback;
                isUnknownResultCode = true;
                return true;
            }

            resultCode = (EnhancementResultCode)rawCode;
            isUnknownResultCode = false;
            return true;
        }
    }
}

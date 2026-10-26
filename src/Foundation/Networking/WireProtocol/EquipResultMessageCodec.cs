using System;
using System.Buffers.Binary;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EquipResultMessage"/> (wire name <c>EquipResult</c>; 12-byte body, little-endian).
    /// No allocation. Does not use <see cref="WireEnumCodec"/>: that class has no decoder for
    /// <see cref="EquipFailReason"/>, whose valid set (<c>0..6, 255</c>) is not contiguous.
    /// </summary>
    public static class EquipResultMessageCodec
    {
        /// <summary>Writes the 12-byte body at offset 0 of <paramref name="destination"/>; returns 12.</summary>
        /// <param name="destination">At least <see cref="EquipResultMessage.BodySize"/> bytes.</param>
        /// <param name="requestId">Echo of the request's id.</param>
        /// <param name="success">Written as 1 or 0.</param>
        /// <param name="failReason"><see cref="EquipFailReason.None"/> on success.</param>
        /// <param name="affectedSlot">The gear slot of the request.</param>
        /// <param name="slotItemId">The slot's item after the operation; 0 when empty.</param>
        /// <param name="slotEnhancementLevel">The slot's enhancement level after the operation; 0 when empty.</param>
        public static int WriteBody(Span<byte> destination, uint requestId, bool success, EquipFailReason failReason,
            byte affectedSlot, uint slotItemId, byte slotEnhancementLevel)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), requestId);
            destination[4] = success ? (byte)1 : (byte)0;
            destination[5] = (byte)failReason;
            destination[6] = affectedSlot;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(7, 4), slotItemId);
            destination[11] = slotEnhancementLevel;
            return EquipResultMessage.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> if <paramref name="body"/> is not exactly
        /// <see cref="EquipResultMessage.BodySize"/> bytes. Any non-zero <c>success</c> byte reads as
        /// <see langword="true"/>. A <c>failReason</c> byte outside the defined set does not fail and does not throw
        /// (CR-NET-7.4): <paramref name="failReason"/> is <see cref="EquipResultMessage.UnknownFailReasonFallback"/>,
        /// <paramref name="isUnknownFailReason"/> is set, and one <see cref="Debug.LogWarning(object)"/> anomaly is logged.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint requestId, out bool success, out EquipFailReason failReason,
            out byte affectedSlot, out uint slotItemId, out byte slotEnhancementLevel, out bool isUnknownFailReason)
        {
            if (body.Length != EquipResultMessage.BodySize)
            {
                requestId = 0;
                success = false;
                failReason = EquipFailReason.None;
                affectedSlot = 0;
                slotItemId = 0;
                slotEnhancementLevel = 0;
                isUnknownFailReason = false;
                return false;
            }

            requestId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            success = body[4] != 0;
            affectedSlot = body[6];
            slotItemId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(7, 4));
            slotEnhancementLevel = body[11];

            byte rawReason = body[5];
            isUnknownFailReason = rawReason > (byte)EquipFailReason.InventoryError && rawReason != (byte)EquipFailReason.CriticalFailure;
            if (isUnknownFailReason)
            {
                Debug.LogWarning($"[EquipResultMessageCodec] TryReadBody: received out-of-range failReason byte {rawReason} " +
                    $"for messageTypeId={EquipResultMessage.MessageTypeId} — decoding as {EquipResultMessage.UnknownFailReasonFallback} (CR-NET-7.4).");
                failReason = EquipResultMessage.UnknownFailReasonFallback;
            }
            else
            {
                failReason = (EquipFailReason)rawReason;
            }

            return true;
        }
    }
}

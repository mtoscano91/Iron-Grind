using System;
using System.Buffers.Binary;
using IronGrind.EnhancementSystem;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EnhancementAttemptResultMessage"/> (wire name <c>EnhancementAttemptResult</c>;
    /// 6-byte body). No allocation. Does not use <see cref="WireEnumCodec"/>: an unknown <c>resultCode</c> must decode
    /// as a rejection and never throw (AC-NC-42), and that class has no decoder for this enum.
    /// </summary>
    public static class EnhancementAttemptResultMessageCodec
    {
        /// <summary>Writes the 6-byte body at offset 0 of <paramref name="destination"/>; returns 6.</summary>
        /// <param name="destination">At least <see cref="EnhancementAttemptResultMessage.BodySize"/> bytes.</param>
        /// <param name="requestId">Echo of the request's id.</param>
        /// <param name="resultCode">The authoritative result code.</param>
        /// <param name="newLevel">The item's level after a Success; 0 on Destruction and on every Rejected* code.</param>
        public static int WriteBody(Span<byte> destination, uint requestId, EnhancementResultCode resultCode, byte newLevel)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), requestId);
            destination[4] = (byte)resultCode;
            destination[5] = newLevel;
            return EnhancementAttemptResultMessage.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> only if <paramref name="body"/> is shorter than
        /// 6 bytes. A <c>resultCode</c> byte above <see cref="EnhancementAttemptResultMessage.MaxKnownResultCode"/> does
        /// not fail and does not throw (AC-NC-42): <paramref name="isUnknownResultCode"/> is set,
        /// <paramref name="resultCode"/> is <see cref="EnhancementAttemptResultMessage.UnknownResultCodeFallback"/>,
        /// <paramref name="newLevel"/> is 0, and exactly one <see cref="Debug.LogWarning(object)"/> anomaly is logged.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out uint requestId, out EnhancementResultCode resultCode,
            out byte newLevel, out bool isUnknownResultCode)
        {
            if (body.Length < EnhancementAttemptResultMessage.BodySize)
            {
                requestId = 0;
                resultCode = default;
                newLevel = 0;
                isUnknownResultCode = false;
                return false;
            }

            requestId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            byte rawCode = body[4];

            if (rawCode > EnhancementAttemptResultMessage.MaxKnownResultCode)
            {
                Debug.LogWarning($"[EnhancementAttemptResultMessageCodec] TryReadBody: received out-of-range resultCode byte {rawCode} " +
                    $"for messageTypeId={EnhancementAttemptResultMessage.MessageTypeId} — decoding as a rejection (AC-NC-42, CR-NET-7.4).");
                resultCode = EnhancementAttemptResultMessage.UnknownResultCodeFallback;
                newLevel = 0;
                isUnknownResultCode = true;
                return true;
            }

            resultCode = (EnhancementResultCode)rawCode;
            newLevel = body[5];
            isUnknownResultCode = false;
            return true;
        }
    }
}

using System;
using System.Buffers.Binary;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="EnhancementStateUpdate"/> (7-byte body). Probabilities are encoded as
    /// <c>ushort</c> × 10,000 (CR-NET-7.2); no raw <see cref="float"/> reaches the wire. No allocation.
    /// </summary>
    public static class EnhancementStateUpdateCodec
    {
        /// <summary>
        /// Writes the 7-byte body at offset 0 of <paramref name="destination"/>; returns 7. A probability outside
        /// [0, 1] is clamped and one <see cref="Debug.LogWarning(object)"/> anomaly is logged (same policy as
        /// <see cref="WireFixedPointCodec"/>'s guarded encoders).
        /// </summary>
        /// <param name="destination">At least <see cref="EnhancementStateUpdate.BodySize"/> bytes.</param>
        /// <param name="itemSlotIndex">Echo of the request's item slot.</param>
        /// <param name="scrollSlotIndex">Echo of the request's scroll slot.</param>
        /// <param name="currentLevel">The item's current enhancement level.</param>
        /// <param name="pSuccess">P_s[k] in [0, 1].</param>
        /// <param name="pDestruction">P_d[k] in [0, 1].</param>
        public static int WriteBody(Span<byte> destination, byte itemSlotIndex, byte scrollSlotIndex, byte currentLevel,
            float pSuccess, float pDestruction)
        {
            destination[0] = itemSlotIndex;
            destination[1] = scrollSlotIndex;
            destination[2] = currentLevel;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3, 2), EncodeProbability(pSuccess, "pSuccess"));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(5, 2), EncodeProbability(pDestruction, "pDestruction"));
            return EnhancementStateUpdate.BodySize;
        }

        /// <summary>
        /// Attempts to decode the body; <see langword="false"/> if shorter than 7 bytes. Probabilities are the wire
        /// value divided by 10,000.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out byte itemSlotIndex, out byte scrollSlotIndex,
            out byte currentLevel, out float pSuccess, out float pDestruction)
        {
            if (body.Length < EnhancementStateUpdate.BodySize)
            {
                itemSlotIndex = 0;
                scrollSlotIndex = 0;
                currentLevel = 0;
                pSuccess = 0f;
                pDestruction = 0f;
                return false;
            }

            itemSlotIndex = body[0];
            scrollSlotIndex = body[1];
            currentLevel = body[2];
            pSuccess = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(3, 2)) / (float)EnhancementStateUpdate.ProbabilityScale;
            pDestruction = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(5, 2)) / (float)EnhancementStateUpdate.ProbabilityScale;
            return true;
        }

        private static ushort EncodeProbability(float probability, string fieldName)
        {
            float clamped = Mathf.Clamp01(probability);
            if (clamped != probability)
            {
                Debug.LogWarning($"[EnhancementStateUpdateCodec] WriteBody: {fieldName} ({probability}) out of range [0, 1] — " +
                    $"clamped to {clamped} before encoding (CR-NET-7.2).");
            }

            return (ushort)Mathf.RoundToInt(clamped * EnhancementStateUpdate.ProbabilityScale);
        }
    }
}

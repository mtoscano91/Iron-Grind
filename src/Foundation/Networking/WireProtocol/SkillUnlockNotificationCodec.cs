using System;
using System.Buffers.Binary;
using IronGrind.SkillSystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="SkillUnlockNotification"/> (4-byte body: <c>uint skillID</c>, little-endian).
    /// No allocation.
    /// </summary>
    public static class SkillUnlockNotificationCodec
    {
        /// <summary>Writes the 4-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="SkillUnlockNotification.BodySize"/> bytes.</param>
        /// <param name="message">The notification to write.</param>
        /// <returns><see cref="SkillUnlockNotification.BodySize"/>, always.</returns>
        public static int WriteBody(Span<byte> destination, in SkillUnlockNotification message)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, SkillUnlockNotification.BodySize), message.SkillId.Value);
            return SkillUnlockNotification.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> unless <paramref name="body"/> is exactly
        /// <see cref="SkillUnlockNotification.BodySize"/> bytes (a short or a long body is rejected).
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SkillUnlockNotification message)
        {
            if (body.Length != SkillUnlockNotification.BodySize)
            {
                message = default;
                return false;
            }

            message = new SkillUnlockNotification(new SkillID(BinaryPrimitives.ReadUInt32LittleEndian(body)));
            return true;
        }
    }
}

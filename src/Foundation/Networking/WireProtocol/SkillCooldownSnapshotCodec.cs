using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="SkillCooldownSnapshot"/> (80-byte body, 20 little-endian <c>uint</c>s). No allocation.
    /// </summary>
    public static class SkillCooldownSnapshotCodec
    {
        /// <summary>Writes the 80-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="SkillCooldownSnapshot.BodySize"/> bytes.</param>
        /// <param name="message">The snapshot to write.</param>
        /// <returns><see cref="SkillCooldownSnapshot.BodySize"/>, always.</returns>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 80 bytes; nothing is written.</exception>
        public static int WriteBody(Span<byte> destination, in SkillCooldownSnapshot message)
        {
            if (destination.Length < SkillCooldownSnapshot.BodySize)
            {
                throw new ArgumentException("destination must hold at least SkillCooldownSnapshot.BodySize (80) bytes.", nameof(destination));
            }

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                SkillCooldownEntry entry = message.GetEntry(i);
                int offset = i * SkillCooldownSnapshot.EntrySize;
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), entry.SkillId);
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 4, 4), entry.CooldownExpiryTick);
            }

            return SkillCooldownSnapshot.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> unless <paramref name="body"/> is exactly
        /// <see cref="SkillCooldownSnapshot.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="message">The decoded snapshot, or default on failure.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SkillCooldownSnapshot message)
        {
            message = default;
            if (body.Length != SkillCooldownSnapshot.BodySize)
            {
                return false;
            }

            for (int i = 0; i < SkillCooldownSnapshot.EntryCount; i++)
            {
                int offset = i * SkillCooldownSnapshot.EntrySize;
                var entry = new SkillCooldownEntry(
                    BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(offset, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(offset + 4, 4)));
                message.SetEntry(i, in entry);
            }

            return true;
        }
    }
}

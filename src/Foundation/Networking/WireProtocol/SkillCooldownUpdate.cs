using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message sent to the caster only, carrying the absolute server tick at which a skill's cooldown
    /// ends (skill-system.md CR-SK-12 / CR-SK-23; Skill System Story 009). Carried in the R-U batch's
    /// <see cref="RUBatchCategory.SkillCooldownUpdate"/> category, in the same batch as the <see cref="SkillCastResult"/>.
    /// </summary>
    /// <remarks>
    /// Wire layout (little-endian, 8-byte body, 12 bytes in a batch): <c>uint skillID</c> (offset 0),
    /// <c>uint cooldownExpiryTick</c> (4). <see cref="MessageTypeId"/> <c>0x030A</c> follows
    /// <see cref="SkillCastResult"/> (<c>0x0309</c>). Codec: <see cref="SkillCooldownUpdateCodec"/>.
    /// </remarks>
    public readonly struct SkillCooldownUpdate : IEquatable<SkillCooldownUpdate>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 8 bytes.</summary>
        public const int BodySize = 8;

        /// <summary>Total wire size as an R-U batch sub-message: header + <see cref="BodySize"/> = 12 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="SkillCooldownUpdate"/>.</summary>
        public const ushort MessageTypeId = 0x030A;

        /// <summary>The skill whose cooldown changed.</summary>
        public readonly uint SkillId;

        /// <summary>The absolute server tick at which the cooldown ends.</summary>
        public readonly uint CooldownExpiryTick;

        /// <summary>Initializes a new <see cref="SkillCooldownUpdate"/>.</summary>
        /// <param name="skillId">The skill id.</param>
        /// <param name="cooldownExpiryTick">The absolute server tick at which the cooldown ends.</param>
        public SkillCooldownUpdate(uint skillId, uint cooldownExpiryTick)
        {
            SkillId = skillId;
            CooldownExpiryTick = cooldownExpiryTick;
        }

        /// <inheritdoc/>
        public bool Equals(SkillCooldownUpdate other)
            => SkillId == other.SkillId && CooldownExpiryTick == other.CooldownExpiryTick;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SkillCooldownUpdate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (SkillId.GetHashCode() * 397) ^ CooldownExpiryTick.GetHashCode();

        /// <summary>Returns true if both updates have identical field values.</summary>
        public static bool operator ==(SkillCooldownUpdate left, SkillCooldownUpdate right) => left.Equals(right);

        /// <summary>Returns true if the updates differ in any field.</summary>
        public static bool operator !=(SkillCooldownUpdate left, SkillCooldownUpdate right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"SkillCooldownUpdate(Skill={SkillId}, ExpiryTick={CooldownExpiryTick})";
    }

    /// <summary>
    /// Body-level codec for <see cref="SkillCooldownUpdate"/> (8-byte body, two little-endian <c>uint</c>s). No allocation.
    /// </summary>
    public static class SkillCooldownUpdateCodec
    {
        /// <summary>Writes the 8-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="SkillCooldownUpdate.BodySize"/> bytes.</param>
        /// <param name="message">The update to write.</param>
        /// <returns><see cref="SkillCooldownUpdate.BodySize"/>, always.</returns>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 8 bytes; nothing is written.</exception>
        public static int WriteBody(Span<byte> destination, in SkillCooldownUpdate message)
        {
            if (destination.Length < SkillCooldownUpdate.BodySize)
            {
                throw new ArgumentException("destination must hold at least SkillCooldownUpdate.BodySize (8) bytes.", nameof(destination));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), message.SkillId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), message.CooldownExpiryTick);
            return SkillCooldownUpdate.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> unless <paramref name="body"/> is exactly
        /// <see cref="SkillCooldownUpdate.BodySize"/> bytes.
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="message">The decoded update, or default on failure.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SkillCooldownUpdate message)
        {
            if (body.Length != SkillCooldownUpdate.BodySize)
            {
                message = default;
                return false;
            }

            message = new SkillCooldownUpdate(
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)));
            return true;
        }
    }
}

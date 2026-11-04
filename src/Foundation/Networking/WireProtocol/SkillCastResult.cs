using System;
using System.Buffers.Binary;
using IronGrind.SkillSystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message reporting the outcome of one skill cast (skill-system.md CR-SK-9;
    /// networking-wire-protocol.md SkillCastResult; Skill System Story 009). Carried in the R-U batch's
    /// <see cref="RUBatchCategory.SkillCastResult"/> category. Accepted casts go to the caster and to every client
    /// whose relevance set holds the caster or the target; rejected casts go to the caster only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> so the struct itself never allocates. Codec: <see cref="SkillCastResultCodec"/>.
    /// Ids are raw <c>uint</c> values, as in <see cref="SkillCastRequest"/>. <see cref="RejectionCode"/> is stored as a
    /// raw byte so a receiver can range-check it before casting to <see cref="SkillCastRejectionCode"/>.
    /// </para>
    /// <para>
    /// Wire layout (little-endian, 28-byte body, 32 bytes in a batch): <c>uint skillID</c> (offset 0),
    /// <c>uint casterID</c> (4), <c>uint targetID</c> (8), <c>bool castAccepted</c> (12), <c>byte rejectionCode</c> (13),
    /// <c>int damageDealt</c> (14), <c>int healAmount</c> (18), <c>bool isCrit</c> (22), <c>bool isKill</c> (23),
    /// <c>int newCooldownTicks</c> (24).
    /// </para>
    /// <para>
    /// <see cref="MessageTypeId"/> <c>0x0309</c> sits in the skill block 0x0308-0x030A (request U-U; result and
    /// cooldown update R-U), after <see cref="SkillCastRequest"/> (<c>0x0308</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = SkillCastResult.Accepted(7u, 1001u, 2001u, damageDealt: 120, healAmount: 0, isCrit: false, isKill: false, newCooldownTicks: 40);
    /// Span&lt;byte&gt; body = stackalloc byte[SkillCastResult.BodySize];
    /// SkillCastResultCodec.WriteBody(body, in result);
    /// </code>
    /// </example>
    public readonly struct SkillCastResult : IEquatable<SkillCastResult>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 28 bytes.</summary>
        public const int BodySize = 28;

        /// <summary>Total wire size as an R-U batch sub-message: header + <see cref="BodySize"/> = 32 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="SkillCastResult"/>.</summary>
        public const ushort MessageTypeId = 0x0309;

        /// <summary>The skill that was cast.</summary>
        public readonly uint SkillId;

        /// <summary>The caster's entity id.</summary>
        public readonly uint CasterId;

        /// <summary>The target's entity id.</summary>
        public readonly uint TargetId;

        /// <summary>True if the cast was accepted; false for a rejection.</summary>
        public readonly bool CastAccepted;

        /// <summary>A <see cref="SkillCastRejectionCode"/> value as a raw byte; 0 when <see cref="CastAccepted"/> is true.</summary>
        public readonly byte RejectionCode;

        /// <summary>Damage dealt; 0 when the skill has no damage or on rejection.</summary>
        public readonly int DamageDealt;

        /// <summary>HP healed; 0 when the skill has no heal or on rejection.</summary>
        public readonly int HealAmount;

        /// <summary>True if the damage was a critical hit; false on rejection.</summary>
        public readonly bool IsCrit;

        /// <summary>True if the cast killed the target; false on rejection.</summary>
        public readonly bool IsKill;

        /// <summary>Cooldown length in ticks; 0 on rejection or for a skill with no cooldown.</summary>
        public readonly int NewCooldownTicks;

        /// <summary>Initializes a new <see cref="SkillCastResult"/> with every field explicit. Prefer the factories.</summary>
        public SkillCastResult(
            uint skillId, uint casterId, uint targetId, bool castAccepted, byte rejectionCode,
            int damageDealt, int healAmount, bool isCrit, bool isKill, int newCooldownTicks)
        {
            SkillId = skillId;
            CasterId = casterId;
            TargetId = targetId;
            CastAccepted = castAccepted;
            RejectionCode = rejectionCode;
            DamageDealt = damageDealt;
            HealAmount = healAmount;
            IsCrit = isCrit;
            IsKill = isKill;
            NewCooldownTicks = newCooldownTicks;
        }

        /// <summary>Creates an accepted result (<see cref="CastAccepted"/> true, <see cref="RejectionCode"/> 0).</summary>
        public static SkillCastResult Accepted(
            uint skillId, uint casterId, uint targetId, int damageDealt, int healAmount,
            bool isCrit, bool isKill, int newCooldownTicks)
            => new SkillCastResult(skillId, casterId, targetId, true, 0, damageDealt, healAmount, isCrit, isKill, newCooldownTicks);

        /// <summary>Creates a rejected result; damage, heal, crit, kill and cooldown fields are zero or false.</summary>
        public static SkillCastResult Rejected(uint skillId, uint casterId, uint targetId, byte rejectionCode)
            => new SkillCastResult(skillId, casterId, targetId, false, rejectionCode, 0, 0, false, false, 0);

        /// <summary>Creates a rejected result from a <see cref="SkillCastRejectionCode"/>.</summary>
        public static SkillCastResult Rejected(uint skillId, uint casterId, uint targetId, SkillCastRejectionCode rejectionCode)
            => Rejected(skillId, casterId, targetId, (byte)rejectionCode);

        /// <inheritdoc/>
        public bool Equals(SkillCastResult other)
            => SkillId == other.SkillId && CasterId == other.CasterId && TargetId == other.TargetId
               && CastAccepted == other.CastAccepted && RejectionCode == other.RejectionCode
               && DamageDealt == other.DamageDealt && HealAmount == other.HealAmount
               && IsCrit == other.IsCrit && IsKill == other.IsKill && NewCooldownTicks == other.NewCooldownTicks;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is SkillCastResult other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = SkillId.GetHashCode();
            h = (h * 397) ^ CasterId.GetHashCode();
            h = (h * 397) ^ TargetId.GetHashCode();
            h = (h * 397) ^ CastAccepted.GetHashCode();
            h = (h * 397) ^ RejectionCode;
            h = (h * 397) ^ DamageDealt;
            h = (h * 397) ^ HealAmount;
            h = (h * 397) ^ IsCrit.GetHashCode();
            h = (h * 397) ^ IsKill.GetHashCode();
            h = (h * 397) ^ NewCooldownTicks;
            return h;
        }

        /// <summary>Returns true if both results have identical field values.</summary>
        public static bool operator ==(SkillCastResult left, SkillCastResult right) => left.Equals(right);

        /// <summary>Returns true if the results differ in any field.</summary>
        public static bool operator !=(SkillCastResult left, SkillCastResult right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"SkillCastResult(Skill={SkillId}, Caster={CasterId}, Target={TargetId}, Accepted={CastAccepted}, Code={RejectionCode}, Damage={DamageDealt}, Heal={HealAmount}, Crit={IsCrit}, Kill={IsKill}, CooldownTicks={NewCooldownTicks})";
    }

    /// <summary>
    /// Body-level codec for <see cref="SkillCastResult"/> (28-byte body, little-endian). No allocation.
    /// </summary>
    public static class SkillCastResultCodec
    {
        /// <summary>Writes the 28-byte body at offset 0 of <paramref name="destination"/>.</summary>
        /// <param name="destination">At least <see cref="SkillCastResult.BodySize"/> bytes.</param>
        /// <param name="message">The result to write.</param>
        /// <returns><see cref="SkillCastResult.BodySize"/>, always.</returns>
        /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than 28 bytes; nothing is written.</exception>
        public static int WriteBody(Span<byte> destination, in SkillCastResult message)
        {
            if (destination.Length < SkillCastResult.BodySize)
            {
                throw new ArgumentException("destination must hold at least SkillCastResult.BodySize (28) bytes.", nameof(destination));
            }

            Span<byte> body = destination.Slice(0, SkillCastResult.BodySize);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(0, 4), message.SkillId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(4, 4), message.CasterId);
            BinaryPrimitives.WriteUInt32LittleEndian(body.Slice(8, 4), message.TargetId);
            body[12] = message.CastAccepted ? (byte)1 : (byte)0;
            body[13] = message.RejectionCode;
            BinaryPrimitives.WriteInt32LittleEndian(body.Slice(14, 4), message.DamageDealt);
            BinaryPrimitives.WriteInt32LittleEndian(body.Slice(18, 4), message.HealAmount);
            body[22] = message.IsCrit ? (byte)1 : (byte)0;
            body[23] = message.IsKill ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(body.Slice(24, 4), message.NewCooldownTicks);
            return SkillCastResult.BodySize;
        }

        /// <summary>
        /// Decodes the body. Returns <see langword="false"/> unless <paramref name="body"/> is exactly
        /// <see cref="SkillCastResult.BodySize"/> bytes. A bool byte is true when non-zero; the rejection code is
        /// passed through unchecked (the receiver range-checks it).
        /// </summary>
        /// <param name="body">The body bytes.</param>
        /// <param name="message">The decoded result, or default on failure.</param>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out SkillCastResult message)
        {
            if (body.Length != SkillCastResult.BodySize)
            {
                message = default;
                return false;
            }

            message = new SkillCastResult(
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8, 4)),
                body[12] != 0,
                body[13],
                BinaryPrimitives.ReadInt32LittleEndian(body.Slice(14, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(body.Slice(18, 4)),
                body[22] != 0,
                body[23] != 0,
                BinaryPrimitives.ReadInt32LittleEndian(body.Slice(24, 4)));
            return true;
        }
    }
}

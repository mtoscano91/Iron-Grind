using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message reporting a single damage instance (CR-NET-7's Message Schemas
    /// section). Sent to all zone clients except the attacker — the attacker's own client
    /// receives the separate <c>SelfDamageEvent</c> via the R-OD priority path instead (a distinct
    /// message not implemented by this story).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="BatchSubMessageCodec.WriteDamageEvent"/> /
    /// <see cref="BatchSubMessageCodec.TryReadDamageEvent"/>.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> the GDD states application <c>MessageTypeID</c>
    /// values (<c>0x0200–0xDFFF</c>) are "assigned in the Networking ADR", but ADR-004 does not
    /// enumerate a full per-message ID table — only the <c>0x0100–0x01FF</c> batch-header IDs are
    /// registered there. <c>0x0301</c> is adopted here because it is already used, unchanged,
    /// as the <c>attackerEntityId</c>/<c>DamageType</c> field example's <c>messageTypeId</c> in
    /// both <see cref="WireIdCodec"/>'s and <see cref="WireEnumCodec"/>'s existing XML doc-comment
    /// examples (Story 004) and in <c>WireProtocol_EntityIdEnumGuards_tests.cs</c>'s
    /// <c>SampleMessageTypeId</c> constant — i.e. this value is already the de-facto convention
    /// established elsewhere in this codebase for <c>DamageEvent</c>, just never previously
    /// declared as a named constant. This should be confirmed/formally registered in a future
    /// Networking ADR amendment or ID registry; treat it as provisional until then.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var damageEvent = new DamageEvent(
    ///     attackerEntityId: new EntityID(1), targetEntityId: new EntityID(2),
    ///     finalDamage: 42, isCrit: true, damageType: DamageType.Physical);
    /// Span&lt;byte&gt; buffer = stackalloc byte[DamageEvent.BatchSize];
    /// int written = BatchSubMessageCodec.WriteDamageEvent(buffer, in damageEvent);
    /// </code>
    /// </example>
    public readonly struct DamageEvent : IEquatable<DamageEvent>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+4+4+1+1 = 14 bytes.</summary>
        public const int BodySize = 14;

        /// <summary>Total wire size as an R-U batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 18 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="DamageEvent"/> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0x0301;

        /// <summary>The entity that dealt the damage. Must not be <see cref="EntityID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly EntityID AttackerEntityId;

        /// <summary>The entity that received the damage. Must not be <see cref="EntityID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly EntityID TargetEntityId;

        /// <summary>The server-computed final damage amount, after all mitigation.</summary>
        public readonly int FinalDamage;

        /// <summary><see langword="true"/> if this hit was a critical strike.</summary>
        public readonly bool IsCrit;

        /// <summary>The damage classification (CR-NET-7.9).</summary>
        public readonly DamageType DamageType;

        /// <summary>Initializes a new <see cref="DamageEvent"/>.</summary>
        /// <example>
        /// <code>
        /// var damageEvent = new DamageEvent(new EntityID(1), new EntityID(2), 42, true, DamageType.Physical);
        /// </code>
        /// </example>
        public DamageEvent(EntityID attackerEntityId, EntityID targetEntityId, int finalDamage, bool isCrit, DamageType damageType)
        {
            AttackerEntityId = attackerEntityId;
            TargetEntityId = targetEntityId;
            FinalDamage = finalDamage;
            IsCrit = isCrit;
            DamageType = damageType;
        }

        /// <inheritdoc/>
        public bool Equals(DamageEvent other)
            => AttackerEntityId == other.AttackerEntityId
            && TargetEntityId == other.TargetEntityId
            && FinalDamage == other.FinalDamage
            && IsCrit == other.IsCrit
            && DamageType == other.DamageType;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DamageEvent other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = AttackerEntityId.GetHashCode();
            h = (h * 397) ^ TargetEntityId.GetHashCode();
            h = (h * 397) ^ FinalDamage.GetHashCode();
            h = (h * 397) ^ IsCrit.GetHashCode();
            h = (h * 397) ^ DamageType.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both events have identical field values.</summary>
        public static bool operator ==(DamageEvent left, DamageEvent right) => left.Equals(right);

        /// <summary>Returns true if the events differ in any field.</summary>
        public static bool operator !=(DamageEvent left, DamageEvent right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"DamageEvent(Attacker={AttackerEntityId}, Target={TargetEntityId}, Damage={FinalDamage}, Crit={IsCrit}, Type={DamageType})";
    }
}

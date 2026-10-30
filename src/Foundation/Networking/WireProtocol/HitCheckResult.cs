using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message reporting the outcome of one server <c>CheckHit</c> call to the one
    /// player involved (the attacker for player attacks, the defender for monster attacks). Carried
    /// in the R-U batch's <see cref="RUBatchCategory.HitCheckResult"/> category. See
    /// <c>hit-detection.md</c> CR-HD-10 and <c>networking-wire-protocol.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> so the struct itself never allocates, matching
    /// <see cref="EntityHealthUpdate"/>. Encoded/decoded via
    /// <see cref="BatchSubMessageCodec.WriteHitCheckResult"/> /
    /// <see cref="BatchSubMessageCodec.TryReadHitCheckResult"/>.
    /// </para>
    /// <para>
    /// Wire layout (little-endian): attacker 4 bytes, target 4 bytes, result 1 byte, server tick
    /// 4 bytes = 13-byte body, 17 bytes in a batch.
    /// </para>
    /// <para>
    /// <see cref="MessageTypeId"/> <c>0x0306</c> sits in the combat cluster, next to
    /// <see cref="PartyMemberHealthUpdate"/> (<c>0x0305</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = new HitCheckResult(new EntityID(7), new EntityID(51), HitResult.MissFacing, 1000u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[HitCheckResult.BatchSize];
    /// int written = BatchSubMessageCodec.WriteHitCheckResult(buffer, in result);
    /// </code>
    /// </example>
    public readonly struct HitCheckResult : IEquatable<HitCheckResult>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+4+1+4 = 13 bytes.</summary>
        public const int BodySize = 13;

        /// <summary>Total wire size as an R-U batch sub-message: header + <see cref="BodySize"/> = 17 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="HitCheckResult"/>.</summary>
        public const ushort MessageTypeId = 0x0306;

        /// <summary>The entity that made the attack.</summary>
        public readonly EntityID AttackerEntityId;

        /// <summary>The entity that was attacked.</summary>
        public readonly EntityID TargetEntityId;

        /// <summary>The outcome of the check. One byte on the wire.</summary>
        public readonly HitResult Result;

        /// <summary>The server tick the check was resolved on.</summary>
        public readonly uint ServerTick;

        /// <summary>Initializes a new <see cref="HitCheckResult"/>.</summary>
        /// <example>
        /// <code>
        /// var r = new HitCheckResult(new EntityID(7), new EntityID(51), HitResult.Hit, 1000u);
        /// </code>
        /// </example>
        public HitCheckResult(EntityID attackerEntityId, EntityID targetEntityId, HitResult result, uint serverTick)
        {
            AttackerEntityId = attackerEntityId;
            TargetEntityId = targetEntityId;
            Result = result;
            ServerTick = serverTick;
        }

        /// <inheritdoc/>
        public bool Equals(HitCheckResult other)
            => AttackerEntityId == other.AttackerEntityId && TargetEntityId == other.TargetEntityId
               && Result == other.Result && ServerTick == other.ServerTick;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is HitCheckResult other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = AttackerEntityId.GetHashCode();
            h = (h * 397) ^ TargetEntityId.GetHashCode();
            h = (h * 397) ^ (int)Result;
            h = (h * 397) ^ ServerTick.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both results have identical field values.</summary>
        public static bool operator ==(HitCheckResult left, HitCheckResult right) => left.Equals(right);

        /// <summary>Returns true if the results differ in any field.</summary>
        public static bool operator !=(HitCheckResult left, HitCheckResult right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"HitCheckResult(Attacker={AttackerEntityId}, Target={TargetEntityId}, Result={Result}, ServerTick={ServerTick})";
    }
}

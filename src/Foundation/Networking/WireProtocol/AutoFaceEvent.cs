using System;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message telling every zone client that an entity's facing was snapped toward its auto-attack
    /// target (auto-attack-combat.md Rules 20-22, hit-detection.md CR-HD-7 / AC-HD-18). Carried in the R-U batch's
    /// <see cref="RUBatchCategory.AutoFaceEvent"/> category; facing is never replicated in the position packet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> so the struct itself never allocates. Encoded/decoded via
    /// <see cref="BatchSubMessageCodec.WriteAutoFaceEventBody"/> / <see cref="BatchSubMessageCodec.TryReadAutoFaceEvent"/>.
    /// Rotation and direction use the CR-NET-7.2 fixed-point guards of <see cref="WireFixedPointCodec"/>.
    /// </para>
    /// <para>
    /// Wire layout (little-endian): entity 4 bytes, rotation 4 x short (x32767) 8 bytes, direction 3 x short (x32767)
    /// 6 bytes = 18-byte body, 22 bytes in a batch. <see cref="MessageTypeId"/> <c>0x0307</c> follows
    /// <see cref="HitCheckResult"/> (<c>0x0306</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var evt = new AutoFaceEvent(new EntityID(7), Quaternion.LookRotation(Vector3.forward), Vector3.forward);
    /// Span&lt;byte&gt; body = stackalloc byte[AutoFaceEvent.BodySize];
    /// BatchSubMessageCodec.WriteAutoFaceEventBody(body, in evt);
    /// </code>
    /// </example>
    public readonly struct AutoFaceEvent : IEquatable<AutoFaceEvent>
    {
        /// <summary>Wire size of the body only: 4 + 8 + 6 = 18 bytes.</summary>
        public const int BodySize = 18;

        /// <summary>Total wire size as an R-U batch sub-message: header + <see cref="BodySize"/> = 22 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Wire <c>MessageTypeID</c> for <see cref="AutoFaceEvent"/>.</summary>
        public const ushort MessageTypeId = 0x0307;

        /// <summary>The entity whose facing snaps.</summary>
        public readonly EntityID EntityId;

        /// <summary>The facing rotation being applied.</summary>
        public readonly Quaternion Rotation;

        /// <summary>The unit movement direction being applied.</summary>
        public readonly Vector3 Direction;

        /// <summary>Initializes a new <see cref="AutoFaceEvent"/>.</summary>
        /// <param name="entityId">The entity whose facing snaps.</param>
        /// <param name="rotation">The facing rotation.</param>
        /// <param name="direction">The unit direction.</param>
        public AutoFaceEvent(EntityID entityId, Quaternion rotation, Vector3 direction)
        {
            EntityId = entityId;
            Rotation = rotation;
            Direction = direction;
        }

        /// <inheritdoc/>
        public bool Equals(AutoFaceEvent other)
            => EntityId == other.EntityId && Rotation.Equals(other.Rotation) && Direction.Equals(other.Direction);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is AutoFaceEvent other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ Rotation.GetHashCode();
            h = (h * 397) ^ Direction.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both events have identical field values.</summary>
        public static bool operator ==(AutoFaceEvent left, AutoFaceEvent right) => left.Equals(right);

        /// <summary>Returns true if the events differ in any field.</summary>
        public static bool operator !=(AutoFaceEvent left, AutoFaceEvent right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"AutoFaceEvent(Entity={EntityId}, Rotation={Rotation}, Direction={Direction})";
    }
}

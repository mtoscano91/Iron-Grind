using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// U-U Position-packet sub-message reporting one zone entity's authoritative position, as
    /// fixed-point centimeters (CR-NET-7's Message Schemas section). Sent via
    /// <see cref="PositionPacketWriter"/> (<c>TICK_BATCH_UU = 0x0102</c>) for every zone entity
    /// other than the receiver, sorted ascending by <see cref="EntityId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="BatchSubMessageCodec.WriteEntityPositionUpdate"/> /
    /// <see cref="BatchSubMessageCodec.TryReadEntityPositionUpdate"/>. <see cref="PosX"/>/
    /// <see cref="PosY"/>/<see cref="PosZ"/> are fixed-point at ×100 scale (centimeters) — this
    /// story does not re-derive the fixed-point encode/decode helpers already defined by
    /// <see cref="WireFixedPointCodec"/> for other position-like fields; the raw <see cref="short"/>
    /// values are stored/transmitted directly, matching the schema as specified in
    /// <c>networking-wire-protocol.md</c>.
    /// </para>
    /// <para>
    /// <b>Overflow policy:</b> unlike <see cref="CycleTimerBroadcast"/>, this packet may drop
    /// entries on overflow — highest <see cref="EntityId"/> first, after ascending sort (see
    /// <see cref="PositionPacketWriter"/> remarks).
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> same genuinely-unassigned situation as
    /// <see cref="CycleTimerBroadcast.MessageTypeId"/> — no de-facto convention exists elsewhere
    /// in this codebase for this sub-message. <c>0x0303</c> is chosen to sit adjacent to
    /// <see cref="DamageEvent.MessageTypeId"/> (<c>0x0301</c>) and
    /// <see cref="CycleTimerBroadcast.MessageTypeId"/> (<c>0x0302</c>). Flag for formal
    /// registration in a future Networking ADR amendment or ID registry.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var position = new EntityPositionUpdate(entityId: new EntityID(5), posX: 1234, posY: 0, posZ: -500);
    /// Span&lt;byte&gt; buffer = stackalloc byte[EntityPositionUpdate.BatchSize];
    /// int written = BatchSubMessageCodec.WriteEntityPositionUpdate(buffer, in position);
    /// </code>
    /// </example>
    public readonly struct EntityPositionUpdate : IEquatable<EntityPositionUpdate>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+2+2+2 = 10 bytes.</summary>
        public const int BodySize = 10;

        /// <summary>Total wire size as a batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 14 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="EntityPositionUpdate"/> — see type-level remarks (genuinely unassigned in the source GDD).</summary>
        public const ushort MessageTypeId = 0x0303;

        /// <summary>The entity whose position this reports. Must not be <see cref="EntityID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly EntityID EntityId;

        /// <summary>Fixed-point X position (×100, centimeters).</summary>
        public readonly short PosX;

        /// <summary>Fixed-point Y position (×100, centimeters).</summary>
        public readonly short PosY;

        /// <summary>Fixed-point Z position (×100, centimeters).</summary>
        public readonly short PosZ;

        /// <summary>Initializes a new <see cref="EntityPositionUpdate"/>.</summary>
        /// <example>
        /// <code>
        /// var position = new EntityPositionUpdate(new EntityID(5), 1234, 0, -500);
        /// </code>
        /// </example>
        public EntityPositionUpdate(EntityID entityId, short posX, short posY, short posZ)
        {
            EntityId = entityId;
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
        }

        /// <inheritdoc/>
        public bool Equals(EntityPositionUpdate other)
            => EntityId == other.EntityId && PosX == other.PosX && PosY == other.PosY && PosZ == other.PosZ;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EntityPositionUpdate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ PosX.GetHashCode();
            h = (h * 397) ^ PosY.GetHashCode();
            h = (h * 397) ^ PosZ.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both updates have identical field values.</summary>
        public static bool operator ==(EntityPositionUpdate left, EntityPositionUpdate right) => left.Equals(right);

        /// <summary>Returns true if the updates differ in any field.</summary>
        public static bool operator !=(EntityPositionUpdate left, EntityPositionUpdate right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"EntityPositionUpdate(EntityId={EntityId}, PosX={PosX}, PosY={PosY}, PosZ={PosZ})";
    }
}

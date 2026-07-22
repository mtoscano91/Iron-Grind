using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message reporting one entity's current/max HP, relevance-filtered per
    /// <c>networking-relevance-filter.md</c> RFR-1/RFR-2 (Networking Core Story 028). Carried in the
    /// R-U batch's <see cref="RUBatchCategory.EntityHealthUpdate"/> category via
    /// <see cref="PendingSubMessage"/> — see <see cref="RelevanceFilter"/>, which is the only
    /// production constructor of this type's <see cref="PendingSubMessage"/> wrapping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation for the struct itself, matching
    /// <see cref="DamageEvent"/>. Encoded/decoded via
    /// <see cref="BatchSubMessageCodec.WriteEntityHealthUpdate"/> /
    /// <see cref="BatchSubMessageCodec.TryReadEntityHealthUpdate"/>. This type doubles as both the
    /// wire schema and the domain-level input <see cref="RelevanceFilter.BuildHealthUpdateSubMessages"/>
    /// accepts for its self-slot and target-slot parameters (RFR-1) — the same "struct is both the
    /// wire payload and the value callers construct" pattern <see cref="DamageEvent"/> and
    /// <see cref="GoldSyncEvent"/> already establish in this folder, deliberately reused here rather
    /// than inventing a separate domain-only type.
    /// </para>
    /// <para>
    /// <b>Two slots share this type (RFR-1):</b> the self slot (always present, <see cref="EntityId"/>
    /// must never be <see cref="EntityID.Invalid"/>) and the target slot (0 or 1 present —
    /// <see cref="EntityID.Invalid"/> as <see cref="EntityId"/> is the caller-facing sentinel meaning
    /// "no target," matching the <c>SetTarget</c> RPC's own wire convention in RFR-3a: "EntityID = 0
    /// means deselect current target." <see cref="RelevanceFilter"/> never serializes an instance
    /// with <see cref="EntityID.Invalid"/> onto the wire — it is only ever used as the "no target"
    /// signal on the input side.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> <c>networking-wire-protocol.md</c> defines
    /// this sub-message's body/batch sizes (12/16 bytes) but — like <see cref="CycleTimerBroadcast"/>
    /// and <see cref="EntityPositionUpdate"/> before it — does not assign a concrete wire ID.
    /// <c>0x0304</c> is chosen to sit adjacent to the existing <c>0x0301</c>-<c>0x0303</c> combat
    /// cluster (<see cref="DamageEvent"/>, <see cref="CycleTimerBroadcast"/>,
    /// <see cref="EntityPositionUpdate"/>). Flag for formal registration in a future Networking ADR
    /// amendment or ID registry before cross-team/client implementations rely on this exact value.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var selfSlot = new EntityHealthUpdate(entityId: new EntityID(1), currentHP: 750, maxHP: 1000);
    /// Span&lt;byte&gt; buffer = stackalloc byte[EntityHealthUpdate.BatchSize];
    /// int written = BatchSubMessageCodec.WriteEntityHealthUpdate(buffer, in selfSlot);
    /// </code>
    /// </example>
    public readonly struct EntityHealthUpdate : IEquatable<EntityHealthUpdate>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+4+4 = 12 bytes.</summary>
        public const int BodySize = 12;

        /// <summary>Total wire size as an R-U batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 16 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="EntityHealthUpdate"/> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0x0304;

        /// <summary>
        /// The entity this HP snapshot describes. On the wire, must not be <see cref="EntityID.Invalid"/>
        /// (CR-NET-7.3 zero-write guard, enforced by <see cref="WireIdCodec.SerializeEntityId"/>). As
        /// a <see cref="RelevanceFilter"/> target-slot input, <see cref="EntityID.Invalid"/> is the
        /// caller-facing "no target" sentinel — see type-level remarks.
        /// </summary>
        public readonly EntityID EntityId;

        /// <summary>The entity's current HP (<c>FloorToInt</c> per CR-NET-7.2).</summary>
        public readonly int CurrentHP;

        /// <summary>The entity's max HP (required to render HP bar fraction in HUD).</summary>
        public readonly int MaxHP;

        /// <summary>Initializes a new <see cref="EntityHealthUpdate"/>.</summary>
        /// <example>
        /// <code>
        /// var ehu = new EntityHealthUpdate(new EntityID(1), currentHP: 750, maxHP: 1000);
        /// </code>
        /// </example>
        public EntityHealthUpdate(EntityID entityId, int currentHP, int maxHP)
        {
            EntityId = entityId;
            CurrentHP = currentHP;
            MaxHP = maxHP;
        }

        /// <inheritdoc/>
        public bool Equals(EntityHealthUpdate other)
            => EntityId == other.EntityId && CurrentHP == other.CurrentHP && MaxHP == other.MaxHP;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is EntityHealthUpdate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ CurrentHP.GetHashCode();
            h = (h * 397) ^ MaxHP.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both updates have identical field values.</summary>
        public static bool operator ==(EntityHealthUpdate left, EntityHealthUpdate right) => left.Equals(right);

        /// <summary>Returns true if the updates differ in any field.</summary>
        public static bool operator !=(EntityHealthUpdate left, EntityHealthUpdate right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"EntityHealthUpdate(EntityId={EntityId}, CurrentHP={CurrentHP}, MaxHP={MaxHP})";
    }
}

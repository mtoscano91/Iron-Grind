using System;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// R-U batch sub-message reporting one party member's current/max HP and MP (Networking Core
    /// Story 028, <c>networking-relevance-filter.md</c> RFR-1). Carried in the R-U batch's
    /// <see cref="RUBatchCategory.PartyMemberHealthUpdate"/> category via <see cref="PendingSubMessage"/>
    /// — see <see cref="RelevanceFilter"/>, which is the only production constructor of this type's
    /// <see cref="PendingSubMessage"/> wrapping. Sent per-client about that client's own party members
    /// only — never zone-wide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation for the struct itself, matching
    /// <see cref="DamageEvent"/> and <see cref="EntityHealthUpdate"/>. Encoded/decoded via
    /// <see cref="BatchSubMessageCodec.WritePartyMemberHealthUpdate"/> /
    /// <see cref="BatchSubMessageCodec.TryReadPartyMemberHealthUpdate"/>. This type doubles as both
    /// the wire schema and the domain-level input <see cref="RelevanceFilter.BuildHealthUpdateSubMessages"/>
    /// accepts for its <c>partyMembers</c> parameter — see <see cref="EntityHealthUpdate"/>'s remarks
    /// for the rationale (the established <see cref="DamageEvent"/>/<see cref="GoldSyncEvent"/>
    /// dual-role pattern, reused rather than inventing a separate "snapshot" type).
    /// </para>
    /// <para>
    /// <c>maxHP</c>/<c>maxMP</c> are required fields (not cached separately) because either may
    /// change mid-session via level-up stat grants or buff effects — per
    /// <c>networking-wire-protocol.md</c>'s own schema note.
    /// </para>
    /// <para>
    /// <b>Dead party members remain in this set (EC-RFR-2):</b> a party member with <c>CurrentHP == 0</c>
    /// is not excluded by <see cref="RelevanceFilter"/> — the Party System (not yet implemented) governs
    /// when dead members leave the caller-supplied party list. This type has no opinion on the death
    /// state itself; it only carries whatever HP/MP values the caller supplies.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> <c>networking-wire-protocol.md</c> defines
    /// this sub-message's body/batch sizes (20/24 bytes) but — like <see cref="EntityHealthUpdate"/>
    /// before it — does not assign a concrete wire ID. <c>0x0305</c> is chosen to sit adjacent to the
    /// existing <c>0x0301</c>-<c>0x0304</c> combat/HP cluster. Flag for formal registration in a
    /// future Networking ADR amendment or ID registry before cross-team/client implementations rely
    /// on this exact value.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var member = new PartyMemberHealthUpdate(
    ///     entityId: new EntityID(2), currentHP: 500, maxHP: 500, currentMP: 100, maxMP: 100);
    /// Span&lt;byte&gt; buffer = stackalloc byte[PartyMemberHealthUpdate.BatchSize];
    /// int written = BatchSubMessageCodec.WritePartyMemberHealthUpdate(buffer, in member);
    /// </code>
    /// </example>
    public readonly struct PartyMemberHealthUpdate : IEquatable<PartyMemberHealthUpdate>
    {
        /// <summary>Wire size of the body only (excludes the 4-byte sub-message header): 4+4+4+4+4 = 20 bytes.</summary>
        public const int BodySize = 20;

        /// <summary>Total wire size as an R-U batch sub-message: <see cref="BatchSubMessageFraming.SubMessageHeaderSize"/> + <see cref="BodySize"/> = 24 bytes.</summary>
        public const int BatchSize = BatchSubMessageFraming.SubMessageHeaderSize + BodySize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="PartyMemberHealthUpdate"/> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0x0305;

        /// <summary>The party member's entity. Must not be <see cref="EntityID.Invalid"/> (CR-NET-7.3 zero-write guard).</summary>
        public readonly EntityID EntityId;

        /// <summary>The party member's current HP (<c>FloorToInt</c> per CR-NET-7.2).</summary>
        public readonly int CurrentHP;

        /// <summary>The party member's max HP (required to render HP bar fraction in HUD).</summary>
        public readonly int MaxHP;

        /// <summary>The party member's current MP.</summary>
        public readonly int CurrentMP;

        /// <summary>The party member's max MP (required to render MP bar fraction in HUD).</summary>
        public readonly int MaxMP;

        /// <summary>Initializes a new <see cref="PartyMemberHealthUpdate"/>.</summary>
        /// <example>
        /// <code>
        /// var member = new PartyMemberHealthUpdate(new EntityID(2), 500, 500, 100, 100);
        /// </code>
        /// </example>
        public PartyMemberHealthUpdate(EntityID entityId, int currentHP, int maxHP, int currentMP, int maxMP)
        {
            EntityId = entityId;
            CurrentHP = currentHP;
            MaxHP = maxHP;
            CurrentMP = currentMP;
            MaxMP = maxMP;
        }

        /// <inheritdoc/>
        public bool Equals(PartyMemberHealthUpdate other)
            => EntityId == other.EntityId
            && CurrentHP == other.CurrentHP
            && MaxHP == other.MaxHP
            && CurrentMP == other.CurrentMP
            && MaxMP == other.MaxMP;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PartyMemberHealthUpdate other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = EntityId.GetHashCode();
            h = (h * 397) ^ CurrentHP.GetHashCode();
            h = (h * 397) ^ MaxHP.GetHashCode();
            h = (h * 397) ^ CurrentMP.GetHashCode();
            h = (h * 397) ^ MaxMP.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both updates have identical field values.</summary>
        public static bool operator ==(PartyMemberHealthUpdate left, PartyMemberHealthUpdate right) => left.Equals(right);

        /// <summary>Returns true if the updates differ in any field.</summary>
        public static bool operator !=(PartyMemberHealthUpdate left, PartyMemberHealthUpdate right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"PartyMemberHealthUpdate(EntityId={EntityId}, CurrentHP={CurrentHP}, MaxHP={MaxHP}, CurrentMP={CurrentMP}, MaxMP={MaxMP})";
    }
}

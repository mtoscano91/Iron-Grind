namespace IronGrind.Networking
{
    /// <summary>
    /// The nine sub-message categories that make up the Path 2a Reliable-Unordered (R-U) batch
    /// packet (CR-NET-7.7). Declared in <b>canonical write order</b> — the order
    /// <see cref="RUBatchWriter"/> serializes categories into a fresh batch, highest priority
    /// first (CR-NET-7.7: "Writing highest-priority sub-messages first prevents buffer exhaustion
    /// from lower-priority messages blocking critical events").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Write order is not the same as drop order.</b> When the batch overflows
    /// <see cref="RUBatchWriter.MAX_MESSAGE_BODY_BYTES"/>, whole categories are evicted in a
    /// separately-specified priority order (lowest priority dropped first) that is <i>not</i>
    /// simply the reverse of this enum's declaration order — see
    /// <see cref="RUBatchWriter"/>'s remarks for the drop-order table and the rationale for why
    /// the two orderings are independently specified in the GDD rather than derived from one
    /// another (e.g. <see cref="ConnectionQualityUpdate"/> is written last but is also the last
    /// category dropped, not the first).
    /// </para>
    /// <para>
    /// <see cref="DamageEvent"/> (value 0) is never dropped at the category level — it is bounded
    /// by its own separate intra-class overflow policy instead (see
    /// <see cref="RUBatchWriter"/> remarks). Concrete typed schemas exist in this codebase only
    /// for <see cref="DamageEvent"/> and <see cref="GoldSyncEvent"/> (both owned by
    /// <c>networking-wire-protocol.md</c> itself). The remaining seven categories belong to
    /// systems not yet implemented in this codebase (Skill System, Party System, Loot Table
    /// System, Client-Side Prediction) and are carried generically as opaque, already-serialized
    /// payloads via <see cref="PendingSubMessage"/> until those systems' own stories define
    /// concrete schemas.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pending = new PendingSubMessage(RUBatchCategory.SkillCastResult, messageTypeId: 0x0A10, payload);
    /// </code>
    /// </example>
    public enum RUBatchCategory : byte
    {
        /// <summary>Beat-resolution damage events. Concrete schema: <see cref="DamageEvent"/>. Never dropped at the category level.</summary>
        DamageEvent = 0,

        /// <summary>Entity health updates, relevance-filtered (<c>networking-relevance-filter.md</c>, Story 028 — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        EntityHealthUpdate = 1,

        /// <summary>Party member health updates (Party System — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        PartyMemberHealthUpdate = 2,

        /// <summary>Per-tick authoritative self-position delivery for client-side prediction reconciliation (Client-Side Prediction — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        SelfPositionUpdate = 3,

        /// <summary>Skill cast outcome (Skill System — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        SkillCastResult = 4,

        /// <summary>Absolute gold balance sync. Concrete schema: <see cref="GoldSyncEvent"/>.</summary>
        GoldSyncEvent = 5,

        /// <summary>Skill cooldown state change (Skill System — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        SkillCooldownUpdate = 6,

        /// <summary>Real-time auction bid feed (Loot Table System — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        LootBidUpdate = 7,

        /// <summary>OWL-threshold-crossing connection quality notice (B-NP-4 — not yet implemented). Carried via <see cref="PendingSubMessage"/>.</summary>
        ConnectionQualityUpdate = 8,
    }
}

namespace IronGrind.Networking
{
    /// <summary>
    /// The eleven sub-message categories that make up the Path 2a Reliable-Unordered (R-U) batch
    /// packet (CR-NET-7.7). Values 0-8 are declared in <b>canonical write order</b>;
    /// <c>HitCheckResult</c> (9) and <c>AutoFaceEvent</c> (10) were appended later to keep existing values stable, so their
    /// write positions (HitCheckResult, then AutoFaceEvent, between <see cref="SkillCooldownUpdate"/> and <see cref="LootBidUpdate"/>)
    /// are set by <c>RUBatchWriter</c>'s write-order table, not by its numeric value. That table is
    /// the order <c>RUBatchWriter</c> serializes categories into a fresh batch, highest priority
    /// first (CR-NET-7.7: "Writing highest-priority sub-messages first prevents buffer exhaustion
    /// from lower-priority messages blocking critical events").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Write order is not the same as drop order.</b> When the batch overflows
    /// <c>RUBatchWriter.MAX_MESSAGE_BODY_BYTES</c>, whole categories are evicted in a
    /// separately-specified priority order (lowest priority dropped first) that is <i>not</i>
    /// simply the reverse of this enum's declaration order — see
    /// <c>RUBatchWriter</c>'s remarks for the drop-order table and the rationale for why
    /// the two orderings are independently specified in the GDD rather than derived from one
    /// another (e.g. <see cref="ConnectionQualityUpdate"/> is written last but is also the last
    /// category dropped, not the first).
    /// </para>
    /// <para>
    /// <see cref="DamageEvent"/> (value 0) is never dropped at the category level — it is bounded
    /// by its own separate intra-class overflow policy instead (see
    /// <c>RUBatchWriter</c> remarks). Concrete typed schemas exist in this codebase only
    /// for <see cref="DamageEvent"/> and <see cref="GoldSyncEvent"/> (both owned by
    /// <c>networking-wire-protocol.md</c> itself). The remaining nine categories are carried
    /// generically as opaque, already-serialized payloads via <c>PendingSubMessage</c>. Most belong
    /// to systems not yet implemented in this codebase (Skill System, Party System, Loot Table
    /// System, Client-Side Prediction) until those systems' own stories define concrete schemas;
    /// <see cref="IronGrind.Networking.HitCheckResult"/> and <see cref="IronGrind.Networking.AutoFaceEvent"/> (appended the same way) have their schemas and take the same opaque path.
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

        /// <summary>Entity health updates, relevance-filtered (<c>networking-relevance-filter.md</c>, Story 028 — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        EntityHealthUpdate = 1,

        /// <summary>Party member health updates (Party System — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        PartyMemberHealthUpdate = 2,

        /// <summary>Per-tick authoritative self-position delivery for client-side prediction reconciliation (Client-Side Prediction — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        SelfPositionUpdate = 3,

        /// <summary>Skill cast outcome (Skill System — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        SkillCastResult = 4,

        /// <summary>Absolute gold balance sync. Concrete schema: <see cref="GoldSyncEvent"/>.</summary>
        GoldSyncEvent = 5,

        /// <summary>Skill cooldown state change (Skill System — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        SkillCooldownUpdate = 6,

        /// <summary>Real-time auction bid feed (Loot Table System — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        LootBidUpdate = 7,

        /// <summary>OWL-threshold-crossing connection quality notice (B-NP-4 — not yet implemented). Carried via <c>PendingSubMessage</c>.</summary>
        ConnectionQualityUpdate = 8,

        /// <summary>Hit/miss outcome of one <c>CheckHit</c> call, sent to the player involved (CR-HD-10). Appended after <see cref="ConnectionQualityUpdate"/> so existing values do not change; written between <see cref="SkillCooldownUpdate"/> and <see cref="LootBidUpdate"/>. Carried via <c>PendingSubMessage</c>; schema: <see cref="IronGrind.Networking.HitCheckResult"/>.</summary>
        HitCheckResult = 9,

        /// <summary>Facing snap toward the auto-attack target, sent to every zone client (auto-attack-combat.md Rules 20-22). Appended after <see cref="HitCheckResult"/> so existing values do not change; written right after it. Carried via <c>PendingSubMessage</c>; schema: <see cref="IronGrind.Networking.AutoFaceEvent"/>.</summary>
        AutoFaceEvent = 10,
    }
}

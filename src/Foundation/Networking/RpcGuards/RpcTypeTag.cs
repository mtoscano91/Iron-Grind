namespace IronGrind.Networking
{
    /// <summary>
    /// Identifies which named rate-limit bucket an inbound RPC belongs to, for
    /// <see cref="CrossCuttingRpcGuardChain"/>'s per-entity, per-RPC-type rate limiting (Cross-Cutting
    /// Constraint 3, Networking Core Story 010). This is a tag on the generic
    /// <see cref="InboundRpcDescriptor"/> carrier — not a real message class. Neither
    /// <c>AllocateFreePointRequest</c> nor <c>NotifySkillUsed</c> exists as a concrete type in this
    /// codebase yet (owned by the not-yet-started Leveling System and Skill System epics
    /// respectively); this enum lets the guard chain's rate-limit logic be proven now, against a
    /// test double, without inventing placeholder message types for either system (mirrors Story
    /// 007's <c>PendingSubMessage</c> category-tag approach for the same forward-dependency shape).
    /// </summary>
    /// <remarks>
    /// Declared as its own enum (rather than a <see langword="string"/> tag, which the story's
    /// Implementation Notes explicitly permit as an alternative) to keep the rate-limit lookup
    /// allocation-free and comparison-free on the hot path — this guard chain runs on every inbound
    /// RPC before it reaches game logic, a genuine hot path at scale (per the story's own Performance
    /// Budget: "no allocation on the reject path").
    /// </remarks>
    /// <example>
    /// <code>
    /// var descriptor = new InboundRpcDescriptor(
    ///     clientId: 7,
    ///     senderEntityId: myEntityId,
    ///     rpcTypeTag: RpcTypeTag.AllocateFreePoint,
    ///     currentTick: tickLoop.ServerTickNumber);
    /// </code>
    /// </example>
    public enum RpcTypeTag : byte
    {
        /// <summary>
        /// The (not-yet-implemented) Leveling System's <c>AllocateFreePointRequest</c> RPC.
        /// Rate-limited via <see cref="CrossCuttingRpcGuardChain.ALLOC_FREE_POINT_RATE_LIMIT_MS"/>
        /// (AC-NC-20).
        /// </summary>
        AllocateFreePoint = 0,

        /// <summary>
        /// The (not-yet-implemented) Skill System's <c>NotifySkillUsed</c> RPC. Rate-limited via
        /// <see cref="CrossCuttingRpcGuardChain.NOTIFY_SKILL_USED_RATE_LIMIT_MS"/> (AC-NC-46).
        /// </summary>
        NotifySkillUsed = 1,

        /// <summary>
        /// The <c>SetTarget</c> RPC (Networking Core Story 029, <c>networking-relevance-filter.md</c>
        /// RFR-3a). Per <c>networking-core.md</c>'s Cross-Cutting Constraint 3 ("All other RPCs: no
        /// rate limit specified at MVP"), <c>SetTarget</c> has no rate limit —
        /// <see cref="CrossCuttingRpcGuardChain"/>'s internal <c>GetRequiredTickGap</c> returns 0 for
        /// this tag.
        /// </summary>
        SetTarget = 2,
    }
}

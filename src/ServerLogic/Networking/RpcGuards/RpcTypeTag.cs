namespace IronGrind.Networking
{
    /// <summary>
    /// Identifies which named rate-limit bucket an inbound RPC belongs to, for
    /// <see cref="CrossCuttingRpcGuardChain"/>'s per-entity, per-RPC-type rate limiting (Cross-Cutting
    /// Constraint 3, Networking Core Story 010). This is a tag on the generic
    /// <see cref="InboundRpcDescriptor"/> carrier — not a real message class. The Skill System's
    /// message for <c>NotifySkillUsed</c> is <see cref="SkillCastRequest"/> (Skill System Story 004
    /// registers it with this tag). <c>AllocateFreePointRequest</c> has no concrete type yet (owned
    /// by the not-yet-started Leveling System epic); this enum lets the guard chain's rate-limit logic
    /// be proven against a test double, without inventing placeholder message types (mirrors Story
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
        /// The Skill System's cast RPC (<see cref="SkillCastRequest"/>, tag name from the GDD's <c>NotifySkillUsed</c>). Rate-limited via
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

        /// <summary>
        /// <c>EnhancementAttemptRequest</c> (Enhancement Story 010, ADR-014 Decision 1). No rate limit.
        /// </summary>
        EnhancementAttemptRequest = 3,

        /// <summary>
        /// <c>EnhancementPreviewRequest</c> (Enhancement Story 010, ADR-014 Decision 1). No rate limit
        /// (the wire protocol's general ADR-001 10 req/s note has no tag).
        /// </summary>
        EnhancementPreviewRequest = 4,

        /// <summary><c>CancelEnhancement</c> (Enhancement Story 010, ADR-014 Decision 1). No rate limit.</summary>
        CancelEnhancement = 5,

        /// <summary><c>OpenNPCInteraction</c> (Enhancement Story 010, ADR-014 Decision 1). No rate limit.</summary>
        OpenNPCInteraction = 6,

        /// <summary><c>CloseNPCInteraction</c> (Enhancement Story 010, ADR-014 Decision 1). No rate limit.</summary>
        CloseNPCInteraction = 7,

        /// <summary><c>EquipRequest</c> (Equipment Story 008, ADR-014 Decision 1). No rate limit.</summary>
        EquipRequest = 8,

        /// <summary><c>MovementIntentMessage</c> (Movement Story 004, ADR-014 Decision 1). No rate limit: two per tick are allowed (AC-MOV-28).</summary>
        MovementIntent = 9,

        /// <summary><c>SetAutoAttack</c> (Auto-Attack Combat Story 006, ADR-014 Decision 1). No rate limit (gap 0).</summary>
        SetAutoAttack = 10,
    }
}

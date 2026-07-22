using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// A generic carrier describing one inbound RPC, for <see cref="CrossCuttingRpcGuardChain.Evaluate"/>
    /// to run its four guards against (Networking Core Story 010). No concrete RPC message type
    /// exists in this codebase yet — <c>AllocateFreePointRequest</c> and <c>NotifySkillUsed</c> are
    /// owned by the not-yet-started Leveling System and Skill System epics respectively. A future
    /// story that implements either RPC's real handler constructs one of these per inbound call and
    /// passes it through <see cref="CrossCuttingRpcGuardChain.Evaluate"/> before touching game logic.
    /// </summary>
    /// <remarks>
    /// <b>Deviation from the story's literal Implementation Notes text, called out explicitly:</b>
    /// the story describes this carrier as <c>(EntityID senderEntityId, string rpcTypeTag, uint
    /// currentTick)</c> — three fields, no client/connection identity. That shape cannot express two
    /// of this story's own guards: the ownership check (AC-NC-02, "an <c>EntityID</c> it does not
    /// own") and the session-ready gate (AC-NC-23) both require knowing <i>which connection sent the
    /// RPC</i>, distinct from the <c>EntityID</c> the RPC claims — exactly the <c>clientId ↔
    /// EntityID</c> mapping ADR-004 Decision 4 describes. <see cref="ClientId"/> is added to make the
    /// descriptor complete enough to run all four guards; this was raised and approved before
    /// implementation (not silently added).
    /// </remarks>
    /// <example>
    /// <code>
    /// var descriptor = new InboundRpcDescriptor(
    ///     clientId: connection.ClientId,
    ///     senderEntityId: claimedEntityId,
    ///     rpcTypeTag: RpcTypeTag.AllocateFreePoint,
    ///     currentTick: tickLoop.ServerTickNumber);
    ///
    /// RpcGuardResult result = guardChain.Evaluate(descriptor);
    /// if (result != RpcGuardResult.Accepted)
    /// {
    ///     return; // dropped — never forwarded to game logic, never queued
    /// }
    /// // ... forward to the real AllocateFreePointRequest handler ...
    /// </code>
    /// </example>
    public readonly struct InboundRpcDescriptor
    {
        /// <summary>
        /// The NGO connection identity that physically sent this RPC (ADR-004 Decision 4). Used by
        /// the session-ready gate and the ownership check — distinct from
        /// <see cref="SenderEntityId"/>, which is only the <c>EntityID</c> the RPC's payload claims.
        /// </summary>
        public readonly uint ClientId;

        /// <summary>
        /// The <see cref="EntityID"/> claimed by this RPC's payload (e.g. the entity to allocate a
        /// free point to, or the entity that used a skill). This is <i>not</i> verified to belong to
        /// <see cref="ClientId"/> until <see cref="CrossCuttingRpcGuardChain.Evaluate"/>'s ownership
        /// guard runs.
        /// </summary>
        public readonly EntityID SenderEntityId;

        /// <summary>Which named rate-limit bucket applies to this RPC (Cross-Cutting Constraint 3).</summary>
        public readonly RpcTypeTag RpcTypeTag;

        /// <summary>
        /// The server tick at which this RPC is being evaluated — always
        /// <see cref="ServerTickLoop.ServerTickNumber"/> at the moment of receipt, never a
        /// wall-clock value (matches this folder's tick-based-only rate-limiting convention).
        /// </summary>
        public readonly uint CurrentTick;

        /// <summary>Constructs a new inbound RPC descriptor.</summary>
        /// <param name="clientId">The connection identity that sent this RPC.</param>
        /// <param name="senderEntityId">The <see cref="EntityID"/> claimed by the RPC's payload.</param>
        /// <param name="rpcTypeTag">Which named rate-limit bucket applies.</param>
        /// <param name="currentTick">The current <see cref="ServerTickLoop.ServerTickNumber"/>.</param>
        public InboundRpcDescriptor(uint clientId, EntityID senderEntityId, RpcTypeTag rpcTypeTag, uint currentTick)
        {
            ClientId = clientId;
            SenderEntityId = senderEntityId;
            RpcTypeTag = rpcTypeTag;
            CurrentTick = currentTick;
        }
    }
}

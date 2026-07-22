namespace IronGrind.Networking
{
    /// <summary>
    /// The outcome of running an inbound RPC through <see cref="CrossCuttingRpcGuardChain.Evaluate"/>
    /// (Networking Core Story 010). Exactly one non-<see cref="Accepted"/> value corresponds to each
    /// of this story's four blocking acceptance criteria — see each member's doc comment.
    /// </summary>
    /// <remarks>
    /// Every rejection value means the same thing operationally: the RPC is dropped before reaching
    /// game logic, never queued for later processing, and an anomaly is logged
    /// (<see cref="System.Diagnostics.Debug"/>-style <c>Debug.LogWarning</c> — see
    /// <see cref="CrossCuttingRpcGuardChain"/>). The distinct values exist so tests and callers can
    /// assert on the specific reason without string-matching a log message.
    /// </remarks>
    public enum RpcGuardResult : byte
    {
        /// <summary>The RPC passed all four guards and may be forwarded to game logic.</summary>
        Accepted = 0,

        /// <summary>
        /// The RPC's <see cref="InboundRpcDescriptor.SenderEntityId"/> is not registered to any
        /// client in the current zone session (Cross-Cutting Constraint 1) — this is a different
        /// failure than <see cref="RejectedNotOwner"/>: the entity is not known to the server at
        /// all, as opposed to known but owned by a different client.
        /// </summary>
        RejectedUnknownEntity = 1,

        /// <summary>
        /// The sending client has not yet received <c>SessionReady</c> (Cross-Cutting Constraint 2,
        /// AC-NC-23). The RPC is dropped, not queued for once <c>SessionReady</c> is later sent.
        /// </summary>
        RejectedSessionNotReady = 2,

        /// <summary>
        /// The RPC arrived before the minimum inter-request interval for its
        /// <see cref="InboundRpcDescriptor.RpcTypeTag"/> had elapsed (Cross-Cutting Constraint 3,
        /// AC-NC-20, AC-NC-46). Excess requests are rejected, never queued for later processing.
        /// </summary>
        RejectedRateLimited = 3,

        /// <summary>
        /// The RPC's <see cref="InboundRpcDescriptor.SenderEntityId"/> is registered to a client
        /// other than <see cref="InboundRpcDescriptor.ClientId"/> — the entity is known to the
        /// server, but the sending connection does not own it (AC-NC-02).
        /// </summary>
        RejectedNotOwner = 4,
    }
}

namespace IronGrind.Networking
{
    /// <summary>
    /// The outcome of running one irreversible-outcome request through
    /// <see cref="CommitBeforeBroadcastSequencer.Execute{TOutcome}"/> (Networking Core Story 011,
    /// CR-NET-5). Exactly one non-<see cref="Committed"/> value corresponds to each of this story's
    /// two negative paths — see each member's doc comment.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RpcGuardResult"/> (Story 010): that enum reports why an RPC was
    /// dropped before reaching game logic at all; this enum reports how a request that <i>did</i>
    /// reach game logic concluded its commit-before-broadcast sequence. The two are never compared
    /// to each other and a caller may use both in the same request's lifecycle (guard chain first,
    /// then this sequencer for the request's irreversible-outcome step).
    /// </remarks>
    public enum CommitBeforeBroadcastResult : byte
    {
        /// <summary>
        /// The request was valid, the outcome was computed, the persistence write was confirmed
        /// durable, and the outcome message was broadcast. The happy path (CR-NET-5.3 steps 1-6).
        /// </summary>
        Committed = 0,

        /// <summary>
        /// <c>validateRequest</c> returned <see langword="false"/> (CR-NET-5.3 step 2). The request
        /// was rejected immediately — no acknowledgment, no outcome computation, no persistence
        /// write, and no broadcast ever occurred.
        /// </summary>
        RejectedInvalidRequest = 1,

        /// <summary>
        /// <c>persistOutcome</c> returned <see langword="false"/>, or threw an exception (both are
        /// treated identically — see <see cref="CommitBeforeBroadcastSequencer"/> remarks on
        /// exception-safety hardening). No outcome message was broadcast. The full write-failure
        /// protocol ran: caller rollback, client disconnect, critical infrastructure alert, session
        /// preservation for <see cref="CommitBeforeBroadcastSequencer.SESSION_TTL_SECONDS"/> — in
        /// that order, per CR-CP-5's numbered protocol definition (see
        /// <see cref="CommitBeforeBroadcastSequencer"/> remarks for why that order is authoritative
        /// over CR-NET-5.5's prose summary).
        /// </summary>
        PersistenceFailed = 2,
    }
}

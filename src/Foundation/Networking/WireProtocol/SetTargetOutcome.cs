namespace IronGrind.Networking
{
    /// <summary>
    /// The outcome of running a <see cref="SetTarget"/> RPC through
    /// <see cref="TargetSlotTracker.ProcessSetTarget"/> (Networking Core Story 029, RFR-3/RFR-3a).
    /// </summary>
    /// <remarks>
    /// Every rejection value means the RPC was silently discarded — the target slot is left
    /// unchanged, and no client-visible error is ever sent (RFR-3a/RFR-5). The distinct values exist
    /// so tests and callers can assert on the specific reason without string-matching a log message,
    /// matching <see cref="RpcGuardResult"/>'s own convention in this folder.
    /// </remarks>
    public enum SetTargetOutcome : byte
    {
        /// <summary>
        /// The target slot was updated — either a deselect (<c>targetEntityId == EntityID.Invalid</c>)
        /// or a valid non-self target (RFR-3/RFR-4).
        /// </summary>
        Accepted = 0,

        /// <summary>
        /// Rejected silently: <c>targetEntityId</c> equals the caller's own <c>EntityID</c>
        /// (RFR-5/EC-RFR-4). Target slot unchanged; a <c>SelfTargetAttempt</c> advisory anomaly is
        /// logged.
        /// </summary>
        RejectedSelfTarget = 1,

        /// <summary>
        /// Rejected silently: <c>targetEntityId</c> is not present in the caller-supplied valid zone
        /// EntityIDs (RFR-3a). Target slot unchanged; an <c>InvalidTargetEntityId</c> advisory
        /// anomaly is logged.
        /// </summary>
        RejectedInvalidTarget = 2,
    }
}

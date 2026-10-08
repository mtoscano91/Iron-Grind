namespace IronGrind.Networking
{
    /// <summary>
    /// What <see cref="IIrreversibleOutcomeCoordinator.Begin{TOutcome, TResult}"/> did with a request
    /// (ADR-011 Decision 5, Networking Core Story 031).
    /// </summary>
    public enum IrreversibleOutcomeBeginResult : byte
    {
        /// <summary>The outcome was applied in memory, the write started and tracked, and the gate is closed until it completes.</summary>
        Started = 0,

        /// <summary>Validation returned false. Nothing else ran and the gate was not closed.</summary>
        RejectedInvalidRequest = 1,

        /// <summary>The character's gate was already closed (one write in flight). No delegate was called.</summary>
        RejectedWriteInFlight = 2,

        /// <summary>
        /// The write could not be started (the delegate threw or returned no task). The failure
        /// protocol has already run and the gate is open again.
        /// </summary>
        PersistenceFailed = 3
    }
}

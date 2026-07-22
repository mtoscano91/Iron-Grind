namespace IronGrind.Networking
{
    /// <summary>
    /// The outcome of querying whether a character has an active respec Phase 1 item reservation on
    /// reconnect, and whether its own independent 30-second TTL is still valid (CR-NET-6.4 step 4,
    /// AC-NC-13). Returned by the caller-supplied <c>queryRespecReservationStatus</c> delegate in
    /// <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/> — no real <c>ItemReservation</c>
    /// type exists in this codebase yet, so this enum plus two caller-supplied
    /// <see cref="System.Action{T}"/> delegates (<c>representRespecPhase2</c>,
    /// <c>releaseRespecReservationAndNotify</c>) stand in for it (delegate-seam precedent, same shape
    /// as Story 011's forward dependencies).
    /// </summary>
    public enum RespecReservationStatus : byte
    {
        /// <summary>No respec Phase 1 reservation is active for this character. No action taken.</summary>
        NoActiveReservation = 0,

        /// <summary>
        /// A reservation is active and its own 30-second TTL has not expired — Phase 2 is re-presented
        /// to the client (AC-NC-13a).
        /// </summary>
        TtlValid = 1,

        /// <summary>
        /// A reservation is active but its own 30-second TTL has expired — the reservation is released
        /// and the client is notified "Respec scroll returned to inventory" (AC-NC-13b).
        /// </summary>
        TtlExpired = 2,
    }
}

namespace IronGrind.Networking
{
    /// <summary>
    /// Structural constants of the tick completion queue and the per-character mutation gate
    /// (ADR-011 Decision 2 and Decision 4, Networking Core Story 030). Compile-time constants, in
    /// line with this folder's other structural constants (<see cref="ServerTickLoop.TICK_RATE_HZ"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// var queue = new TickCompletionQueue(TickCompletionConstants.PERSISTENCE_WATCHDOG_TICKS);
    /// </code>
    /// </example>
    public static class TickCompletionConstants
    {
        /// <summary>
        /// Number of ticks after tracking at which a still-incomplete task is cancelled, alerted and
        /// reported as <see cref="TickTaskStatus.TimedOut"/> (200 ticks = 10 s at 20 Hz, equal to
        /// <c>CHARACTER_LOAD_TIMEOUT_SECONDS</c>). ADR-011 Decision 2.
        /// </summary>
        public const int PERSISTENCE_WATCHDOG_TICKS = 200;

        /// <summary>
        /// Upper bound on held client requests per character while the mutation gate is closed
        /// (ADR-011 Decision 4). Only declared here; the request dispatcher that enforces it is out
        /// of scope for Story 030.
        /// </summary>
        public const int MAX_HELD_REQUESTS_PER_CHARACTER = 16;
    }
}

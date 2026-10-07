namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Result of <see cref="EnhancementService.BeginAttempt"/>: either a rejection (nothing
    /// changed, no draw) or a pending attempt whose steps 2-6a are done (design/gdd/enhancement-system.md
    /// CR-ENH-7, CR-ENH-8, CR-ENH-15). Two-phase design decided at readiness 2026-10-07.
    /// </summary>
    /// <remarks>
    /// The pending <see cref="Outcome"/> is known BEFORE the commit. It must not be sent to a
    /// client until <see cref="EnhancementService.CompleteAttempt"/> has run after a successful
    /// commit (CR-ENH-11). Enhancement Story 004.
    /// </remarks>
    public readonly struct EnhancementAttemptStart
    {
        private EnhancementAttemptStart(bool isPending, EnhancementResultCode rejectionCode, EnhancementOutcome outcome, byte newLevel)
        {
            IsPending = isPending;
            RejectionCode = rejectionCode;
            Outcome = outcome;
            NewLevel = newLevel;
        }

        /// <summary>True iff the attempt was applied to the bag and awaits its commit and <see cref="EnhancementService.CompleteAttempt"/>.</summary>
        public bool IsPending { get; }

        /// <summary>The rejection reason. Meaningful only when <see cref="IsPending"/> is false.</summary>
        public EnhancementResultCode RejectionCode { get; }

        /// <summary>The resolved outcome (CR-ENH-9, CR-ENH-10). Meaningful only when <see cref="IsPending"/> is true; not to be sent to a client before the commit (CR-ENH-11).</summary>
        public EnhancementOutcome Outcome { get; }

        /// <summary>When pending: the level after the attempt (previous + 1), or 0 on destruction. 0 when rejected.</summary>
        public byte NewLevel { get; }

        /// <summary>Creates a pending start.</summary>
        /// <param name="outcome">The resolved outcome.</param>
        /// <param name="newLevel">Level after the attempt; 0 on destruction.</param>
        public static EnhancementAttemptStart Pending(EnhancementOutcome outcome, byte newLevel)
        {
            return new EnhancementAttemptStart(true, EnhancementResultCode.Success, outcome, newLevel);
        }

        /// <summary>Creates a rejected start; nothing changed in the bag.</summary>
        /// <remarks>
        /// <see cref="Outcome"/> then holds the enum's zero value, which happens to be
        /// <see cref="EnhancementOutcome.Success"/>. It carries no meaning on a rejection — always
        /// check <see cref="IsPending"/> first.
        /// </remarks>
        /// <param name="code">The rejection reason.</param>
        public static EnhancementAttemptStart Rejected(EnhancementResultCode code)
        {
            return new EnhancementAttemptStart(false, code, EnhancementOutcome.Success, 0);
        }
    }
}

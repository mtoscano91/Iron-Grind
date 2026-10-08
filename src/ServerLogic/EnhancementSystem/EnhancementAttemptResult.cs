namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Final result of an enhancement attempt, returned by <see cref="EnhancementService.CompleteAttempt"/>
    /// after a successful commit (design/gdd/enhancement-system.md UI-ENH-2 field set; CR-ENH-15 step 7,
    /// AC-ENH-9 to AC-ENH-12). Two-phase design decided at readiness 2026-10-07. Enhancement Story 004.
    /// </summary>
    public readonly struct EnhancementAttemptResult
    {
        /// <summary>Creates a result.</summary>
        /// <param name="outcome">The attempt outcome.</param>
        /// <param name="newLevel">Level after the attempt; 0 on destruction.</param>
        /// <param name="resultCode"><see cref="EnhancementResultCode.Success"/> or <see cref="EnhancementResultCode.Destruction"/>.</param>
        public EnhancementAttemptResult(EnhancementOutcome outcome, byte newLevel, EnhancementResultCode resultCode)
        {
            Outcome = outcome;
            NewLevel = newLevel;
            ResultCode = resultCode;
        }

        /// <summary>The attempt outcome (CR-ENH-9, CR-ENH-10).</summary>
        public EnhancementOutcome Outcome { get; }

        /// <summary>The item's level after the attempt; 0 on destruction.</summary>
        public byte NewLevel { get; }

        /// <summary><see cref="EnhancementResultCode.Success"/> or <see cref="EnhancementResultCode.Destruction"/>.</summary>
        public EnhancementResultCode ResultCode { get; }
    }
}

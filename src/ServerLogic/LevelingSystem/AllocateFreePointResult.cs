namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Result of an <see cref="LevelingService.AllocateFreePoint"/> call.
    /// </summary>
    /// <remarks>
    /// Story 003 implemented only the <see cref="RejectedSystemBusy"/> path — the minimal
    /// busy-rejection stub required by AC-LS-10 (EC-LS-09). Story 005 implements the real CR-3
    /// allocation semantics and adds <see cref="RejectedNoFreePoints"/> (Guard 1) and
    /// <see cref="RejectedInvalidStat"/> (Guard 2) alongside <see cref="Success"/>. Guards are
    /// evaluated in a fixed order — busy-check, then Guard 1, then Guard 2 — so a rejection
    /// always reflects the first failing check (EC-LS-12).
    /// </remarks>
    public enum AllocateFreePointResult
    {
        /// <summary>
        /// The allocation succeeded: <c>heldFreePoints</c> decremented by 1, the target stat
        /// incremented by 1, and F-3–F-9 re-derived at the current tier.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Rejected: a CR-2.9 consecutive-level-up loop is currently in progress
        /// (<see cref="LevelingService.IsLevelingUpInProgress"/> was <see langword="true"/> at
        /// call time). <c>heldFreePoints</c> is unchanged and no <c>SetBaseStat</c> call was made.
        /// </summary>
        RejectedSystemBusy = 1,

        /// <summary>
        /// Rejected: Guard 1 (CR-3 / AC-LS-12) — <see cref="LevelingService.GetHeldFreePoints"/>
        /// was 0 for this entity at call time. No decrement, no write.
        /// </summary>
        RejectedNoFreePoints = 2,

        /// <summary>
        /// Rejected: Guard 2 (CR-3 / AC-LS-13) — the requested target stat was not one of the
        /// four allocatable primary stats (<c>Strength</c>/<c>Dexterity</c>/<c>Vitality</c>/
        /// <c>Intelligence</c>). Fires BEFORE the decrement — <c>heldFreePoints</c> is unchanged
        /// and no <c>SetBaseStat</c> call was made. Structurally guarantees <c>StatID.Level</c>
        /// (and every other non-primary <c>StatID</c>) can never be written through this API.
        /// </summary>
        RejectedInvalidStat = 3,
    }
}

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Result of an enhancement attempt (design/gdd/enhancement-system.md UI-ENH-2; rejection
    /// codes from CR-ENH-15 step 2). Member order follows the GDD. Enhancement Story 003.
    /// </summary>
    /// <remarks>
    /// The numeric values are provisional until the wire encoding is fixed by TD-046; do not
    /// persist or transmit them as raw numbers before then.
    /// </remarks>
    public enum EnhancementResultCode : byte
    {
        /// <summary>The attempt succeeded and the item's level rose by one (CR-ENH-9).</summary>
        Success = 0,

        /// <summary>The attempt failed and the item was destroyed (CR-ENH-10).</summary>
        Destruction = 1,

        /// <summary>The item is already at the maximum level (CR-ENH-2, AC-ENH-14).</summary>
        RejectedAtMaxLevel = 2,

        /// <summary>The scroll's target tier differs from the item's tier (CR-ENH-3, AC-ENH-3).</summary>
        RejectedTierMismatch = 3,

        /// <summary>The item is a ring or necklace (CR-ENH-5, AC-ENH-5).</summary>
        RejectedAccessoryType = 4,

        /// <summary>The item slot is empty, out of range or holds an unresolvable item (CR-ENH-4, AC-ENH-4, AC-ENH-22).</summary>
        RejectedItemNotFound = 5,

        /// <summary>The item slot is locked, or another attempt is in progress for this player (CR-ENH-15, CR-ENH-18).</summary>
        RejectedConcurrentAttempt = 6,

        /// <summary>The player has no active NPC session (CR-ENH-17, AC-ENH-27).</summary>
        RejectedNoNPCSession = 7,

        /// <summary>The scroll slot is empty, out of range, unresolvable, or holds a non-scroll item (CR-ENH-15 step 2).</summary>
        RejectedScrollNotFound = 8,

        /// <summary>The item is not upgradeable or is not equipment (AC-ENH-37).</summary>
        RejectedNotUpgradeable = 9
    }
}

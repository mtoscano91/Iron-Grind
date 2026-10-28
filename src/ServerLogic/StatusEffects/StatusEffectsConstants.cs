using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Compile-time constants of the Status Effects system (status-effects.md CR-SE-1, CR-SE-15,
    /// F-SE-2, Tuning Knobs, AC-SE-26).
    /// </summary>
    public static class StatusEffectsConstants
    {
        /// <summary>Most stat modifiers one buff may carry.</summary>
        public const int MAX_STATS_PER_BUFF = 8;

        /// <summary>Most buffs active on one entity at once.</summary>
        public const int MAX_ACTIVE_BUFFS_PER_ENTITY = 8;

        /// <summary>Most modifiers summed across all buffs on one entity (F-SE-2).</summary>
        public const int MODIFIER_CAPACITY_PER_ENTITY = 32;

        /// <summary>Longest authored duration in ticks: 300 s x 20 Hz.</summary>
        public const uint MAX_DURATION_TICKS = 6000;

        /// <summary>Entity slots the effect table must cover: all players plus all mobs of a zone.</summary>
        public const int MAX_ENTITY_SLOTS = ZoneSlotPool.MAX_PLAYERS_PER_ZONE + ZoneSlotPool.MAX_MOBS_PER_ZONE;

        // Do not remove the two GUARD_ consts below: nothing reads them, they exist only to fail the build.
        // AC-SE-26 rule 1: MAX_ACTIVE_BUFFS_PER_ENTITY >= floor(MODIFIER_CAPACITY_PER_ENTITY / MAX_STATS_PER_BUFF).
        // Violation divides by constant zero, which is a compile error (CS0020).
        private const int GUARD_BUFF_SLOTS_COVER_CAPACITY =
            1 / (MAX_ACTIVE_BUFFS_PER_ENTITY >= MODIFIER_CAPACITY_PER_ENTITY / MAX_STATS_PER_BUFF ? 1 : 0);

        // AC-SE-26 rule 2: MAX_STATS_PER_BUFF <= MODIFIER_CAPACITY_PER_ENTITY.
        // Violation divides by constant zero, which is a compile error (CS0020).
        private const int GUARD_BUFF_FITS_IN_CAPACITY =
            1 / (MAX_STATS_PER_BUFF <= MODIFIER_CAPACITY_PER_ENTITY ? 1 : 0);
    }
}

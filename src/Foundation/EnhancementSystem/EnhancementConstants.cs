namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Enhancement System default tuning values (design/gdd/enhancement-system.md, Tuning Knobs
    /// TK-ENH-1 to TK-ENH-8). These are the GDD defaults; a running system reads its values from an
    /// injected <see cref="EnhancementConfig"/>, never from this class directly.
    /// </summary>
    public static class EnhancementConstants
    {
        /// <summary>
        /// Highest enhancement level (CR-ENH-2). No attempt exists from this level. Tuning knob
        /// (guidance range 9-12, not enforced).
        /// </summary>
        public const byte MAX_ENHANCEMENT_LEVEL = 10;

        /// <summary>
        /// First level at which the weapon prestige band is visible (CR-ENH-12, TK-ENH-5).
        /// Levels below it map to <see cref="PrestigeBand.None"/>.
        /// </summary>
        public const byte PRESTIGE_MID_THRESHOLD = 5;

        /// <summary>
        /// First level at which the weapon glows (CR-ENH-12, TK-ENH-4). Must be greater than
        /// <see cref="PRESTIGE_MID_THRESHOLD"/>.
        /// </summary>
        public const byte ENHANCEMENT_GLOW_THRESHOLD = 7;

        /// <summary>
        /// First level of the high prestige band (CR-ENH-12, TK-ENH-6). Must be greater than
        /// <see cref="ENHANCEMENT_GLOW_THRESHOLD"/> and not above <see cref="MAX_ENHANCEMENT_LEVEL"/>.
        /// </summary>
        public const byte PRESTIGE_HIGH_THRESHOLD = 8;

        /// <summary>
        /// Ceiling for a weapon's elemental damage after enhancement. Owned by the Item Database
        /// (design/registry/entities.yaml, source item-database.md, value 9999); this copy must match
        /// the registry. Replace with the Item Database's constant if one is added.
        /// </summary>
        public const int ELEMENTAL_DAMAGE_CEILING = 9999;

        /// <summary>Flat stat bonus per enhancement level, Bronze tier (F-ENH-1, TK-ENH-7).</summary>
        public const int BONUS_PER_LEVEL_BRONZE = 3;

        /// <summary>Flat stat bonus per enhancement level, Iron tier (F-ENH-1, TK-ENH-7).</summary>
        public const int BONUS_PER_LEVEL_IRON = 4;

        /// <summary>Flat stat bonus per enhancement level, Steel tier (F-ENH-1, TK-ENH-7).</summary>
        public const int BONUS_PER_LEVEL_STEEL = 6;

        /// <summary>Flat stat bonus per enhancement level, Dark Steel tier (F-ENH-1, TK-ENH-7).</summary>
        public const int BONUS_PER_LEVEL_DARK_STEEL = 10;

        /// <summary>Elemental damage bonus per enhancement level, Bronze weapons (F-ENH-2, TK-ENH-8).</summary>
        public const int ELEMENTAL_BONUS_PER_LEVEL_BRONZE = 1;

        /// <summary>Elemental damage bonus per enhancement level, Iron weapons (F-ENH-2, TK-ENH-8).</summary>
        public const int ELEMENTAL_BONUS_PER_LEVEL_IRON = 2;

        /// <summary>Elemental damage bonus per enhancement level, Steel weapons (F-ENH-2, TK-ENH-8).</summary>
        public const int ELEMENTAL_BONUS_PER_LEVEL_STEEL = 3;

        /// <summary>Elemental damage bonus per enhancement level, Dark Steel weapons (F-ENH-2, TK-ENH-8).</summary>
        public const int ELEMENTAL_BONUS_PER_LEVEL_DARK_STEEL = 5;

        /// <summary>
        /// Level whose success is announced server-wide (CR-ENH-14, AC-ENH-18). A fixed design value,
        /// not a tuning knob and not derived from the level cap.
        /// </summary>
        public const byte SERVER_BROADCAST_LEVEL = 9;

        /// <summary>Lowest allowed per-level success probability (TK-ENH-1, F-ENH-4).</summary>
        public const double MIN_SUCCESS_PROBABILITY = 0.01;

        /// <summary>Highest allowed per-level success probability (TK-ENH-1, F-ENH-4).</summary>
        public const double MAX_SUCCESS_PROBABILITY = 0.95;
    }
}

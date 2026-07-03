namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Material tier of a piece of equipment. Determines the base stat ceiling and
    /// the enhancement track available. Higher tiers have higher stat floors.
    /// </summary>
    /// <remarks>
    /// Enhancement is prestige within a tier, not a parallel power axis — a +5 Bronze
    /// item is intentionally comparable to a +0 Iron item in raw stats.
    /// Backed by <see cref="byte"/> for IL2CPP efficiency.
    /// </remarks>
    public enum GearTier : byte
    {
        /// <summary>No tier assigned (default / placeholder value).</summary>
        None      = 0,

        /// <summary>Entry-level tier. Appropriate for early-game content.</summary>
        Bronze    = 1,

        /// <summary>Mid-tier. Appropriate for mid-game content.</summary>
        Iron      = 2,

        /// <summary>Late-game tier.</summary>
        Steel     = 3,

        /// <summary>End-game tier. Highest available material quality.</summary>
        DarkSteel = 4,
    }
}

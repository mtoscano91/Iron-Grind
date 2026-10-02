namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Loot Table System tuning constants (design/gdd/loot-table-system.md, Tuning Knobs).
    /// </summary>
    public static class LootTableConstants
    {
        /// <summary>
        /// The GDD's <c>TAG_THRESHOLD_FRACTION</c> (default 0.33) expressed in thousandths: the share
        /// of a mob's MaxHP a single party must deal to lock the tag (F-LT-3 / CR-LT-4). Tuning knob;
        /// safe range [100, 500] (the GDD's [0.10, 0.50]).
        /// </summary>
        /// <remarks>
        /// An integer on purpose, so the threshold is computed with exact integer arithmetic for any
        /// value of the knob. Floating point is not exact here: in single precision 300 x 0.33f is
        /// 99.0000076 and the ceiling is 100 where the GDD's AC-LT-4 requires 99; in double precision
        /// 0.33 happens to be exact for every MaxHP, but 0.10 is not (30 x 0.10 gives a ceiling of 4
        /// instead of 3).
        /// </remarks>
        public const int TAG_THRESHOLD_PERMILLE = 330;

        /// <summary>The divisor that goes with <see cref="TAG_THRESHOLD_PERMILLE"/>.</summary>
        public const int PERMILLE_DIVISOR = 1000;

        /// <summary>
        /// Smallest tag threshold (F-LT-3: the threshold is never zero). Also the value used when a
        /// mob reports a MaxHP below 1.
        /// </summary>
        public const int MIN_TAG_THRESHOLD = 1;

        /// <summary>
        /// Ground item time to live in server ticks (CR-LT-12): <c>expiryTick = spawnTick + GROUND_ITEM_TTL_TICKS</c>.
        /// Tuning knob; safe range [600, 7200] (design/gdd/loot-table-system.md).
        /// </summary>
        public const int GROUND_ITEM_TTL_TICKS = 2400;

        /// <summary>
        /// Total ticks a ground item's TTL may be paused (initial <c>PauseBudgetRemaining</c>).
        /// Tuning knob; safe range [0, 3600] (design/gdd/loot-table-system.md).
        /// </summary>
        public const int GROUND_ITEM_TTL_PAUSE_CAP_TICKS = 1200;

        /// <summary>
        /// Distance in world units within which an item's assignee picks it up automatically
        /// (CR-LT-7). Tuning knob; safe range [1.0, 5.0]; provisional (design/registry/entities.yaml).
        /// The boundary is inside: <c>distance &lt;= radius</c> counts as within range. The GDD does
        /// not say; this is the implementation's stated choice. The distance is 3D (height counts),
        /// so an item on another level directly above or below is out of reach.
        /// </summary>
        public const float PICKUP_RADIUS_UNITS = 2.0f;

        /// <summary>
        /// Ticks before expiry at which the assignee gets the expiry warning (CR-LT-13.3): raised once,
        /// on the tick where <c>expiryTick - currentTick</c> equals this value. Tuning knob; safe range
        /// [100, 1200]; must be less than <see cref="GROUND_ITEM_TTL_TICKS"/>.
        /// </summary>
        public const int EXPIRY_WARNING_TICKS = 600;

        /// <summary>
        /// Length of a Rare drop auction window in server ticks (CR-LT-8): <c>windowCloseTick =
        /// auctionOpenTick + AUCTION_WINDOW_TICKS</c>. Tuning knob; safe range [200, 1200].
        /// <see cref="GROUND_ITEM_TTL_TICKS"/> must stay well above it, so the auction can resolve
        /// before the item expires.
        /// </summary>
        public const int AUCTION_WINDOW_TICKS = 600;
    }
}

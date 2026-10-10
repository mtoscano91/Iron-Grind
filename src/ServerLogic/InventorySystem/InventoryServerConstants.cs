using IronGrind.Networking;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Server-only Inventory System tuning constants. Split from <see cref="InventoryConstants"/> (which is in
    /// <c>IronGrind.Foundation</c> because the wire codecs read the slot count) because these values depend on
    /// server-only types.
    /// </summary>
    public static class InventoryServerConstants
    {
        /// <summary>
        /// Bag-full notification deduplication window (GDD Rule 4.10: at most one
        /// <see cref="IInventoryService.OnInventoryFull"/> per character per 30 seconds), in
        /// server ticks: 30 s × <see cref="ServerTickLoop.TICK_RATE_HZ"/> = 600.
        /// </summary>
        public const uint BAG_FULL_DEDUP_WINDOW_TICKS = 30 * ServerTickLoop.TICK_RATE_HZ;
    }
}

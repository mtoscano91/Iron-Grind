using IronGrind.Networking;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Inventory System tuning constants. Lives outside <see cref="InventoryService"/> so that
    /// consumers depending only on <see cref="IInventoryService"/> (ADR-010 Tier 1) — e.g. the
    /// future Inventory UI — can read the slot count without referencing the concrete class.
    /// </summary>
    public static class InventoryConstants
    {
        /// <summary>
        /// Fixed inventory capacity per character (GDD Rule 1.1 / Tuning Knob). Valid slot
        /// indices are <c>[0, INVENTORY_SLOT_COUNT)</c>. Never hardcode <c>20</c> elsewhere —
        /// reference this constant. Compile-time constant because it sizes the service-owned
        /// change buffer backing <see cref="InventoryChangedEventArgs"/>.
        /// </summary>
        public const int INVENTORY_SLOT_COUNT = 20;

        /// <summary>
        /// Bag-full notification deduplication window (GDD Rule 4.10: at most one
        /// <see cref="IInventoryService.OnInventoryFull"/> per character per 30 seconds), in
        /// server ticks: 30 s × <see cref="ServerTickLoop.TICK_RATE_HZ"/> = 600.
        /// </summary>
        public const uint BAG_FULL_DEDUP_WINDOW_TICKS = 30 * ServerTickLoop.TICK_RATE_HZ;
    }
}

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
    }
}

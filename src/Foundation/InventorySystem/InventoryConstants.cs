namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Inventory System tuning constants shared by server and client. Lives outside the server-side
    /// <c>InventoryService</c> so that consumers depending only on <c>IInventoryService</c> (ADR-010 Tier 1) — e.g. the
    /// future Inventory UI — and the wire codecs can read the slot count without referencing the concrete class.
    /// Server-only values are in <c>InventoryServerConstants</c>.
    /// </summary>
    public static class InventoryConstants
    {
        /// <summary>
        /// Fixed inventory capacity per character (GDD Rule 1.1 / Tuning Knob). Valid slot
        /// indices are <c>[0, INVENTORY_SLOT_COUNT)</c>. Never hardcode the number elsewhere —
        /// reference this constant. Compile-time constant because it sizes the service-owned
        /// change buffer backing <c>InventoryChangedEventArgs</c>.
        /// </summary>
        /// <remarks>
        /// This constant fixes the size of the <c>InventoryFullSync</c> wire message (body
        /// <c>1 + 10 × INVENTORY_SLOT_COUNT</c> bytes). Above 51 slots that body exceeds the 512-byte cap
        /// of CR-NET-7.6 and the message must move to the bulk-transfer range.
        /// </remarks>
        public const int INVENTORY_SLOT_COUNT = 20;
    }
}

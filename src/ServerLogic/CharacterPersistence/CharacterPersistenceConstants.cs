namespace IronGrind.CharacterPersistence
{
    /// <summary>Character Persistence constants. The inventory slot count is <c>InventoryConstants.INVENTORY_SLOT_COUNT</c>.</summary>
    public static class CharacterPersistenceConstants
    {
        /// <summary>Timeout for loading a character, in seconds (CR-CP-8). Must stay below the 30 s connecting timeout.</summary>
        public const int CHARACTER_LOAD_TIMEOUT_SECONDS = 10;

        /// <summary>Timeout for a persistence write, in seconds (ADR-006 Decision 4).</summary>
        public const int PERSISTENCE_WRITE_TIMEOUT_SECONDS = 5;

        /// <summary>A write slower than this many milliseconds logs a warning (ADR-006 Decision 4).</summary>
        public const int PERSISTENCE_WRITE_WARNING_MS = 50;

        /// <summary>A write slower than this many milliseconds is critical (ADR-006 Decision 4).</summary>
        public const int PERSISTENCE_WRITE_CRITICAL_MS = 100;

        /// <summary>Number of persisted gear slots; equals the number of <c>IronGrind.ItemDatabase.GearSlot</c> members (a test pins the equality).</summary>
        public const int GEAR_SLOT_COUNT = 7;
    }
}

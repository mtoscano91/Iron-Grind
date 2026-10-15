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

        /// <summary>Lowest valid character level in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_LEVEL_MIN = 1;

        /// <summary>Highest valid character level in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_LEVEL_MAX = 60;

        /// <summary>Lowest valid experience in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_EXPERIENCE_MIN = 0;

        /// <summary>Lowest valid held free points in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_HELD_FREE_POINTS_MIN = 0;

        /// <summary>Lowest valid value of each of STR, DEX, VIT and INT in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_ATTRIBUTE_MIN = 1;

        /// <summary>Highest valid value of each of STR, DEX, VIT and INT in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_ATTRIBUTE_MAX = 999;

        /// <summary>Lowest valid MaxHP and MaxMP in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_MAX_RESOURCE_MIN = 1;

        /// <summary>Highest valid MaxHP and MaxMP in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_MAX_RESOURCE_MAX = 99999;

        /// <summary>Lowest valid AttackPower, Defense and MagicDefense in a loaded record (CR-CP-3 step 3).</summary>
        public const int LOAD_COMBAT_STAT_MIN = 0;
    }
}

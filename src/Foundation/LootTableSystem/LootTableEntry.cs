using IronGrind.CharacterStats;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// One drop entry of a loot table: an item and its independent drop chance
    /// (design/gdd/loot-table-system.md, CR-LT-2 / F-LT-4).
    /// </summary>
    public readonly struct LootTableEntry
    {
        /// <summary>Initializes a new entry.</summary>
        /// <param name="itemId">The item that may drop.</param>
        /// <param name="dropChance">Drop probability, valid range [0.0, 1.0] (checked by the validator).</param>
        public LootTableEntry(ItemID itemId, float dropChance)
        {
            ItemId = itemId;
            DropChance = dropChance;
        }

        /// <summary>The item that may drop.</summary>
        public ItemID ItemId { get; }

        /// <summary>Drop probability, valid range [0.0, 1.0].</summary>
        public float DropChance { get; }
    }
}

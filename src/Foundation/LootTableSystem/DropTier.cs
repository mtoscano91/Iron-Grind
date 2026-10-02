namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Drop tier of a dropped item (design/gdd/loot-table-system.md, CR-LT-5).
    /// Backed by <see cref="byte"/> for consistency with the other tier enums.
    /// </summary>
    public enum DropTier : byte
    {
        /// <summary>Bronze / Iron equipment, consumables and any item not in the equipment cache.</summary>
        Common = 0,

        /// <summary>Steel / DarkSteel equipment.</summary>
        Rare = 1,
    }
}

using IronGrind.Currency;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Argument of <see cref="IInventoryService.OnInventoryFull"/> (GDD Rule 4.10). A
    /// <see langword="readonly struct"/> — zero heap allocation per emit, never boxes (ADR-010
    /// Decision 3).
    /// </summary>
    public readonly struct InventoryFullEventArgs
    {
        /// <summary>The character whose pickup was blocked by a full bag.</summary>
        public readonly CharacterID CharacterID;

        /// <summary>Creates a new event argument.</summary>
        /// <param name="characterId">The character whose pickup was blocked.</param>
        public InventoryFullEventArgs(CharacterID characterId)
        {
            CharacterID = characterId;
        }
    }
}

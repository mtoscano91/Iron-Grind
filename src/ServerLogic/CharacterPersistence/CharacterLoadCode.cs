namespace IronGrind.CharacterPersistence
{
    /// <summary>Outcome code of <see cref="ICharacterPersistence.LoadCharacter"/> (CR-CP-1).</summary>
    public enum CharacterLoadCode : byte
    {
        /// <summary>The record was loaded.</summary>
        Success = 0,

        /// <summary>No record exists for the character id.</summary>
        CharacterNotFound = 1,

        /// <summary>The record belongs to a different account than the one requesting it.</summary>
        AccountMismatch = 2,

        /// <summary>The stored record failed validation.</summary>
        CorruptRecord = 3,

        /// <summary>A stored item id is not in the Item Database.</summary>
        ItemDatabaseMiss = 4,

        /// <summary>
        /// The Item Database was not ready, so <c>IInventoryService.ImportSnapshot</c> refused the bag
        /// (returned false). Returned by <c>ApplyLoadedRecord</c>; retry the load or reject the login.
        /// The caller must discard the entity: the stat writes and the Leveling registration (class type,
        /// held free points) before the import have already happened, and an inventory that failed to load
        /// is never treated as an empty one.
        /// </summary>
        InventoryNotRestored = 5,

        /// <summary>The database failed, timed out or was cancelled.</summary>
        DatabaseError = 255,
    }
}

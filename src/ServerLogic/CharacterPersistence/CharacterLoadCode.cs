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

        /// <summary>The database failed, timed out or was cancelled.</summary>
        DatabaseError = 255,
    }
}

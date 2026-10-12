namespace IronGrind.CharacterPersistence
{
    /// <summary>Outcome code of the save methods of <see cref="ICharacterPersistence"/> (CR-CP-1).</summary>
    public enum CharacterSaveResult : byte
    {
        /// <summary>The write was committed.</summary>
        Success = 0,

        /// <summary>No record exists for the character id.</summary>
        CharacterNotFound = 1,

        /// <summary>The stored version no longer matches the version the write was based on.</summary>
        ConcurrencyConflict = 2,

        /// <summary>The database failed, timed out or was cancelled.</summary>
        DatabaseError = 255,
    }
}

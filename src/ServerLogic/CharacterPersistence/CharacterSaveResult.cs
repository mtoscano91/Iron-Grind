namespace IronGrind.CharacterPersistence
{
    /// <summary>Outcome code of the save methods of <see cref="ICharacterPersistence"/> (CR-CP-1).</summary>
    public enum CharacterSaveResult : byte
    {
        /// <summary>The write was committed.</summary>
        Success = 0,

        /// <summary>No record exists for the character id (for the purchase methods: the purchase belongs to another character).</summary>
        CharacterNotFound = 1,

        /// <summary>The stored version no longer matches the version the write was based on.</summary>
        ConcurrencyConflict = 2,

        /// <summary>
        /// Purchase methods only; the save methods never return it. The store call started but its result is unknown
        /// (timeout or cancellation in flight): it may have committed. The caller must not compensate and must retry the
        /// same call with the same arguments until a definite result (ADR-001 Amendment A2, 2026-10-26).
        /// </summary>
        OutcomeUnknown = 254,

        /// <summary>The database failed, timed out or was cancelled.</summary>
        DatabaseError = 255,
    }
}

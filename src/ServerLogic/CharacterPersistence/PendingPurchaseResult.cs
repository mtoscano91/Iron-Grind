namespace IronGrind.CharacterPersistence
{
    /// <summary>Outcome code of <see cref="ICharacterPersistence.BeginPurchase"/> (ADR-001 Decisions 2-4, Amendment A2).</summary>
    public enum PendingPurchaseResult : byte
    {
        /// <summary>The pending purchase was recorded and the resulting gold values were stored.</summary>
        Success = 0,

        /// <summary>
        /// Not returned by the store since ADR-001 Amendment A2 (2026-10-26): the Currency System rejects an
        /// unaffordable purchase first. The value stays for wire and log stability.
        /// </summary>
        InsufficientGold = 1,

        /// <summary>
        /// Not returned by the store since ADR-001 Amendment A2 (2026-10-26): the purchase write is not
        /// version-guarded. The value stays for wire and log stability.
        /// </summary>
        ConcurrencyConflict = 2,

        /// <summary>
        /// The store call started but its result is unknown (timeout or cancellation in flight): it may have committed.
        /// The caller must not compensate and must retry the same call with the same arguments until a definite result
        /// (ADR-001 Amendment A2, 2026-10-26).
        /// </summary>
        OutcomeUnknown = 254,

        /// <summary>The database failed, the token was cancelled before the write started, or the character has no stored record; nothing was applied.</summary>
        DatabaseError = 255,
    }
}

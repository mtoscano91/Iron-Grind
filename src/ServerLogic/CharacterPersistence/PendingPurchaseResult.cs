namespace IronGrind.CharacterPersistence
{
    /// <summary>Outcome code of <see cref="ICharacterPersistence.BeginPurchase"/> (ADR-001 Decisions 2-4).</summary>
    public enum PendingPurchaseResult : byte
    {
        /// <summary>The gold was debited and the pending purchase recorded.</summary>
        Success = 0,

        /// <summary>The character's gold balance is below the purchase cost.</summary>
        InsufficientGold = 1,

        /// <summary>The stored gold version no longer matches the version the write was based on.</summary>
        ConcurrencyConflict = 2,

        /// <summary>The database failed, timed out or was cancelled.</summary>
        DatabaseError = 255,
    }
}

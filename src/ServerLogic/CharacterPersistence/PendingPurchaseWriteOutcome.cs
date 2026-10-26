namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Outcome of <see cref="IPendingPurchaseStore.CompletePurchaseAsync"/> and <see cref="IPendingPurchaseStore.RefundPurchaseAsync"/>
    /// (ADR-001 Amendment A2).
    /// </summary>
    public enum PendingPurchaseWriteOutcome : byte
    {
        /// <summary>The record was deleted (and, for a refund, the gold values were written in the same write).</summary>
        Applied = 0,

        /// <summary>No purchase has that id (never existed or already removed); nothing was written, including no gold.</summary>
        NotFound = 1,

        /// <summary>The purchase exists but belongs to a different character; nothing was written.</summary>
        OtherCharacter = 2,
    }
}

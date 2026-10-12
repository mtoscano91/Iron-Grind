namespace IronGrind.CharacterPersistence
{
    /// <summary>State of a row in the <c>pending_purchases</c> table (ADR-001 Decisions 2-4).</summary>
    public enum PendingPurchaseState : byte
    {
        /// <summary>The gold has been debited; the purchase is outstanding.</summary>
        GoldDebited = 0,

        /// <summary>The gold was refunded; the purchase did not complete.</summary>
        Refunded = 1,

        /// <summary>The item was delivered; the purchase is complete.</summary>
        Completed = 2,
    }
}

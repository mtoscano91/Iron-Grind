namespace IronGrind.CharacterPersistence
{
    /// <summary>Result of <see cref="ICharacterPersistence.BeginPurchase"/>: an outcome code and, on success, the new purchase id (ADR-001 Amendment A2).</summary>
    public readonly struct PendingPurchaseBeginResult
    {
        /// <summary>The outcome of the call.</summary>
        public readonly PendingPurchaseResult Result;

        /// <summary>
        /// The store-generated id of the new <see cref="PendingPurchaseRecord"/> (greater than 0); 0 unless
        /// <see cref="Result"/> is <see cref="PendingPurchaseResult.Success"/>. The caller passes it to
        /// <c>CompletePurchase</c> or <c>RefundPurchase</c>.
        /// </summary>
        public readonly long PurchaseId;

        /// <summary>Constructs a begin result.</summary>
        /// <param name="result">The outcome of the call.</param>
        /// <param name="purchaseId">The new purchase id on success; 0 otherwise.</param>
        public PendingPurchaseBeginResult(PendingPurchaseResult result, long purchaseId)
        {
            Result = result;
            PurchaseId = purchaseId;
        }
    }
}

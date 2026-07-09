namespace IronGrind.Currency
{
    /// <summary>
    /// Return type of <see cref="ICurrencyService.AddGold"/> (and, in a later story,
    /// <c>TrySpendGold</c>). <see langword="readonly struct"/> — zero GC allocation per call.
    /// </summary>
    /// <remarks>
    /// Callers must check <see cref="Error"/> before trusting <see cref="NewBalance"/> —
    /// never assume <see cref="Success"/> without checking.
    /// </remarks>
    public readonly struct GoldMutationResult
    {
        /// <summary><see langword="true"/> when the mutation applied (including a cap-clamped <c>AddGold</c>).</summary>
        public readonly bool Success;

        /// <summary>Balance after the mutation. Meaningful only when <see cref="Success"/> is <see langword="true"/>; otherwise equals <see cref="PreviousBalance"/> (no write occurred).</summary>
        public readonly uint NewBalance;

        /// <summary>Balance immediately before this call.</summary>
        public readonly uint PreviousBalance;

        /// <summary><see cref="GoldMutationError.None"/> on success; otherwise the reason the mutation did not apply.</summary>
        public readonly GoldMutationError Error;

        /// <summary>Creates a new result.</summary>
        public GoldMutationResult(bool success, uint newBalance, uint previousBalance, GoldMutationError error)
        {
            Success = success;
            NewBalance = newBalance;
            PreviousBalance = previousBalance;
            Error = error;
        }
    }
}

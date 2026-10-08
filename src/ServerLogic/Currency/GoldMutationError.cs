namespace IronGrind.Currency
{
    /// <summary>
    /// Error code returned by <see cref="ICurrencyService.AddGold"/> when a mutation does
    /// not apply cleanly. <see cref="None"/> indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency. Note: cap-clamped <c>AddGold</c>
    /// calls return <see cref="None"/> with <c>Success=true</c> — callers detect the clamp
    /// from <c>result.NewBalance == GOLD_CAP</c>, not from a dedicated error code (a prior
    /// <c>WouldExceedCap</c> value was removed 2026-04-26 for exactly this reason).
    /// </remarks>
    public enum GoldMutationError : byte
    {
        /// <summary>No error — the mutation succeeded.</summary>
        None = 0,

        /// <summary>`TrySpendGold` rejected: balance is less than the requested cost.</summary>
        InsufficientFunds = 1,

        /// <summary>The supplied <see cref="CharacterID"/> is not a known/registered character.</summary>
        CharacterNotFound = 2,

        /// <summary>The requested amount or cost was zero — a caller bug, not a player-facing error.</summary>
        InvalidAmount = 3,

        /// <summary>Optimistic concurrency check failed twice in a row (Rule 11).</summary>
        ConcurrencyConflict = 4,

        /// <summary>The operation (e.g. <c>TransferGold</c>) is not implemented at MVP.</summary>
        NotImplemented = 5,
    }
}

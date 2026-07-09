using System;

namespace IronGrind.Currency
{
    /// <summary>
    /// Event payload for <see cref="ICurrencyService.OnGoldSync"/>. Carries the post-mutation
    /// state of a single successful gold balance change — never a delta, always the resulting
    /// absolute state, so a consumer can safely apply this as a full state sync (ADR-010
    /// Decision 3, Tier 2 Broadcast Events).
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> per ADR-010 Decision 3 — zero heap allocation on emit,
    /// which matters because <see cref="CurrencySystem"/> fires this on the hot path of every
    /// successful <see cref="CurrencySystem.AddGold"/>/<see cref="CurrencySystem.TrySpendGold"/>
    /// call. Only ever constructed by <see cref="CurrencySystem"/> on a successful mutation —
    /// never on a guard-rejected call (<see cref="GoldMutationError.CharacterNotFound"/>,
    /// <see cref="GoldMutationError.InvalidAmount"/>, <see cref="GoldMutationError.InsufficientFunds"/>,
    /// or <see cref="GoldMutationError.ConcurrencyConflict"/>).
    /// </remarks>
    public readonly struct GoldSyncEventArgs
    {
        /// <summary>The character whose balance changed.</summary>
        public readonly CharacterID CharacterID;

        /// <summary>The character's gold balance immediately after this mutation (matches <see cref="CurrencySystem.GetBalance"/> at the moment of emission).</summary>
        public readonly uint NewBalance;

        /// <summary>The character's mutation version immediately after this mutation — always exactly one greater than the version observed before the mutation. Monotonically increasing per character, with no skips or repeats.</summary>
        public readonly uint Version;

        /// <summary>The audit reason supplied by the caller of the mutation that produced this event.</summary>
        public readonly GoldTransactionReason Reason;

        /// <summary>
        /// Constructs a new <see cref="GoldSyncEventArgs"/> describing a single successful gold
        /// balance mutation.
        /// </summary>
        /// <param name="characterId">The character whose balance changed.</param>
        /// <param name="newBalance">The balance immediately after the mutation.</param>
        /// <param name="version">The mutation version immediately after the mutation (pre-mutation version + 1).</param>
        /// <param name="reason">The audit reason supplied by the caller of the mutation.</param>
        public GoldSyncEventArgs(CharacterID characterId, uint newBalance, uint version, GoldTransactionReason reason)
        {
            CharacterID = characterId;
            NewBalance = newBalance;
            Version = version;
            Reason = reason;
        }
    }
}

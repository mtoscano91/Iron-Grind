using System;

namespace IronGrind.Currency
{
    /// <summary>
    /// Read/write contract for the server-authoritative gold economy. Inject this interface
    /// into any system that needs to mutate or query a character's gold balance — never
    /// depend on the concrete <see cref="CurrencySystem"/> class directly (ADR-010 Tier 1).
    /// </summary>
    /// <remarks>
    /// Story 001 established <see cref="AddGold"/> and <see cref="GetBalance"/>. Story 002 added
    /// <see cref="TrySpendGold"/>. Story 003 adds <see cref="RegisterCharacter"/> and revises
    /// <see cref="AddGold"/>/<see cref="TrySpendGold"/> to require explicit registration first —
    /// a <see cref="CharacterID"/> that was never registered now fails fast with
    /// <see cref="GoldMutationError.CharacterNotFound"/> instead of being lazily treated as
    /// balance 0. Story 004 makes <see cref="AddGold"/> and <see cref="TrySpendGold"/>
    /// thread-safe for concurrent calls — including concurrent calls on the SAME
    /// <see cref="CharacterID"/> — via per-character atomicity (optimistic version-checked
    /// compare-and-swap with a single retry for <see cref="TrySpendGold"/>, surfacing
    /// <see cref="GoldMutationError.ConcurrencyConflict"/> on a second consecutive conflict; a
    /// single atomic lock for <see cref="AddGold"/>, which never rejects on conflict). Callers do
    /// not need to add their own locking around these calls. Story 005 adds
    /// <see cref="OnGoldSync"/>, the Tier 2 broadcast event (ADR-010 Decision 3) that notifies
    /// independent consumers (HUD, Character Persistence, Networking Core) of every successful
    /// balance mutation. Story 006 adds <see cref="TransferGold"/> as a stub that always returns
    /// <see cref="GoldMutationError.NotImplemented"/> — GDD Rule 8 forbids real P2P transfer
    /// logic at MVP; that is deferred to the (not-yet-built) Trade System.
    /// </remarks>
    public interface ICurrencyService
    {
        /// <summary>
        /// Tier 2 broadcast event (ADR-010 Decision 3) fired after every successful
        /// <see cref="AddGold"/> or <see cref="TrySpendGold"/> call, carrying the resulting
        /// post-mutation state (AC-CS-F-01). NOT fired when either call is guard-rejected —
        /// <see cref="GoldMutationError.CharacterNotFound"/> or <see cref="GoldMutationError.InvalidAmount"/>
        /// from <see cref="AddGold"/> (AC-CS-F-02); <see cref="GoldMutationError.CharacterNotFound"/>,
        /// <see cref="GoldMutationError.InvalidAmount"/>, or <see cref="GoldMutationError.InsufficientFunds"/>
        /// from <see cref="TrySpendGold"/> (AC-CS-F-03). A failed retry surfacing
        /// <see cref="GoldMutationError.ConcurrencyConflict"/> also does not fire this event —
        /// only a successful write does. Uses a plain <see cref="Action{T}"/> with a
        /// <see langword="readonly struct"/> argument (<see cref="GoldSyncEventArgs"/>) per
        /// ADR-010 Decision 3 — never <c>UnityEvent</c>, never a class-typed argument.
        /// </summary>
        event Action<GoldSyncEventArgs> OnGoldSync;


        /// <summary>
        /// Registers <paramref name="charId"/> with an initial gold balance, making it a valid
        /// target for <see cref="AddGold"/> and <see cref="TrySpendGold"/>. Must be called
        /// before either mutator will operate on a given character — an unregistered
        /// <see cref="CharacterID"/> causes both to fail fast with
        /// <see cref="GoldMutationError.CharacterNotFound"/>.
        /// </summary>
        /// <remarks>
        /// This is a test/bootstrap seam analogous to <c>CharacterStatsFixture.Create()</c> —
        /// in production this is called by Character Persistence on character load, seeding
        /// <paramref name="initialBalance"/> from <c>character_records.gold_balance</c>. It is a
        /// direct write, not a mutation formula — it does not go through F-CS-1/F-CS-2 and is
        /// not audited via <see cref="GoldTransactionReason"/>. Calling it again on an
        /// already-registered character overwrites the balance unconditionally (no guard) —
        /// callers must not treat this as a general-purpose "set gold" API (GDD Rule 9 forbids
        /// that); it exists solely for registration/bootstrap.
        /// </remarks>
        /// <param name="charId">The character to register.</param>
        /// <param name="initialBalance">Starting gold balance for the newly registered character.</param>
        void RegisterCharacter(CharacterID charId, uint initialBalance);

        /// <summary>
        /// Credits <paramref name="amount"/> gold to <paramref name="charId"/>'s balance,
        /// clamped to <c>GOLD_CAP</c> (F-CS-1). Fails fast with
        /// <see cref="GoldMutationError.CharacterNotFound"/> if <paramref name="charId"/> was
        /// never passed to <see cref="RegisterCharacter"/>, or with
        /// <see cref="GoldMutationError.InvalidAmount"/> if <paramref name="amount"/> is zero.
        /// Otherwise always succeeds (the cap clamp itself is never a failure).
        /// </summary>
        /// <param name="charId">The character receiving gold. Must be registered.</param>
        /// <param name="amount">Amount to credit. Must be nonzero.</param>
        /// <param name="reason">Audit reason for this credit.</param>
        GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason);

        /// <summary>Returns the current gold balance for <paramref name="charId"/>. Returns 0 for a character that has never received gold or was never registered.</summary>
        uint GetBalance(CharacterID charId);

        /// <summary>
        /// Attempts to debit <paramref name="cost"/> gold from <paramref name="charId"/>'s
        /// balance (F-CS-2). Atomic from the caller's perspective: either the full cost is
        /// debited or the balance is left completely unchanged — there is no partial spend.
        /// Fails fast with <see cref="GoldMutationError.CharacterNotFound"/> if
        /// <paramref name="charId"/> was never passed to <see cref="RegisterCharacter"/>, or
        /// with <see cref="GoldMutationError.InvalidAmount"/> if <paramref name="cost"/> is
        /// zero. Otherwise succeeds if and only if <c>balance &gt;= cost</c>; if not, returns
        /// <see cref="GoldMutationError.InsufficientFunds"/> with the balance untouched. Has no
        /// rollback mechanism — a caller that grants a benefit after a successful spend and then
        /// needs to undo it must call <see cref="AddGold"/> itself.
        /// </summary>
        /// <param name="charId">The character spending gold. Must be registered.</param>
        /// <param name="cost">Amount to debit. Must be nonzero.</param>
        /// <param name="reason">Audit reason for this debit.</param>
        GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason);

        /// <summary>
        /// Stub for a future peer-to-peer gold transfer between two characters. Always returns
        /// <see cref="GoldMutationError.NotImplemented"/> and never mutates either character's
        /// balance — GDD Rule 8 explicitly forbids real P2P transfer logic at MVP. This is
        /// deferred to the Trade System GDD, which does not exist yet and is outside the
        /// 35-system MVP scope.
        /// </summary>
        /// <remarks>
        /// <see cref="GoldMutationResult.NewBalance"/> and <see cref="GoldMutationResult.PreviousBalance"/>
        /// are meaningless placeholders on the returned result when
        /// <see cref="GoldMutationResult.Error"/> is <see cref="GoldMutationError.NotImplemented"/> —
        /// callers must not read either field in that case.
        /// </remarks>
        /// <param name="fromId">The character that would send gold. Unused — no lookup occurs.</param>
        /// <param name="toId">The character that would receive gold. Unused — no lookup occurs.</param>
        /// <param name="amount">The amount that would be transferred. Unused — no validation occurs.</param>
        GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount);
    }
}

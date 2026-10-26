using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The one async persistence interface for characters (CR-CP-1; ADR-006, ADR-011 Decision 3).
    /// </summary>
    /// <remarks>
    /// The name is a grandfathered exception to the <c>I[SystemName]Service</c> rule (ADR-010).
    /// Tick-driven code never <c>await</c>s these methods: it passes the returned task, in the same
    /// statement, to <c>ITickCompletionQueue.Track</c> (ADR-011). Implementations copy what they need
    /// from live state before their first <c>await</c> and use <c>ConfigureAwait(false)</c> (ADR-011).
    /// The purchase methods are ADR-001 Decisions 2-4; their rows live in a separate
    /// <c>pending_purchases</c> table.
    /// </remarks>
    public interface ICharacterPersistence
    {
        /// <summary>Loads a character, checking that it belongs to <paramref name="ownerAccountID"/>.</summary>
        /// <param name="id">The character to load.</param>
        /// <param name="ownerAccountID">The account requesting the load.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct);

        /// <summary>Saves a character after an irreversible outcome, completing only after the store confirmed the write.</summary>
        /// <remarks>
        /// Writes the whole record (CR-CP-4); the expected <c>SaveVersion</c> is read at the write's turn in the
        /// per-character queue. The returned task never faults and is never cancelled. A conflict is critical (one
        /// error log) and is not retried. This method disconnects no one and reverts nothing: the caller owns the
        /// revert or disconnect on any result other than <see cref="CharacterSaveResult.Success"/> (CR-CP-5, ADR-011 Decision 5).
        /// </remarks>
        /// <param name="id">The character to save.</param>
        /// <param name="trigger">The outcome that triggered the save (log lines only).</param>
        /// <param name="ct">Cancellation token for the queued write.</param>
        /// <returns>
        /// <see cref="CharacterSaveResult.Success"/> once the store confirmed; <see cref="CharacterSaveResult.ConcurrencyConflict"/>
        /// (critical), <see cref="CharacterSaveResult.DatabaseError"/> or <see cref="CharacterSaveResult.CharacterNotFound"/> otherwise.
        /// </returns>
        Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct);

        /// <summary>Saves a character at the end of a session.</summary>
        /// <param name="id">The character to save.</param>
        /// <param name="reason">Why the session ended.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct);

        /// <summary>
        /// Saves a character at the end of a session, writing <paramref name="hpOverride"/> as
        /// <c>CurrentHP</c>. Used by Zone Instancing teardown for Dead/Respawning slots (CR-ZI-12 step 2).
        /// </summary>
        /// <param name="id">The character to save.</param>
        /// <param name="reason">Why the session ended.</param>
        /// <param name="hpOverride">The value to write as <c>CurrentHP</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct);

        /// <summary>
        /// Creates the stub record of a new character. Deviation from CR-CP-1: <paramref name="classType"/>
        /// is a <see cref="byte"/> because the GDD's <c>ClassType</c> type does not exist
        /// (<c>LevelingService.RegisterPlayerClassType(EntityID, byte)</c> uses a raw byte).
        /// </summary>
        /// <param name="accountId">The owning account.</param>
        /// <param name="characterId">The new character's id.</param>
        /// <param name="classType">Raw class type.</param>
        /// <param name="ct">Cancellation token.</param>
        Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct);

        /// <summary>
        /// Records a pending purchase and stores the character's resulting gold in one write (ADR-001 Amendment A2).
        /// The caller debits in memory FIRST; this method never computes a debit.
        /// </summary>
        /// <remarks>
        /// Stores <paramref name="goldBalance"/> / <paramref name="goldVersion"/> as given (no balance check, no clamp) and
        /// does not advance <c>SaveVersion</c>. The write is queued behind every earlier write of the character. Idempotent per
        /// request: a retry whose (<c>CharId</c>, <c>RequestId</c>) is already recorded returns <see cref="PendingPurchaseResult.Success"/>
        /// with the existing id and writes nothing. Results: <see cref="PendingPurchaseResult.Success"/>;
        /// <see cref="PendingPurchaseResult.DatabaseError"/> (token cancelled before the write started, unknown character, store failure: nothing applied,
        /// <see cref="PendingPurchaseBeginResult.PurchaseId"/> 0, safe to compensate); <see cref="PendingPurchaseResult.OutcomeUnknown"/> (the write started and
        /// timed out or was cancelled: it may have committed, so the caller must NOT compensate and must retry the same call with the same arguments until a
        /// definite result). <see cref="PendingPurchaseResult.InsufficientGold"/> and <see cref="PendingPurchaseResult.ConcurrencyConflict"/>
        /// are never returned. Requires a store that also implements <see cref="IPendingPurchaseStore"/> and a service with a write queue.
        /// </remarks>
        /// <example>
        /// Buy: <c>TrySpendGold(charId, totalCost, reason)</c> in memory (insufficient funds: reject, the store is not called);
        /// then <c>BeginPurchase(charId, record, balanceAfter, versionAfter, ct)</c>; if that does not return
        /// <see cref="PendingPurchaseResult.Success"/>, <c>AddGold(charId, totalCost, CompensatingRefund)</c> and reject.
        /// On success keep <see cref="PendingPurchaseBeginResult.PurchaseId"/> for <see cref="CompletePurchase"/> or <see cref="RefundPurchase"/>.
        /// </example>
        /// <param name="charId">The purchasing character; must equal <paramref name="record"/>'s <see cref="PendingPurchaseRecord.CharId"/>.</param>
        /// <param name="record">The purchase to record (its id and state are ignored; it is stored as <see cref="PendingPurchaseState.GoldDebited"/>).</param>
        /// <param name="goldBalance">The in-memory gold balance after the debit.</param>
        /// <param name="goldVersion">The in-memory gold version after the debit.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="System.ArgumentException">Thrown synchronously when <paramref name="charId"/> differs from the record's character (a caller bug).</exception>
        /// <exception cref="System.InvalidOperationException">Thrown synchronously when the service has no purchase store or no write queue (a wiring error).</exception>
        Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct);

        /// <summary>
        /// Deletes a pending purchase after the grant succeeded (ADR-001 Amendment A2). No gold write. Idempotent: an id that
        /// does not exist returns <see cref="CharacterSaveResult.Success"/>; an id owned by another character returns
        /// <see cref="CharacterSaveResult.CharacterNotFound"/> and deletes nothing. Queued behind every earlier write of the character.
        /// A store failure or a token cancelled before the write started returns <see cref="CharacterSaveResult.DatabaseError"/>;
        /// a started write that timed out or was cancelled returns <see cref="CharacterSaveResult.OutcomeUnknown"/> (retry the same call).
        /// </summary>
        /// <param name="purchaseId">The <see cref="PendingPurchaseRecord.Id"/> of the purchase.</param>
        /// <param name="charId">The purchasing character; it also names the write queue slot.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct);

        /// <summary>
        /// Deletes a pending purchase and stores the refunded gold in one write, STORE FIRST (ADR-001 Amendment A2).
        /// </summary>
        /// <remarks>
        /// The caller computes the cap-safe in-memory balance plus the purchase's total cost and the in-memory version plus 1,
        /// calls this method, and only on <see cref="CharacterSaveResult.Success"/> calls
        /// <c>AddGold(charId, totalCost, CompensatingRefund)</c> in memory. An id that does not exist returns
        /// <see cref="CharacterSaveResult.Success"/> and writes nothing (idempotent; the caller must then not credit again if
        /// it knows the refund already ran); an id owned by another character returns <see cref="CharacterSaveResult.CharacterNotFound"/>;
        /// a store failure or a token cancelled before the write started returns <see cref="CharacterSaveResult.DatabaseError"/> and leaves the
        /// record outstanding; a started write that timed out or was cancelled returns <see cref="CharacterSaveResult.OutcomeUnknown"/>: the caller
        /// must NOT credit memory and must retry the same call with the same arguments until a definite result. Queued behind every earlier write
        /// of the character.
        /// </remarks>
        /// <param name="purchaseId">The <see cref="PendingPurchaseRecord.Id"/> of the purchase.</param>
        /// <param name="charId">The purchasing character.</param>
        /// <param name="goldBalance">The gold balance to store (balance plus the refund, cap-safe).</param>
        /// <param name="goldVersion">The gold version to store (version plus 1).</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct);

        /// <summary>
        /// Loads the purchases of a character that are not yet completed or refunded (ADR-001). The list is empty, not null,
        /// when there is none, in ascending <see cref="PendingPurchaseRecord.Id"/> order. Read-only, so not queued. The result type
        /// has no error code: a store failure or a timeout faults the returned task, and a token cancelled at entry returns a
        /// cancelled task without calling the store.
        /// </summary>
        /// <param name="charId">The character.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct);
    }
}

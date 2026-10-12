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

        /// <summary>Saves a character after an irreversible outcome.</summary>
        /// <param name="id">The character to save.</param>
        /// <param name="trigger">The outcome that triggered the save.</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>Begins a purchase: debits gold and records a pending purchase (ADR-001).</summary>
        /// <param name="charId">The purchasing character.</param>
        /// <param name="record">The purchase to record.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct);

        /// <summary>Marks a pending purchase completed (ADR-001).</summary>
        /// <param name="purchaseId">The <see cref="PendingPurchaseRecord.Id"/> of the purchase.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct);

        /// <summary>Refunds a pending purchase (ADR-001).</summary>
        /// <param name="purchaseId">The <see cref="PendingPurchaseRecord.Id"/> of the purchase.</param>
        /// <param name="charId">The purchasing character.</param>
        /// <param name="refundAmount">The gold to return.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct);

        /// <summary>Loads the purchases of a character that are not yet completed or refunded (ADR-001).</summary>
        /// <param name="charId">The character.</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct);
    }
}

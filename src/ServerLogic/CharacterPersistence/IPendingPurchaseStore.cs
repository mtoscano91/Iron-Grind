using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The store seam of the <c>pending_purchases</c> lifecycle (ADR-001 Amendment A2, Character Persistence Story 011).
    /// A sibling of <see cref="ICharacterRecordStore"/>, which stays unchanged; one class may implement both.
    /// </summary>
    /// <remarks>
    /// Same failure contract as <see cref="ICharacterRecordStore"/>: a store failure is a faulted task, cancellation is a
    /// cancelled task. The store never checks a balance and never clamps: gold values are written as given (the
    /// Currency System in memory is the authority). A purchase write never changes <c>SaveVersion</c>.
    /// </remarks>
    public interface IPendingPurchaseStore
    {
        /// <summary>
        /// One atomic write: inserts <paramref name="record"/> in state <see cref="PendingPurchaseState.GoldDebited"/> with a
        /// new id and sets the character row's <c>GoldBalance</c> / <c>GoldVersion</c> to exactly the given values.
        /// </summary>
        /// <remarks>
        /// Idempotent per request, for a retry after an unknown outcome: when a <see cref="PendingPurchaseState.GoldDebited"/>
        /// record with the same (<see cref="PendingPurchaseRecord.CharId"/>, <see cref="PendingPurchaseRecord.RequestId"/>)
        /// already exists, that record's id is returned and nothing is written (no new record, no gold write).
        /// </remarks>
        /// <param name="record">The purchase; its <see cref="PendingPurchaseRecord.CharId"/> names the character and its id and state are ignored.</param>
        /// <param name="goldBalance">The gold balance to store.</param>
        /// <param name="goldVersion">The gold version to store.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The new (or, for a retry, the existing) id, greater than 0 and increasing; 0 when the character has no row (nothing written).</returns>
        Task<long> BeginPurchaseAsync(PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct);

        /// <summary>Deletes a purchase record of the character. No gold write.</summary>
        /// <param name="purchaseId">The id of the purchase.</param>
        /// <param name="charId">The character that must own the purchase.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <see cref="PendingPurchaseWriteOutcome.Applied"/> if the record was deleted, <see cref="PendingPurchaseWriteOutcome.NotFound"/>
        /// if none existed, <see cref="PendingPurchaseWriteOutcome.OtherCharacter"/> (nothing deleted) if it belongs to another character.
        /// </returns>
        Task<PendingPurchaseWriteOutcome> CompletePurchaseAsync(long purchaseId, CharacterID charId, CancellationToken ct);

        /// <summary>
        /// One atomic write: deletes the purchase record and sets the character row's <c>GoldBalance</c> / <c>GoldVersion</c>
        /// to the given values.
        /// </summary>
        /// <param name="purchaseId">The id of the purchase.</param>
        /// <param name="charId">The character that must own the purchase.</param>
        /// <param name="goldBalance">The gold balance to store.</param>
        /// <param name="goldVersion">The gold version to store.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>See <see cref="PendingPurchaseWriteOutcome"/>; only <see cref="PendingPurchaseWriteOutcome.Applied"/> writes anything.</returns>
        Task<PendingPurchaseWriteOutcome> RefundPurchaseAsync(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct);

        /// <summary>Loads every purchase of a character in state <see cref="PendingPurchaseState.GoldDebited"/>.</summary>
        /// <param name="charId">The character.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A fresh list in ascending <see cref="PendingPurchaseRecord.Id"/> order, never null; empty when there is none.</returns>
        Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingAsync(CharacterID charId, CancellationToken ct);
    }
}

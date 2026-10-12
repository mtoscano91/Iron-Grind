using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>ADR-006's <c>CharacterPersistenceService</c>, built over <see cref="ICharacterRecordStore"/>.</summary>
    /// <remarks>
    /// Built over the store seam so it runs and is tested with no database. Implemented so far: <c>CreateStub</c>.
    /// Each of the other methods returns a task faulted with <see cref="NotSupportedException"/> naming the
    /// story that implements it — a faulted task, not a throw, so that tick code passing the call to
    /// <c>ITickCompletionQueue.Track</c> gets it through the normal failure path (ADR-011).
    /// </remarks>
    public sealed class CharacterPersistenceService : ICharacterPersistence
    {
        private readonly ICharacterRecordStore _store;

        /// <summary>Creates the service over a store.</summary>
        /// <param name="store">The record store.</param>
        public CharacterPersistenceService(ICharacterRecordStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Inserts the Level 1 stub record of a new character (CR-CP-9, F-CP-3).
        /// </summary>
        /// <remarks>
        /// CR-CP-1 gives <c>CreateStub</c> no result code, so failure is carried by the returned task: it faults
        /// with <see cref="InvalidOperationException"/> when the character id already has a record (nothing is
        /// written), faults with the store's exception when the store fails, and is cancelled when the token is
        /// cancelled. It reads no live game state: its snapshot is the record it builds before the first
        /// <c>await</c> (ADR-011 Decision 3). There is no stub timeout constant; the caller's token is the bound
        /// (GDD OQ-CP-4).
        /// </remarks>
        /// <param name="accountId">The owning account.</param>
        /// <param name="characterId">The new character's id.</param>
        /// <param name="classType">Raw class type.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that completes when the stub is stored; faulted or cancelled as described in the remarks.</returns>
        public async Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct)
        {
            CharacterRecord record = CharacterStubDefaults.CreateRecord(accountId, characterId, classType);
            bool inserted = await _store.InsertAsync(record, ct).ConfigureAwait(false);
            if (!inserted)
            {
                throw new InvalidOperationException("Character " + characterId + " already has a record; CreateStub wrote nothing.");
            }
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 003): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
        {
            return Task.FromException<CharacterLoadResult>(new NotSupportedException("LoadCharacter is implemented by Character Persistence Story 003."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 008): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("SaveIrreversibleOutcome is implemented by Character Persistence Story 008."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 007): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("SaveSession is implemented by Character Persistence Story 007."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 007): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("SaveSession is implemented by Character Persistence Story 007."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 011): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<PendingPurchaseResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, CancellationToken ct)
        {
            return Task.FromException<PendingPurchaseResult>(new NotSupportedException("BeginPurchase is implemented by Character Persistence Story 011."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 011): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("CompletePurchase is implemented by Character Persistence Story 011."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 011): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint refundAmount, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("RefundPurchase is implemented by Character Persistence Story 011."));
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 011): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct)
        {
            return Task.FromException<IReadOnlyList<PendingPurchaseRecord>>(new NotSupportedException("LoadOutstandingPurchases is implemented by Character Persistence Story 011."));
        }
    }
}

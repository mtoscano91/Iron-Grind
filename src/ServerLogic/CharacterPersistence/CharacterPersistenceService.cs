using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LevelingSystem;

namespace IronGrind.CharacterPersistence
{
    /// <summary>ADR-006's <c>CharacterPersistenceService</c>, built over <see cref="ICharacterRecordStore"/>.</summary>
    /// <remarks>
    /// Built over the store seam so it runs and is tested with no database. Implemented so far: <c>CreateStub</c>, <c>LoadCharacter</c> (Story 003)
    /// and the synchronous <c>ApplyLoadedRecord</c>. Each of the other methods returns a task faulted with <see cref="NotSupportedException"/> naming the
    /// story that implements it — a faulted task, not a throw, so that tick code passing the call to
    /// <c>ITickCompletionQueue.Track</c> gets it through the normal failure path (ADR-011).
    /// </remarks>
    public sealed class CharacterPersistenceService : ICharacterPersistence
    {
        private readonly ICharacterRecordStore _store;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly LevelingService _leveling;

        /// <summary>Creates the service over a store, with no game-state wiring (<c>ApplyLoadedRecord</c> then throws).</summary>
        /// <param name="store">The record store.</param>
        public CharacterPersistenceService(ICharacterRecordStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>Creates the service over a store and the live game-state services that <c>ApplyLoadedRecord</c> writes to.</summary>
        /// <param name="store">The record store.</param>
        /// <param name="stats">Character Stats.</param>
        /// <param name="leveling">The Leveling service.</param>
        public CharacterPersistenceService(ICharacterRecordStore store, IronGrind.CharacterStats.CharacterStats stats, LevelingService leveling)
            : this(store)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _leveling = leveling ?? throw new ArgumentNullException(nameof(leveling));
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
        /// <remarks>
        /// Implements CR-CP-3 steps 1-3 as validation only (Story 003): id check, fetch, account check and range
        /// validation, all on the fetched record, which is not live state. A fetched record whose
        /// <c>CharacterID</c> is not the requested one, and a float stat that is infinite, are also
        /// <see cref="CharacterLoadCode.CorruptRecord"/> (both beyond the GDD's step 3 list). It never touches Character Stats or
        /// Leveling; <see cref="ApplyLoadedRecord"/> does the writes. The task never faults and is never
        /// cancelled: a store fault, or <paramref name="ct"/> firing while the fetch is pending (CR-CP-8, the
        /// caller's token is the timeout), gives <see cref="CharacterLoadCode.DatabaseError"/>. The service
        /// observes the token itself because a store call need not honour it. A fetch that is abandoned by a
        /// cancel and later faults has its exception observed by a synchronous continuation.
        /// </remarks>
        public async Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct)
        {
            if (id == CharacterID.Invalid)
            {
                return new CharacterLoadResult(CharacterLoadCode.CorruptRecord, ZoneID.Invalid);
            }

            CharacterRecord record;
            try
            {
                record = await FetchObservingTokenAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return new CharacterLoadResult(CharacterLoadCode.DatabaseError, ZoneID.Invalid);
            }

            if (record == null)
            {
                return new CharacterLoadResult(CharacterLoadCode.CharacterNotFound, ZoneID.Invalid);
            }

            // A row for another character is corrupt, not someone else's: checked before the account.
            if (record.CharacterID != id)
            {
                return new CharacterLoadResult(CharacterLoadCode.CorruptRecord, ZoneID.Invalid);
            }

            if (record.AccountID != ownerAccountID)
            {
                return new CharacterLoadResult(CharacterLoadCode.AccountMismatch, ZoneID.Invalid);
            }

            if (!IsInRange(record))
            {
                return new CharacterLoadResult(CharacterLoadCode.CorruptRecord, ZoneID.Invalid);
            }

            return new CharacterLoadResult(CharacterLoadCode.Success, record.LastZoneID, record);
        }

        // Races the store fetch against the token. Throws OperationCanceledException if the token wins.
        private async Task<CharacterRecord> FetchObservingTokenAsync(CharacterID id, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                // Nothing can win a race against a token that never fires.
                return await _store.FetchAsync(id, ct).ConfigureAwait(false);
            }

            Task<CharacterRecord> fetch = _store.FetchAsync(id, ct);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (CancellationTokenRegistration registration = ct.Register(() => cancelled.TrySetResult(true)))
            {
                Task winner = await Task.WhenAny(fetch, cancelled.Task).ConfigureAwait(false);
                if (winner != fetch)
                {
                    // The fetch is abandoned; observe its exception if it faults later.
                    fetch.ContinueWith(
                        t => { Exception ignored = t.Exception; },
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw new OperationCanceledException(ct);
                }
            }

            return await fetch.ConfigureAwait(false);
        }

        private static bool IsInRange(CharacterRecord r)
        {
            return r.Level >= CharacterPersistenceConstants.LOAD_LEVEL_MIN
                && r.Level <= CharacterPersistenceConstants.LOAD_LEVEL_MAX
                && r.Experience >= CharacterPersistenceConstants.LOAD_EXPERIENCE_MIN
                && r.HeldFreePoints >= CharacterPersistenceConstants.LOAD_HELD_FREE_POINTS_MIN
                && IsAttribute(r.STR) && IsAttribute(r.DEX) && IsAttribute(r.VIT) && IsAttribute(r.INT)
                && IsMaxResource(r.MaxHP) && IsMaxResource(r.MaxMP)
                && r.AttackPower >= CharacterPersistenceConstants.LOAD_COMBAT_STAT_MIN
                && r.Defense >= CharacterPersistenceConstants.LOAD_COMBAT_STAT_MIN
                && r.MagicDefense >= CharacterPersistenceConstants.LOAD_COMBAT_STAT_MIN
                && IsPositiveFinite(r.CritChance)
                && IsPositiveFinite(r.CritMultiplier)
                && IsPositiveFinite(r.AttackRange)
                && IsPositiveFinite(r.AttackSpeedMultiplier)
                && IsPositiveFinite(r.MovementSpeed);
        }

        // CR-CP-3 says "> 0"; an infinite stat is also corrupt (NaN already fails "> 0").
        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value);
        }

        private static bool IsAttribute(int value)
        {
            return value >= CharacterPersistenceConstants.LOAD_ATTRIBUTE_MIN && value <= CharacterPersistenceConstants.LOAD_ATTRIBUTE_MAX;
        }

        private static bool IsMaxResource(int value)
        {
            return value >= CharacterPersistenceConstants.LOAD_MAX_RESOURCE_MIN && value <= CharacterPersistenceConstants.LOAD_MAX_RESOURCE_MAX;
        }

        /// <summary>
        /// Writes a validated record's base stats and leveling state into the live game state (CR-CP-3 step 3).
        /// Synchronous; the caller runs it on the tick thread from its <c>ITickCompletionQueue.Track</c> callback,
        /// with the record from <see cref="CharacterLoadResult.Record"/>. It does not re-validate.
        /// </summary>
        /// <remarks>
        /// Order: class type registered with Leveling, the 11 int stats, the 5 float stats, then the held free
        /// points through <c>RestoreLevelingState</c>. Stories 004 and 005 extend this method with their steps
        /// (the CR-CP-3 order is kept inside it). CurrentHP and CurrentMP are deliberately not written here:
        /// they are Story 004's step 8, after the gear modifiers are registered, because the clamp is to the
        /// effective maximum. Held free points above the class maximum, or a class type Leveling does not
        /// know, are clamped by <c>RestoreLevelingState</c> with a logged error (Leveling AC-LS-30), not rejected.
        /// </remarks>
        /// <param name="entityId">The entity to restore, allocated by the caller.</param>
        /// <param name="record">The validated record.</param>
        /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The service was built without Character Stats and Leveling.</exception>
        public void ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (_stats == null || _leveling == null)
            {
                throw new InvalidOperationException("ApplyLoadedRecord needs the service to be built with Character Stats and Leveling.");
            }

            _leveling.RegisterPlayerClassType(entityId, record.ClassType);

            _stats.SetBaseStat(entityId, StatID.Level, record.Level);
            _stats.SetBaseStat(entityId, StatID.Experience, record.Experience);
            _stats.SetBaseStat(entityId, StatID.Strength, record.STR);
            _stats.SetBaseStat(entityId, StatID.Dexterity, record.DEX);
            _stats.SetBaseStat(entityId, StatID.Vitality, record.VIT);
            _stats.SetBaseStat(entityId, StatID.Intelligence, record.INT);
            _stats.SetBaseStat(entityId, StatID.MaxHP, record.MaxHP);
            _stats.SetBaseStat(entityId, StatID.MaxMP, record.MaxMP);
            _stats.SetBaseStat(entityId, StatID.AttackPower, record.AttackPower);
            _stats.SetBaseStat(entityId, StatID.Defense, record.Defense);
            _stats.SetBaseStat(entityId, StatID.MagicDefense, record.MagicDefense);

            _stats.SetBaseStatFloat(entityId, StatID.CritChance, record.CritChance);
            _stats.SetBaseStatFloat(entityId, StatID.CritMultiplier, record.CritMultiplier);
            _stats.SetBaseStatFloat(entityId, StatID.AttackRange, record.AttackRange);
            _stats.SetBaseStatFloat(entityId, StatID.AttackSpeedMultiplier, record.AttackSpeedMultiplier);
            _stats.SetBaseStatFloat(entityId, StatID.MovementSpeed, record.MovementSpeed);

            _leveling.RestoreLevelingState(entityId, new LevelingStateSnapshot(record.HeldFreePoints));
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

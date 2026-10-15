using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;

namespace IronGrind.CharacterPersistence
{
    /// <summary>ADR-006's <c>CharacterPersistenceService</c>, built over <see cref="ICharacterRecordStore"/>.</summary>
    /// <remarks>
    /// Built over the store seam so it runs and is tested with no database. Implemented so far: <c>CreateStub</c>, <c>LoadCharacter</c> (Story 003)
    /// and the synchronous <c>ApplyLoadedRecord</c> (Story 003 base stats; Story 004 inventory, gold and current HP / MP),
    /// and both <c>SaveSession</c> overloads (Story 007: snapshot on the tick thread, then the per-character write queue). Each of the other methods returns a task faulted with <see cref="NotSupportedException"/> naming the
    /// story that implements it — a faulted task, not a throw, so that tick code passing the call to
    /// <c>ITickCompletionQueue.Track</c> gets it through the normal failure path (ADR-011).
    /// <para>The one exception to "returns a task, never throws" is a wiring error: a service built without the
    /// dependencies a method needs (<c>ApplyLoadedRecord</c> without Inventory and Currency, <c>SaveSession</c> without
    /// the zone locator, write queue and clock) throws <see cref="InvalidOperationException"/> synchronously.</para>
    /// </remarks>
    public sealed class CharacterPersistenceService : ICharacterPersistence
    {
        private readonly ICharacterRecordStore _store;
        private readonly IronGrind.CharacterStats.CharacterStats _stats;
        private readonly LevelingService _leveling;
        private readonly IInventoryService _inventory;
        private readonly ICurrencyStateRestore _currency;
        private readonly ICharacterZoneLocator _zones;
        private readonly CharacterWriteQueue _queue;
        // Always injected: Character Persistence never reads the system clock (Story 002 rule).
        private readonly Func<DateTime> _utcNow;
        private readonly TimeSpan _writeTimeout = TimeSpan.FromSeconds(CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS);

        // Persistence-layer session table (Story 007 readiness decision 2): lock-guarded, holds no game state.
        private readonly Dictionary<CharacterID, SessionEntry> _sessions = new Dictionary<CharacterID, SessionEntry>();
        private readonly object _sessionLock = new object();

        private sealed class SessionEntry
        {
            public EntityID EntityId;
            public AccountID AccountId;
            public string CharacterName;
            public byte ClassType;
            public DateTime CreatedAtUtc;
            public ZoneID LastZoneId;
            public GearSlotRecord[] GearSlots;
            public uint SaveVersion;
        }

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
        /// Creates the fully wired service: every dependency <c>ApplyLoadedRecord</c> writes to (Story 004).
        /// </summary>
        /// <param name="store">The record store.</param>
        /// <param name="stats">Character Stats.</param>
        /// <param name="leveling">The Leveling service.</param>
        /// <param name="inventory">The Inventory service that receives the bag through <c>ImportSnapshot</c>.</param>
        /// <param name="currency">The Currency restore seam that receives the gold balance and version.</param>
        public CharacterPersistenceService(
            ICharacterRecordStore store,
            IronGrind.CharacterStats.CharacterStats stats,
            LevelingService leveling,
            IInventoryService inventory,
            ICurrencyStateRestore currency)
            : this(store, stats, leveling)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _currency = currency ?? throw new ArgumentNullException(nameof(currency));
        }

        /// <summary>
        /// Creates the fully wired service including the save path (Story 007): the zone locator, the per-character
        /// write queue, the clock used to measure write latency, and the write timeout.
        /// </summary>
        /// <remarks>
        /// The earlier constructors leave the zone locator and queue unset; on such a service
        /// <see cref="SaveSession(CharacterID, SessionEndReason, CancellationToken)"/> throws
        /// <see cref="InvalidOperationException"/> synchronously (a wiring bug, like <see cref="ApplyLoadedRecord"/>).
        /// </remarks>
        /// <param name="store">The record store.</param>
        /// <param name="stats">Character Stats.</param>
        /// <param name="leveling">The Leveling service.</param>
        /// <param name="inventory">The Inventory service (import on load, export on save).</param>
        /// <param name="currency">The Currency persistence seam (restore on load, read on save).</param>
        /// <param name="zones">Tells the save which zone the character is in.</param>
        /// <param name="queue">The per-character write queue (Story 006).</param>
        /// <param name="utcNow">The UTC clock used to measure write latency.</param>
        /// <param name="writeTimeout">How long a store update may take before the save gives up with <see cref="CharacterSaveResult.DatabaseError"/>; must be greater than zero.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="writeTimeout"/> is zero or negative.</exception>
        public CharacterPersistenceService(
            ICharacterRecordStore store,
            IronGrind.CharacterStats.CharacterStats stats,
            LevelingService leveling,
            IInventoryService inventory,
            ICurrencyStateRestore currency,
            ICharacterZoneLocator zones,
            CharacterWriteQueue queue,
            Func<DateTime> utcNow,
            TimeSpan writeTimeout)
            : this(store, stats, leveling, inventory, currency)
        {
            _zones = zones ?? throw new ArgumentNullException(nameof(zones));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (writeTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(writeTimeout), "The write timeout must be greater than zero.");
            }

            _writeTimeout = writeTimeout;
        }

        /// <summary>
        /// Creates the fully wired service with a new write queue and the default write timeout
        /// (<see cref="CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS"/>). The clock is still
        /// injected: Character Persistence never reads the system clock.
        /// </summary>
        /// <param name="store">The record store.</param>
        /// <param name="stats">Character Stats.</param>
        /// <param name="leveling">The Leveling service.</param>
        /// <param name="inventory">The Inventory service.</param>
        /// <param name="currency">The Currency persistence seam.</param>
        /// <param name="zones">Tells the save which zone the character is in.</param>
        /// <param name="utcNow">The UTC clock used to measure write latency.</param>
        public CharacterPersistenceService(
            ICharacterRecordStore store,
            IronGrind.CharacterStats.CharacterStats stats,
            LevelingService leveling,
            IInventoryService inventory,
            ICurrencyStateRestore currency,
            ICharacterZoneLocator zones,
            Func<DateTime> utcNow)
            : this(
                store,
                stats,
                leveling,
                inventory,
                currency,
                zones,
                new CharacterWriteQueue(),
                utcNow,
                TimeSpan.FromSeconds(CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS))
        {
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
        /// <c>CharacterID</c> is not the requested one, a float stat that is infinite, a <c>GoldBalance</c> above
        /// <see cref="CurrencySystem.GOLD_CAP"/>, and a <c>CurrentHP</c> or <c>CurrentMP</c> that is NaN are also
        /// <see cref="CharacterLoadCode.CorruptRecord"/> (all beyond the GDD's step 3 list). It never touches Character Stats or
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
                && IsPositiveFinite(r.MovementSpeed)
                // Beyond the GDD's step 3 list. Gold above the cap would break Currency's balance <= cap invariant
                // (AddGold's cap arithmetic wraps). CurrentHP / CurrentMP may be negative or above the maximum
                // (dead-on-load and the clamp handle those), but NaN slips past both comparisons.
                && r.GoldBalance <= CurrencySystem.GOLD_CAP
                && !float.IsNaN(r.CurrentHP)
                && !float.IsNaN(r.CurrentMP);
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
        /// Writes a validated record into the live game state (CR-CP-3 steps 3 and 5 to 8): base stats and leveling
        /// state, the inventory, the gold, then current HP and MP.
        /// Synchronous; the caller runs it on the tick thread from its <c>ITickCompletionQueue.Track</c> callback,
        /// with the record from <see cref="CharacterLoadResult.Record"/>. It does not re-validate.
        /// </summary>
        /// <remarks>
        /// Order: class type registered with Leveling, the 11 int stats, the 5 float stats, the held free
        /// points through <c>RestoreLevelingState</c> (Story 003); then the inventory import (step 5), the gold
        /// restore (step 6) and current HP / MP (step 8) (Story 004). Step 7 (at-cap) has no call: Leveling keeps no
        /// at-cap flag and derives it from Level == 60, which is already written. Story 005 inserts step 4 (equipment)
        /// between the stat writes and the inventory import and returns <see cref="CharacterLoadCode.ItemDatabaseMiss"/>
        /// through this return value. Any existing session entry for <c>record.CharacterID</c> is removed first (after
        /// the argument and wiring checks, before any write), so a reload that returns early or throws leaves no stale
        /// entry: <c>SaveSession</c> then returns <see cref="CharacterSaveResult.CharacterNotFound"/> until a load succeeds.
        /// Held free points above the class maximum, or a class type Leveling does not
        /// know, are clamped by <c>RestoreLevelingState</c> with a logged error (Leveling AC-LS-30), not rejected.
        /// <para>Current HP / MP are written last, after the gold, because the setters clamp to the effective maximum
        /// (F-CP-4, no code of its own here); a saved HP of 0 or less (dead on load, EC-DR-2) loads as the effective
        /// MaxHP. A negative saved MP is clamped to 0 by <c>SetCurrentMP</c>.</para>
        /// <para>On a code other than <see cref="CharacterLoadCode.Success"/> (and, from Story 005,
        /// <see cref="CharacterLoadCode.ItemDatabaseMiss"/>) the caller discards the entity: the earlier stat writes
        /// and the Leveling registration (class type, held free points) have already happened and are not rolled
        /// back. The same holds if this method throws part-way — for one, <c>ImportSnapshot</c> throws
        /// <see cref="InvalidOperationException"/> when called from inside an <c>OnInventoryChanged</c> subscriber.</para>
        /// </remarks>
        /// <param name="entityId">The entity to restore, allocated by the caller.</param>
        /// <param name="record">The validated record.</param>
        /// <returns>
        /// <see cref="CharacterLoadCode.Success"/> when everything was applied; <see cref="CharacterLoadCode.InventoryNotRestored"/>
        /// when <c>ImportSnapshot</c> refused the bag, in which case no gold, HP or MP is written.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The service was not built with the five-argument constructor (Character Stats, Leveling, Inventory and Currency); or, from <c>ImportSnapshot</c>, the call was made from inside an <c>OnInventoryChanged</c> subscriber.</exception>
        public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (_stats == null || _leveling == null || _inventory == null || _currency == null)
            {
                throw new InvalidOperationException("ApplyLoadedRecord needs the service to be built with Character Stats, Leveling, Inventory and Currency.");
            }

            // Story 007 review: a reload that fails part-way must not leave the previous session's entry behind.
            lock (_sessionLock)
            {
                _sessions.Remove(record.CharacterID);
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

            // Step 5: dense 20-slot arrays to a sparse snapshot. A refused import is never an empty inventory.
            InventorySnapshot snapshot = BuildInventorySnapshot(record);
            if (!_inventory.ImportSnapshot(record.CharacterID, snapshot))
            {
                return CharacterLoadCode.InventoryNotRestored;
            }

            // Step 6: gold balance and version, no OnGoldSync.
            _currency.RestoreCharacter(record.CharacterID, record.GoldBalance, record.GoldVersion);

            // Step 7 (at-cap) has no call: Leveling derives it from Level == 60, already written above.

            // Step 8: current HP / MP. The setters clamp to [0, effective max] (F-CP-4). Dead on load (EC-DR-2) gets full HP.
            float hp = record.CurrentHP <= 0f ? (float)_stats.GetEffectiveStat(entityId, StatID.MaxHP) : record.CurrentHP;
            _stats.SetCurrentHP(entityId, hp);
            _stats.SetCurrentMP(entityId, record.CurrentMP);

            // Story 007: the session entry exists only for a character that loaded fully.
            var entry = new SessionEntry
            {
                EntityId = entityId,
                AccountId = record.AccountID,
                CharacterName = record.CharacterName,
                ClassType = record.ClassType,
                CreatedAtUtc = record.CreatedAtUtc,
                LastZoneId = record.LastZoneID,
                GearSlots = (GearSlotRecord[])record.GearSlots.Clone(),
                SaveVersion = record.SaveVersion,
            };
            lock (_sessionLock)
            {
                _sessions[record.CharacterID] = entry;
            }

            return CharacterLoadCode.Success;
        }

        private static InventorySnapshot BuildInventorySnapshot(CharacterRecord record)
        {
            var entries = new List<InventorySnapshotEntry>(InventoryConstants.INVENTORY_SLOT_COUNT);
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (record.InventorySlots[i] == ItemID.Invalid)
                {
                    continue;
                }

                entries.Add(new InventorySnapshotEntry((byte)i, record.InventorySlots[i].RawValue, record.InventoryItemCounts[i], record.InventoryEnhancementLevels[i]));
            }

            return entries.Count == 0 ? InventorySnapshot.Empty : new InventorySnapshot(entries);
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented yet (Character Persistence Story 008): returns a task faulted with <see cref="NotSupportedException"/>.</remarks>
        public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct)
        {
            return Task.FromException<CharacterSaveResult>(new NotSupportedException("SaveIrreversibleOutcome is implemented by Character Persistence Story 008."));
        }

        /// <summary>
        /// Saves a character's session state (CR-CP-10, Story 007): the current game state is copied on the calling
        /// (tick) thread, then one version-checked update is queued on the character's write queue.
        /// </summary>
        /// <remarks>
        /// Everything read from live game state (stats, leveling, inventory, gold, zone) is read in the synchronous
        /// prefix, before the queue is entered; the queued write touches only that copy, the store, the session table,
        /// the clock and the log (ADR-011 Decision 3). The expected <c>SaveVersion</c> is taken from the session entry
        /// at call time, so a second call queued behind the first carries the same version and ends with
        /// <see cref="CharacterSaveResult.ConcurrencyConflict"/> (EC-CP-4, AC-CP-20). Gear is written as loaded.
        /// <c>IsTransitioning</c> is neither read nor written (EC-CP-3).
        /// <para>The returned task never faults and is never cancelled: a store fault, a timeout, a cancelled token or
        /// a failure while taking the snapshot gives <see cref="CharacterSaveResult.DatabaseError"/> (one error log
        /// where the write ran); no one is disconnected and nothing is torn down (CR-CP-11). A character with no
        /// session entry (never fully loaded, or released) gives <see cref="CharacterSaveResult.CharacterNotFound"/> with no
        /// store call. A write slower than the warning / critical thresholds logs a warning / error (ADR-006 Decision 4).</para>
        /// <para>Nothing is written, and <see cref="CharacterSaveResult.DatabaseError"/> is returned with one error log
        /// and no store call, when Currency has no state for the character, or when a <c>hpOverride</c> is NaN.
        /// A save cancelled by its token while still waiting in the queue (its write never ran) gives
        /// <see cref="CharacterSaveResult.DatabaseError"/> with one warning log.</para>
        /// <para>Stale version after an unknown outcome: after a <see cref="CharacterSaveResult.DatabaseError"/> from a
        /// timeout, from a cancel after the write started, or from a store fault, the outcome of the write is unknown
        /// (ADR-011 Decision 2). If it did commit, the stored <c>SaveVersion</c> is ahead of the session entry, and every
        /// later <c>SaveSession</c> for that character returns <see cref="CharacterSaveResult.ConcurrencyConflict"/>
        /// until the character is reloaded. The recovery policy belongs to Stories 008 and 010 (epic design conflict 7).</para>
        /// </remarks>
        /// <param name="id">The character to save.</param>
        /// <param name="reason">Why the session ended; the same handling for every reason.</param>
        /// <param name="ct">Cancellation token for the queued write.</param>
        /// <returns>A task with the save outcome.</returns>
        /// <exception cref="InvalidOperationException">The service was built without the zone locator and write queue (a wiring bug).</exception>
        public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct)
        {
            return SaveSessionCore(id, reason, null, ct);
        }

        /// <summary>
        /// Same as <see cref="SaveSession(CharacterID, SessionEndReason, CancellationToken)"/>, but writes
        /// <paramref name="hpOverride"/> as <c>CurrentHP</c> instead of the value read from Character Stats
        /// (AC-CP-30, e.g. 0 for a zone closed on a living character). The override is written as given, without a
        /// clamp; the next load clamps it (F-CP-4). <c>CurrentMP</c> is still read from Character Stats.
        /// </summary>
        /// <param name="id">The character to save.</param>
        /// <param name="reason">Why the session ended.</param>
        /// <param name="hpOverride">The HP to write. NaN gives <see cref="CharacterSaveResult.DatabaseError"/> with one error log and nothing written; a negative or infinite value is written as given. See the other overload's remarks for the stale-version case after an unknown write outcome.</param>
        /// <param name="ct">Cancellation token for the queued write.</param>
        /// <returns>A task with the save outcome; never faulted or cancelled.</returns>
        /// <exception cref="InvalidOperationException">The service was built without the zone locator and write queue (a wiring bug).</exception>
        public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct)
        {
            return SaveSessionCore(id, reason, hpOverride, ct);
        }

        // Synchronous prefix: not async, so every live read happens on the caller's thread before the queue is entered.
        private Task<CharacterSaveResult> SaveSessionCore(CharacterID id, SessionEndReason reason, float? hpOverride, CancellationToken ct)
        {
            if (_stats == null || _leveling == null || _inventory == null || _currency == null || _zones == null || _queue == null || _utcNow == null)
            {
                throw new InvalidOperationException("SaveSession needs the service to be built with Character Stats, Leveling, Inventory, Currency, a zone locator and a write queue.");
            }

            CharacterRecord snapshot;
            uint expected;
            try
            {
                if (!TryBuildSnapshot(id, hpOverride, out snapshot, out expected))
                {
                    return Task.FromResult(CharacterSaveResult.CharacterNotFound);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[CharacterPersistence] SaveSession (" + reason + ") for " + id + " could not take its snapshot: " + e.Message);
                return Task.FromResult(CharacterSaveResult.DatabaseError);
            }

            Task<CharacterSaveResult> queued;
            try
            {
                queued = _queue.Enqueue(id, ct2 => WriteAsync(id, snapshot, expected, reason, ct2), ct);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[CharacterPersistence] SaveSession (" + reason + ") for " + id + " could not be queued: " + e.Message);
                return Task.FromResult(CharacterSaveResult.DatabaseError);
            }

            return ObserveOutcomeAsync(queued, id, reason);
        }

        // The only async wrapper: awaits the queue's task and maps any fault or cancellation to DatabaseError.
        // WriteAsync logs its own failures and returns normally, so a faulted queue task needs no second log here.
        // Only a task cancelled before its write ran (WriteAsync never started, so it logged nothing) is logged here.
        private static async Task<CharacterSaveResult> ObserveOutcomeAsync(Task<CharacterSaveResult> queued, CharacterID id, SessionEndReason reason)
        {
            try
            {
                return await queued.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (queued.IsCanceled)
            {
                UnityEngine.Debug.LogWarning("[CharacterPersistence] SaveSession (" + reason + ") for " + id + " was cancelled before its write ran; nothing written.");
                return CharacterSaveResult.DatabaseError;
            }
            catch (Exception)
            {
                return CharacterSaveResult.DatabaseError;
            }
        }

        private bool TryBuildSnapshot(CharacterID id, float? hpOverride, out CharacterRecord record, out uint expected)
        {
            SessionEntry entry;
            lock (_sessionLock)
            {
                if (!_sessions.TryGetValue(id, out entry))
                {
                    record = null;
                    expected = 0u;
                    return false;
                }

                expected = entry.SaveVersion;
            }

            // Thrown to SaveSessionCore, which logs it once as an error and returns DatabaseError; no store call, no queue entry.
            if (hpOverride.HasValue && float.IsNaN(hpOverride.Value))
            {
                throw new InvalidOperationException("hpOverride is NaN; nothing written");
            }

            EntityID e = entry.EntityId;
            record = new CharacterRecord
            {
                CharacterID = id,
                AccountID = entry.AccountId,
                CharacterName = entry.CharacterName,
                ClassType = entry.ClassType,
                CreatedAtUtc = entry.CreatedAtUtc,
                SaveVersion = expected,
            };

            // Step 2: the four getters. GetEffectiveStat is never used; modifiers are not persisted.
            record.Level = _stats.GetBaseStat(e, StatID.Level);
            record.Experience = _stats.GetBaseStat(e, StatID.Experience);
            record.STR = _stats.GetBaseStat(e, StatID.Strength);
            record.DEX = _stats.GetBaseStat(e, StatID.Dexterity);
            record.VIT = _stats.GetBaseStat(e, StatID.Vitality);
            record.INT = _stats.GetBaseStat(e, StatID.Intelligence);
            record.MaxHP = _stats.GetBaseStat(e, StatID.MaxHP);
            record.MaxMP = _stats.GetBaseStat(e, StatID.MaxMP);
            record.AttackPower = _stats.GetBaseStat(e, StatID.AttackPower);
            record.Defense = _stats.GetBaseStat(e, StatID.Defense);
            record.MagicDefense = _stats.GetBaseStat(e, StatID.MagicDefense);
            record.CritChance = _stats.GetBaseStatFloat(e, StatID.CritChance);
            record.CritMultiplier = _stats.GetBaseStatFloat(e, StatID.CritMultiplier);
            record.AttackRange = _stats.GetBaseStatFloat(e, StatID.AttackRange);
            record.AttackSpeedMultiplier = _stats.GetBaseStatFloat(e, StatID.AttackSpeedMultiplier);
            record.MovementSpeed = _stats.GetBaseStatFloat(e, StatID.MovementSpeed);
            record.CurrentHP = hpOverride ?? _stats.GetCurrentHP(e);
            record.CurrentMP = _stats.GetCurrentMP(e);
            record.HeldFreePoints = _leveling.GetLevelingState(e).HeldFreePoints;

            // Step 3: gear is written as loaded (no Equipment System yet).
            Array.Copy(entry.GearSlots, record.GearSlots, record.GearSlots.Length);

            // Step 4: sparse snapshot to the dense 20-slot layout. Absent slots stay ItemID.Invalid / 0 / 0.
            InventorySnapshot inventory = _inventory.ExportSnapshot(id);
            if (inventory != null)
            {
                foreach (InventorySnapshotEntry slot in inventory.Slots)
                {
                    if (slot.SlotIndex >= InventoryConstants.INVENTORY_SLOT_COUNT)
                    {
                        continue;
                    }

                    record.InventorySlots[slot.SlotIndex] = new ItemID(slot.ItemId);
                    record.InventoryItemCounts[slot.SlotIndex] = slot.Quantity;
                    record.InventoryEnhancementLevels[slot.SlotIndex] = slot.EnhancementLevel;
                }
            }

            // Step 5: gold. A character Currency does not know must not be written as 0 / 0: the save is refused.
            if (!_currency.TryGetState(id, out uint balance, out uint version))
            {
                throw new InvalidOperationException("Currency has no state for " + id + "; nothing written");
            }

            record.GoldBalance = balance;
            record.GoldVersion = version;

            // Step 6: the current zone; Invalid keeps the loaded one.
            ZoneID zone = _zones.GetCurrentZone(id);
            record.LastZoneID = zone == ZoneID.Invalid ? entry.LastZoneId : zone;
            return true;
        }

        // Runs at the write's queue turn, possibly on a pool thread. Touches only the snapshot, the store, the session
        // table (under its lock), the clock and the log.
        private async Task<CharacterSaveResult> WriteAsync(CharacterID id, CharacterRecord snapshot, uint expected, SessionEndReason reason, CancellationToken ct)
        {
            DateTime start = _utcNow();
            int rows;
            try
            {
                rows = await UpdateObservingTimeoutAndTokenAsync(snapshot, expected, ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // CR-CP-11: one error for every reason alike. Nobody is disconnected, nothing is torn down.
                UnityEngine.Debug.LogError("[CharacterPersistence] SaveSession (" + reason + ") for " + id + " failed: " + e.GetType().Name + ": " + e.Message);
                return CharacterSaveResult.DatabaseError;
            }

            double elapsedMs = (_utcNow() - start).TotalMilliseconds;
            if (elapsedMs >= CharacterPersistenceConstants.PERSISTENCE_WRITE_CRITICAL_MS)
            {
                UnityEngine.Debug.LogError("[CharacterPersistence] SaveSession for " + id + " took " + elapsedMs + " ms (critical at " + CharacterPersistenceConstants.PERSISTENCE_WRITE_CRITICAL_MS + " ms).");
            }
            else if (elapsedMs >= CharacterPersistenceConstants.PERSISTENCE_WRITE_WARNING_MS)
            {
                UnityEngine.Debug.LogWarning("[CharacterPersistence] SaveSession for " + id + " took " + elapsedMs + " ms (warning at " + CharacterPersistenceConstants.PERSISTENCE_WRITE_WARNING_MS + " ms).");
            }

            if (rows == 1)
            {
                lock (_sessionLock)
                {
                    if (_sessions.TryGetValue(id, out SessionEntry entry) && entry.SaveVersion == expected)
                    {
                        entry.SaveVersion = unchecked(expected + 1u);
                    }
                }

                return CharacterSaveResult.Success;
            }

            UnityEngine.Debug.LogWarning("[CharacterPersistence] SaveSession (" + reason + ") for " + id + " conflicted: no stored row matched CharacterID " + id + " with SaveVersion " + expected + " (CR-CP-6, EC-CP-4). Not retried.");
            return CharacterSaveResult.ConcurrencyConflict;
        }

        // Races the store update against the write timeout and the token. Throws TimeoutException or
        // OperationCanceledException when the race is lost; the abandoned update's fault is observed.
        private async Task<int> UpdateObservingTimeoutAndTokenAsync(CharacterRecord snapshot, uint expected, CancellationToken ct)
        {
            Task<int> update = _store.UpdateAsync(snapshot, expected, ct);
            using (var timerCts = new CancellationTokenSource())
            {
                try
                {
                    Task timeout = Task.Delay(_writeTimeout, timerCts.Token);
                    var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (CancellationTokenRegistration registration = ct.CanBeCanceled ? ct.Register(() => cancelled.TrySetResult(true)) : default)
                    {
                        Task winner = await Task.WhenAny(update, timeout, cancelled.Task).ConfigureAwait(false);
                        if (winner == update)
                        {
                            return await update.ConfigureAwait(false);
                        }

                        // The update is abandoned (it may still commit); observe its exception if it faults later.
                        update.ContinueWith(
                            t => { Exception ignored = t.Exception; },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        if (winner == timeout)
                        {
                            throw new TimeoutException("The store update did not complete within " + _writeTimeout.TotalMilliseconds + " ms.");
                        }

                        throw new OperationCanceledException(ct);
                    }
                }
                finally
                {
                    // Every exit path stops the timer, so no delay outlives the call.
                    timerCts.Cancel();
                }
            }
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

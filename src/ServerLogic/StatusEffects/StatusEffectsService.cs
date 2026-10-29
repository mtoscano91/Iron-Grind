using System;
using System.Runtime.ExceptionServices;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Owns the per-entity effect table and validates new effects (status-effects.md CR-SE-2,
    /// CR-SE-3, CR-SE-13, F-SE-1, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6, EC-SE-11). Stories 002-003: validation, atomic
    /// application through <see cref="IBuffModifierLayer"/> with rollback, row storage and read-only accessors.
    /// Story 005 adds per-tick processing and expiry (<see cref="OnServerTick"/>).
    /// Story 004 adds refresh on reapply and in-place replacement (CR-SE-4). Story 006 adds the expiry and
    /// replacement MaxHP / MaxMP clamp (CR-SE-8 steps 2-3) and brackets the tick pass, and a replacement made
    /// outside the tick, in a Character Stats stat batch (CR-SE-17). Story 007 adds death cleanup (CR-SE-10,
    /// EC-SE-2, EC-SE-3): the service subscribes to entity deaths through <see cref="IEntityDeathSource"/>, marks
    /// the slot dead in the handler (no Character Stats call) and cleans the dead entity's rows on the next
    /// <see cref="OnServerTick"/>; it also adds <see cref="ApplyInstantHeal"/> (CR-SE-3, TR-se-005) and
    /// <see cref="OnEntityRespawned"/>. Story 008 adds zone departure cleanup (CR-SE-11, EC-SE-9):
    /// <see cref="ClearEntity"/>, called by the slot pool through <see cref="IZoneSlotReleaseListener"/> before
    /// the entity's slot is released.
    /// Not thread-safe: every call runs on the server tick thread.
    /// </summary>
    public sealed class StatusEffectsService : IDisposable, IZoneSlotReleaseListener
    {
        private const string LOG_PREFIX = "[StatusEffects]";
        private const int ROWS_PER_ENTITY = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
        private const int MAX_SLOTS = StatusEffectsConstants.MAX_ENTITY_SLOTS;
        private const int NO_ROW = -1;
        private const int NO_SLOT = -1;

        private readonly IBuffModifierLayer _modifierLayer;
        private readonly IStatResourceSink _resourceSink;
        private readonly IEntitySlotIndex _slotIndex;
        private readonly ActiveEffect[] _rows;
        private readonly int[] _rowCounts;
        private readonly int[] _tickStartRowCounts;
        private readonly int[] _occupiedSlots;
        private readonly bool[] _dead;
        private readonly bool[] _tickStartDead;
        private readonly IEntityDeathSource _deathSource;
        private readonly CharacterStats.CharacterStats.EntityDiedHandler _deathHandler;
        private bool _disposed;

        // Test-only injection (see InjectApplyEffectDuringTick). Plain fields: no delegate on the tick path.
        private bool _injectionPending;
        private EntityID _injectionTarget;
        private BuffDefinition _injectionDefinition;
        private int _injectionTurnSlot = NO_SLOT;
        private bool _secondInjectionPending;
        private EntityID _secondInjectionTarget;
        private BuffDefinition _secondInjectionDefinition;

        // Test-only ClearEntity injection (see InjectClearEntityDuringTick).
        private bool _clearInjectionPending;
        private EntityID _clearInjectionEntity;
        private int _clearInjectionTurnSlot = NO_SLOT;

        // Tick loop state (see OnServerTick, FreeRowTickAware). _tickSlot is NO_SLOT outside a tick.
        private int _tickSlot = NO_SLOT;
        private int _tickIndex;
        private int _tickEnd;

        // True while Status Effects owns an open stat batch (the tick, or a replacement outside the tick).
        private bool _batchOpen;

        // First exception thrown by a clamp (a death subscriber can throw from ClampHPToMax); kept while the pass
        // continues and surfaced after the batch ends. A single field: no list, no allocation.
        private Exception _clampException;

        /// <summary>
        /// Test-only: result of the last injected <see cref="ApplyEffect"/> call made by <see cref="OnServerTick"/>;
        /// null until an injection fires, and reset to null by each new <see cref="InjectApplyEffectDuringTick(EntityID, in BuffDefinition, EntityID)"/>.
        /// </summary>
        internal ApplyResult? LastInjectedResult;

        /// <summary>Test-only: result of the second injected call (see <see cref="InjectSecondApplyEffectDuringTick"/>); null until it fires.</summary>
        internal ApplyResult? LastSecondInjectedResult;

        /// <summary>Creates the service and allocates every table once (CR-SE-15).</summary>
        /// <param name="modifierLayer">Character Stats buff-modifier seam.</param>
        /// <param name="resourceSink">Character Stats HP/MP seam (regen).</param>
        /// <param name="slotIndex">Entity to slot-index resolver.</param>
        /// <param name="deathSource">Entity-death subscription seam; the constructor subscribes once (ADR-010).</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public StatusEffectsService(IBuffModifierLayer modifierLayer, IStatResourceSink resourceSink,
            IEntitySlotIndex slotIndex, IEntityDeathSource deathSource)
        {
            if (modifierLayer == null) throw new ArgumentNullException(nameof(modifierLayer));
            if (resourceSink == null) throw new ArgumentNullException(nameof(resourceSink));
            if (slotIndex == null) throw new ArgumentNullException(nameof(slotIndex));
            if (deathSource == null) throw new ArgumentNullException(nameof(deathSource));

            _modifierLayer = modifierLayer;
            _resourceSink = resourceSink;
            _slotIndex = slotIndex;
            _rows = new ActiveEffect[MAX_SLOTS * ROWS_PER_ENTITY];
            _rowCounts = new int[MAX_SLOTS];
            _tickStartRowCounts = new int[MAX_SLOTS];
            _occupiedSlots = new int[MAX_SLOTS];
            _dead = new bool[MAX_SLOTS];
            _tickStartDead = new bool[MAX_SLOTS];

            // One delegate instance, kept so Unsubscribe matches by reference.
            _deathSource = deathSource;
            _deathHandler = OnEntityDied;
            _deathSource.Subscribe(_deathHandler);
        }

        /// <summary>
        /// Unsubscribes from the death source (ADR-010). Idempotent: once it has completed, a second call does
        /// nothing. After this a death changes nothing; rows already marked dead are still cleaned by
        /// <see cref="OnServerTick"/>. Do not call it from inside a Character Stats event handler: there
        /// <c>Unsubscribe</c> throws (Editor / development builds; the service stays subscribed and a later call
        /// retries) or silently does nothing (release builds; the handler stays registered but ignores deaths).
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _deathSource.Unsubscribe(_deathHandler);
            _disposed = true;
        }

        /// <summary>The death-source handler: ignores deaths after <see cref="Dispose"/>, else marks the entity dead.</summary>
        private void OnEntityDied(EntityID entity)
        {
            if (!_disposed)
                MarkEntityDead(entity);
        }

        /// <summary>
        /// Advances every active effect by one server tick (status-effects.md CR-SE-6, CR-SE-7, CR-SE-8 steps 1 and 4).
        /// Per row, in order: decrement <see cref="ActiveEffect.TicksRemaining"/>; HP regen; MP regen; on reaching 0,
        /// remove each populated modifier through the layer (index order), reduce occupancy, free the row (later rows
        /// shift down, so buff-bar order is kept) and only then clamp HP / MP. Rows created during the tick are never
        /// processed by it (EC-SE-8): the row counts are snapshotted first. Story 006: the pass is wrapped in a stat
        /// batch (always), so <c>OnStatChanged</c> fires once per (entity, stat) after the pass, and an expired
        /// MaxHP / MaxMP buff clamps current HP / MP right after its row is freed. Story 007 (CR-SE-10, AC-SE-16):
        /// the dead flags are snapshotted too; a slot dead at pass start has every row cleaned (modifiers removed,
        /// occupancy released, row freed, clamps; no decrement, no regen). A slot whose death fires during the pass
        /// is skipped from then on (its remaining rows get no decrement and no regen) and is cleaned by the next
        /// tick. Called from <c>ZoneTickPipeline</c> phase 3 (wiring: Story 009). Allocation-free.
        /// </summary>
        /// <remarks>
        /// Every row is processed exactly once even when a death subscriber (fired by the HP clamp) or an
        /// <c>OnStatChanged</c> subscriber throws: a clamp exception is captured, the pass continues, and the first
        /// one is rethrown after the batch ends. Precedence when several fail: an exception from the pass itself
        /// (for example a layer remove) over a captured clamp exception over an exception from
        /// <c>EndStatBatch</c>; the others are dropped so the original is never masked. Throws
        /// <see cref="InvalidOperationException"/> before processing anything if a stat transaction is already open.
        /// </remarks>
        public void OnServerTick()
        {
            // CR-SE-17: the whole pass is one stat batch, always. If Begin throws (a transaction is already open)
            // nothing is processed. End always runs and Rollback is never used: the modifier writes are not
            // reverted, so discarding their events would hide real changes (TD-085).
            _modifierLayer.BeginStatBatch();
            _batchOpen = true;
            _clampException = null;
            Exception passException = null;
            try
            {
                Array.Copy(_rowCounts, _tickStartRowCounts, MAX_SLOTS);
                Array.Copy(_dead, _tickStartDead, MAX_SLOTS);
                ProcessAllSlots();
            }
            catch (Exception ex)
            {
                passException = ex;
            }

            EndBatchAndSurface(passException);
        }

        /// <summary>
        /// Ends the Status Effects stat batch and rethrows the winning exception: pass exception, else the captured
        /// clamp exception, else End's own. Resets the batch state first so a throw never leaves it open.
        /// </summary>
        private void EndBatchAndSurface(Exception passException)
        {
            _batchOpen = false;
            Exception endException = null;
            try
            {
                _modifierLayer.EndStatBatch();
            }
            catch (Exception ex)
            {
                endException = ex;
            }

            Exception first = passException ?? _clampException ?? endException;
            _clampException = null;
            if (first != null)
                ExceptionDispatchInfo.Capture(first).Throw();
        }

        /// <summary>The slot loop of <see cref="OnServerTick"/>; resets the tick-slot state even when it throws.</summary>
        private void ProcessAllSlots()
        {
            try
            {
                for (int slot = 0; slot < MAX_SLOTS; slot++)
                {
                    if (_tickStartDead[slot])
                    {
                        // Dead at pass start: clean every row (Story 007). No decrement, no regen.
                        if (_rowCounts[slot] > 0)
                            CleanUpDeadSlot(slot);
                        continue;
                    }

                    // Died earlier in this pass (before the loop reached it): skipped, cleaned next tick.
                    int end = _tickStartRowCounts[slot];
                    if (end == 0 || _dead[slot])
                        continue;

                    // Loop state lives in fields so a failed replacement during the tick can free a row through
                    // FreeRowTickAware without skipping or double-processing any row (Story 004, EC-SE-8).
                    // _tickIndex is always the next row to process: every row below it has been processed.
                    _tickSlot = slot;
                    _tickIndex = 0;
                    _tickEnd = end;
                    while (_tickIndex < _tickEnd)
                    {
                        // An expired row is freed inside ProcessRow (the shifted-in row sits on the same index
                        // and has not been processed yet, so the index stays); otherwise move to the next row.
                        if (!ProcessRow(slot, _tickIndex))
                            _tickIndex++;

                        RunPendingInjection(slot);

                        // The entity died while its row was processed (the HP clamp fired the death event): the
                        // remaining rows get no decrement and no regen this pass; the next tick cleans them.
                        if (_dead[slot])
                            break;
                    }
                }
            }
            finally
            {
                _tickSlot = NO_SLOT;
            }
        }

        /// <summary>
        /// Death cleanup of one slot (CR-SE-10, AC-SE-16): runs the expiry procedure on every row, always index 0
        /// so buff-bar order is kept. The loop state is set like a normal slot's so <see cref="FreeRowTickAware"/>
        /// stays correct. Inside the tick's stat batch.
        /// </summary>
        private void CleanUpDeadSlot(int slot)
        {
            _tickSlot = slot;
            _tickIndex = 0;
            _tickEnd = _rowCounts[slot];
            while (_rowCounts[slot] > 0)
                ExpireRow(slot, 0);
        }

        /// <summary>
        /// Runs one row's tick (CR-SE-6): decrement, HP regen, MP regen, and on reaching 0 the modifier removals
        /// and occupancy release (CR-SE-8 step 1). Returns true when the row expired; it is already freed and
        /// <c>_tickEnd</c> already lowered, so the caller keeps the index. The row is freed before the clamps so a
        /// throwing death subscriber cannot leave it behind (see <see cref="ClampAfterMaxChange"/>).
        /// </summary>
        private bool ProcessRow(int slot, int rowIndex)
        {
            ref ActiveEffect e = ref _rows[slot * ROWS_PER_ENTITY + rowIndex];
            // Validation rejects a 0 duration; the guard only keeps the counter from wrapping.
            if (e.TicksRemaining > 0u)
                e.TicksRemaining--;

            if (e.HpRegenPerTick > 0f)
                _resourceSink.ApplyRegen(e.EntityId, e.HpRegenPerTick);
            if (e.MpRegenPerTick > 0f)
                _resourceSink.ApplyManaRegen(e.EntityId, e.MpRegenPerTick);

            if (e.TicksRemaining != 0u)
                return false;

            ExpireRow(slot, rowIndex);
            return true;
        }

        /// <summary>
        /// Expiry procedure (CR-SE-8 steps 1-3), shared by normal expiry and death cleanup (Story 007; Story 008
        /// reuses it). Captures what the clamps need, removes the modifiers and releases occupancy, frees the row,
        /// lowers <c>_tickEnd</c>, then clamps: a clamp exception is captured, never propagated.
        /// </summary>
        private void ExpireRow(int slot, int rowIndex)
        {
            ref ActiveEffect e = ref _rows[slot * ROWS_PER_ENTITY + rowIndex];
            EntityID entity = e.EntityId;
            bool maxHp = RowHasStat(e, StatID.MaxHP);
            bool maxMp = RowHasStat(e, StatID.MaxMP);
            RemoveModifiers(slot, rowIndex);
            RemoveRow(slot, rowIndex);
            _tickEnd--;
            ClampAfterMaxChange(entity, maxHp, maxMp);
        }

        /// <summary>
        /// Removes every populated modifier of the row through the layer (index order) and releases its occupancy
        /// (CR-SE-8 step 1). Shared by expiry and remove-then-reapply. Does not free the row and does not clamp: the
        /// caller calls <see cref="ClampAfterMaxChange"/> (expiry right after; replacement once after the new adds).
        /// Inside the stat batch the removals' events are deferred, so a subscriber cannot throw mid-loop (TD-085).
        /// If a layer remove itself throws, occupancy is not released and the row stays (the exception ends the
        /// pass); an expired row is then retried by the next tick.
        /// </summary>
        private void RemoveModifiers(int slot, int rowIndex)
        {
            ref ActiveEffect e = ref _rows[slot * ROWS_PER_ENTITY + rowIndex];
            int modifierCount = e.ModifierCount;
            for (int m = 0; m < modifierCount; m++)
                _modifierLayer.RemoveBuffModifier(e.EntityId, e.GetModifier(m).StatId, e.BuffId);
            _occupiedSlots[slot] -= modifierCount;
        }

        /// <summary>
        /// CR-SE-8 steps 2-3 (AC-SE-15, EC-SE-4, EC-SE-5): HP first, then MP. Each clamp is called whenever the stat
        /// was involved, even when no clamp is needed (the sink does nothing then). Always called after the row
        /// and occupancy are consistent. The HP clamp can fire <c>OnEntityDied</c> immediately, and a throwing
        /// subscriber must not skip the MP clamp or abort the pass, so each clamp's exception is captured (first one
        /// kept in <c>_clampException</c>) and surfaced by <see cref="EndBatchAndSurface"/>.
        /// </summary>
        private void ClampAfterMaxChange(EntityID entity, bool maxHpInvolved, bool maxMpInvolved)
        {
            if (maxHpInvolved)
                ClampCapturing(entity, true);
            if (maxMpInvolved)
                ClampCapturing(entity, false);
        }

        private void ClampCapturing(EntityID entity, bool hp)
        {
            try
            {
                if (hp)
                    _resourceSink.ClampHPToMax(entity);
                else
                    _resourceSink.ClampMPToMax(entity);
            }
            catch (Exception ex)
            {
                if (_clampException == null)
                    _clampException = ex;
            }
        }

        private static bool RowHasStat(in ActiveEffect row, StatID stat)
        {
            int count = row.ModifierCount;
            for (int i = 0; i < count; i++)
            {
                if (row.GetModifier(i).StatId == stat)
                    return true;
            }

            return false;
        }

        private static bool DefinitionHasStat(in BuffDefinition definition, StatID stat)
        {
            int count = definition.ModifierCount;
            for (int i = 0; i < count; i++)
            {
                if (definition.GetModifier(i).StatId == stat)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Frees a row outside the tick loop's own expiry path and, when the row is in the slot being processed,
        /// adjusts the loop so no remaining row is skipped and no row created this tick is processed:
        /// freed index below the next-to-process index moves both index and end down; inside the range moves the
        /// end down; at or past the end (created this tick) changes nothing. In any other slot, a freed row below
        /// that slot's tick-start snapshot lowers the snapshot, so a slot the tick has not reached yet does not
        /// process past its last row (harmless for slots already processed, and outside a tick).
        /// </summary>
        private void FreeRowTickAware(int slot, int rowIndex)
        {
            RemoveRow(slot, rowIndex);
            if (slot != _tickSlot)
            {
                if (rowIndex < _tickStartRowCounts[slot])
                    _tickStartRowCounts[slot]--;
                return;
            }

            if (rowIndex < _tickIndex)
            {
                _tickIndex--;
                _tickEnd--;
            }
            else if (rowIndex < _tickEnd)
            {
                _tickEnd--;
            }
        }

        /// <summary>
        /// Frees row <paramref name="rowIndex"/> of the slot (CR-SE-8 step 4): later rows shift down one index, so
        /// buff-bar order is kept, and the vacated last row is cleared. Uses the current row count, which includes
        /// rows appended during this tick.
        /// </summary>
        private void RemoveRow(int slot, int rowIndex)
        {
            int rowBase = slot * ROWS_PER_ENTITY;
            int count = _rowCounts[slot];
            Array.Copy(_rows, rowBase + rowIndex + 1, _rows, rowBase + rowIndex, count - 1 - rowIndex);
            _rows[rowBase + count - 1] = default;
            _rowCounts[slot] = count - 1;
        }

        /// <summary>
        /// Test-only seam (the GDD's TestingOnly): during the next <see cref="OnServerTick"/>, after the first row of
        /// <paramref name="target"/>'s own slot has been processed, calls <see cref="ApplyEffect"/> once on
        /// <paramref name="target"/> (caster = target). The result is stored in <see cref="LastInjectedResult"/>.
        /// </summary>
        /// <param name="target">Entity that receives the effect.</param>
        /// <param name="definition">The buff to apply.</param>
        internal void InjectApplyEffectDuringTick(EntityID target, in BuffDefinition definition)
        {
            InjectApplyEffectDuringTick(target, definition, target);
        }

        /// <summary>
        /// Test-only seam: as the two-argument overload, but the call is made after the first row of
        /// <paramref name="duringTurnOf"/>'s slot is processed. If that slot has no rows at the start of a tick, the
        /// injection does not fire and stays pending for a later tick.
        /// </summary>
        /// <param name="target">Entity that receives the effect.</param>
        /// <param name="definition">The buff to apply.</param>
        /// <param name="duringTurnOf">Entity whose first processed row triggers the call.</param>
        internal void InjectApplyEffectDuringTick(EntityID target, in BuffDefinition definition, EntityID duringTurnOf)
        {
            _injectionTarget = target;
            _injectionDefinition = definition;
            _injectionTurnSlot = TryResolveSlot(duringTurnOf, out int turnSlot) ? turnSlot : NO_SLOT;
            _injectionPending = true;
            _secondInjectionPending = false;
            LastInjectedResult = null;
            LastSecondInjectedResult = null;
        }

        /// <summary>
        /// Test-only seam: queues a second <see cref="ApplyEffect"/> (caster = target) that runs immediately after the
        /// first injection fires, in the same tick and turn. Call it after the three-argument
        /// <see cref="InjectApplyEffectDuringTick(EntityID, in BuffDefinition, EntityID)"/>; a new first injection
        /// cancels it. The result is stored in <see cref="LastSecondInjectedResult"/>.
        /// </summary>
        /// <param name="target">Entity that receives the effect.</param>
        /// <param name="definition">The buff to apply.</param>
        internal void InjectSecondApplyEffectDuringTick(EntityID target, in BuffDefinition definition)
        {
            _secondInjectionTarget = target;
            _secondInjectionDefinition = definition;
            _secondInjectionPending = true;
            LastSecondInjectedResult = null;
        }

        /// <summary>
        /// Test-only seam (Story 008): during the next <see cref="OnServerTick"/>, after the first row of
        /// <paramref name="duringTurnOf"/>'s slot has been processed, calls <see cref="ClearEntity"/> once on
        /// <paramref name="entity"/>, as a slot release triggered mid-pass would. If that slot has no rows at the start
        /// of a tick, the injection stays pending for a later tick.
        /// </summary>
        /// <param name="entity">Entity to clear.</param>
        /// <param name="duringTurnOf">Entity whose first processed row triggers the call.</param>
        internal void InjectClearEntityDuringTick(EntityID entity, EntityID duringTurnOf)
        {
            _clearInjectionEntity = entity;
            _clearInjectionTurnSlot = TryResolveSlot(duringTurnOf, out int turnSlot) ? turnSlot : NO_SLOT;
            _clearInjectionPending = true;
        }

        private void RunPendingInjection(int slot)
        {
            if (_clearInjectionPending && slot == _clearInjectionTurnSlot)
            {
                _clearInjectionPending = false;
                ClearEntity(_clearInjectionEntity);
            }

            if (!_injectionPending || slot != _injectionTurnSlot)
                return;

            _injectionPending = false;
            LastInjectedResult = ApplyEffect(_injectionTarget, _injectionTarget, _injectionDefinition);

            if (_secondInjectionPending)
            {
                _secondInjectionPending = false;
                LastSecondInjectedResult = ApplyEffect(_secondInjectionTarget, _secondInjectionTarget, _secondInjectionDefinition);
            }
        }

        /// <summary>
        /// Validates and records a new effect on <paramref name="target"/>. Checks run in this order and the
        /// first failure returns with no change: definition shape (NoOp, duration, stat), target (unknown or
        /// dead), row availability, modifier capacity. Then every modifier is added through the layer; if one is
        /// rejected the ones already added are removed in reverse order and <see cref="ApplyResult.CapacityExceeded"/>
        /// is returned with no row written (CR-SE-3).
        /// </summary>
        /// <remarks>
        /// Re-applying a <see cref="BuffDefinition.BuffId"/> already active on the target (Story 004, CR-SE-4,
        /// F-SE-4) resets <see cref="ActiveEffect.TicksRemaining"/> to the new duration and stores the new caster,
        /// with no Character Stats call, when the modifier values and regen rates are the same. If they differ, the
        /// old modifiers are removed and the new ones added, overwriting the same row (buff-bar position kept);
        /// capacity is pre-checked with the old slots excluded, and only a layer add failure after the removal
        /// loses the old effect (row freed, <see cref="ApplyResult.CapacityExceeded"/>). A refresh skips the row
        /// limit. A plain refresh does not update the duration stored in the Character Stats modifier entries
        /// (audit only). A new-buff apply and a plain refresh from outside the tick are not bracketed: every add and
        /// every rollback removal fires Character Stats' <c>OnStatChanged</c> at once, so subscribers can see a change
        /// that a failed apply then reverts. A replacement (Story 006, CR-SE-17) is different: outside the tick it is
        /// wrapped in its own stat batch, so subscribers get one event per affected stat after the row is consistent
        /// and a subscriber throw surfaces from the batch end (the replacement itself is complete); inside the tick it
        /// uses the tick's batch. After a replacement, current HP / MP are clamped once against the final maximum
        /// when MaxHP / MaxMP was among the old or new modifiers. If a layer add throws during a replacement,
        /// the modifiers added so far are removed, the old effect is lost (row freed), the clamp runs and the
        /// exception is rethrown; the caller sees it instead of <see cref="ApplyResult.CapacityExceeded"/>. For a new
        /// buff, a throwing add removes the modifiers added so far and rethrows. A clamp exception (a death
        /// subscriber) during a replacement is captured after the row and occupancy are consistent and rethrown when
        /// the batch ends; precedence is the replacement's own exception, then the clamp exception, then
        /// <c>EndStatBatch</c>'s. A replacement made outside the tick throws <see cref="InvalidOperationException"/>
        /// (nothing changed) if the caller already has a Character Stats transaction open.
        /// </remarks>
        /// <param name="target">The entity that receives the effect.</param>
        /// <param name="caster">The entity that applied it (stored for buff-bar attribution).</param>
        /// <param name="definition">The authored buff.</param>
        /// <returns>The outcome.</returns>
        public ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition)
        {
            // (1) Definition shape.
            ApplyResult shape = ValidateDefinition(definition);
            if (shape != ApplyResult.Success)
                return shape;

            // (2) Target.
            if (!TryResolveSlot(target, out int slot) || _dead[slot])
                return ApplyResult.InvalidTarget;

            // (3) Rows.
            int existingRow = FindRow(slot, definition.BuffId);
            if (existingRow != NO_ROW)
            {
                // Same BuffID already active: refresh (CR-SE-4). Skips the row limit and the normal capacity check.
                return RefreshOrReplace(slot, existingRow - slot * ROWS_PER_ENTITY, target, caster, definition);
            }

            int rowCount = _rowCounts[slot];
            if (rowCount >= ROWS_PER_ENTITY)
                return ApplyResult.EffectSlotsFull;

            // (4) Modifier capacity (F-SE-2, F-SE-3). CharacterStats caps buff modifiers at 32 per stat;
            // the GDD guard here is 32 per entity, which is stricter. Kept on purpose.
            int modifierCount = definition.ModifierCount;
            if (_occupiedSlots[slot] + modifierCount > StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY)
                return ApplyResult.CapacityExceeded;

            // (5) Character Stats (CR-SE-3, atomic): add every modifier or none. Both failures that can occur
            // after validation (32-per-stat capacity full, or a call made while Character Stats is firing
            // OnStatChanged) map to CapacityExceeded. The re-entrant case only returns in release builds; in the
            // Editor and development builds Character Stats throws InvalidOperationException, which propagates. The row and occupancy are written only after all adds succeed.
            if (!TryAddModifiers(target, definition))
                return ApplyResult.CapacityExceeded;

            _rows[slot * ROWS_PER_ENTITY + rowCount] = new ActiveEffect(definition, target, caster);
            _rowCounts[slot] = rowCount + 1;
            _occupiedSlots[slot] += modifierCount;
            return ApplyResult.Success;
        }

        /// <summary>
        /// Refresh of an active BuffID (CR-SE-4, F-SE-4). Same values: hard reset of ticks and caster, no layer
        /// call. Different values: remove-then-reapply in place.
        /// </summary>
        private ApplyResult RefreshOrReplace(int slot, int rowIndex, EntityID target, EntityID caster, in BuffDefinition definition)
        {
            int rowAbs = slot * ROWS_PER_ENTITY + rowIndex;
            ref ActiveEffect existing = ref _rows[rowAbs];

            if (HasSameValues(existing, definition))
            {
                existing.TicksRemaining = definition.DurationTicks;
                existing.CasterEntityId = caster;
                return ApplyResult.Success;
            }

            // Capacity pre-check with the old slots excluded, before any layer call (nothing changes on failure).
            int oldCount = existing.ModifierCount;
            int newCount = definition.ModifierCount;
            if (_occupiedSlots[slot] - oldCount + newCount > StatusEffectsConstants.MODIFIER_CAPACITY_PER_ENTITY)
                return ApplyResult.CapacityExceeded;

            return ReplaceInPlace(slot, rowIndex, target, caster, definition);
        }

        /// <summary>
        /// Remove-then-reapply in place (CR-SE-4 exception, CR-SE-8 steps 1-3, CR-SE-3): removes the old modifiers,
        /// adds the new ones and overwrites the same row. On a layer rejection or a throw the old effect
        /// is lost and the row is freed tick-aware. Capacity was pre-checked by the caller.
        /// Story 006: outside the tick the whole replacement is its own stat batch (Begin / End in a finally), so
        /// subscribers never see the transient lower value; inside the tick it uses the tick's batch (no nested Begin).
        /// If Begin throws, nothing has changed. If both the replacement and End throw, the replacement's exception wins.
        /// </summary>
        private ApplyResult ReplaceInPlace(int slot, int rowIndex, EntityID target, EntityID caster, in BuffDefinition definition)
        {
            if (_batchOpen)
                return ReplaceCore(slot, rowIndex, target, caster, definition);

            _modifierLayer.BeginStatBatch();
            _batchOpen = true;
            _clampException = null;
            ApplyResult result = ApplyResult.Success;
            Exception passException = null;
            try
            {
                result = ReplaceCore(slot, rowIndex, target, caster, definition);
            }
            catch (Exception ex)
            {
                passException = ex;
            }

            EndBatchAndSurface(passException);
            return result;
        }

        /// <summary>
        /// The remove, add, write and clamp steps of a replacement. The clamp runs once at the end against the final
        /// maximum (CR-SE-4 exception), if MaxHP / MaxMP was among the old or the new modifiers; on the add-failure
        /// and throw paths it runs after the rollback (and on a throw, before the rethrow).
        /// </summary>
        private ApplyResult ReplaceCore(int slot, int rowIndex, EntityID target, EntityID caster, in BuffDefinition definition)
        {
            // Capture the old row's flags before the row is overwritten.
            ref ActiveEffect old = ref _rows[slot * ROWS_PER_ENTITY + rowIndex];
            bool maxHp = RowHasStat(old, StatID.MaxHP) || DefinitionHasStat(definition, StatID.MaxHP);
            bool maxMp = RowHasStat(old, StatID.MaxMP) || DefinitionHasStat(definition, StatID.MaxMP);

            RemoveModifiers(slot, rowIndex); // also releases the old occupancy

            bool added;
            try
            {
                added = TryAddModifiers(target, definition);
            }
            catch
            {
                // TryAddModifiers rolled back its own adds; the old modifiers are already gone, so the row
                // must not stay behind describing modifiers that are not applied.
                FreeRowTickAware(slot, rowIndex);
                ClampAfterMaxChange(target, maxHp, maxMp);
                throw;
            }

            if (!added)
            {
                // The layer rejected an add after the old modifiers were removed. TryAddModifiers rolled back the
                // new adds, so the old effect is lost: free its row (later rows shift down) and report it.
                FreeRowTickAware(slot, rowIndex);
                ClampAfterMaxChange(target, maxHp, maxMp);
                return ApplyResult.CapacityExceeded;
            }

            _rows[slot * ROWS_PER_ENTITY + rowIndex] = new ActiveEffect(definition, target, caster);
            _occupiedSlots[slot] += definition.ModifierCount;
            ClampAfterMaxChange(target, maxHp, maxMp);
            return ApplyResult.Success;
        }

        /// <summary>
        /// "Same values" (F-SE-4): same modifier count, and per index the same stat, flat and percentage bonus
        /// (exact float equality, so NaN never matches), and the same regen rates. Duration and caster are ignored.
        /// </summary>
        private static bool HasSameValues(in ActiveEffect row, in BuffDefinition definition)
        {
            int count = definition.ModifierCount;
            if (row.ModifierCount != count
                || row.HpRegenPerTick != definition.HpRegenPerTick
                || row.MpRegenPerTick != definition.MpRegenPerTick)
                return false;

            for (int i = 0; i < count; i++)
            {
                StatModifier a = row.GetModifier(i);
                StatModifier b = definition.GetModifier(i);
                if (a.StatId != b.StatId || a.FlatBonus != b.FlatBonus || a.PctBonus != b.PctBonus)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Adds every modifier of <paramref name="definition"/> through the layer. On the first rejection, removes
        /// (in reverse order) each modifier this attempt added and returns false.
        /// </summary>
        private bool TryAddModifiers(EntityID target, in BuffDefinition definition)
        {
            int modifierCount = definition.ModifierCount;
            int added = 0;
            try
            {
                for (; added < modifierCount; added++)
                {
                    StatModifier modifier = definition.GetModifier(added);
                    // DurationTicks is at most MAX_DURATION_TICKS (6000) after validation, so the int cast is safe.
                    // Character Stats keeps the duration for audit only; Status Effects owns expiry.
                    var entry = new BuffModifierEntry(
                        modifier.FlatBonus, modifier.PctBonus, (int)definition.DurationTicks, definition.BuffId);

                    if (!_modifierLayer.TryAddBuffModifier(target, modifier.StatId, entry))
                    {
                        RemoveAdded(target, definition, added);
                        return false;
                    }
                }
            }
            catch
            {
                // An OnStatChanged subscriber threw. Character Stats stores the entry before firing, so the
                // modifier being added may be in place too; removal is idempotent, so include it.
                RemoveAdded(target, definition, added + 1);
                throw;
            }

            return true;
        }

        /// <summary>Removes, in reverse order, the first <paramref name="count"/> modifiers of the definition.</summary>
        private void RemoveAdded(EntityID target, in BuffDefinition definition, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _modifierLayer.RemoveBuffModifier(target, definition.GetModifier(i).StatId, definition.BuffId);
        }

        /// <summary>
        /// Records that the entity is dead so later applications are rejected (EC-SE-2). The flag is the
        /// PendingRemoval marker for all of the entity's rows (CR-SE-10). Called by the death-source handler
        /// (<see cref="OnEntityDied"/>): it only sets the flag and never calls Character Stats (no stat write inside
        /// its event handlers). Unknown entities are ignored.
        /// </summary>
        /// <param name="entity">The entity that died.</param>
        internal void MarkEntityDead(EntityID entity)
        {
            if (TryResolveSlot(entity, out int slot))
                _dead[slot] = true;
        }

        /// <summary>
        /// Zone departure cleanup (status-effects.md CR-SE-11, EC-SE-9; AC-SE-21, AC-SE-22, AC-SE-23): runs the
        /// expiry procedure on every row of the entity, Active or PendingRemoval (remove modifiers, release
        /// occupancy, free the row, clamps), then leaves rows 0, occupancy 0 and the dead flag cleared. Rows of
        /// other entities that name this one as caster are untouched (CR-SE-13). Unknown entity or no rows: no layer
        /// call (the dead flag is still cleared); a second call does nothing.
        /// </summary>
        /// <remarks>
        /// Rows are freed with <see cref="FreeRowTickAware"/>, so a call during the tick pass neither skips nor
        /// double-processes rows. The removals run inside the tick's stat batch when one is open, else inside their
        /// own batch; if that Begin throws (another Character Stats transaction is open) the error is logged and
        /// the removals run unbatched. Never throws: any exception from the layer, a clamp or End is caught and
        /// logged, and the entity's rows, occupancy and dead flag are reset anyway (modifiers whose removal threw
        /// may stay in Character Stats; the entity is leaving). Inside the tick a clamp exception is left for
        /// <see cref="OnServerTick"/> to rethrow after its batch ends, like any other clamp in the pass.
        /// Allocation-free except on the error path.
        /// </remarks>
        /// <param name="entity">The entity leaving the zone.</param>
        public void ClearEntity(EntityID entity)
        {
            if (!TryResolveSlot(entity, out int slot))
                return;

            if (_rowCounts[slot] == 0)
            {
                _dead[slot] = false;
                return;
            }

            bool insideBatch = _batchOpen;
            bool ownsBatch = !insideBatch && TryBeginOwnBatch();

            try
            {
                while (_rowCounts[slot] > 0)
                {
                    ref ActiveEffect e = ref _rows[slot * ROWS_PER_ENTITY];
                    EntityID rowEntity = e.EntityId;
                    bool maxHp = RowHasStat(e, StatID.MaxHP);
                    bool maxMp = RowHasStat(e, StatID.MaxMP);
                    RemoveModifiers(slot, 0);
                    FreeRowTickAware(slot, 0);
                    ClampAfterMaxChange(rowEntity, maxHp, maxMp);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(LOG_PREFIX + " ClearEntity: a layer call threw; resetting the slot: " + ex);
                HardResetSlot(slot);
            }

            if (ownsBatch)
                EndOwnBatch();

            if (!insideBatch && _clampException != null)
            {
                UnityEngine.Debug.LogError(LOG_PREFIX + " ClearEntity: a clamp threw: " + _clampException);
                _clampException = null;
            }

            _dead[slot] = false;
        }

        /// <summary>
        /// Opens <see cref="ClearEntity"/>'s own stat batch (outside the tick). Returns false after logging when
        /// Begin throws (another Character Stats transaction is open); the removals then run unbatched.
        /// </summary>
        private bool TryBeginOwnBatch()
        {
            _clampException = null;
            try
            {
                _modifierLayer.BeginStatBatch();
                _batchOpen = true;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(LOG_PREFIX + " ClearEntity: BeginStatBatch failed; removing modifiers unbatched: " + ex);
                return false;
            }
        }

        /// <summary>Closes the batch opened by <see cref="TryBeginOwnBatch"/>; an End exception is logged, not thrown.</summary>
        private void EndOwnBatch()
        {
            _batchOpen = false;
            try
            {
                _modifierLayer.EndStatBatch();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(LOG_PREFIX + " ClearEntity: EndStatBatch failed: " + ex);
            }
        }

        /// <summary>
        /// <see cref="IZoneSlotReleaseListener"/>: called by the slot pool before the entity's slot is released
        /// (CR-SE-11, OQ-SE-2). Calls <see cref="ClearEntity"/>, which never throws.
        /// </summary>
        /// <param name="id">The entity whose slot is about to be released.</param>
        void IZoneSlotReleaseListener.BeforeRelease(EntityID id)
        {
            ClearEntity(id);
        }

        /// <summary>
        /// Error-path reset of one slot: clears the remaining rows, occupancy, and keeps a running tick pass
        /// consistent (the slot being processed ends its loop; a slot not yet reached processes no rows). Does not
        /// touch the dead flag: <see cref="ClearEntity"/> clears it afterwards.
        /// </summary>
        private void HardResetSlot(int slot)
        {
            Array.Clear(_rows, slot * ROWS_PER_ENTITY, ROWS_PER_ENTITY);
            _rowCounts[slot] = 0;
            _occupiedSlots[slot] = 0;
            _tickStartRowCounts[slot] = 0;
            if (slot == _tickSlot)
            {
                _tickIndex = 0;
                _tickEnd = 0;
            }
        }

        /// <summary>True when the entity's dead (PendingRemoval) flag is set; false for an unknown entity.</summary>
        /// <param name="entity">The entity.</param>
        internal bool IsEntityDead(EntityID entity)
        {
            return TryResolveSlot(entity, out int slot) && _dead[slot];
        }

        /// <summary>
        /// Clears the entity's dead flag so it can receive effects and heals again (CR-SE-10). Death &amp; Respawn
        /// calls this at T-3 (CR-DR-11a); <see cref="ClearEntity"/> also clears the flag on slot release. On an alive or unknown
        /// entity this does nothing and never throws. Cleanup is keyed on the flag at the start of a tick, so a respawn
        /// before the first tick after the death would leave the old rows in place; Death &amp; Respawn's T-3 comes many
        /// ticks after T-1, so this does not arise.
        /// </summary>
        /// <param name="entity">The respawned entity.</param>
        public void OnEntityRespawned(EntityID entity)
        {
            if (TryResolveSlot(entity, out int slot))
                _dead[slot] = false;
        }

        /// <summary>
        /// Delivers a one-shot HP restoration through <see cref="IStatResourceSink.ApplyRegen"/> (CR-SE-3,
        /// TR-se-005). A path distinct from duration-tracked buffs: no row is created and occupancy is unchanged.
        /// Checks in order: amount (<c>!(amount &gt; 0)</c>, which also rejects NaN) then target (unknown or dead).
        /// </summary>
        /// <param name="entity">The entity to heal.</param>
        /// <param name="amount">HP to restore; must be greater than 0.</param>
        /// <returns><see cref="ApplyResult.Success"/>, <see cref="ApplyResult.InvalidAmount"/> or <see cref="ApplyResult.InvalidTarget"/>.</returns>
        public ApplyResult ApplyInstantHeal(EntityID entity, float amount)
        {
            if (!(amount > 0f))
                return ApplyResult.InvalidAmount;

            if (!TryResolveSlot(entity, out int slot) || _dead[slot])
                return ApplyResult.InvalidTarget;

            _resourceSink.ApplyRegen(entity, amount);
            return ApplyResult.Success;
        }

        /// <summary>Returns the entity's occupied modifier slots (F-SE-3: sum of row ModifierCount); 0 if unknown.</summary>
        /// <param name="entity">The entity.</param>
        internal int GetOccupiedSlots(EntityID entity)
        {
            return TryResolveSlot(entity, out int slot) ? _occupiedSlots[slot] : 0;
        }

        /// <summary>Returns the number of rows in use for the entity; 0 if unknown.</summary>
        /// <param name="entity">The entity.</param>
        internal int GetActiveEffectCount(EntityID entity)
        {
            return TryResolveSlot(entity, out int slot) ? _rowCounts[slot] : 0;
        }

        /// <summary>
        /// Read-only view of the entity's active rows (status-effects.md CR-SE-13, AC-SE-31). Allocation-free and
        /// makes no Character Stats call. The span is invalidated by the next mutation of the table, including
        /// in-place row changes from refresh (Story 004) and from <see cref="OnServerTick"/> (decrement, expiry shift); do not hold it across calls.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The entity's rows; empty for an unknown entity.</returns>
        public ReadOnlySpan<ActiveEffect> GetActiveEffects(EntityID entity)
        {
            if (!TryResolveSlot(entity, out int slot))
                return ReadOnlySpan<ActiveEffect>.Empty;
            return new ReadOnlySpan<ActiveEffect>(_rows, slot * ROWS_PER_ENTITY, _rowCounts[slot]);
        }

        /// <summary>Finds the entity's row for <paramref name="buffId"/>; makes no Character Stats call.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="buffId">The buff to find.</param>
        /// <param name="effect">A copy of the row when found.</param>
        /// <returns>True when the row exists.</returns>
        public bool TryGetActiveEffect(EntityID entity, BuffID buffId, out ActiveEffect effect)
        {
            if (TryResolveSlot(entity, out int slot))
            {
                int row = FindRow(slot, buffId);
                if (row != NO_ROW)
                {
                    effect = _rows[row];
                    return true;
                }
            }

            effect = default;
            return false;
        }

        /// <summary>Check (1): NoOp, then duration, then prohibited or undefined stats, then repeated stats; Success when the shape is valid.</summary>
        private static ApplyResult ValidateDefinition(in BuffDefinition definition)
        {
            int modifierCount = definition.ModifierCount;
            if (modifierCount == 0 && definition.HpRegenPerTick == 0f && definition.MpRegenPerTick == 0f)
                return ApplyResult.NoOpDefinition;

            if (!StatusEffectsMath.IsWithinAuthoringCeiling(definition.DurationTicks))
                return ApplyResult.InvalidDuration;

            for (int i = 0; i < modifierCount; i++)
            {
                if (IsProhibitedStat(definition.GetModifier(i).StatId))
                    return ApplyResult.InvalidStat;
            }

            // CR-SE-2 / EC-SE-11: one modifier per stat. At most 8 slots, so the nested scan allocates nothing.
            for (int i = 0; i < modifierCount; i++)
            {
                StatID stat = definition.GetModifier(i).StatId;
                for (int j = i + 1; j < modifierCount; j++)
                {
                    if (definition.GetModifier(j).StatId == stat)
                        return ApplyResult.InvalidStat;
                }
            }

            return ApplyResult.Success;
        }

        /// <summary>Resolves the entity's slot; false when unknown or the resolver returns an index out of range.</summary>
        private bool TryResolveSlot(EntityID entity, out int slot)
        {
            return _slotIndex.TryGetSlotIndex(entity, out slot)
                && slot >= 0 && slot < StatusEffectsConstants.MAX_ENTITY_SLOTS;
        }

        /// <summary>Returns the index into <c>_rows</c> of the slot's row for <paramref name="buffId"/>, or <see cref="NO_ROW"/>.</summary>
        private int FindRow(int slot, BuffID buffId)
        {
            int rowBase = slot * ROWS_PER_ENTITY;
            int rowEnd = rowBase + _rowCounts[slot];
            for (int row = rowBase; row < rowEnd; row++)
            {
                if (_rows[row].BuffId == buffId)
                    return row;
            }

            return NO_ROW;
        }

        /// <summary>
        /// True for the stats buffs may not change (CR-SE-2) and for any value outside the defined
        /// <see cref="StatID"/> range: Character Stats indexes its arrays by stat, so an undefined value would
        /// throw mid-application and skip the rollback.
        /// </summary>
        private static bool IsProhibitedStat(StatID stat)
        {
            return (int)stat >= CharacterStats.CharacterStats.StatSlotCount
                || stat == StatID.Level
                || stat == StatID.Experience
                || stat == StatID.CurrentHP
                || stat == StatID.CurrentMP;
        }
    }
}

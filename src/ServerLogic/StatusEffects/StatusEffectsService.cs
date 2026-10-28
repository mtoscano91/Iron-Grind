using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Owns the per-entity effect table and validates new effects (status-effects.md CR-SE-2,
    /// CR-SE-3, CR-SE-13, F-SE-1, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6, EC-SE-11). Stories 002-003: validation, atomic
    /// application through <see cref="IBuffModifierLayer"/> with rollback, row storage and read-only accessors.
    /// Story 005 adds per-tick processing and expiry (<see cref="OnServerTick"/>).
    /// Refresh and death wiring come in later stories.
    /// Not thread-safe: every call runs on the server tick thread.
    /// </summary>
    public sealed class StatusEffectsService
    {
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

        // Test-only injection (see InjectApplyEffectDuringTick). Plain fields: no delegate on the tick path.
        private bool _injectionPending;
        private EntityID _injectionTarget;
        private BuffDefinition _injectionDefinition;
        private int _injectionTurnSlot = NO_SLOT;

        /// <summary>
        /// Test-only: result of the last injected <see cref="ApplyEffect"/> call made by <see cref="OnServerTick"/>;
        /// null until an injection fires, and reset to null by each new <see cref="InjectApplyEffectDuringTick(EntityID, in BuffDefinition, EntityID)"/>.
        /// </summary>
        internal ApplyResult? LastInjectedResult;

        /// <summary>Creates the service and allocates every table once (CR-SE-15).</summary>
        /// <param name="modifierLayer">Character Stats buff-modifier seam.</param>
        /// <param name="resourceSink">Character Stats HP/MP seam (regen).</param>
        /// <param name="slotIndex">Entity to slot-index resolver.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public StatusEffectsService(IBuffModifierLayer modifierLayer, IStatResourceSink resourceSink, IEntitySlotIndex slotIndex)
        {
            if (modifierLayer == null) throw new ArgumentNullException(nameof(modifierLayer));
            if (resourceSink == null) throw new ArgumentNullException(nameof(resourceSink));
            if (slotIndex == null) throw new ArgumentNullException(nameof(slotIndex));

            _modifierLayer = modifierLayer;
            _resourceSink = resourceSink;
            _slotIndex = slotIndex;
            _rows = new ActiveEffect[MAX_SLOTS * ROWS_PER_ENTITY];
            _rowCounts = new int[MAX_SLOTS];
            _tickStartRowCounts = new int[MAX_SLOTS];
            _occupiedSlots = new int[MAX_SLOTS];
            _dead = new bool[MAX_SLOTS];
        }

        /// <summary>
        /// Advances every active effect by one server tick (status-effects.md CR-SE-6, CR-SE-7, CR-SE-8 steps 1 and 4).
        /// Per row, in order: decrement <see cref="ActiveEffect.TicksRemaining"/>; HP regen; MP regen; on reaching 0,
        /// remove each populated modifier through the layer (index order), free the row (later rows shift down, so
        /// buff-bar order is kept) and reduce occupancy. Rows created during the tick are never processed by it
        /// (EC-SE-8): the row counts are snapshotted first. Slots marked dead are skipped entirely until Story 007.
        /// Called from <c>ZoneTickPipeline</c> phase 3 (wiring: Story 009); <c>OnStatChanged</c> is not batched until Story 006.
        /// Allocation-free.
        /// </summary>
        public void OnServerTick()
        {
            Array.Copy(_rowCounts, _tickStartRowCounts, MAX_SLOTS);

            for (int slot = 0; slot < MAX_SLOTS; slot++)
            {
                int end = _tickStartRowCounts[slot];
                if (end == 0 || _dead[slot])
                    continue;

                int i = 0;
                while (i < end)
                {
                    if (ProcessRow(slot, i))
                    {
                        RemoveRow(slot, i);
                        end--;
                        // Stay on index i: the shifted-in row has not been processed yet.
                    }
                    else
                    {
                        i++;
                    }

                    RunPendingInjection(slot);
                }
            }
        }

        /// <summary>
        /// Runs one row's tick (CR-SE-6): decrement, HP regen, MP regen, and on reaching 0 the modifier removals
        /// and occupancy release (CR-SE-8 step 1). Returns true when the row expired; the caller frees it.
        /// </summary>
        private bool ProcessRow(int slot, int rowIndex)
        {
            ref ActiveEffect e = ref _rows[slot * ROWS_PER_ENTITY + rowIndex];
            // Validation rejects a 0 duration, so a row only starts a tick at 0 if a subscriber threw during its
            // expiry on the previous tick; the guard keeps it from wrapping and the removals below are retried.
            if (e.TicksRemaining > 0u)
                e.TicksRemaining--;

            if (e.HpRegenPerTick > 0f)
                _resourceSink.ApplyRegen(e.EntityId, e.HpRegenPerTick);
            if (e.MpRegenPerTick > 0f)
                _resourceSink.ApplyManaRegen(e.EntityId, e.MpRegenPerTick);

            if (e.TicksRemaining != 0u)
                return false;

            int modifierCount = e.ModifierCount;
            for (int m = 0; m < modifierCount; m++)
                _modifierLayer.RemoveBuffModifier(e.EntityId, e.GetModifier(m).StatId, e.BuffId);
            _occupiedSlots[slot] -= modifierCount;
            return true;
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
            LastInjectedResult = null;
        }

        private void RunPendingInjection(int slot)
        {
            if (!_injectionPending || slot != _injectionTurnSlot)
                return;

            _injectionPending = false;
            LastInjectedResult = ApplyEffect(_injectionTarget, _injectionTarget, _injectionDefinition);
        }

        /// <summary>
        /// Validates and records a new effect on <paramref name="target"/>. Checks run in this order and the
        /// first failure returns with no change: definition shape (NoOp, duration, stat), target (unknown or
        /// dead), row availability, modifier capacity. Then every modifier is added through the layer; if one is
        /// rejected the ones already added are removed in reverse order and <see cref="ApplyResult.CapacityExceeded"/>
        /// is returned with no row written (CR-SE-3).
        /// </summary>
        /// <remarks>
        /// Re-applying a <see cref="BuffDefinition.BuffId"/> already active on the target returns
        /// <see cref="ApplyResult.Success"/> with no change (refresh is Story 004). Every add and every rollback
        /// removal fires Character Stats' <c>OnStatChanged</c>, so subscribers can see a change that a failed apply
        /// then reverts (batching is Story 006). If a subscriber throws during an add, the modifiers added so far
        /// (including the one whose event threw) are removed and the exception is rethrown.
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
            if (FindRow(slot, definition.BuffId) != NO_ROW)
            {
                // Same BuffID already active: refresh semantics (reset ticks, replace on changed
                // modifier values) are Story 004. Until then this is a Success with no change.
                return ApplyResult.Success;
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
        /// Records that the entity is dead so later applications are rejected (EC-SE-2). Story 007 calls this
        /// from the <c>OnEntityDied</c> handler. Unknown entities are ignored.
        /// </summary>
        /// <param name="entity">The entity that died.</param>
        internal void MarkEntityDead(EntityID entity)
        {
            if (TryResolveSlot(entity, out int slot))
                _dead[slot] = true;
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

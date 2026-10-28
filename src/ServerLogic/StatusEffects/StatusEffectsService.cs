using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Owns the per-entity effect table and validates new effects (status-effects.md CR-SE-2,
    /// CR-SE-3, CR-SE-13, F-SE-1, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6, EC-SE-11). Stories 002-003: validation, atomic
    /// application through <see cref="IBuffModifierLayer"/> with rollback, row storage and read-only accessors.
    /// Refresh, ticking, expiry and death wiring come in later stories.
    /// Not thread-safe: every call runs on the server tick thread.
    /// </summary>
    public sealed class StatusEffectsService
    {
        private const int ROWS_PER_ENTITY = StatusEffectsConstants.MAX_ACTIVE_BUFFS_PER_ENTITY;
        private const int NO_ROW = -1;

        private readonly IBuffModifierLayer _modifierLayer;
        private readonly IEntitySlotIndex _slotIndex;
        private readonly ActiveEffect[] _rows;
        private readonly int[] _rowCounts;
        private readonly int[] _occupiedSlots;
        private readonly bool[] _dead;

        /// <summary>Creates the service and allocates every table once (CR-SE-15).</summary>
        /// <param name="modifierLayer">Character Stats seam.</param>
        /// <param name="slotIndex">Entity to slot-index resolver.</param>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public StatusEffectsService(IBuffModifierLayer modifierLayer, IEntitySlotIndex slotIndex)
        {
            if (modifierLayer == null) throw new ArgumentNullException(nameof(modifierLayer));
            if (slotIndex == null) throw new ArgumentNullException(nameof(slotIndex));

            _modifierLayer = modifierLayer;
            _slotIndex = slotIndex;
            _rows = new ActiveEffect[StatusEffectsConstants.MAX_ENTITY_SLOTS * ROWS_PER_ENTITY];
            _rowCounts = new int[StatusEffectsConstants.MAX_ENTITY_SLOTS];
            _occupiedSlots = new int[StatusEffectsConstants.MAX_ENTITY_SLOTS];
            _dead = new bool[StatusEffectsConstants.MAX_ENTITY_SLOTS];
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
        /// in-place row changes from refresh and expiry (Stories 004, 005); do not hold it across calls.
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

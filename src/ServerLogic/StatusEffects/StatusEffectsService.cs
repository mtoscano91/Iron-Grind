using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Owns the per-entity effect table and validates new effects (status-effects.md CR-SE-2,
    /// CR-SE-3, F-SE-1, F-SE-2, F-SE-3, EC-SE-2, EC-SE-6). Story 002: validation and row storage only.
    /// Calling Character Stats, rollback, refresh, ticking and death wiring come in later stories.
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
        /// <param name="modifierLayer">Character Stats seam (not called in Story 002).</param>
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
        /// dead), row availability, modifier capacity.
        /// </summary>
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

            // Story 003 adds the IBuffModifierLayer.TryAddBuffModifier calls and rollback here, before the row
            // is written. Story 002 never calls _modifierLayer.
            _rows[slot * ROWS_PER_ENTITY + rowCount] = new ActiveEffect(definition, target, caster);
            _rowCounts[slot] = rowCount + 1;
            _occupiedSlots[slot] += modifierCount;
            return ApplyResult.Success;
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

        /// <summary>Finds the entity's row for <paramref name="buffId"/>.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="buffId">The buff to find.</param>
        /// <param name="effect">A copy of the row when found.</param>
        /// <returns>True when the row exists.</returns>
        internal bool TryGetActiveEffect(EntityID entity, BuffID buffId, out ActiveEffect effect)
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

        /// <summary>Check (1): NoOp, then duration, then prohibited stats; Success when the shape is valid.</summary>
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

        private static bool IsProhibitedStat(StatID stat)
        {
            return stat == StatID.Level
                || stat == StatID.Experience
                || stat == StatID.CurrentHP
                || stat == StatID.CurrentMP;
        }
    }
}

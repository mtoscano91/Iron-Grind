using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Production <see cref="IBuffModifierLayer"/> over <see cref="CharacterStats.CharacterStats"/>
    /// (status-effects.md CR-SE-3, Interactions).
    /// </summary>
    public sealed class CharacterStatsBuffModifierLayer : IBuffModifierLayer
    {
        private readonly CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container that owns the buff stack.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsBuffModifierLayer(CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <summary>
        /// Adds the entry and reports whether the stat's buff count grew. An overwrite of an existing entry with
        /// the same BuffID reports false; that cannot happen from <see cref="StatusEffectsService"/> because
        /// CR-SE-2 rejects repeated stats and a re-apply of an active BuffID never reaches the add path. That also
        /// relies on every path that frees a row (expiry, death, zone departure — Stories 005, 007, 008) removing the
        /// row's modifiers from Character Stats; a leftover entry would make a later apply of that BuffID fail.
        /// </summary>
        /// <param name="entityId">The entity that receives the modifier.</param>
        /// <param name="statId">The stat changed.</param>
        /// <param name="entry">The modifier entry.</param>
        /// <returns>True when the modifier was added.</returns>
        public bool TryAddBuffModifier(EntityID entityId, StatID statId, BuffModifierEntry entry)
        {
            // AddBuffModifier is void and logs-and-returns on a write-locked stat, the 32-per-stat capacity, or a
            // re-entrant call during OnStatChanged. Comparing the buff count before and after detects those
            // without changing Character Stats' API.
            int before = ReadBuffCount(entityId, statId);
            _stats.AddBuffModifier(entityId, statId, entry);
            return ReadBuffCount(entityId, statId) > before;
        }

        /// <inheritdoc/>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            _stats.RemoveBuffModifier(entityId, statId, buffId);
        }

        /// <inheritdoc/>
        public void BeginStatBatch()
        {
            _stats.BeginStatTransaction();
        }

        /// <inheritdoc/>
        public void EndStatBatch()
        {
            _stats.EndStatTransaction();
        }

        private int ReadBuffCount(EntityID entityId, StatID statId)
        {
            return _stats.EntityBuffCount.TryGetValue(entityId, out int[] counts) ? counts[(int)statId] : 0;
        }
    }
}

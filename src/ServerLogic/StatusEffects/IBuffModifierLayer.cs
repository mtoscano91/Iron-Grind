using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Seam between Status Effects and the Character Stats buff-modifier stack.
    /// The production adapter over CharacterStats is <see cref="CharacterStatsBuffModifierLayer"/>;
    /// <see cref="StatusEffectsService"/> calls it and rolls back on partial failure (Story 003).
    /// </summary>
    public interface IBuffModifierLayer
    {
        /// <summary>Adds a buff modifier to one stat of one entity.</summary>
        /// <param name="entityId">The entity that receives the modifier.</param>
        /// <param name="statId">The stat changed.</param>
        /// <param name="entry">The modifier entry.</param>
        /// <returns>True when the modifier was added; false when it was rejected.</returns>
        bool TryAddBuffModifier(EntityID entityId, StatID statId, BuffModifierEntry entry);

        /// <summary>Removes the modifier owned by <paramref name="buffId"/> from one stat of one entity.</summary>
        /// <param name="entityId">The entity that carries the modifier.</param>
        /// <param name="statId">The stat changed.</param>
        /// <param name="buffId">The buff that owns the modifier.</param>
        void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId);
    }
}

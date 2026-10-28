using System;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Production <see cref="IEntityDeathSource"/> over <see cref="CharacterStats.CharacterStats"/>
    /// (status-effects.md CR-SE-10, ADR-010).
    /// </summary>
    public sealed class CharacterStatsDeathSource : IEntityDeathSource
    {
        private readonly CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container that raises entity deaths.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsDeathSource(CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public void Subscribe(CharacterStats.CharacterStats.EntityDiedHandler handler)
        {
            _stats.Subscribe(handler);
        }

        /// <inheritdoc/>
        public void Unsubscribe(CharacterStats.CharacterStats.EntityDiedHandler handler)
        {
            _stats.Unsubscribe(handler);
        }
    }
}

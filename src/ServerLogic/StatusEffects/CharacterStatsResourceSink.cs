using System;
using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Production <see cref="IStatResourceSink"/> over <see cref="CharacterStats.CharacterStats"/>
    /// (status-effects.md CR-SE-6, Interactions).
    /// </summary>
    public sealed class CharacterStatsResourceSink : IStatResourceSink
    {
        private readonly CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container that owns the HP and MP pools.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsResourceSink(CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public void ApplyRegen(EntityID entityId, float amount)
        {
            _stats.ApplyRegen(entityId, amount);
        }

        /// <inheritdoc/>
        public void ApplyManaRegen(EntityID entityId, float amount)
        {
            _stats.ApplyManaRegen(entityId, amount);
        }

        /// <inheritdoc/>
        public void ClampHPToMax(EntityID entityId)
        {
            // OQ-SE-1: ApplyDamage(0) clamps CurrentHP to the effective MaxHP and fires nothing unless HP reaches 0.
            _stats.ApplyDamage(entityId, 0f);
        }

        /// <inheritdoc/>
        public void ClampMPToMax(EntityID entityId)
        {
            _stats.ClampCurrentMPToMax(entityId);
        }
    }
}

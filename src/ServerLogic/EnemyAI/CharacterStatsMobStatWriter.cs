using System;
using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// <see cref="IMobStatWriter"/> adapter over the zone's <see cref="IronGrind.CharacterStats.CharacterStats"/>
    /// container (Enemy AI Story 007). Forwards each call unchanged.
    /// </summary>
    public sealed class CharacterStatsMobStatWriter : IMobStatWriter
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Initializes the adapter.</summary>
        /// <param name="stats">The stats container to write to. Must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsMobStatWriter(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public void SetBaseStat(EntityID entityId, StatID statId, int value)
        {
            _stats.SetBaseStat(entityId, statId, value);
        }

        /// <inheritdoc/>
        public void SetCurrentHP(EntityID entityId, float value)
        {
            _stats.SetCurrentHP(entityId, value);
        }
    }
}

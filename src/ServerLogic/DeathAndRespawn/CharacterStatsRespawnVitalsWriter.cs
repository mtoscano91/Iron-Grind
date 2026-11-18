using System;
using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// <see cref="IRespawnVitalsWriter"/> over the zone's <see cref="IronGrind.CharacterStats.CharacterStats"/>
    /// (CR-DR-9; Death and Respawn Story 004, AC-3). Writes current HP / MP through SetCurrentHP / SetCurrentMP,
    /// which clamp to the effective maximum.
    /// </summary>
    public sealed class CharacterStatsRespawnVitalsWriter : IRespawnVitalsWriter
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Initializes the adapter.</summary>
        /// <param name="stats">The stats container. Must not be null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsRespawnVitalsWriter(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc/>
        public void RestoreToFull(EntityID entity)
        {
            _stats.SetCurrentHP(entity, _stats.GetEffectiveStat(entity, StatID.MaxHP));
            _stats.SetCurrentMP(entity, _stats.GetEffectiveStat(entity, StatID.MaxMP));
        }
    }
}

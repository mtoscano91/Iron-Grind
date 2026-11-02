using System;
using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// <see cref="ISkillLevelSource"/> over Character Stats: the effective <see cref="StatID.Level"/> (Story 002).
    /// </summary>
    public sealed class CharacterStatsSkillLevelSource : ISkillLevelSource
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsSkillLevelSource(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public int GetLevel(EntityID entity)
        {
            return _stats.GetEffectiveStat(entity, StatID.Level);
        }
    }
}

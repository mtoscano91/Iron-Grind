using System;
using IronGrind.CharacterStats;
using CharacterStatsContainer = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary><see cref="ISkillStatSource"/> over <c>CharacterStats.GetEffectiveStat</c> (Story 007).</summary>
    public sealed class CharacterStatsSkillStatSource : ISkillStatSource
    {
        private readonly CharacterStatsContainer _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsSkillStatSource(CharacterStatsContainer stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public int GetStat(EntityID entity, StatID stat)
        {
            return _stats.GetEffectiveStat(entity, stat);
        }
    }
}

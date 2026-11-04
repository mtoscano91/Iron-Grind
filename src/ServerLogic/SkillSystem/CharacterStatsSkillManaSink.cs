using System;
using IronGrind.CharacterStats;
using CharacterStatsContainer = IronGrind.CharacterStats.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary><see cref="ISkillManaSink"/> over <c>CharacterStats.ConsumeMana</c> (Story 006).</summary>
    /// <remarks>
    /// <c>CharacterStats.ConsumeMana</c> takes a float; the integer cost is widened exactly (costs are far below 2^24).
    /// It returns false while a stat event is firing, which the executor reports as InsufficientMana.
    /// </remarks>
    public sealed class CharacterStatsSkillManaSink : ISkillManaSink
    {
        private readonly CharacterStatsContainer _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to deduct from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsSkillManaSink(CharacterStatsContainer stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public bool ConsumeMana(EntityID entity, int cost)
        {
            return _stats.ConsumeMana(entity, (float)cost);
        }
    }
}

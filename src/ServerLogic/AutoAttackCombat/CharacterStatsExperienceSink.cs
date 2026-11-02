using System;
using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAutoAttackExperienceSink"/> over <c>CharacterStats.AddExperience</c>. A pure pass-through: Character
    /// Stats already ignores an amount of 0 or less and a non-player entity.
    /// </summary>
    /// <remarks>This adapter does not catch exceptions.</remarks>
    /// <example>
    /// <code>
    /// IAutoAttackExperienceSink sink = new CharacterStatsExperienceSink(characterStats);
    /// </code>
    /// </example>
    public sealed class CharacterStatsExperienceSink : IAutoAttackExperienceSink
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsExperienceSink(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public void AddExperience(EntityID attacker, int amount)
        {
            _stats.AddExperience(attacker, amount);
        }
    }
}

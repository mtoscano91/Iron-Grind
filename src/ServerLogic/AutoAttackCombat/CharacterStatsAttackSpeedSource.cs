using System;
using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAttackSpeedSource"/> over <c>CharacterStats.GetEffectiveStatFloat</c> for
    /// <see cref="StatID.AttackSpeedMultiplier"/>. Uses the float read, not the int <c>GetEffectiveStat</c>, which
    /// floors. The raw value passes through; <see cref="AutoAttackCycle"/> clamps.
    /// </summary>
    /// <remarks>
    /// This adapter does not catch exceptions. An entity Character Stats does not know reads as 0f, which
    /// <see cref="AutoAttackCycle"/> clamps to the slowest cycle (40 ticks), so a player's stats must be registered
    /// before combat can activate (Story 006 composition).
    /// </remarks>
    /// <example>
    /// <code>
    /// IAttackSpeedSource source = new CharacterStatsAttackSpeedSource(characterStats);
    /// </code>
    /// </example>
    public sealed class CharacterStatsAttackSpeedSource : IAttackSpeedSource
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsAttackSpeedSource(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public float GetAttackSpeedMultiplier(EntityID entity)
        {
            return _stats.GetEffectiveStatFloat(entity, StatID.AttackSpeedMultiplier);
        }
    }
}

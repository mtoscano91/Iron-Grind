using System;
using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAutoAttackStatSource"/> over Character Stats. BaseDamage is the effective
    /// <see cref="StatID.AttackPower"/> (int; there is no BaseDamage stat, character-stats.md Rule 6) and the range
    /// is the float read of <see cref="StatID.AttackRange"/>.
    /// </summary>
    /// <remarks>This adapter does not catch exceptions. An entity Character Stats does not know reads as 0.</remarks>
    /// <example>
    /// <code>
    /// IAutoAttackStatSource source = new CharacterStatsAutoAttackStatSource(characterStats);
    /// </code>
    /// </example>
    public sealed class CharacterStatsAutoAttackStatSource : IAutoAttackStatSource
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to read.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsAutoAttackStatSource(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public int GetBaseDamage(EntityID attacker)
        {
            return _stats.GetEffectiveStat(attacker, StatID.AttackPower);
        }

        /// <inheritdoc />
        public float GetAttackRange(EntityID attacker)
        {
            return _stats.GetEffectiveStatFloat(attacker, StatID.AttackRange);
        }
    }
}

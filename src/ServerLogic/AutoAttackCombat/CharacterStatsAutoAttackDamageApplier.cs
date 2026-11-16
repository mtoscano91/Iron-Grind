using System;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAutoAttackDamageApplier"/> over <c>CharacterStats.ApplyDamage</c> (Enemy AI Story 012, AC-2): reduces the
    /// target's HP by <see cref="DamageResult.FinalDamage"/> and does nothing else. Combat wraps it in
    /// <see cref="DamageOutputApplier"/>; the Skill damage step receives it unwrapped.
    /// </summary>
    /// <remarks>This adapter does not catch exceptions. Allocates nothing.</remarks>
    /// <example>
    /// <code>
    /// IAutoAttackDamageApplier inner = new CharacterStatsAutoAttackDamageApplier(characterStats);
    /// </code>
    /// </example>
    public sealed class CharacterStatsAutoAttackDamageApplier : IAutoAttackDamageApplier
    {
        private readonly IronGrind.CharacterStats.CharacterStats _stats;

        /// <summary>Creates the adapter.</summary>
        /// <param name="stats">The Character Stats container to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
        public CharacterStatsAutoAttackDamageApplier(IronGrind.CharacterStats.CharacterStats stats)
        {
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public void Apply(EntityID attacker, EntityID target, in DamageResult result)
        {
            _stats.ApplyDamage(target, result.FinalDamage);
        }
    }
}

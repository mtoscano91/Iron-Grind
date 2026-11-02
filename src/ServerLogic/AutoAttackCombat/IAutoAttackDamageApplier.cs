using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Applies a resolved auto-attack hit (design/gdd/auto-attack-combat.md Rule 10 Step 3). Called only for a result
    /// with <see cref="DamageResult.FinalDamage"/> above 0. Story 004 adds the kill path behind it
    /// (<see cref="DamageResult.IsKill"/> handling is Story 004's). The damage output (Story 007) is sent by
    /// <see cref="DamageOutputApplier"/>, which decorates the applier.
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate.</remarks>
    public interface IAutoAttackDamageApplier
    {
        /// <summary>Applies the result to the target.</summary>
        /// <param name="attacker">The attacking player.</param>
        /// <param name="target">The target entity.</param>
        /// <param name="result">The resolved hit.</param>
        void Apply(EntityID attacker, EntityID target, in DamageResult result);
    }
}

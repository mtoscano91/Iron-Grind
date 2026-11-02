using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Resolves one auto-attack hit (design/gdd/auto-attack-combat.md Rule 10 Step 3). The crit roll stays inside the
    /// implementation, one draw per call; the caller never passes a roll in.
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate.</remarks>
    public interface IAutoAttackDamageCalculator
    {
        /// <summary>Resolves the hit with the auto-attack context.</summary>
        /// <param name="baseDamage">The attacker's base damage.</param>
        /// <param name="attacker">The attacking player.</param>
        /// <param name="target">The target entity.</param>
        /// <returns>The result; <see cref="DamageResult.FinalDamage"/> 0 means the call was rejected.</returns>
        DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target);
    }
}

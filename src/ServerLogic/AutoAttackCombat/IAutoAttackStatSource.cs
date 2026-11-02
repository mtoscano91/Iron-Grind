using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Supplies the per-attempt combat stats of an attacker (design/gdd/auto-attack-combat.md Rule 10;
    /// character-stats.md Rule 6). Both values are read on every fire attempt and never cached by the caller.
    /// </summary>
    /// <remarks>Tick thread. Implementations must not throw or allocate.</remarks>
    public interface IAutoAttackStatSource
    {
        /// <summary>The attacker's base damage (its effective AttackPower), passed to damage calculation as is.</summary>
        /// <param name="attacker">The attacking player.</param>
        /// <returns>The base damage.</returns>
        int GetBaseDamage(EntityID attacker);

        /// <summary>The attacker's strike radius in world units.</summary>
        /// <param name="attacker">The attacking player.</param>
        /// <returns>The attack range.</returns>
        float GetAttackRange(EntityID attacker);
    }
}

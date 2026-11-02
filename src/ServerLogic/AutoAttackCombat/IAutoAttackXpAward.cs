using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Supplies the XP a killed mob is worth (design/gdd/auto-attack-combat.md Rule 10 Step 3; leveling-system.md CR-1.1).
    /// Called once per kill, before the experience is added and before the damage is applied. Auto-Attack Combat Story 004.
    /// </summary>
    /// <remarks>
    /// Tick thread. Implementations must not allocate. There is no production implementation in this epic: Enemy AI or
    /// Leveling provides it later (through the mob definition registry).
    /// </remarks>
    public interface IAutoAttackXpAward
    {
        /// <summary>The XP awarded for killing the target.</summary>
        /// <param name="target">The killed mob.</param>
        /// <returns>The XP amount; 0 awards nothing.</returns>
        int GetXPAward(EntityID target);
    }
}

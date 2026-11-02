using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// The narrow seam the inbound request handlers use to reach Auto-Attack Combat (Story 006,
    /// design/gdd/auto-attack-combat.md Rule 1; ADR-014 Decision 3). <see cref="AutoAttackCombatSystem"/>
    /// implements it; tests substitute a recorder.
    /// </summary>
    /// <remarks>Tick thread, called from the request step (step 2), before the simulation step (EC-8).</remarks>
    public interface IAutoAttackInputSink
    {
        /// <summary>Selects a target, or deselects with <see cref="EntityID.Invalid"/>.</summary>
        /// <param name="attacker">The player selecting.</param>
        /// <param name="target">The new target, or <see cref="EntityID.Invalid"/>.</param>
        void SetTarget(EntityID attacker, EntityID target);

        /// <summary>Sets the auto-attack toggle.</summary>
        /// <param name="attacker">The player toggling.</param>
        /// <param name="enabled">The new toggle value.</param>
        void SetAutoAttack(EntityID attacker, bool enabled);
    }
}

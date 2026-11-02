using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Receives the experience earned by a killing auto (design/gdd/auto-attack-combat.md Rule 10 Step 3).
    /// Auto-Attack Combat Story 004.
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate. Called by direct call, never through an event (ADR-010).</remarks>
    public interface IAutoAttackExperienceSink
    {
        /// <summary>Adds experience to the attacker.</summary>
        /// <param name="attacker">The killing player.</param>
        /// <param name="amount">The XP to add; may be 0.</param>
        void AddExperience(EntityID attacker, int amount);
    }
}

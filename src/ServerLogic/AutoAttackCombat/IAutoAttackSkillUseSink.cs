using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// The narrow surface the Skill System injects to tell Auto-Attack Combat that a skill was used
    /// (design/gdd/auto-attack-combat.md Rules 12-15; TR-aa-005). Auto-Attack Combat Story 005.
    /// </summary>
    /// <remarks>
    /// Tick thread. ADR-010: a direct call on an injected interface, not an event. Implementations must not allocate.
    /// </remarks>
    public interface IAutoAttackSkillUseSink
    {
        /// <summary>
        /// Resets the caster's auto-attack timer because a skill was used (Rules 12, 14).
        /// </summary>
        /// <param name="casterEntityID">The player who cast.</param>
        /// <remarks>
        /// Called only for a validated, accepted cast: the Skill System validates (Rules 13, 14); this method does not.
        /// A skill that misses or is absorbed still calls it (Rule 14). Tick thread. Ignored for an entity with no player
        /// slot; for an idle player it only zeroes the frozen timer.
        /// </remarks>
        void NotifySkillUsed(EntityID casterEntityID);
    }
}

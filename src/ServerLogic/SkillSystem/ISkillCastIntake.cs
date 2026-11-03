using IronGrind.CharacterStats;
using IronGrind.Networking;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Where the inbound handler hands a validated (V-1, V-2) <see cref="SkillCastRequest"/> to the Skill System
    /// (skill-system.md CR-SK-1, CR-SK-2; Skill System Story 004).
    /// </summary>
    /// <example>
    /// <code>
    /// intake.Submit(context.SenderEntityId, in request);
    /// </code>
    /// </example>
    public interface ISkillCastIntake
    {
        /// <summary>
        /// Stores the request in the caster's slot. First-wins: a request for an occupied slot is discarded silently
        /// (EC-SK-11). An entity without a player slot is dropped with a warning.
        /// </summary>
        /// <param name="caster">The connection's sender entity (already checked against the request body by V-1).</param>
        /// <param name="request">The decoded request.</param>
        void Submit(EntityID caster, in SkillCastRequest request);
    }
}

using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Narrow read seam for V-0: is the caster alive (skill-system.md V-0; Skill System Story 005).
    /// <c>IEntityDeathSource</c> only offers a death subscription, not a query, so the step needs this one.
    /// </summary>
    /// <remarks>Called on the tick thread once per stored cast. Must not allocate or throw.</remarks>
    public interface ISkillCasterAliveSource
    {
        /// <summary>Whether <paramref name="entity"/> is alive.</summary>
        /// <param name="entity">The caster.</param>
        /// <returns>True when alive.</returns>
        bool IsAlive(EntityID entity);
    }
}

using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>Narrow read seam for the effective stat a skill's damage scales from (skill-system.md CR-SK-4, F-SK-1, F-SK-2; Story 007).</summary>
    /// <remarks>Tick thread. Implementations must not allocate or throw.</remarks>
    public interface ISkillStatSource
    {
        /// <summary>Reads one effective stat.</summary>
        /// <param name="entity">The caster.</param>
        /// <param name="stat">The stat to read.</param>
        /// <returns>The effective stat value.</returns>
        int GetStat(EntityID entity, StatID stat);
    }
}

using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>Narrow read seam for V-5b: is the caster silenced (skill-system.md V-5b; Story 005).</summary>
    /// <remarks>Called on the tick thread once per stored cast. Must not allocate or throw.</remarks>
    public interface ISkillSilenceSource
    {
        /// <summary>Whether <paramref name="entity"/> may not cast skills right now.</summary>
        /// <param name="entity">The caster.</param>
        /// <returns>True when silenced.</returns>
        bool IsSilenced(EntityID entity);
    }
}

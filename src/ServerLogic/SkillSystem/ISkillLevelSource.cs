using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>Narrow seam for the entity level the Skill System reads at spawn (Story 002).</summary>
    public interface ISkillLevelSource
    {
        /// <summary>Returns the entity's current level.</summary>
        /// <param name="entity">The entity.</param>
        int GetLevel(EntityID entity);
    }
}

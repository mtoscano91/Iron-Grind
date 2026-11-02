namespace IronGrind.SkillSystem
{
    /// <summary>Narrow read seam over the authored skill roster (Story 002); implemented by <see cref="SkillDatabase"/>.</summary>
    public interface ISkillDefinitionSource
    {
        /// <summary>Finds a skill definition by id; false for an invalid or unknown id.</summary>
        /// <param name="id">The skill id.</param>
        /// <param name="definition">The definition when found.</param>
        bool TryGetDefinition(SkillID id, out SkillDefinition definition);
    }
}

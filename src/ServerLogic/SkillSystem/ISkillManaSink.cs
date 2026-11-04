using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// The mana deduction the Skill System needs (skill-system.md V-9, F-SK-4; Skill System Story 006). Production
    /// adapter: <see cref="CharacterStatsSkillManaSink"/>.
    /// </summary>
    public interface ISkillManaSink
    {
        /// <summary>Deducts <paramref name="cost"/> mana when the entity has enough.</summary>
        /// <param name="entity">The caster.</param>
        /// <param name="cost">The effective cost, greater than 0 (a free skill never calls this).</param>
        /// <returns>True when the mana was deducted; false (nothing written) when it was insufficient.</returns>
        bool ConsumeMana(EntityID entity, int cost);
    }
}

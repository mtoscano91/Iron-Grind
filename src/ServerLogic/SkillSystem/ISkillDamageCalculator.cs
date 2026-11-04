using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.SkillSystem
{
    /// <summary>Seam over <c>DamageCalculator.Calculate</c> for skill damage (skill-system.md CR-SK-5; Story 007).</summary>
    /// <remarks>Tick thread. Crit rolls happen inside the implementation (ADR-013); the Skill System draws no random numbers.</remarks>
    public interface ISkillDamageCalculator
    {
        /// <summary>Resolves one skill hit.</summary>
        /// <param name="baseDamage">F-SK-1 / F-SK-2 base damage.</param>
        /// <param name="attackerId">The caster.</param>
        /// <param name="targetId">The target.</param>
        /// <param name="context">PhysicalSkill or MagicalSkill.</param>
        /// <returns>The resolved hit.</returns>
        DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context);
    }
}

using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Seam to Status Effects for the Skill System, the sole external caller of these two methods (skill-system.md CR-SK-7,
    /// F-SK-3; status-effects.md CR-SE-14; Skill System Story 008).
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate.</remarks>
    public interface ISkillEffectSink
    {
        /// <summary>Applies an authored buff or debuff to a target.</summary>
        /// <param name="target">The entity that receives the effect.</param>
        /// <param name="caster">The casting entity.</param>
        /// <param name="definition">The authored buff.</param>
        /// <returns>The outcome; the Skill System never rejects a cast on it.</returns>
        ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition);

        /// <summary>Delivers a one-shot heal.</summary>
        /// <param name="entity">The entity to heal.</param>
        /// <param name="amount">HP to restore.</param>
        /// <returns>The outcome; the Skill System never rejects a cast on it.</returns>
        ApplyResult ApplyInstantHeal(EntityID entity, float amount);
    }
}

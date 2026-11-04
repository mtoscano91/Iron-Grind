using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>Adapter: <see cref="ISkillEffectSink"/> over <see cref="StatusEffectsService"/> (pass-through; Skill System Story 008).</summary>
    public sealed class StatusEffectsSkillEffectSink : ISkillEffectSink
    {
        private readonly StatusEffectsService _statusEffects;

        /// <summary>Creates the adapter.</summary>
        /// <param name="statusEffects">The Status Effects service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="statusEffects"/> is null.</exception>
        public StatusEffectsSkillEffectSink(StatusEffectsService statusEffects)
        {
            _statusEffects = statusEffects ?? throw new ArgumentNullException(nameof(statusEffects));
        }

        /// <inheritdoc />
        public ApplyResult ApplyEffect(EntityID target, EntityID caster, in BuffDefinition definition)
        {
            return _statusEffects.ApplyEffect(target, caster, in definition);
        }

        /// <inheritdoc />
        public ApplyResult ApplyInstantHeal(EntityID entity, float amount)
        {
            return _statusEffects.ApplyInstantHeal(entity, amount);
        }
    }
}

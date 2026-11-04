using System;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Effects half of an accepted cast: buff / debuff application and instant heal
    /// (skill-system.md CR-SK-7, F-SK-3, EC-SK-4, EC-SK-9; status-effects.md CR-SE-14; Skill System Story 008).
    /// </summary>
    /// <remarks>
    /// Runs after the damage step (the composite guarantees it), so <see cref="SkillExecutionOutcome.IsKill"/> is final.
    /// ApplyBuff / ApplyDebuff run before InstantHeal. A target this cast killed gets neither (AC-SK-27; the heal skip
    /// is a code-review decision, 2026-11-04). A miss still applies: only a kill skips.
    /// The buff ApplyResult is read and never acted on: CapacityExceeded (EC-SK-9) and InvalidTarget (EC-SK-4, killed by
    /// someone else earlier this tick) leave the cast accepted with mana and cooldown paid; no retry, no undo.
    /// InstantHeal: floor(INT x HealMultiplier), called even when 0; <see cref="SkillExecutionOutcome.HealAmount"/> is the HP
    /// actually delivered (target CurrentHP read before and after, so a heal clamped at MaxHP reports less than the
    /// computed amount). It is written only when ApplyInstantHeal returns <see cref="ApplyResult.Success"/> and stays 0 otherwise.
    /// Single target at MVP (CR-SK-17): the outcome's IsKill describes that one target; AoE will need a target list and
    /// per-target kill info. Stateless and allocation-free.
    /// </remarks>
    public sealed class SkillEffectExecutionStep : ISkillExecutionStep
    {
        private const byte HEAL_FLAG = (byte)SkillEffectType.InstantHeal;
        private const byte BUFF_FLAG = (byte)SkillEffectType.ApplyBuff;
        private const byte DEBUFF_FLAG = (byte)SkillEffectType.ApplyDebuff;

        private readonly ISkillEffectSink _effects;
        private readonly ISkillStatSource _stats;

        /// <summary>Creates the step.</summary>
        /// <param name="effects">Status Effects seam.</param>
        /// <param name="stats">Reads the caster's INT and the target's CurrentHP (before/after the heal).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public SkillEffectExecutionStep(ISkillEffectSink effects, ISkillStatSource stats)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        }

        /// <inheritdoc />
        public void Execute(in ValidatedSkillCast cast, ref SkillExecutionOutcome outcome)
        {
            if (outcome.IsKill)
            {
                return;
            }

            byte flags = (byte)cast.Definition.SkillEffectType;
            if ((flags & (BUFF_FLAG | DEBUFF_FLAG)) != 0)
            {
                ApplyBuff(in cast);
            }

            if ((flags & HEAL_FLAG) != 0)
            {
                ApplyHeal(in cast, ref outcome);
            }
        }

        /// <summary>F-SK-3: <c>Floor(intelligence * multiplier)</c> in double, clamped to [0, int.MaxValue].</summary>
        /// <param name="intelligence">The caster's effective INT.</param>
        /// <param name="multiplier">The skill's heal multiplier.</param>
        /// <returns>The heal; 0 for NaN, infinity or a non-positive product, <see cref="int.MaxValue"/> on overflow.</returns>
        public static int ComputeHealAmount(int intelligence, float multiplier)
        {
            double d = Math.Floor(intelligence * (double)multiplier);
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0d)
            {
                return 0;
            }

            return d >= int.MaxValue ? int.MaxValue : (int)d;
        }

        // CR-SK-7. Self casts already carry Target == Caster (validation). Result deliberately ignored (EC-SK-4, EC-SK-9).
        private void ApplyBuff(in ValidatedSkillCast cast)
        {
            _effects.ApplyEffect(cast.Target, cast.Caster, in cast.Definition.BuffDefinition);
        }

        // F-SK-3. The cast is never rejected on the result; only the HP actually delivered is reported:
        // max(0, CurrentHP after - CurrentHP before), so a heal clamped at MaxHP reports the clamped amount.
        // ApplyInstantHeal -> ApplyRegen writes CurrentHP synchronously, so the after-read sees it.
        private void ApplyHeal(in ValidatedSkillCast cast, ref SkillExecutionOutcome outcome)
        {
            int stat = _stats.GetStat(cast.Caster, StatID.Intelligence);
            int amount = ComputeHealAmount(stat, cast.Definition.HealMultiplier);
            int before = _stats.GetStat(cast.Target, StatID.CurrentHP);
            if (_effects.ApplyInstantHeal(cast.Target, amount) == ApplyResult.Success)
            {
                int after = _stats.GetStat(cast.Target, StatID.CurrentHP);
                outcome.HealAmount = after > before ? after - before : 0;
            }
        }
    }
}

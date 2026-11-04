using System;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Runs the damage step, then the effects step, sharing one <see cref="SkillExecutionOutcome"/>
    /// (skill-system.md CR-SK-7; AC-SK-26: effects apply only after damage resolves; Skill System Story 008).
    /// </summary>
    /// <remarks>Story 011 wires this single step into the executor. Allocation-free.</remarks>
    public sealed class CompositeSkillExecutionStep : ISkillExecutionStep
    {
        private readonly ISkillExecutionStep _damage;
        private readonly ISkillExecutionStep _effects;

        /// <summary>Creates the composite.</summary>
        /// <param name="damage">Runs first.</param>
        /// <param name="effects">Runs second.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public CompositeSkillExecutionStep(ISkillExecutionStep damage, ISkillExecutionStep effects)
        {
            _damage = damage ?? throw new ArgumentNullException(nameof(damage));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        /// <inheritdoc />
        public void Execute(in ValidatedSkillCast cast, ref SkillExecutionOutcome outcome)
        {
            _damage.Execute(in cast, ref outcome);
            _effects.Execute(in cast, ref outcome);
        }
    }
}

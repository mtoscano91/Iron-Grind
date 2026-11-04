using System;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.SkillSystem
{
    /// <summary><see cref="ISkillDamageCalculator"/> over <see cref="DamageCalculator"/> (Story 007).</summary>
    public sealed class DamageCalculatorSkillAdapter : ISkillDamageCalculator
    {
        private readonly DamageCalculator _calculator;

        /// <summary>Creates the adapter.</summary>
        /// <param name="calculator">The damage calculator.</param>
        /// <exception cref="ArgumentNullException"><paramref name="calculator"/> is null.</exception>
        public DamageCalculatorSkillAdapter(DamageCalculator calculator)
        {
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        /// <inheritdoc />
        public DamageResult Calculate(int baseDamage, EntityID attackerId, EntityID targetId, DamageContext context)
        {
            return _calculator.Calculate(baseDamage, attackerId, targetId, context);
        }
    }
}

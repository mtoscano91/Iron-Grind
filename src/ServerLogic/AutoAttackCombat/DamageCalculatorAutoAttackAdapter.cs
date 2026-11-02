using System;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// <see cref="IAutoAttackDamageCalculator"/> over <see cref="DamageCalculator"/> with
    /// <see cref="DamageContext.PhysicalAuto"/>. The crit roll stays inside <c>Calculate</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// IAutoAttackDamageCalculator calc = new DamageCalculatorAutoAttackAdapter(damageCalculator);
    /// </code>
    /// </example>
    public sealed class DamageCalculatorAutoAttackAdapter : IAutoAttackDamageCalculator
    {
        private readonly DamageCalculator _calculator;

        /// <summary>Creates the adapter.</summary>
        /// <param name="calculator">The damage calculator to call.</param>
        /// <exception cref="ArgumentNullException"><paramref name="calculator"/> is null.</exception>
        public DamageCalculatorAutoAttackAdapter(DamageCalculator calculator)
        {
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        /// <inheritdoc />
        public DamageResult Calculate(int baseDamage, EntityID attacker, EntityID target)
        {
            return _calculator.Calculate(baseDamage, attacker, target, DamageContext.PhysicalAuto);
        }
    }
}

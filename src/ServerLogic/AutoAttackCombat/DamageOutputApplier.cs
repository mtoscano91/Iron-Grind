using System;
using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Decorator over an <see cref="IAutoAttackDamageApplier"/> that sends the damage output after the inner applier
    /// ran (Auto-Attack Combat Story 007). The combat system calls it only for a result with damage, so a miss sends
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Tick thread. Allocates nothing. If the inner applier throws, nothing is emitted and the exception propagates
    /// to the caller.
    /// </remarks>
    /// <example>
    /// <code>
    /// IAutoAttackDamageApplier applier = new DamageOutputApplier(inner, emitter);
    /// </code>
    /// </example>
    public sealed class DamageOutputApplier : IAutoAttackDamageApplier
    {
        private readonly IAutoAttackDamageApplier _inner;
        private readonly IAutoAttackDamageOutput _output;

        /// <summary>Creates the decorator.</summary>
        /// <param name="inner">The applier that changes the target's health.</param>
        /// <param name="output">Sends the client messages.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public DamageOutputApplier(IAutoAttackDamageApplier inner, IAutoAttackDamageOutput output)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <inheritdoc />
        public void Apply(EntityID attacker, EntityID target, in DamageResult result)
        {
            _inner.Apply(attacker, target, in result);
            _output.Emit(attacker, target, in result);
        }
    }
}

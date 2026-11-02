using IronGrind.CharacterStats;
using IronGrind.DamageCalculation;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Sends the client-facing messages of one applied auto-attack hit (Auto-Attack Combat Story 007, Rule 10 Step 2).
    /// </summary>
    /// <remarks>Tick thread. Implementations must not allocate and must not throw.</remarks>
    public interface IAutoAttackDamageOutput
    {
        /// <summary>Sends the output of one hit; a result with no damage sends nothing.</summary>
        /// <param name="attacker">The attacking player.</param>
        /// <param name="target">The target entity.</param>
        /// <param name="result">The resolved hit.</param>
        void Emit(EntityID attacker, EntityID target, in DamageResult result);
    }
}

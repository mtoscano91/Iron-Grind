using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Receives the "cycle complete" signal from <see cref="AutoAttackCombatSystem.Run"/> (Rules 5-7).
    /// Interim seam for Story 002: Story 003 replaces it with Rule 10 resolution (the actual fire).
    /// </summary>
    /// <remarks>Called on the tick thread inside the per-tick loop; implementations must not throw or allocate.</remarks>
    public interface IAutoAttackReadySink
    {
        /// <summary>The attacker's action timer reached its cycle length this tick.</summary>
        /// <param name="attacker">The attacking player.</param>
        /// <param name="currentTick">The server tick being run.</param>
        void OnAutoAttackReady(EntityID attacker, uint currentTick);
    }
}

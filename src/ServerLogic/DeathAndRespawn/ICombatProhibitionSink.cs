using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Narrow seam to Auto-Attack Combat's prohibition flag (CR-DR-3, AC-DR-3). Implemented by
    /// <c>AutoAttackCombatSystem</c>.
    /// </summary>
    public interface ICombatProhibitionSink
    {
        /// <summary>Sets or clears combat prohibition for a player.</summary>
        /// <param name="entity">The player.</param>
        /// <param name="prohibited">True to prohibit, false to clear.</param>
        void SetCombatProhibited(EntityID entity, bool prohibited);
    }
}

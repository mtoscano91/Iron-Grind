using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Receives a ghost's death (death-and-respawn.md CR-DR-Ghost; Story 003, ADR-010). The production adapter, which
    /// records GhostDeath, saves and releases the slot and ends the session, is Story 010. If an implementation throws,
    /// the handler logs the error and the death is dropped (no Dead state, no retry). Implementations must be
    /// idempotent: the handler does not deduplicate ghost deaths, so a repeated death calls the sink again.
    /// </summary>
    public interface IGhostDeathSink
    {
        /// <summary>Called once per ghost death, on the tick thread, instead of the Dead state entry.</summary>
        /// <param name="entity">The ghost's EntityID.</param>
        void OnGhostDied(EntityID entity);
    }
}

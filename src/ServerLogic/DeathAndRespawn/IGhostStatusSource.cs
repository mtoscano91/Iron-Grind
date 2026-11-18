using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Seam that tells Death and Respawn whether a dying entity is a ghost, a disconnected player whose session is
    /// still active (death-and-respawn.md CR-DR-Ghost; Story 003, ADR-010).
    /// </summary>
    public interface IGhostStatusSource
    {
        /// <summary>True when <paramref name="id"/> is a ghost player (disconnected, session active).</summary>
        /// <param name="id">The entity that died.</param>
        /// <returns>False for a connected player, a mob or an unknown id.</returns>
        bool IsGhost(EntityID id);
    }
}

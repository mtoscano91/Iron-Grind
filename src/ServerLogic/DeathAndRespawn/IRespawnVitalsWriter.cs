using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Respawn HP / MP restore seam (death-and-respawn.md CR-DR-9; Death and Respawn Story 004, AC-3). The production
    /// adapter is <see cref="CharacterStatsRespawnVitalsWriter"/>. Tick thread only.
    /// </summary>
    public interface IRespawnVitalsWriter
    {
        /// <summary>Sets the entity's current HP and MP to their effective maximums.</summary>
        /// <param name="entity">The respawning player's EntityID.</param>
        void RestoreToFull(EntityID entity);
    }
}

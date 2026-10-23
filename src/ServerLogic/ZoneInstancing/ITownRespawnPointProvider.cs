using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Answers where a player respawns in town for a zone (Zone Instancing CR-ZI-11, Story 005). This is the seam
    /// Death and Respawn calls (CR-DR-8). Implementations must be O(1) and must not throw.
    /// </summary>
    /// <example>
    /// <code>
    /// ITownRespawnPointProvider provider = new TownRespawnPointCache(ownZone, definition);
    /// Vector3 point = provider.GetTownRespawnPoint(ownZone);
    /// </code>
    /// </example>
    public interface ITownRespawnPointProvider
    {
        /// <summary>Returns the town respawn point for a zone.</summary>
        /// <param name="zone">The zone the player died in.</param>
        /// <returns>The respawn point in world metres; <see cref="Vector3.zero"/> when none can be resolved.</returns>
        Vector3 GetTownRespawnPoint(ZoneID zone);
    }
}

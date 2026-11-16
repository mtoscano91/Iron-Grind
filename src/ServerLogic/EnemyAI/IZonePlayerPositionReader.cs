using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>Narrow seam over Movement's committed player positions, so <see cref="ZoneAggroCandidateSource"/> can be tested with a fake.</summary>
    public interface IZonePlayerPositionReader
    {
        /// <summary>Gets a player's committed world position.</summary>
        /// <param name="id">The player's entity id.</param>
        /// <param name="position">The position when found.</param>
        /// <returns>False when the player has no movement position; the position is then meaningless.</returns>
        bool TryGetPosition(EntityID id, out Vector3 position);
    }
}

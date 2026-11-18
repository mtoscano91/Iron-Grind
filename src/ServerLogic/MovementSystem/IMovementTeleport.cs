using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// Authoritative position set seam (death-and-respawn.md CR-DR-10; Death and Respawn Story 004, AC-4).
    /// Implemented by <see cref="MovementService"/>. Separate from <see cref="IMovementSystem"/>.
    /// </summary>
    public interface IMovementTeleport
    {
        /// <summary>
        /// Places a registered player at <paramref name="position"/>: resets the slot's movement state, drops any
        /// pending intent and sets the position. Facing and occupancy are unchanged.
        /// </summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="position">The new authoritative position.</param>
        /// <returns>False when <paramref name="id"/> is not registered with movement.</returns>
        bool TrySetAuthoritativePosition(EntityID id, Vector3 position);
    }
}

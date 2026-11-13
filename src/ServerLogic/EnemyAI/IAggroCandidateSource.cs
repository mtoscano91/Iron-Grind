using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Player seam for Enemy AI (CR-AI-3 aggro scan, CR-AI-11 pursuit). The production adapter is provided by
    /// Stories 008/009; tests use a fake. HP is not part of this seam: the controller reads it from the stat reader.
    /// </summary>
    public interface IAggroCandidateSource
    {
        /// <summary>
        /// Appends every player (id and world position) to <paramref name="into"/>, in zone pool order
        /// (the "entity list order" of AC-AI-2). The caller owns the list and clears it first.
        /// </summary>
        /// <param name="into">The caller-owned list to fill.</param>
        void CopyPlayers(List<(EntityID Id, Vector3 Position)> into);

        /// <summary>Gets the position of one player (the pursuit target, CR-AI-11).</summary>
        /// <param name="id">The player's entity id.</param>
        /// <param name="position">The player's world position when found.</param>
        /// <returns>False when the player cannot be found; the position is then meaningless.</returns>
        bool TryGetPlayerPosition(EntityID id, out Vector3 position);
    }
}

using System;
using System.Collections.Generic;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// The commit seam of a level-up (Leveling Story 016; ADR-011 Decision 5). <see cref="LevelingService"/>
    /// hands it the four steps of a level-up and the seam decides when they run: immediately
    /// (<see cref="ImmediateLevelUpCommit"/>, the default) or behind the irreversible-outcome coordinator
    /// (apply in memory, persist, then deliver on success or revert on failure).
    /// </summary>
    public interface ILevelUpCommit
    {
        /// <summary>
        /// Runs a level-up through the seam. Tick thread only.
        /// </summary>
        /// <param name="entityId">The entity that crossed its XP threshold.</param>
        /// <param name="wouldLevelUp">True when a level would be gained (below the cap and at or above the threshold); no write may start otherwise.</param>
        /// <param name="applyLevelUp">Applies the level-up in memory and returns the levels gained, ascending (empty when none).</param>
        /// <param name="deliverLevelUp">Broadcasts the levels returned by <paramref name="applyLevelUp"/>; runs only after the level-up was committed.</param>
        /// <param name="revertLevelUp">Restores the state captured before <paramref name="applyLevelUp"/>; does not broadcast.</param>
        void Commit(
            IronGrind.CharacterStats.EntityID entityId,
            Func<bool> wouldLevelUp,
            Func<IReadOnlyList<int>> applyLevelUp,
            Action<IReadOnlyList<int>> deliverLevelUp,
            Action revertLevelUp);
    }
}

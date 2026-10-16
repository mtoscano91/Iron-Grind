using System;
using System.Collections.Generic;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// The default <see cref="ILevelUpCommit"/>: applies the level-up and delivers it in the same call, with no
    /// persistence in between (Leveling Story 016, decision 4). This is the behaviour before the seam existed.
    /// </summary>
    public sealed class ImmediateLevelUpCommit : ILevelUpCommit
    {
        /// <summary>The shared stateless instance.</summary>
        public static readonly ImmediateLevelUpCommit Instance = new ImmediateLevelUpCommit();

        /// <summary>
        /// Applies the level-up if one would be gained, then delivers the levels gained. The revert step is never used.
        /// </summary>
        /// <param name="entityId">The entity that crossed its XP threshold.</param>
        /// <param name="wouldLevelUp">True when a level would be gained.</param>
        /// <param name="applyLevelUp">Applies the level-up in memory and returns the levels gained.</param>
        /// <param name="deliverLevelUp">Broadcasts the levels gained.</param>
        /// <param name="revertLevelUp">Unused: nothing can fail between apply and deliver here.</param>
        public void Commit(
            IronGrind.CharacterStats.EntityID entityId,
            Func<bool> wouldLevelUp,
            Func<IReadOnlyList<int>> applyLevelUp,
            Action<IReadOnlyList<int>> deliverLevelUp,
            Action revertLevelUp)
        {
            if (!wouldLevelUp())
                return;

            IReadOnlyList<int> levels = applyLevelUp();
            if (levels != null && levels.Count > 0)
                deliverLevelUp(levels);
        }
    }
}

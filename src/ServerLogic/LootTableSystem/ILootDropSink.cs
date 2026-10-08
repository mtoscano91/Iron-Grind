using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Seam between kill resolution and the later loot stories (ground item spawn and assignment,
    /// Stories 005-006). Public because it appears in the public constructor of
    /// <see cref="LootTableService"/>.
    /// </summary>
    public interface ILootDropSink
    {
        /// <summary>
        /// Receives the pending drop list of a resolved kill. Never called with an empty list.
        /// </summary>
        /// <param name="mobEntityId">The mob that died.</param>
        /// <param name="winningParty">The party that owns the loot tag (CR-LT-4).</param>
        /// <param name="drops">The dropped item IDs, in table entry order (CR-LT-1).</param>
        /// <param name="position">The mob's world position at death.</param>
        void OnDropsResolved(EntityID mobEntityId, PartyID winningParty, IReadOnlyList<ItemID> drops, UnityEngine.Vector3 position);
    }
}

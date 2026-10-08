using IronGrind.CharacterStats;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Consumer-side interface standing in for the zone entity registry plus Enemy AI's mob
    /// definition registry, neither of which has code yet (design/gdd/loot-table-system.md).
    /// </summary>
    public interface IMobInfoProvider
    {
        /// <summary>Looks up a live mob by its entity ID.</summary>
        /// <param name="mobEntityId">The mob's runtime entity ID.</param>
        /// <param name="info">The mob's info when found; otherwise <c>default</c>.</param>
        /// <returns><see langword="true"/> if the mob is known; otherwise <see langword="false"/>.</returns>
        bool TryGetMob(EntityID mobEntityId, out MobInfo info);
    }
}

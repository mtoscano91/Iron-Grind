using IronGrind.CharacterStats;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Read-only view of Leveling state the local player is allowed to see (ADR-012 Decision 7).
    /// Consumer: <c>RespecScreenPresenter</c>. Level-up notification is not here; it is
    /// <see cref="ILevelingEventBroadcaster.OnLevelUp"/>.
    /// </summary>
    public interface ILocalPlayerLevelingView
    {
        /// <summary>Returns the free points the entity holds unspent (separate from the respec pool).</summary>
        /// <param name="entityId">The entity to query.</param>
        int GetHeldFreePoints(EntityID entityId);
    }
}

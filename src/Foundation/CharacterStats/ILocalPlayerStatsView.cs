namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Read-only view of the local player's stats for client UI (ADR-012 Decision 7). Consumers:
    /// <c>PlayerResourceClusterPresenter</c>, <c>RespecScreenPresenter</c>. On a real client the
    /// implementation is a mirror filled from state messages; members are limited to what a
    /// presenter calls today.
    /// </summary>
    public interface ILocalPlayerStatsView
    {
        /// <summary>Returns the base value of <paramref name="statId"/> for <paramref name="entityId"/>.</summary>
        /// <param name="entityId">The entity to read.</param>
        /// <param name="statId">The stat to read.</param>
        int GetBaseStat(EntityID entityId, StatID statId);

        /// <summary>Registers <paramref name="handler"/> to be called when a stat changes.</summary>
        /// <param name="handler">The handler to add.</param>
        void Subscribe(LocalPlayerStatChangedHandler handler);

        /// <summary>Removes <paramref name="handler"/>; it receives no further notifications.</summary>
        /// <param name="handler">The handler to remove.</param>
        void Unsubscribe(LocalPlayerStatChangedHandler handler);
    }
}

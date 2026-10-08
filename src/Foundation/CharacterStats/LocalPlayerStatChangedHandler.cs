namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Notification that a stat of <paramref name="entityId"/> changed. Named delegate type
    /// (ADR-010: two value-type parameters). It exists because <see cref="ILocalPlayerStatsView"/>
    /// cannot use the delegate nested in the server's stats class. Consumer:
    /// <c>PlayerResourceClusterPresenter</c>.
    /// </summary>
    /// <param name="entityId">The entity whose stat changed.</param>
    /// <param name="statId">The stat that changed.</param>
    public delegate void LocalPlayerStatChangedHandler(EntityID entityId, StatID statId);
}

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Event payload for <see cref="ILevelingEventBroadcaster.OnLevelUp"/>. Fired once per level
    /// gained, in ascending order, only after <see cref="LevelingService.NotifyExperienceCrossedThreshold"/>'s
    /// full CR-2.9 consecutive-level-up loop has completed (CR-2.10) — never mid-iteration. See
    /// <c>story-003-consecutive-level-up-reentrancy.md</c> AC-LS-08.
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> per ADR-010 Decision 3 — zero heap allocation on emit.
    /// Colocated with <see cref="ILevelingEventBroadcaster"/> in <c>IronGrind.LevelingSystem</c>,
    /// following the same per-feature-folder convention as <c>Currency</c>'s
    /// <c>GoldSyncEventArgs</c>/<c>ICurrencyService</c> pair rather than a shared
    /// <c>IronGrind.Events</c> namespace (no such shared namespace exists yet in this codebase).
    /// </remarks>
    public readonly struct LevelUpEventArgs
    {
        /// <summary>The entity that leveled up.</summary>
        public readonly IronGrind.CharacterStats.EntityID EntityId;

        /// <summary>The level reached by this notification (the post-increment level).</summary>
        public readonly int NewLevel;

        /// <summary>Constructs a new <see cref="LevelUpEventArgs"/>.</summary>
        public LevelUpEventArgs(IronGrind.CharacterStats.EntityID entityId, int newLevel)
        {
            EntityId = entityId;
            NewLevel = newLevel;
        }
    }
}

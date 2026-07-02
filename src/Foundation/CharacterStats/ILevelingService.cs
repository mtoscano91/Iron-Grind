namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Tier 1 service interface (ADR-010): CharacterStats notifies the Leveling System
    /// when accumulated Experience crosses a threshold. The threshold is owned by the
    /// Leveling System; CharacterStats never hardcodes XP values.
    /// </summary>
    public interface ILevelingService
    {
        /// <summary>
        /// Returns true when <paramref name="entityId"/> is a player entity.
        /// AddExperience is a no-op for mob entities.
        /// </summary>
        bool IsPlayerEntity(EntityID entityId);

        /// <summary>
        /// Returns the current XP threshold for <paramref name="entityId"/> at their
        /// current level. CharacterStats compares accumulated Experience against this
        /// value to detect level-up events.
        /// </summary>
        int GetExperienceThreshold(EntityID entityId);

        /// <summary>
        /// Called by CharacterStats when accumulated Experience reaches or exceeds
        /// the threshold returned by <see cref="GetExperienceThreshold"/>. The Leveling
        /// System handles all level-up side effects (stat gains, threshold reset, etc.).
        /// </summary>
        void NotifyExperienceCrossedThreshold(EntityID entityId);
    }
}

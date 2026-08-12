using System;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Tier 2 broadcast event surface (ADR-010 Decision 3) for the Leveling System. Kept
    /// separate from <see cref="IronGrind.CharacterStats.ILevelingService"/> — the Tier 1
    /// single-listener interface <c>CharacterStats</c> depends on via constructor injection
    /// (ADR-010 Decision 2) — because <see cref="OnLevelUp"/> has multiple, independent
    /// subscribers (HUD, Audio, Skill System). See Story 003's "ADR Decision Summary".
    /// </summary>
    public interface ILevelingEventBroadcaster
    {
        /// <summary>
        /// Fired once per level gained, in ascending order, only after a full CR-2.9
        /// consecutive-level-up loop has completed (CR-2.10, AC-LS-08) — never mid-iteration.
        /// Per-subscriber exceptions are caught and logged individually by the implementation;
        /// one throwing subscriber never prevents remaining subscribers or subsequent levels
        /// from being notified (CR-2.10).
        /// </summary>
        event Action<LevelUpEventArgs> OnLevelUp;
    }
}

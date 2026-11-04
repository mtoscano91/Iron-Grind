using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Narrow read seam for V-7: whether a target is in the zone, alive, and a player or a mob
    /// (skill-system.md V-7, CR-SK-17; Story 005). At MVP any player is friendly and any mob is hostile.
    /// </summary>
    /// <remarks>Called on the tick thread. Must not allocate or throw.</remarks>
    public interface ISkillTargetSource
    {
        /// <summary>Looks the target up.</summary>
        /// <param name="target">The target entity.</param>
        /// <param name="kind">Player or mob, when found.</param>
        /// <param name="isAlive">Whether the target is alive, when found.</param>
        /// <returns>False when the target is not in this zone.</returns>
        bool TryGetTarget(EntityID target, out SkillTargetKind kind, out bool isAlive);
    }
}

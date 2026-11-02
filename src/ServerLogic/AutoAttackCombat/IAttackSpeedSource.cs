using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Seam through which Auto-Attack Combat reads an entity's attack speed multiplier (F-1). Sampled only when a
    /// cycle starts, never mid-cycle. Injected so the system does not depend on a Character Stats singleton.
    /// </summary>
    public interface IAttackSpeedSource
    {
        /// <summary>Reads the current attack speed multiplier for <paramref name="entity"/>.</summary>
        /// <param name="entity">The entity to read.</param>
        /// <returns>The raw multiplier; clamping is <see cref="AutoAttackCycle"/>'s job.</returns>
        float GetAttackSpeedMultiplier(EntityID entity);
    }
}

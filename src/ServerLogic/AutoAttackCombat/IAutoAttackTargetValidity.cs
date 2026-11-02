using IronGrind.CharacterStats;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Answers whether an entity is a valid auto-attack target: a living mob in the same zone instance; a
    /// player is never valid (design/gdd/auto-attack-combat.md Rule 1; death-and-respawn.md CR-DR-16).
    /// Injected into <see cref="AutoAttackCombatSystem"/> so tests use a fake and Enemy AI supplies the real answer.
    /// </summary>
    /// <remarks>Called on the tick thread, including once per active player per tick. Must not allocate or throw.</remarks>
    public interface IAutoAttackTargetValidity
    {
        /// <summary>Whether <paramref name="target"/> can be auto-attacked right now.</summary>
        /// <param name="target">The entity to check.</param>
        /// <returns>True for a living mob in this zone instance.</returns>
        bool IsValidTarget(EntityID target);
    }
}

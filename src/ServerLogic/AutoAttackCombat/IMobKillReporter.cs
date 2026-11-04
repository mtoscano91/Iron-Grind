using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Lets a non-auto damage source (the Skill System) report a mob kill so the combat service raises the same
    /// Tier 2 <c>OnMobDied</c> as an auto-attack kill (ADR-010 Decision 3 and note 2026-11-04; Skill System Story 007 Decision D).
    /// </summary>
    /// <remarks>
    /// Tick thread. Implementations must not allocate. <c>OnMobDied</c> subscribers run synchronously inside
    /// <see cref="ReportKill"/>, before the caster's cooldown write, so they must not cast skills: that could
    /// double-cast in one tick.
    /// </remarks>
    public interface IMobKillReporter
    {
        /// <summary>Reports that <paramref name="killer"/> killed <paramref name="target"/>.</summary>
        /// <param name="killer">The killing player.</param>
        /// <param name="target">The killed mob.</param>
        /// <param name="deathPosition">Where the mob died, in world units.</param>
        void ReportKill(EntityID killer, EntityID target, Vector3 deathPosition);
    }
}

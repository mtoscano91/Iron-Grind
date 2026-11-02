using System;
using IronGrind.Events;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// The combat broadcast surface (ADR-010 Decision 3). Auto-Attack Combat Story 004.
    /// </summary>
    public interface ICombatService
    {
        /// <summary>
        /// Tier 2 broadcast raised once per kill, after the damage is applied, for loot, party kill credit and respawn.
        /// </summary>
        /// <remarks>
        /// Subscribers use named methods, subscribe once at construction and never use lambdas. XP is NOT delivered through
        /// this event: it is a direct call (ADR-010 note 2026-10-30), so there must be no XP subscriber. A subscriber
        /// that throws is logged; it does not break the tick or stop the other subscribers.
        /// </remarks>
        event Action<MobDeathContext> OnMobDied;
    }
}

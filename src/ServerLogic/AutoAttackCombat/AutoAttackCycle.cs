using System;
using IronGrind.Networking;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// Attack cycle length in whole server ticks (design/gdd/auto-attack-combat.md F-1, Rules 5-7, EC-11 to EC-18;
    /// TR-aa-002). Auto-Attack Combat Story 002.
    /// </summary>
    /// <remarks>
    /// Pure and allocation-free. The result is
    /// <c>ceil(TICK_RATE_HZ / max(multiplier, 0.5))</c>: the tick rate is divided by the multiplier rather than
    /// computing <c>(1 / m) * TICK_RATE_HZ</c>, which avoids float error at exact boundaries (1.25 gives exactly 16).
    /// </remarks>
    /// <example>
    /// <code>
    /// int ticks = AutoAttackCycle.ComputeCycleTicks(1.3f);   // 16
    /// </code>
    /// </example>
    public static class AutoAttackCycle
    {
        /// <summary>Lowest attack speed multiplier the cycle honours; anything below it (or not a number) is raised to it.</summary>
        public const float MIN_ATTACK_SPEED_MULTIPLIER = 0.5f;

        /// <summary>Computes the cycle length in ticks for an attack speed multiplier.</summary>
        /// <param name="attackSpeedMultiplier">The raw multiplier; 0, negatives, values under 0.5 and NaN clamp to 0.5.</param>
        /// <returns>The cycle length, at least 1. No exception and no log for any input.</returns>
        public static int ComputeCycleTicks(float attackSpeedMultiplier)
        {
            float m = attackSpeedMultiplier;
            if (!(m >= MIN_ATTACK_SPEED_MULTIPLIER))
            {
                m = MIN_ATTACK_SPEED_MULTIPLIER;
            }

            double ticks = Math.Ceiling(ServerTickLoop.TICK_RATE_HZ / (double)m);
            // +Infinity gives 0; after the clamp the result is at most TICK_RATE_HZ / 0.5, so int cannot overflow.
            return ticks < 1.0 ? 1 : (int)ticks;
        }
    }
}

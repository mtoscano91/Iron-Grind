using IronGrind.Networking;

namespace IronGrind.StatusEffects
{
    /// <summary>Pure helpers for Status Effects (status-effects.md F-SE-1).</summary>
    public static class StatusEffectsMath
    {
        /// <summary>
        /// Converts seconds to ticks: floor(seconds x <see cref="ServerTickLoop.TICK_RATE_HZ"/>).
        /// Negative, zero or NaN input returns 0; values too large for <see cref="uint"/> saturate at
        /// <see cref="uint.MaxValue"/>. Callers validate the result (Story 002).
        /// </summary>
        /// <param name="seconds">Duration in seconds.</param>
        /// <returns>Whole ticks, rounded down.</returns>
        public static uint SecondsToTicks(float seconds)
        {
            if (!(seconds > 0f))
                return 0u;

            double ticks = System.Math.Floor((double)seconds * ServerTickLoop.TICK_RATE_HZ);
            if (ticks >= uint.MaxValue)
                return uint.MaxValue;
            return (uint)ticks;
        }

        /// <summary>True when <paramref name="ticks"/> is in [1, <see cref="StatusEffectsConstants.MAX_DURATION_TICKS"/>].</summary>
        /// <param name="ticks">Duration in ticks.</param>
        public static bool IsWithinAuthoringCeiling(uint ticks)
        {
            return ticks >= 1u && ticks <= StatusEffectsConstants.MAX_DURATION_TICKS;
        }
    }
}

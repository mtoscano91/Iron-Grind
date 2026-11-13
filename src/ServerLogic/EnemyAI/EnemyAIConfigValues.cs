using System;
using System.Globalization;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// The Enemy AI global knobs as the systems take them (enemy-ai.md Tuning Knobs). Every value is inside its safe
    /// range: <see cref="Create"/> and <see cref="FromAsset"/> clamp an out-of-range value to the nearest bound and log
    /// one warning per clamped knob; a non-finite float becomes the default with one warning. In-range values log nothing.
    /// </summary>
    /// <remarks>
    /// Build values only with <see cref="Defaults"/>, <see cref="Create"/> or <see cref="FromAsset"/>, never with
    /// <c>default(EnemyAIConfigValues)</c> (every knob 0, outside the range).
    /// </remarks>
    /// <example>
    /// <code>
    /// EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(asset);
    /// </code>
    /// </example>
    public readonly struct EnemyAIConfigValues
    {
        /// <summary>AGGRO_SCAN_INTERVAL_TICKS default.</summary>
        public const int AGGRO_SCAN_INTERVAL_TICKS_DEFAULT = 5;
        /// <summary>AGGRO_SCAN_INTERVAL_TICKS safe range minimum.</summary>
        public const int AGGRO_SCAN_INTERVAL_TICKS_MIN = 1;
        /// <summary>AGGRO_SCAN_INTERVAL_TICKS safe range maximum.</summary>
        public const int AGGRO_SCAN_INTERVAL_TICKS_MAX = 10;

        /// <summary>PURSUIT_REPOSITION_THRESHOLD default, in metres.</summary>
        public const float PURSUIT_REPOSITION_THRESHOLD_DEFAULT = 1.0f;
        /// <summary>PURSUIT_REPOSITION_THRESHOLD safe range minimum.</summary>
        public const float PURSUIT_REPOSITION_THRESHOLD_MIN = 0.5f;
        /// <summary>PURSUIT_REPOSITION_THRESHOLD safe range maximum.</summary>
        public const float PURSUIT_REPOSITION_THRESHOLD_MAX = 3.0f;

        /// <summary>RETURN_ARRIVAL_THRESHOLD default, in metres.</summary>
        public const float RETURN_ARRIVAL_THRESHOLD_DEFAULT = 0.5f;
        /// <summary>RETURN_ARRIVAL_THRESHOLD safe range minimum.</summary>
        public const float RETURN_ARRIVAL_THRESHOLD_MIN = 0.25f;
        /// <summary>RETURN_ARRIVAL_THRESHOLD safe range maximum.</summary>
        public const float RETURN_ARRIVAL_THRESHOLD_MAX = 2.0f;

        /// <summary>DEATH_LINGER_TICKS default.</summary>
        public const int DEATH_LINGER_TICKS_DEFAULT = 30;
        /// <summary>DEATH_LINGER_TICKS safe range minimum.</summary>
        public const int DEATH_LINGER_TICKS_MIN = 10;
        /// <summary>DEATH_LINGER_TICKS safe range maximum.</summary>
        public const int DEATH_LINGER_TICKS_MAX = 100;

        private const string LOG_PREFIX = "[EnemyAIConfig] ";

        /// <summary>The defaults of every knob (code constant for tests and the no-asset composition path).</summary>
        public static EnemyAIConfigValues Defaults => new EnemyAIConfigValues(
            AGGRO_SCAN_INTERVAL_TICKS_DEFAULT, PURSUIT_REPOSITION_THRESHOLD_DEFAULT,
            RETURN_ARRIVAL_THRESHOLD_DEFAULT, DEATH_LINGER_TICKS_DEFAULT);

        private EnemyAIConfigValues(int aggroScanIntervalTicks, float pursuitRepositionThreshold,
            float returnArrivalThreshold, int deathLingerTicks)
        {
            AggroScanIntervalTicks = aggroScanIntervalTicks;
            PursuitRepositionThreshold = pursuitRepositionThreshold;
            ReturnArrivalThreshold = returnArrivalThreshold;
            DeathLingerTicks = deathLingerTicks;
        }

        /// <summary>AGGRO_SCAN_INTERVAL_TICKS: ticks between aggro scans of an idle mob, in [1, 10].</summary>
        public int AggroScanIntervalTicks { get; }

        /// <summary>PURSUIT_REPOSITION_THRESHOLD: target movement in metres before a pursuer re-paths, in [0.5, 3.0].</summary>
        public float PursuitRepositionThreshold { get; }

        /// <summary>RETURN_ARRIVAL_THRESHOLD: distance in metres from home that counts as arrived, in [0.25, 2.0].</summary>
        public float ReturnArrivalThreshold { get; }

        /// <summary>DEATH_LINGER_TICKS: ticks a dead mob stays before release, in [10, 100].</summary>
        public int DeathLingerTicks { get; }

        /// <summary>Builds the values, clamping each into its range with one warning per clamped knob.</summary>
        /// <param name="aggroScanIntervalTicks">AGGRO_SCAN_INTERVAL_TICKS.</param>
        /// <param name="pursuitRepositionThreshold">PURSUIT_REPOSITION_THRESHOLD.</param>
        /// <param name="returnArrivalThreshold">RETURN_ARRIVAL_THRESHOLD.</param>
        /// <param name="deathLingerTicks">DEATH_LINGER_TICKS.</param>
        /// <returns>The values, every knob inside its range.</returns>
        public static EnemyAIConfigValues Create(int aggroScanIntervalTicks, float pursuitRepositionThreshold,
            float returnArrivalThreshold, int deathLingerTicks)
        {
            return new EnemyAIConfigValues(
                ClampKnob("AGGRO_SCAN_INTERVAL_TICKS", aggroScanIntervalTicks,
                    AGGRO_SCAN_INTERVAL_TICKS_MIN, AGGRO_SCAN_INTERVAL_TICKS_MAX),
                ClampKnob("PURSUIT_REPOSITION_THRESHOLD", pursuitRepositionThreshold,
                    PURSUIT_REPOSITION_THRESHOLD_MIN, PURSUIT_REPOSITION_THRESHOLD_MAX,
                    PURSUIT_REPOSITION_THRESHOLD_DEFAULT),
                ClampKnob("RETURN_ARRIVAL_THRESHOLD", returnArrivalThreshold,
                    RETURN_ARRIVAL_THRESHOLD_MIN, RETURN_ARRIVAL_THRESHOLD_MAX, RETURN_ARRIVAL_THRESHOLD_DEFAULT),
                ClampKnob("DEATH_LINGER_TICKS", deathLingerTicks,
                    DEATH_LINGER_TICKS_MIN, DEATH_LINGER_TICKS_MAX));
        }

        /// <summary>Builds the values from an asset, with the same clamping and warnings as <see cref="Create"/>.</summary>
        /// <param name="asset">The authored asset; not null.</param>
        /// <returns>The values, every knob inside its range.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        public static EnemyAIConfigValues FromAsset(EnemyAIConfig asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            return Create(asset.AggroScanIntervalTicks, asset.PursuitRepositionThreshold,
                asset.ReturnArrivalThreshold, asset.DeathLingerTicks);
        }

        private static float ClampKnob(string knob, float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Debug.LogWarning(LOG_PREFIX + knob + " is not finite; using default " + Format(fallback));
                return fallback;
            }

            if (value < min || value > max)
            {
                float clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", "
                    + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        private static int ClampKnob(string knob, int value, int min, int max)
        {
            if (value < min || value > max)
            {
                int clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", "
                    + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

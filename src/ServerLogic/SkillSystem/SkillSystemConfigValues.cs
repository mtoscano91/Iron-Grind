using System;
using System.Globalization;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// The Skill System knobs as the systems take them (skill-system.md Tuning Knobs). Every value is inside its
    /// safe range: <see cref="Create"/> and <see cref="FromAsset"/> clamp an out-of-range value to the nearest bound
    /// and log one warning per clamped knob; a NaN becomes the default with one warning. In-range values log nothing.
    /// </summary>
    /// <remarks>
    /// Build values only with <see cref="Default"/>, <see cref="Create"/> or <see cref="FromAsset"/>, never with
    /// <c>default(SkillSystemConfigValues)</c> (every knob 0, outside the range).
    /// </remarks>
    public readonly struct SkillSystemConfigValues
    {
        /// <summary>MANA_COST_MULTIPLIER default.</summary>
        public const float MANA_COST_MULTIPLIER_DEFAULT = 1.0f;
        /// <summary>MANA_COST_MULTIPLIER safe range minimum.</summary>
        public const float MANA_COST_MULTIPLIER_MIN = 0.5f;
        /// <summary>MANA_COST_MULTIPLIER safe range maximum.</summary>
        public const float MANA_COST_MULTIPLIER_MAX = 2.0f;

        /// <summary>CAST_STALE_TICK_TOLERANCE default, in ticks.</summary>
        public const int CAST_STALE_TICK_TOLERANCE_DEFAULT = 5;
        /// <summary>CAST_STALE_TICK_TOLERANCE safe range minimum.</summary>
        public const int CAST_STALE_TICK_TOLERANCE_MIN = 2;
        /// <summary>CAST_STALE_TICK_TOLERANCE safe range maximum.</summary>
        public const int CAST_STALE_TICK_TOLERANCE_MAX = 10;

        private const string LOG_PREFIX = "[SkillSystemConfig] ";

        /// <summary>The defaults of both knobs.</summary>
        public static readonly SkillSystemConfigValues Default = new SkillSystemConfigValues(
            MANA_COST_MULTIPLIER_DEFAULT, CAST_STALE_TICK_TOLERANCE_DEFAULT);

        private SkillSystemConfigValues(float manaCostMultiplier, int castStaleTickTolerance)
        {
            ManaCostMultiplier = manaCostMultiplier;
            CastStaleTickTolerance = castStaleTickTolerance;
        }

        /// <summary>Global mana cost multiplier, in [0.5, 2.0].</summary>
        public float ManaCostMultiplier { get; }

        /// <summary>How many ticks behind the server tick a cast may be, in [2, 10].</summary>
        public int CastStaleTickTolerance { get; }

        /// <summary>Builds the values, clamping each into its range with one warning per clamped knob.</summary>
        /// <param name="manaCostMultiplier">MANA_COST_MULTIPLIER.</param>
        /// <param name="castStaleTickTolerance">CAST_STALE_TICK_TOLERANCE.</param>
        /// <returns>The values, every knob inside its range.</returns>
        public static SkillSystemConfigValues Create(float manaCostMultiplier, int castStaleTickTolerance)
        {
            return new SkillSystemConfigValues(
                ClampKnob("MANA_COST_MULTIPLIER", manaCostMultiplier,
                    MANA_COST_MULTIPLIER_MIN, MANA_COST_MULTIPLIER_MAX, MANA_COST_MULTIPLIER_DEFAULT),
                ClampKnob("CAST_STALE_TICK_TOLERANCE", castStaleTickTolerance,
                    CAST_STALE_TICK_TOLERANCE_MIN, CAST_STALE_TICK_TOLERANCE_MAX));
        }

        /// <summary>Builds the values from an asset, with the same clamping and warnings as <see cref="Create"/>.</summary>
        /// <param name="asset">The authored asset; not null.</param>
        /// <returns>The values, every knob inside its range.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        public static SkillSystemConfigValues FromAsset(SkillSystemConfig asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            return Create(asset.ManaCostMultiplier, asset.CastStaleTickTolerance);
        }

        private static float ClampKnob(string knob, float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value))
            {
                Debug.LogWarning(LOG_PREFIX + knob + " is NaN; using default " + Format(fallback));
                return fallback;
            }

            if (value < min || value > max)
            {
                float clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", " + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        private static int ClampKnob(string knob, int value, int min, int max)
        {
            if (value < min || value > max)
            {
                int clamped = value < min ? min : max;
                Debug.LogWarning(LOG_PREFIX + knob + " " + Format(value) + " is outside [" + Format(min) + ", " + Format(max) + "]; using " + Format(clamped));
                return clamped;
            }

            return value;
        }

        private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

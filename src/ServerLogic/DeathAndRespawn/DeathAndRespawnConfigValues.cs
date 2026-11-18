using System.Globalization;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// The Death and Respawn knobs as the service takes them (death-and-respawn.md F-DR-1, EC-DR-5, EC-DR-9;
    /// Death and Respawn Story 001, TR-dr-001). An out-of-range value is refused, never clamped:
    /// <see cref="Validate"/> logs one error per bad knob (name, value, range), plus a separate error if the F-DR-1
    /// tick floor fails, and returns false.
    /// </summary>
    /// <remarks>
    /// <c>default(DeathAndRespawnConfigValues)</c> has every knob 0 and fails <see cref="Validate"/>; use
    /// <see cref="Defaults"/>, the constructor or <see cref="FromAsset"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var values = new DeathAndRespawnConfigValues(7.0f, 40);
    /// if (values.Validate()) { int ticks = values.RespawnDelayTicks; }
    /// </code>
    /// </example>
    public readonly struct DeathAndRespawnConfigValues
    {
        /// <summary>RESPAWN_DELAY_SECONDS default.</summary>
        public const float RESPAWN_DELAY_SECONDS_DEFAULT = 7.0f;
        /// <summary>RESPAWN_DELAY_SECONDS safe range minimum.</summary>
        public const float RESPAWN_DELAY_SECONDS_MIN = 5.0f;
        /// <summary>RESPAWN_DELAY_SECONDS safe range maximum.</summary>
        public const float RESPAWN_DELAY_SECONDS_MAX = 10.0f;

        /// <summary>RESPAWN_ANCHOR_RETRY_TICKS default.</summary>
        public const int RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT = 40;
        /// <summary>RESPAWN_ANCHOR_RETRY_TICKS safe range minimum.</summary>
        public const int RESPAWN_ANCHOR_RETRY_TICKS_MIN = 20;
        /// <summary>RESPAWN_ANCHOR_RETRY_TICKS safe range maximum.</summary>
        public const int RESPAWN_ANCHOR_RETRY_TICKS_MAX = 200;

        /// <summary>F-DR-1 startup assertion floor for <see cref="RespawnDelayTicks"/>.</summary>
        public const int RESPAWN_DELAY_TICKS_MIN = 100;

        private const string LOG_PREFIX = "[DeathAndRespawnConfig] ";

        /// <summary>Builds values as given; no range check (call <see cref="Validate"/>).</summary>
        /// <param name="respawnDelaySeconds">RESPAWN_DELAY_SECONDS.</param>
        /// <param name="respawnAnchorRetryTicks">RESPAWN_ANCHOR_RETRY_TICKS.</param>
        public DeathAndRespawnConfigValues(float respawnDelaySeconds, int respawnAnchorRetryTicks)
        {
            RespawnDelaySeconds = respawnDelaySeconds;
            RespawnAnchorRetryTicks = respawnAnchorRetryTicks;
        }

        /// <summary>The defaults of every knob.</summary>
        public static DeathAndRespawnConfigValues Defaults => new DeathAndRespawnConfigValues(
            RESPAWN_DELAY_SECONDS_DEFAULT, RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT);

        /// <summary>RESPAWN_DELAY_SECONDS.</summary>
        public float RespawnDelaySeconds { get; }

        /// <summary>RESPAWN_ANCHOR_RETRY_TICKS.</summary>
        public int RespawnAnchorRetryTicks { get; }

        /// <summary>
        /// RESPAWN_DELAY_TICKS (F-DR-1): <see cref="RespawnDelaySeconds"/> converted with <see cref="ToTicks"/>.
        /// Only meaningful after <see cref="Validate"/> returned true (a NaN or infinite delay gives an undefined int).
        /// </summary>
        public int RespawnDelayTicks => ToTicks(RespawnDelaySeconds);

        /// <summary>
        /// F-DR-1 conversion: <c>Mathf.RoundToInt(seconds * TICK_RATE_HZ)</c>; never truncates. Rounds to nearest with
        /// ties to even (an exact x.5 product rounds to the even int), so author delays on a 0.05 s grid.
        /// Only meaningful for a finite, validated delay.
        /// </summary>
        /// <param name="seconds">A duration in seconds.</param>
        /// <returns>The duration in server ticks, rounded to nearest (ties to even).</returns>
        public static int ToTicks(float seconds)
        {
            return Mathf.RoundToInt(seconds * (float)ServerTickLoop.TICK_RATE_HZ);
        }

        /// <summary>Builds values from an asset.</summary>
        /// <param name="asset">The config asset; null yields <see cref="Defaults"/> with one warning.</param>
        /// <returns>The asset's values as authored (not validated), or the defaults for a null asset.</returns>
        public static DeathAndRespawnConfigValues FromAsset(DeathAndRespawnConfig asset)
        {
            if (asset == null)
            {
                Debug.LogWarning(LOG_PREFIX + "no config asset; using defaults.");
                return Defaults;
            }

            return new DeathAndRespawnConfigValues(asset.RespawnDelaySeconds, asset.RespawnAnchorRetryTicks);
        }

        /// <summary>
        /// Checks every knob against its safe range (NaN and infinities fail) and, when the delay passed,
        /// asserts <see cref="RespawnDelayTicks"/> &gt;= <see cref="RESPAWN_DELAY_TICKS_MIN"/> (F-DR-1).
        /// Logs one error per failing knob (name, value, range), plus one separate error if the tick floor fails
        /// (unreachable at 20 Hz: 5.0 s is exactly 100 ticks; a guard against a future tick rate change). Never clamps.
        /// </summary>
        /// <returns>True when every knob is valid.</returns>
        public bool Validate()
        {
            bool secondsOk = CheckSeconds("RESPAWN_DELAY_SECONDS", RespawnDelaySeconds,
                RESPAWN_DELAY_SECONDS_MIN, RESPAWN_DELAY_SECONDS_MAX);
            bool retryOk = CheckRange("RESPAWN_ANCHOR_RETRY_TICKS", RespawnAnchorRetryTicks,
                RESPAWN_ANCHOR_RETRY_TICKS_MIN, RESPAWN_ANCHOR_RETRY_TICKS_MAX);
            bool ok = secondsOk && retryOk;

            if (secondsOk && !CheckDelayTicksFloor(RespawnDelayTicks))
            {
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// The F-DR-1 startup assertion: <paramref name="ticks"/> &gt;= <see cref="RESPAWN_DELAY_TICKS_MIN"/>.
        /// Logs one error (value and minimum) when it fails.
        /// </summary>
        /// <param name="ticks">A derived RESPAWN_DELAY_TICKS.</param>
        /// <returns>True when the floor holds.</returns>
        internal static bool CheckDelayTicksFloor(int ticks)
        {
            if (ticks >= RESPAWN_DELAY_TICKS_MIN)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + "RESPAWN_DELAY_TICKS = " + ticks.ToString(CultureInfo.InvariantCulture)
                + " is below the minimum " + RESPAWN_DELAY_TICKS_MIN.ToString(CultureInfo.InvariantCulture) + ".");
            return false;
        }

        private static bool CheckSeconds(string knob, float value, float min, float max)
        {
            if (!(float.IsNaN(value) || value < min || value > max))
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + knob + " = " + value.ToString(CultureInfo.InvariantCulture)
                + " is outside [" + min.ToString(CultureInfo.InvariantCulture) + ", "
                + max.ToString(CultureInfo.InvariantCulture) + "].");
            return false;
        }

        private static bool CheckRange(string knob, int value, int min, int max)
        {
            if (value >= min && value <= max)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + knob + " = " + value.ToString(CultureInfo.InvariantCulture)
                + " is outside [" + min.ToString(CultureInfo.InvariantCulture) + ", "
                + max.ToString(CultureInfo.InvariantCulture) + "].");
            return false;
        }
    }
}

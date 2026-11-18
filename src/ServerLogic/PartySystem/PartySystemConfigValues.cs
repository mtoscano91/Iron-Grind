using System.Globalization;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The Party System knobs as the service takes them (party-system.md Tuning Knobs, F-PS-1; Party System
    /// Story 001, TR-party-005). An out-of-range value is refused, never clamped: <see cref="Validate"/> logs one
    /// error per failing field and returns false.
    /// </summary>
    /// <remarks>
    /// <c>default(PartySystemConfigValues)</c> has every knob 0 and fails <see cref="Validate"/>; use
    /// <see cref="Defaults"/>, the constructor or <see cref="FromAsset"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var values = PartySystemConfigValues.Defaults;
    /// if (values.Validate()) { int ticks = values.InviteTimeoutTicks; }
    /// </code>
    /// </example>
    public readonly struct PartySystemConfigValues
    {
        /// <summary>MAX_PARTY_SIZE default.</summary>
        public const int MAX_PARTY_SIZE_DEFAULT = 4;
        /// <summary>MAX_PARTY_SIZE safe range minimum.</summary>
        public const int MAX_PARTY_SIZE_MIN = 2;
        /// <summary>MAX_PARTY_SIZE safe range maximum (PartyStateUpdate carries a fixed 4 member slots).</summary>
        public const int MAX_PARTY_SIZE_MAX = 4;

        /// <summary>XP_PARTY_DEDUCTION_RATE default.</summary>
        public const float XP_PARTY_DEDUCTION_RATE_DEFAULT = 0.10f;
        /// <summary>XP_PARTY_DEDUCTION_RATE safe range minimum.</summary>
        public const float XP_PARTY_DEDUCTION_RATE_MIN = 0.00f;
        /// <summary>XP_PARTY_DEDUCTION_RATE safe range maximum.</summary>
        public const float XP_PARTY_DEDUCTION_RATE_MAX = 0.20f;

        /// <summary>LEVEL_RANGE_GATE default.</summary>
        public const int LEVEL_RANGE_GATE_DEFAULT = 5;
        /// <summary>LEVEL_RANGE_GATE safe range minimum.</summary>
        public const int LEVEL_RANGE_GATE_MIN = 0;
        /// <summary>LEVEL_RANGE_GATE safe range maximum.</summary>
        public const int LEVEL_RANGE_GATE_MAX = 15;

        /// <summary>INVITE_TIMEOUT_SECONDS default.</summary>
        public const float INVITE_TIMEOUT_SECONDS_DEFAULT = 60.0f;
        /// <summary>INVITE_TIMEOUT_SECONDS safe range minimum.</summary>
        public const float INVITE_TIMEOUT_SECONDS_MIN = 30.0f;
        /// <summary>INVITE_TIMEOUT_SECONDS safe range maximum.</summary>
        public const float INVITE_TIMEOUT_SECONDS_MAX = 120.0f;

        /// <summary>INVITE_RETRY_SECONDS default.</summary>
        public const float INVITE_RETRY_SECONDS_DEFAULT = 5.0f;
        /// <summary>INVITE_RETRY_SECONDS safe range minimum.</summary>
        public const float INVITE_RETRY_SECONDS_MIN = 1.0f;
        /// <summary>INVITE_RETRY_SECONDS safe range maximum.</summary>
        public const float INVITE_RETRY_SECONDS_MAX = 10.0f;

        /// <summary>RECONNECT_GRACE_SECONDS default.</summary>
        public const float RECONNECT_GRACE_SECONDS_DEFAULT = 5.0f;
        /// <summary>RECONNECT_GRACE_SECONDS safe range minimum.</summary>
        public const float RECONNECT_GRACE_SECONDS_MIN = 2.0f;
        /// <summary>RECONNECT_GRACE_SECONDS safe range maximum.</summary>
        public const float RECONNECT_GRACE_SECONDS_MAX = 30.0f;

        /// <summary>HP_DELTA_THRESHOLD_FRACTION default.</summary>
        public const float HP_DELTA_THRESHOLD_FRACTION_DEFAULT = 0.05f;
        /// <summary>HP_DELTA_THRESHOLD_FRACTION safe range minimum.</summary>
        public const float HP_DELTA_THRESHOLD_FRACTION_MIN = 0.00f;
        /// <summary>HP_DELTA_THRESHOLD_FRACTION safe range maximum.</summary>
        public const float HP_DELTA_THRESHOLD_FRACTION_MAX = 0.20f;

        /// <summary>DISBAND_TIMEOUT_SECONDS default.</summary>
        public const float DISBAND_TIMEOUT_SECONDS_DEFAULT = 10.0f;
        /// <summary>DISBAND_TIMEOUT_SECONDS safe range minimum.</summary>
        public const float DISBAND_TIMEOUT_SECONDS_MIN = 5.0f;
        /// <summary>DISBAND_TIMEOUT_SECONDS safe range maximum.</summary>
        public const float DISBAND_TIMEOUT_SECONDS_MAX = 30.0f;

        /// <summary>
        /// The joint constraint ceiling: rate x (size - 1) must stay below this (multiplier above 0).
        /// </summary>
        public const float JOINT_DEDUCTION_LIMIT = 1.0f;

        private const string LOG_PREFIX = "[PartySystemConfig] ";

        /// <summary>Builds values as given; no range check (call <see cref="Validate"/>).</summary>
        /// <param name="maxPartySize">MAX_PARTY_SIZE.</param>
        /// <param name="xpPartyDeductionRate">XP_PARTY_DEDUCTION_RATE.</param>
        /// <param name="levelRangeGate">LEVEL_RANGE_GATE.</param>
        /// <param name="inviteTimeoutSeconds">INVITE_TIMEOUT_SECONDS.</param>
        /// <param name="inviteRetrySeconds">INVITE_RETRY_SECONDS.</param>
        /// <param name="reconnectGraceSeconds">RECONNECT_GRACE_SECONDS.</param>
        /// <param name="hpDeltaThresholdFraction">HP_DELTA_THRESHOLD_FRACTION.</param>
        /// <param name="disbandTimeoutSeconds">DISBAND_TIMEOUT_SECONDS.</param>
        public PartySystemConfigValues(
            int maxPartySize,
            float xpPartyDeductionRate,
            int levelRangeGate,
            float inviteTimeoutSeconds,
            float inviteRetrySeconds,
            float reconnectGraceSeconds,
            float hpDeltaThresholdFraction,
            float disbandTimeoutSeconds)
        {
            MaxPartySize = maxPartySize;
            XpPartyDeductionRate = xpPartyDeductionRate;
            LevelRangeGate = levelRangeGate;
            InviteTimeoutSeconds = inviteTimeoutSeconds;
            InviteRetrySeconds = inviteRetrySeconds;
            ReconnectGraceSeconds = reconnectGraceSeconds;
            HpDeltaThresholdFraction = hpDeltaThresholdFraction;
            DisbandTimeoutSeconds = disbandTimeoutSeconds;
        }

        /// <summary>The defaults of every knob.</summary>
        public static PartySystemConfigValues Defaults => new PartySystemConfigValues(
            MAX_PARTY_SIZE_DEFAULT,
            XP_PARTY_DEDUCTION_RATE_DEFAULT,
            LEVEL_RANGE_GATE_DEFAULT,
            INVITE_TIMEOUT_SECONDS_DEFAULT,
            INVITE_RETRY_SECONDS_DEFAULT,
            RECONNECT_GRACE_SECONDS_DEFAULT,
            HP_DELTA_THRESHOLD_FRACTION_DEFAULT,
            DISBAND_TIMEOUT_SECONDS_DEFAULT);

        /// <summary>MAX_PARTY_SIZE.</summary>
        public int MaxPartySize { get; }

        /// <summary>XP_PARTY_DEDUCTION_RATE.</summary>
        public float XpPartyDeductionRate { get; }

        /// <summary>LEVEL_RANGE_GATE.</summary>
        public int LevelRangeGate { get; }

        /// <summary>INVITE_TIMEOUT_SECONDS.</summary>
        public float InviteTimeoutSeconds { get; }

        /// <summary>INVITE_RETRY_SECONDS.</summary>
        public float InviteRetrySeconds { get; }

        /// <summary>RECONNECT_GRACE_SECONDS.</summary>
        public float ReconnectGraceSeconds { get; }

        /// <summary>HP_DELTA_THRESHOLD_FRACTION.</summary>
        public float HpDeltaThresholdFraction { get; }

        /// <summary>DISBAND_TIMEOUT_SECONDS.</summary>
        public float DisbandTimeoutSeconds { get; }

        /// <summary><see cref="InviteTimeoutSeconds"/> in server ticks.</summary>
        public int InviteTimeoutTicks => ToTicks(InviteTimeoutSeconds);

        /// <summary><see cref="InviteRetrySeconds"/> in server ticks.</summary>
        public int InviteRetryTicks => ToTicks(InviteRetrySeconds);

        /// <summary><see cref="ReconnectGraceSeconds"/> in server ticks.</summary>
        public int ReconnectGraceTicks => ToTicks(ReconnectGraceSeconds);

        /// <summary><see cref="DisbandTimeoutSeconds"/> in server ticks.</summary>
        public int DisbandTimeoutTicks => ToTicks(DisbandTimeoutSeconds);

        /// <summary>
        /// Seconds to ticks: <c>Mathf.RoundToInt(seconds * TICK_RATE_HZ)</c> (ties to even). Only meaningful for a
        /// finite, validated duration.
        /// </summary>
        /// <param name="seconds">A duration in seconds.</param>
        /// <returns>The duration in server ticks.</returns>
        public static int ToTicks(float seconds)
        {
            return Mathf.RoundToInt(seconds * (float)ServerTickLoop.TICK_RATE_HZ);
        }

        /// <summary>Builds values from an asset.</summary>
        /// <param name="asset">The config asset; null yields <see cref="Defaults"/> with one warning.</param>
        /// <returns>The asset's values as authored (not validated), or the defaults for a null asset.</returns>
        public static PartySystemConfigValues FromAsset(PartySystemConfig asset)
        {
            if (asset == null)
            {
                Debug.LogWarning(LOG_PREFIX + "no config asset; using defaults.");
                return Defaults;
            }

            return new PartySystemConfigValues(
                asset.MaxPartySize,
                asset.XpPartyDeductionRate,
                asset.LevelRangeGate,
                asset.InviteTimeoutSeconds,
                asset.InviteRetrySeconds,
                asset.ReconnectGraceSeconds,
                asset.HpDeltaThresholdFraction,
                asset.DisbandTimeoutSeconds);
        }

        /// <summary>
        /// Checks every knob against its safe range (NaN and infinities fail), the retry-below-timeout rule and the
        /// joint deduction constraint (the two cross rules are skipped for a NaN operand, already reported by its range
        /// check). Logs one error per failing field; never clamps.
        /// </summary>
        /// <returns>True when every knob is valid.</returns>
        public bool Validate()
        {
            bool sizeOk = CheckInt("MAX_PARTY_SIZE", MaxPartySize, MAX_PARTY_SIZE_MIN, MAX_PARTY_SIZE_MAX,
                MaxPartySize > MAX_PARTY_SIZE_MAX
                    ? " PartyStateUpdate has a fixed 4-slot wire schema, so a party cannot exceed 4 members."
                    : string.Empty);
            bool rateOk = CheckFloat("XP_PARTY_DEDUCTION_RATE", XpPartyDeductionRate,
                XP_PARTY_DEDUCTION_RATE_MIN, XP_PARTY_DEDUCTION_RATE_MAX);
            bool gateOk = CheckInt("LEVEL_RANGE_GATE", LevelRangeGate, LEVEL_RANGE_GATE_MIN, LEVEL_RANGE_GATE_MAX,
                string.Empty);
            bool timeoutOk = CheckFloat("INVITE_TIMEOUT_SECONDS", InviteTimeoutSeconds,
                INVITE_TIMEOUT_SECONDS_MIN, INVITE_TIMEOUT_SECONDS_MAX);
            bool retryOk = CheckFloat("INVITE_RETRY_SECONDS", InviteRetrySeconds,
                INVITE_RETRY_SECONDS_MIN, INVITE_RETRY_SECONDS_MAX);
            bool graceOk = CheckFloat("RECONNECT_GRACE_SECONDS", ReconnectGraceSeconds,
                RECONNECT_GRACE_SECONDS_MIN, RECONNECT_GRACE_SECONDS_MAX);
            bool hpOk = CheckFloat("HP_DELTA_THRESHOLD_FRACTION", HpDeltaThresholdFraction,
                HP_DELTA_THRESHOLD_FRACTION_MIN, HP_DELTA_THRESHOLD_FRACTION_MAX);
            bool disbandOk = CheckFloat("DISBAND_TIMEOUT_SECONDS", DisbandTimeoutSeconds,
                DISBAND_TIMEOUT_SECONDS_MIN, DISBAND_TIMEOUT_SECONDS_MAX);

            bool ok = sizeOk && rateOk && gateOk && timeoutOk && retryOk && graceOk && hpOk && disbandOk;

            if (!float.IsNaN(InviteRetrySeconds) && !float.IsNaN(InviteTimeoutSeconds)
                && !CheckRetryBelowTimeout(InviteRetrySeconds, InviteTimeoutSeconds))
            {
                ok = false;
            }

            if (!float.IsNaN(XpPartyDeductionRate) && !CheckJointDeduction(XpPartyDeductionRate, MaxPartySize))
            {
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// The retry rule: <paramref name="retrySeconds"/> must be below <paramref name="timeoutSeconds"/>.
        /// Logs one error when it is not.
        /// </summary>
        /// <param name="retrySeconds">INVITE_RETRY_SECONDS.</param>
        /// <param name="timeoutSeconds">INVITE_TIMEOUT_SECONDS.</param>
        /// <returns>True when the rule holds.</returns>
        internal static bool CheckRetryBelowTimeout(float retrySeconds, float timeoutSeconds)
        {
            if (retrySeconds < timeoutSeconds)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + "INVITE_RETRY_SECONDS = " + Format(retrySeconds)
                + " must be below INVITE_TIMEOUT_SECONDS = " + Format(timeoutSeconds) + ".");
            return false;
        }

        /// <summary>
        /// The joint constraint: <c>rate x (size - 1)</c> must stay below <see cref="JOINT_DEDUCTION_LIMIT"/> so the
        /// F-PS-1 multiplier stays positive. Logs one error when it does not.
        /// </summary>
        /// <param name="rate">XP_PARTY_DEDUCTION_RATE.</param>
        /// <param name="maxPartySize">MAX_PARTY_SIZE.</param>
        /// <returns>True when the constraint holds.</returns>
        internal static bool CheckJointDeduction(float rate, int maxPartySize)
        {
            float total = rate * (float)(maxPartySize - 1);
            if (total < JOINT_DEDUCTION_LIMIT)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + "XP_PARTY_DEDUCTION_RATE x (MAX_PARTY_SIZE - 1) = " + Format(total)
                + " must be below " + Format(JOINT_DEDUCTION_LIMIT) + ".");
            return false;
        }

        private static string Format(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool CheckFloat(string knob, float value, float min, float max)
        {
            if (!(float.IsNaN(value) || value < min || value > max))
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + knob + " = " + Format(value) + " is outside [" + Format(min) + ", "
                + Format(max) + "].");
            return false;
        }

        private static bool CheckInt(string knob, int value, int min, int max, string suffix)
        {
            if (value >= min && value <= max)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + knob + " = " + value.ToString(CultureInfo.InvariantCulture)
                + " is outside [" + min.ToString(CultureInfo.InvariantCulture) + ", "
                + max.ToString(CultureInfo.InvariantCulture) + "]." + suffix);
            return false;
        }
    }
}

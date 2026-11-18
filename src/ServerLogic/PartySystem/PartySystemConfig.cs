using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Designer-editable Party System knobs (party-system.md Tuning Knobs, F-PS-1; Party System Story 001,
    /// TR-party-005). Read through <see cref="PartySystemConfigValues.FromAsset"/> and refused by
    /// <see cref="PartySystemConfigValues.Validate"/> when out of range (no clamping).
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    [CreateAssetMenu(fileName = "PartySystemConfig", menuName = "IronGrind/Party System Config")]
    public sealed class PartySystemConfig : ScriptableObject
    {
        [SerializeField] private int _maxPartySize = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT;
        [SerializeField] private float _xpPartyDeductionRate = PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT;
        [SerializeField] private int _levelRangeGate = PartySystemConfigValues.LEVEL_RANGE_GATE_DEFAULT;
        [SerializeField] private float _inviteTimeoutSeconds = PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT;
        [SerializeField] private float _inviteRetrySeconds = PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT;
        [SerializeField] private float _reconnectGraceSeconds = PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT;
        [SerializeField] private float _hpDeltaThresholdFraction =
            PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT;
        [SerializeField] private float _disbandTimeoutSeconds = PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT;

        /// <summary>MAX_PARTY_SIZE as authored.</summary>
        public int MaxPartySize => _maxPartySize;

        /// <summary>XP_PARTY_DEDUCTION_RATE as authored.</summary>
        public float XpPartyDeductionRate => _xpPartyDeductionRate;

        /// <summary>LEVEL_RANGE_GATE as authored.</summary>
        public int LevelRangeGate => _levelRangeGate;

        /// <summary>INVITE_TIMEOUT_SECONDS as authored.</summary>
        public float InviteTimeoutSeconds => _inviteTimeoutSeconds;

        /// <summary>INVITE_RETRY_SECONDS as authored.</summary>
        public float InviteRetrySeconds => _inviteRetrySeconds;

        /// <summary>RECONNECT_GRACE_SECONDS as authored.</summary>
        public float ReconnectGraceSeconds => _reconnectGraceSeconds;

        /// <summary>HP_DELTA_THRESHOLD_FRACTION as authored.</summary>
        public float HpDeltaThresholdFraction => _hpDeltaThresholdFraction;

        /// <summary>DISBAND_TIMEOUT_SECONDS as authored.</summary>
        public float DisbandTimeoutSeconds => _disbandTimeoutSeconds;

        /// <summary>Sets every knob as given (no validation); test seam only.</summary>
        internal void SetForTests(PartySystemConfigValues values)
        {
            _maxPartySize = values.MaxPartySize;
            _xpPartyDeductionRate = values.XpPartyDeductionRate;
            _levelRangeGate = values.LevelRangeGate;
            _inviteTimeoutSeconds = values.InviteTimeoutSeconds;
            _inviteRetrySeconds = values.InviteRetrySeconds;
            _reconnectGraceSeconds = values.ReconnectGraceSeconds;
            _hpDeltaThresholdFraction = values.HpDeltaThresholdFraction;
            _disbandTimeoutSeconds = values.DisbandTimeoutSeconds;
        }
    }
}

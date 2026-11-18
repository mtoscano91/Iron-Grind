using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Designer-editable Death and Respawn knobs (death-and-respawn.md Tuning Knobs, F-DR-1; Death and Respawn Story 001,
    /// TR-dr-001). Read through <see cref="DeathAndRespawnConfigValues.FromAsset"/> and refused by
    /// <see cref="DeathAndRespawnConfigValues.Validate"/> when out of range (no clamping, EC-DR-5).
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    [CreateAssetMenu(fileName = "DeathAndRespawnConfig", menuName = "IronGrind/Death And Respawn Config")]
    public sealed class DeathAndRespawnConfig : ScriptableObject
    {
        [SerializeField] private float _respawnDelaySeconds = DeathAndRespawnConfigValues.RESPAWN_DELAY_SECONDS_DEFAULT;
        [SerializeField] private int _respawnAnchorRetryTicks = DeathAndRespawnConfigValues.RESPAWN_ANCHOR_RETRY_TICKS_DEFAULT;

        /// <summary>RESPAWN_DELAY_SECONDS as authored.</summary>
        public float RespawnDelaySeconds => _respawnDelaySeconds;

        /// <summary>RESPAWN_ANCHOR_RETRY_TICKS as authored.</summary>
        public int RespawnAnchorRetryTicks => _respawnAnchorRetryTicks;

        /// <summary>Sets every knob as given (no validation); test seam only.</summary>
        internal void SetForTests(float respawnDelaySeconds, int respawnAnchorRetryTicks)
        {
            _respawnDelaySeconds = respawnDelaySeconds;
            _respawnAnchorRetryTicks = respawnAnchorRetryTicks;
        }
    }
}

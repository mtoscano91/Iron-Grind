using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Designer-editable Navigation knobs (navigation-pathfinding.md Tuning Knobs; Navigation Story 001). Systems take a
    /// <see cref="NavigationConfigValues"/> built by <see cref="NavigationConfigValues.FromAsset"/>; out-of-range values
    /// are rejected by <see cref="NavigationConfigValues.Validate"/>, not clamped.
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    /// <example>
    /// <code>
    /// NavigationConfigValues values = NavigationConfigValues.FromAsset(config);
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "NavigationConfig", menuName = "IronGrind/Navigation Config")]
    public class NavigationConfig : ScriptableObject
    {
        [SerializeField] private int _maxAgentPoolSize = NavigationConfigValues.MAX_AGENT_POOL_SIZE_DEFAULT;
        [SerializeField] private int _maxPathUpdatesPerTick = NavigationConfigValues.MAX_PATH_UPDATES_PER_TICK_DEFAULT;
        [SerializeField] private int _partialPathTimeoutTicks = NavigationConfigValues.PARTIAL_PATH_TIMEOUT_TICKS_DEFAULT;
        [SerializeField] private float _returnSpeedCap = NavigationConfigValues.RETURN_SPEED_CAP_DEFAULT;
        [SerializeField] private float _minValidSpawnFraction = NavigationConfigValues.MIN_VALID_SPAWN_FRACTION_DEFAULT;
        [SerializeField] private int _navStuckRerouteDetectionTicks = NavigationConfigValues.NAV_STUCK_REROUTE_DETECTION_TICKS_DEFAULT;
        [SerializeField] private int _navOffMeshRecoveryTicks = NavigationConfigValues.NAV_OFF_MESH_RECOVERY_TICKS_DEFAULT;
        [SerializeField] private float _spawnSampleRadius = NavigationConfigValues.SPAWN_SAMPLE_RADIUS_DEFAULT;

        /// <summary>MAX_AGENT_POOL_SIZE as authored.</summary>
        public int MaxAgentPoolSize => _maxAgentPoolSize;

        /// <summary>MAX_PATH_UPDATES_PER_TICK as authored.</summary>
        public int MaxPathUpdatesPerTick => _maxPathUpdatesPerTick;

        /// <summary>PARTIAL_PATH_TIMEOUT_TICKS as authored.</summary>
        public int PartialPathTimeoutTicks => _partialPathTimeoutTicks;

        /// <summary>RETURN_SPEED_CAP as authored, in m/s.</summary>
        public float ReturnSpeedCap => _returnSpeedCap;

        /// <summary>MIN_VALID_SPAWN_FRACTION as authored.</summary>
        public float MinValidSpawnFraction => _minValidSpawnFraction;

        /// <summary>NAV_STUCK_REROUTE_DETECTION_TICKS as authored.</summary>
        public int NavStuckRerouteDetectionTicks => _navStuckRerouteDetectionTicks;

        /// <summary>NAV_OFF_MESH_RECOVERY_TICKS as authored.</summary>
        public int NavOffMeshRecoveryTicks => _navOffMeshRecoveryTicks;

        /// <summary>SPAWN_SAMPLE_RADIUS as authored, in world units.</summary>
        public float SpawnSampleRadius => _spawnSampleRadius;

        /// <summary>Sets every knob; test seam only.</summary>
        internal void SetForTesting(NavigationConfigValues values)
        {
            _maxAgentPoolSize = values.MaxAgentPoolSize;
            _maxPathUpdatesPerTick = values.MaxPathUpdatesPerTick;
            _partialPathTimeoutTicks = values.PartialPathTimeoutTicks;
            _returnSpeedCap = values.ReturnSpeedCap;
            _minValidSpawnFraction = values.MinValidSpawnFraction;
            _navStuckRerouteDetectionTicks = values.NavStuckRerouteDetectionTicks;
            _navOffMeshRecoveryTicks = values.NavOffMeshRecoveryTicks;
            _spawnSampleRadius = values.SpawnSampleRadius;
        }

#if UNITY_EDITOR
        // Editor feedback: logs validation errors and warnings. Serialized values are unchanged.
        private void OnValidate()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            NavigationConfigValues.Validate(NavigationConfigValues.FromAsset(this), errors, warnings);
            for (int i = 0; i < errors.Count; i++)
            {
                Debug.LogError("[NavigationConfig] " + errors[i], this);
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[NavigationConfig] " + warnings[i], this);
            }
        }
#endif
    }
}

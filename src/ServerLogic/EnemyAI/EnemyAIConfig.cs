using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Designer-editable Enemy AI global knobs (enemy-ai.md Tuning Knobs, Global Server Constants; Enemy AI Story 002).
    /// Systems take an <see cref="EnemyAIConfigValues"/> built by <see cref="EnemyAIConfigValues.FromAsset"/>, which
    /// clamps out-of-range values. RETURN_SPEED_CAP is owned by Navigation and ATTACK_RANGE_MIN by
    /// <see cref="MobDefinitionValidator"/>; neither is a knob here.
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    /// <example>
    /// <code>
    /// EnemyAIConfigValues values = EnemyAIConfigValues.FromAsset(config);
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "EnemyAIConfig", menuName = "IronGrind/Enemy AI Config")]
    public class EnemyAIConfig : ScriptableObject
    {
        [SerializeField] private int _aggroScanIntervalTicks = EnemyAIConfigValues.AGGRO_SCAN_INTERVAL_TICKS_DEFAULT;
        [SerializeField] private float _pursuitRepositionThreshold = EnemyAIConfigValues.PURSUIT_REPOSITION_THRESHOLD_DEFAULT;
        [SerializeField] private float _returnArrivalThreshold = EnemyAIConfigValues.RETURN_ARRIVAL_THRESHOLD_DEFAULT;
        [SerializeField] private int _deathLingerTicks = EnemyAIConfigValues.DEATH_LINGER_TICKS_DEFAULT;

        /// <summary>AGGRO_SCAN_INTERVAL_TICKS as authored.</summary>
        public int AggroScanIntervalTicks => _aggroScanIntervalTicks;

        /// <summary>PURSUIT_REPOSITION_THRESHOLD as authored, in metres.</summary>
        public float PursuitRepositionThreshold => _pursuitRepositionThreshold;

        /// <summary>RETURN_ARRIVAL_THRESHOLD as authored, in metres.</summary>
        public float ReturnArrivalThreshold => _returnArrivalThreshold;

        /// <summary>DEATH_LINGER_TICKS as authored.</summary>
        public int DeathLingerTicks => _deathLingerTicks;

        /// <summary>Sets every knob as given (no clamping); test seam only.</summary>
        internal void SetForTesting(int aggroScanIntervalTicks, float pursuitRepositionThreshold,
            float returnArrivalThreshold, int deathLingerTicks)
        {
            _aggroScanIntervalTicks = aggroScanIntervalTicks;
            _pursuitRepositionThreshold = pursuitRepositionThreshold;
            _returnArrivalThreshold = returnArrivalThreshold;
            _deathLingerTicks = deathLingerTicks;
        }
    }
}

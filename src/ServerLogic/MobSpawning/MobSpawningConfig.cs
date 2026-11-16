using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Designer-editable Mob Spawning knobs (mob-spawning.md Tuning Knobs; Mob Spawning Story 001).
    /// Read through <see cref="MobSpawningConfigValues.FromAsset"/> and refused by
    /// <see cref="MobSpawningConfigValues.Validate"/> when out of range.
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only (compile error on properties in Unity 6.3).</remarks>
    [CreateAssetMenu(fileName = "MobSpawningConfig", menuName = "IronGrind/Mob Spawning Config")]
    public sealed class MobSpawningConfig : ScriptableObject
    {
        [SerializeField] private int _respawnDelayCommonTicks = MobSpawningConfigValues.RESPAWN_DELAY_COMMON_TICKS_DEFAULT;
        [SerializeField] private int _respawnDelayUncommonTicks = MobSpawningConfigValues.RESPAWN_DELAY_UNCOMMON_TICKS_DEFAULT;
        [SerializeField] private int _respawnDelayRareTicks = MobSpawningConfigValues.RESPAWN_DELAY_RARE_TICKS_DEFAULT;
        [SerializeField] private int _respawnDelayEliteTicks = MobSpawningConfigValues.RESPAWN_DELAY_ELITE_TICKS_DEFAULT;
        [SerializeField] private int _spawnDensityRetryIntervalTicks = MobSpawningConfigValues.SPAWN_DENSITY_RETRY_INTERVAL_TICKS_DEFAULT;

        /// <summary>RESPAWN_DELAY_COMMON_TICKS as authored.</summary>
        public int RespawnDelayCommonTicks => _respawnDelayCommonTicks;

        /// <summary>RESPAWN_DELAY_UNCOMMON_TICKS as authored.</summary>
        public int RespawnDelayUncommonTicks => _respawnDelayUncommonTicks;

        /// <summary>RESPAWN_DELAY_RARE_TICKS as authored.</summary>
        public int RespawnDelayRareTicks => _respawnDelayRareTicks;

        /// <summary>RESPAWN_DELAY_ELITE_TICKS as authored.</summary>
        public int RespawnDelayEliteTicks => _respawnDelayEliteTicks;

        /// <summary>SPAWN_DENSITY_RETRY_INTERVAL_TICKS as authored.</summary>
        public int SpawnDensityRetryIntervalTicks => _spawnDensityRetryIntervalTicks;

        /// <summary>Sets every knob as given (no validation); test seam only.</summary>
        internal void SetForTests(int common, int uncommon, int rare, int elite, int retryInterval)
        {
            _respawnDelayCommonTicks = common;
            _respawnDelayUncommonTicks = uncommon;
            _respawnDelayRareTicks = rare;
            _respawnDelayEliteTicks = elite;
            _spawnDensityRetryIntervalTicks = retryInterval;
        }
    }
}

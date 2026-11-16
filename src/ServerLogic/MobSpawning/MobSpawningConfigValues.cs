using System.Globalization;
using IronGrind.ServerLogic.EnemyAI;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// The Mob Spawning knobs as the spawner takes them (mob-spawning.md Tuning Knobs; CR-MS-9, F-MS-1;
    /// Mob Spawning Story 001). Unlike Enemy AI, an out-of-range value is refused: <see cref="Validate"/> logs one
    /// error per bad knob (knob name and value) and returns false.
    /// </summary>
    /// <remarks>
    /// <c>default(MobSpawningConfigValues)</c> has every knob 0 and fails <see cref="Validate"/>; use
    /// <see cref="Defaults"/>, the constructor or <see cref="FromAsset"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var values = new MobSpawningConfigValues(60, 120, 300, 600, 20);
    /// if (values.Validate()) { int delay = values.RespawnDelayForTier(MobTier.Rare); }
    /// </code>
    /// </example>
    public readonly struct MobSpawningConfigValues
    {
        /// <summary>RESPAWN_DELAY_COMMON_TICKS default.</summary>
        public const int RESPAWN_DELAY_COMMON_TICKS_DEFAULT = 60;
        /// <summary>RESPAWN_DELAY_COMMON_TICKS safe range minimum.</summary>
        public const int RESPAWN_DELAY_COMMON_TICKS_MIN = 20;
        /// <summary>RESPAWN_DELAY_COMMON_TICKS safe range maximum.</summary>
        public const int RESPAWN_DELAY_COMMON_TICKS_MAX = 300;

        /// <summary>RESPAWN_DELAY_UNCOMMON_TICKS default.</summary>
        public const int RESPAWN_DELAY_UNCOMMON_TICKS_DEFAULT = 120;
        /// <summary>RESPAWN_DELAY_UNCOMMON_TICKS safe range minimum.</summary>
        public const int RESPAWN_DELAY_UNCOMMON_TICKS_MIN = 60;
        /// <summary>RESPAWN_DELAY_UNCOMMON_TICKS safe range maximum.</summary>
        public const int RESPAWN_DELAY_UNCOMMON_TICKS_MAX = 600;

        /// <summary>RESPAWN_DELAY_RARE_TICKS default.</summary>
        public const int RESPAWN_DELAY_RARE_TICKS_DEFAULT = 300;
        /// <summary>RESPAWN_DELAY_RARE_TICKS safe range minimum.</summary>
        public const int RESPAWN_DELAY_RARE_TICKS_MIN = 120;
        /// <summary>RESPAWN_DELAY_RARE_TICKS safe range maximum.</summary>
        public const int RESPAWN_DELAY_RARE_TICKS_MAX = 1200;

        /// <summary>RESPAWN_DELAY_ELITE_TICKS default.</summary>
        public const int RESPAWN_DELAY_ELITE_TICKS_DEFAULT = 600;
        /// <summary>RESPAWN_DELAY_ELITE_TICKS safe range minimum.</summary>
        public const int RESPAWN_DELAY_ELITE_TICKS_MIN = 300;
        /// <summary>RESPAWN_DELAY_ELITE_TICKS safe range maximum.</summary>
        public const int RESPAWN_DELAY_ELITE_TICKS_MAX = 3600;

        /// <summary>SPAWN_DENSITY_RETRY_INTERVAL_TICKS default.</summary>
        public const int SPAWN_DENSITY_RETRY_INTERVAL_TICKS_DEFAULT = 20;
        /// <summary>SPAWN_DENSITY_RETRY_INTERVAL_TICKS safe range minimum.</summary>
        public const int SPAWN_DENSITY_RETRY_INTERVAL_TICKS_MIN = 10;
        /// <summary>SPAWN_DENSITY_RETRY_INTERVAL_TICKS safe range maximum.</summary>
        public const int SPAWN_DENSITY_RETRY_INTERVAL_TICKS_MAX = 200;

        private const string LOG_PREFIX = "[MobSpawningConfig] ";

        /// <summary>Builds values as given; no range check (call <see cref="Validate"/>).</summary>
        /// <param name="respawnDelayCommonTicks">RESPAWN_DELAY_COMMON_TICKS.</param>
        /// <param name="respawnDelayUncommonTicks">RESPAWN_DELAY_UNCOMMON_TICKS.</param>
        /// <param name="respawnDelayRareTicks">RESPAWN_DELAY_RARE_TICKS.</param>
        /// <param name="respawnDelayEliteTicks">RESPAWN_DELAY_ELITE_TICKS.</param>
        /// <param name="spawnDensityRetryIntervalTicks">SPAWN_DENSITY_RETRY_INTERVAL_TICKS.</param>
        public MobSpawningConfigValues(int respawnDelayCommonTicks, int respawnDelayUncommonTicks,
            int respawnDelayRareTicks, int respawnDelayEliteTicks, int spawnDensityRetryIntervalTicks)
        {
            RespawnDelayCommonTicks = respawnDelayCommonTicks;
            RespawnDelayUncommonTicks = respawnDelayUncommonTicks;
            RespawnDelayRareTicks = respawnDelayRareTicks;
            RespawnDelayEliteTicks = respawnDelayEliteTicks;
            SpawnDensityRetryIntervalTicks = spawnDensityRetryIntervalTicks;
        }

        /// <summary>The defaults of every knob.</summary>
        public static MobSpawningConfigValues Defaults => new MobSpawningConfigValues(
            RESPAWN_DELAY_COMMON_TICKS_DEFAULT, RESPAWN_DELAY_UNCOMMON_TICKS_DEFAULT,
            RESPAWN_DELAY_RARE_TICKS_DEFAULT, RESPAWN_DELAY_ELITE_TICKS_DEFAULT,
            SPAWN_DENSITY_RETRY_INTERVAL_TICKS_DEFAULT);

        /// <summary>RESPAWN_DELAY_COMMON_TICKS.</summary>
        public int RespawnDelayCommonTicks { get; }

        /// <summary>RESPAWN_DELAY_UNCOMMON_TICKS.</summary>
        public int RespawnDelayUncommonTicks { get; }

        /// <summary>RESPAWN_DELAY_RARE_TICKS.</summary>
        public int RespawnDelayRareTicks { get; }

        /// <summary>RESPAWN_DELAY_ELITE_TICKS.</summary>
        public int RespawnDelayEliteTicks { get; }

        /// <summary>SPAWN_DENSITY_RETRY_INTERVAL_TICKS.</summary>
        public int SpawnDensityRetryIntervalTicks { get; }

        /// <summary>Builds values from an asset.</summary>
        /// <param name="asset">The config asset; null yields <see cref="Defaults"/> with one warning.</param>
        /// <returns>The asset's values as authored (not validated), or the defaults for a null asset.</returns>
        public static MobSpawningConfigValues FromAsset(MobSpawningConfig asset)
        {
            if (asset == null)
            {
                Debug.LogWarning(LOG_PREFIX + "no config asset; using defaults.");
                return Defaults;
            }

            return new MobSpawningConfigValues(asset.RespawnDelayCommonTicks, asset.RespawnDelayUncommonTicks,
                asset.RespawnDelayRareTicks, asset.RespawnDelayEliteTicks, asset.SpawnDensityRetryIntervalTicks);
        }

        /// <summary>
        /// Checks every knob against its safe range and the tier order Common &lt; Uncommon &lt; Rare &lt; Elite.
        /// Logs one error per failing knob (name and value) and one per broken order pair.
        /// </summary>
        /// <returns>True when every knob is valid.</returns>
        public bool Validate()
        {
            bool ok = true;
            ok &= CheckRange("RESPAWN_DELAY_COMMON_TICKS", RespawnDelayCommonTicks,
                RESPAWN_DELAY_COMMON_TICKS_MIN, RESPAWN_DELAY_COMMON_TICKS_MAX);
            ok &= CheckRange("RESPAWN_DELAY_UNCOMMON_TICKS", RespawnDelayUncommonTicks,
                RESPAWN_DELAY_UNCOMMON_TICKS_MIN, RESPAWN_DELAY_UNCOMMON_TICKS_MAX);
            ok &= CheckRange("RESPAWN_DELAY_RARE_TICKS", RespawnDelayRareTicks,
                RESPAWN_DELAY_RARE_TICKS_MIN, RESPAWN_DELAY_RARE_TICKS_MAX);
            ok &= CheckRange("RESPAWN_DELAY_ELITE_TICKS", RespawnDelayEliteTicks,
                RESPAWN_DELAY_ELITE_TICKS_MIN, RESPAWN_DELAY_ELITE_TICKS_MAX);
            ok &= CheckRange("SPAWN_DENSITY_RETRY_INTERVAL_TICKS", SpawnDensityRetryIntervalTicks,
                SPAWN_DENSITY_RETRY_INTERVAL_TICKS_MIN, SPAWN_DENSITY_RETRY_INTERVAL_TICKS_MAX);
            ok &= CheckOrder("RESPAWN_DELAY_UNCOMMON_TICKS", RespawnDelayUncommonTicks,
                "RESPAWN_DELAY_COMMON_TICKS", RespawnDelayCommonTicks);
            ok &= CheckOrder("RESPAWN_DELAY_RARE_TICKS", RespawnDelayRareTicks,
                "RESPAWN_DELAY_UNCOMMON_TICKS", RespawnDelayUncommonTicks);
            ok &= CheckOrder("RESPAWN_DELAY_ELITE_TICKS", RespawnDelayEliteTicks,
                "RESPAWN_DELAY_RARE_TICKS", RespawnDelayRareTicks);
            return ok;
        }

        /// <summary>The respawn delay in ticks for a tier (F-MS-1).</summary>
        /// <param name="tier">The mob tier; an undefined value logs an error and yields the Common delay.</param>
        /// <returns>The tier's respawn delay in ticks.</returns>
        public int RespawnDelayForTier(MobTier tier)
        {
            switch (tier)
            {
                case MobTier.Common: return RespawnDelayCommonTicks;
                case MobTier.Uncommon: return RespawnDelayUncommonTicks;
                case MobTier.Rare: return RespawnDelayRareTicks;
                case MobTier.Elite: return RespawnDelayEliteTicks;
                default:
                    Debug.LogError(LOG_PREFIX + "undefined MobTier " + ((int)tier).ToString(CultureInfo.InvariantCulture)
                        + "; using the Common delay.");
                    return RespawnDelayCommonTicks;
            }
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

        private static bool CheckOrder(string knob, int value, string lowerKnob, int lowerValue)
        {
            if (value > lowerValue)
            {
                return true;
            }

            Debug.LogError(LOG_PREFIX + knob + " = " + value.ToString(CultureInfo.InvariantCulture)
                + " must be greater than " + lowerKnob + " = " + lowerValue.ToString(CultureInfo.InvariantCulture) + ".");
            return false;
        }
    }
}

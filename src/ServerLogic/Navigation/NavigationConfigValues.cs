using System;
using System.Collections.Generic;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// The Navigation knobs as the systems take them (navigation-pathfinding.md Tuning Knobs). Unlike the Skill System,
    /// out-of-range values are not clamped: <see cref="Validate"/> rejects them and the caller refuses to start.
    /// <see cref="Defaults"/> and <see cref="FromAsset"/> copy values as authored.
    /// </summary>
    /// <example>
    /// <code>
    /// var errors = new List&lt;string&gt;();
    /// var warnings = new List&lt;string&gt;();
    /// if (!NavigationConfigValues.Validate(NavigationConfigValues.FromAsset(asset), errors, warnings)) { /* refuse */ }
    /// </code>
    /// </example>
    public readonly struct NavigationConfigValues
    {
        /// <summary>MAX_AGENT_POOL_SIZE default.</summary>
        public const int MAX_AGENT_POOL_SIZE_DEFAULT = ZoneSlotPool.MAX_MOBS_PER_ZONE;
        /// <summary>MAX_AGENT_POOL_SIZE safe range minimum.</summary>
        public const int MAX_AGENT_POOL_SIZE_MIN = 1;
        /// <summary>MAX_AGENT_POOL_SIZE safe range maximum.</summary>
        public const int MAX_AGENT_POOL_SIZE_MAX = ZoneSlotPool.MAX_MOBS_PER_ZONE;

        /// <summary>MAX_PATH_UPDATES_PER_TICK default.</summary>
        public const int MAX_PATH_UPDATES_PER_TICK_DEFAULT = 20;
        /// <summary>MAX_PATH_UPDATES_PER_TICK safe range minimum.</summary>
        public const int MAX_PATH_UPDATES_PER_TICK_MIN = 1;
        /// <summary>MAX_PATH_UPDATES_PER_TICK safe range maximum.</summary>
        public const int MAX_PATH_UPDATES_PER_TICK_MAX = 150;
        /// <summary>Values below this are valid but warn (burst lag passes 1.5 s, F-NAV-3).</summary>
        public const int MAX_PATH_UPDATES_PER_TICK_WARN_BELOW = 5;

        /// <summary>PARTIAL_PATH_TIMEOUT_TICKS default.</summary>
        public const int PARTIAL_PATH_TIMEOUT_TICKS_DEFAULT = 20;
        /// <summary>PARTIAL_PATH_TIMEOUT_TICKS safe range minimum.</summary>
        public const int PARTIAL_PATH_TIMEOUT_TICKS_MIN = 10;
        /// <summary>PARTIAL_PATH_TIMEOUT_TICKS safe range maximum.</summary>
        public const int PARTIAL_PATH_TIMEOUT_TICKS_MAX = 200;

        /// <summary>RETURN_SPEED_CAP default, in m/s.</summary>
        public const float RETURN_SPEED_CAP_DEFAULT = 6.0f;
        /// <summary>RETURN_SPEED_CAP safe range minimum.</summary>
        public const float RETURN_SPEED_CAP_MIN = 1.0f;
        /// <summary>RETURN_SPEED_CAP safe range maximum.</summary>
        public const float RETURN_SPEED_CAP_MAX = 10.0f;

        /// <summary>MIN_VALID_SPAWN_FRACTION default.</summary>
        public const float MIN_VALID_SPAWN_FRACTION_DEFAULT = 0.90f;
        /// <summary>MIN_VALID_SPAWN_FRACTION safe range minimum.</summary>
        public const float MIN_VALID_SPAWN_FRACTION_MIN = 0.50f;
        /// <summary>MIN_VALID_SPAWN_FRACTION safe range maximum.</summary>
        public const float MIN_VALID_SPAWN_FRACTION_MAX = 1.00f;

        /// <summary>NAV_STUCK_REROUTE_DETECTION_TICKS default.</summary>
        public const int NAV_STUCK_REROUTE_DETECTION_TICKS_DEFAULT = 10;
        /// <summary>NAV_STUCK_REROUTE_DETECTION_TICKS safe range minimum.</summary>
        public const int NAV_STUCK_REROUTE_DETECTION_TICKS_MIN = 3;
        /// <summary>NAV_STUCK_REROUTE_DETECTION_TICKS safe range maximum.</summary>
        public const int NAV_STUCK_REROUTE_DETECTION_TICKS_MAX = 60;

        /// <summary>NAV_OFF_MESH_RECOVERY_TICKS default.</summary>
        public const int NAV_OFF_MESH_RECOVERY_TICKS_DEFAULT = 3;
        /// <summary>NAV_OFF_MESH_RECOVERY_TICKS safe range minimum.</summary>
        public const int NAV_OFF_MESH_RECOVERY_TICKS_MIN = 1;
        /// <summary>NAV_OFF_MESH_RECOVERY_TICKS safe range maximum.</summary>
        public const int NAV_OFF_MESH_RECOVERY_TICKS_MAX = 10;

        /// <summary>SPAWN_SAMPLE_RADIUS default, in world units.</summary>
        public const float SPAWN_SAMPLE_RADIUS_DEFAULT = 0.1f;
        /// <summary>SPAWN_SAMPLE_RADIUS safe range minimum.</summary>
        public const float SPAWN_SAMPLE_RADIUS_MIN = 0.01f;
        /// <summary>SPAWN_SAMPLE_RADIUS safe range maximum.</summary>
        public const float SPAWN_SAMPLE_RADIUS_MAX = 1.0f;

        /// <summary>
        /// Lowest agent speed in m/s (CR-NAV-8 clamp range). A plain constant, not a tunable knob; the claim path
        /// (Story 003) and <c>SetSpeed</c> (Story 004) both use it.
        /// </summary>
        public const float AGENT_SPEED_MIN = 0.1f;

        /// <summary>
        /// Highest agent speed in m/s (CR-NAV-8 clamp range). A plain constant, not a tunable knob; the claim path
        /// (Story 003) and <c>SetSpeed</c> (Story 004) both use it.
        /// </summary>
        public const float AGENT_SPEED_MAX = 20.0f;

        /// <summary>The defaults of every knob.</summary>
        public static NavigationConfigValues Defaults => new NavigationConfigValues(
            MAX_AGENT_POOL_SIZE_DEFAULT, MAX_PATH_UPDATES_PER_TICK_DEFAULT, PARTIAL_PATH_TIMEOUT_TICKS_DEFAULT,
            RETURN_SPEED_CAP_DEFAULT, MIN_VALID_SPAWN_FRACTION_DEFAULT, NAV_STUCK_REROUTE_DETECTION_TICKS_DEFAULT,
            NAV_OFF_MESH_RECOVERY_TICKS_DEFAULT, SPAWN_SAMPLE_RADIUS_DEFAULT);

        /// <summary>Builds the values exactly as given (no clamping).</summary>
        /// <param name="maxAgentPoolSize">MAX_AGENT_POOL_SIZE.</param>
        /// <param name="maxPathUpdatesPerTick">MAX_PATH_UPDATES_PER_TICK.</param>
        /// <param name="partialPathTimeoutTicks">PARTIAL_PATH_TIMEOUT_TICKS.</param>
        /// <param name="returnSpeedCap">RETURN_SPEED_CAP.</param>
        /// <param name="minValidSpawnFraction">MIN_VALID_SPAWN_FRACTION.</param>
        /// <param name="navStuckRerouteDetectionTicks">NAV_STUCK_REROUTE_DETECTION_TICKS.</param>
        /// <param name="navOffMeshRecoveryTicks">NAV_OFF_MESH_RECOVERY_TICKS.</param>
        /// <param name="spawnSampleRadius">SPAWN_SAMPLE_RADIUS.</param>
        public NavigationConfigValues(
            int maxAgentPoolSize, int maxPathUpdatesPerTick, int partialPathTimeoutTicks, float returnSpeedCap,
            float minValidSpawnFraction, int navStuckRerouteDetectionTicks, int navOffMeshRecoveryTicks,
            float spawnSampleRadius)
        {
            MaxAgentPoolSize = maxAgentPoolSize;
            MaxPathUpdatesPerTick = maxPathUpdatesPerTick;
            PartialPathTimeoutTicks = partialPathTimeoutTicks;
            ReturnSpeedCap = returnSpeedCap;
            MinValidSpawnFraction = minValidSpawnFraction;
            NavStuckRerouteDetectionTicks = navStuckRerouteDetectionTicks;
            NavOffMeshRecoveryTicks = navOffMeshRecoveryTicks;
            SpawnSampleRadius = spawnSampleRadius;
        }

        /// <summary>MAX_AGENT_POOL_SIZE: agents created at Initialize; must equal ZoneSlotPool.MAX_MOBS_PER_ZONE.</summary>
        public int MaxAgentPoolSize { get; }

        /// <summary>MAX_PATH_UPDATES_PER_TICK: path requests served per tick.</summary>
        public int MaxPathUpdatesPerTick { get; }

        /// <summary>PARTIAL_PATH_TIMEOUT_TICKS: ticks a partial path is tolerated.</summary>
        public int PartialPathTimeoutTicks { get; }

        /// <summary>RETURN_SPEED_CAP: speed cap while returning, in m/s.</summary>
        public float ReturnSpeedCap { get; }

        /// <summary>MIN_VALID_SPAWN_FRACTION: fraction of spawn points that must sit on the NavMesh.</summary>
        public float MinValidSpawnFraction { get; }

        /// <summary>NAV_STUCK_REROUTE_DETECTION_TICKS: ticks without progress before a reroute.</summary>
        public int NavStuckRerouteDetectionTicks { get; }

        /// <summary>NAV_OFF_MESH_RECOVERY_TICKS: ticks off the mesh before recovery.</summary>
        public int NavOffMeshRecoveryTicks { get; }

        /// <summary>SPAWN_SAMPLE_RADIUS: radius of the spawn NavMesh sample, in world units.</summary>
        public float SpawnSampleRadius { get; }

        /// <summary>Copies the values from an asset without clamping.</summary>
        /// <param name="asset">The authored asset; not null.</param>
        /// <returns>The values as authored.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        public static NavigationConfigValues FromAsset(NavigationConfig asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            return new NavigationConfigValues(
                asset.MaxAgentPoolSize, asset.MaxPathUpdatesPerTick, asset.PartialPathTimeoutTicks,
                asset.ReturnSpeedCap, asset.MinValidSpawnFraction, asset.NavStuckRerouteDetectionTicks,
                asset.NavOffMeshRecoveryTicks, asset.SpawnSampleRadius);
        }

        /// <summary>
        /// Checks every knob against its safe range (inclusive) and the pool size against
        /// <see cref="ZoneSlotPool.MAX_MOBS_PER_ZONE"/>. Each message names the knob in its UPPER_SNAKE name.
        /// </summary>
        /// <param name="values">The values to check.</param>
        /// <param name="errors">Receives one message per rejected knob; not null.</param>
        /// <param name="warnings">Receives one message per valid-but-risky knob; not null.</param>
        /// <returns>True when there are no errors.</returns>
        public static bool Validate(in NavigationConfigValues values, List<string> errors, List<string> warnings)
        {
            int before = errors.Count;

            CheckInt("MAX_AGENT_POOL_SIZE", values.MaxAgentPoolSize,
                MAX_AGENT_POOL_SIZE_MIN, MAX_AGENT_POOL_SIZE_MAX, errors);
            if (values.MaxAgentPoolSize != ZoneSlotPool.MAX_MOBS_PER_ZONE)
            {
                errors.Add("MAX_AGENT_POOL_SIZE " + values.MaxAgentPoolSize + " must equal MAX_MOBS_PER_ZONE "
                    + ZoneSlotPool.MAX_MOBS_PER_ZONE);
            }

            CheckInt("MAX_PATH_UPDATES_PER_TICK", values.MaxPathUpdatesPerTick,
                MAX_PATH_UPDATES_PER_TICK_MIN, MAX_PATH_UPDATES_PER_TICK_MAX, errors);
            if (values.MaxPathUpdatesPerTick >= MAX_PATH_UPDATES_PER_TICK_MIN
                && values.MaxPathUpdatesPerTick < MAX_PATH_UPDATES_PER_TICK_WARN_BELOW)
            {
                warnings.Add("MAX_PATH_UPDATES_PER_TICK " + values.MaxPathUpdatesPerTick
                    + " is below " + MAX_PATH_UPDATES_PER_TICK_WARN_BELOW + ": burst lag passes 1.5 s (F-NAV-3)");
            }

            CheckInt("PARTIAL_PATH_TIMEOUT_TICKS", values.PartialPathTimeoutTicks,
                PARTIAL_PATH_TIMEOUT_TICKS_MIN, PARTIAL_PATH_TIMEOUT_TICKS_MAX, errors);
            CheckFloat("RETURN_SPEED_CAP", values.ReturnSpeedCap,
                RETURN_SPEED_CAP_MIN, RETURN_SPEED_CAP_MAX, errors);
            CheckFloat("MIN_VALID_SPAWN_FRACTION", values.MinValidSpawnFraction,
                MIN_VALID_SPAWN_FRACTION_MIN, MIN_VALID_SPAWN_FRACTION_MAX, errors);
            CheckInt("NAV_STUCK_REROUTE_DETECTION_TICKS", values.NavStuckRerouteDetectionTicks,
                NAV_STUCK_REROUTE_DETECTION_TICKS_MIN, NAV_STUCK_REROUTE_DETECTION_TICKS_MAX, errors);
            CheckInt("NAV_OFF_MESH_RECOVERY_TICKS", values.NavOffMeshRecoveryTicks,
                NAV_OFF_MESH_RECOVERY_TICKS_MIN, NAV_OFF_MESH_RECOVERY_TICKS_MAX, errors);
            CheckFloat("SPAWN_SAMPLE_RADIUS", values.SpawnSampleRadius,
                SPAWN_SAMPLE_RADIUS_MIN, SPAWN_SAMPLE_RADIUS_MAX, errors);

            return errors.Count == before;
        }

        private static void CheckInt(string knob, int value, int min, int max, List<string> errors)
        {
            if (value < min || value > max)
            {
                errors.Add(knob + " " + value + " is outside [" + min + ", " + max + "]");
            }
        }

        // Written so that NaN fails.
        private static void CheckFloat(string knob, float value, float min, float max, List<string> errors)
        {
            if (!(value >= min && value <= max))
            {
                errors.Add(knob + " " + value + " is outside [" + min + ", " + max + "]");
            }
        }
    }
}

using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.MobSpawning;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneMobSpawnTimers"/> that forwards to a late-bound <see cref="MobSpawningService"/> (Mob Spawning
    /// Story 006). The Zone Instancing wiring takes its timers once at S5, but the spawner is built at S6 (the zone
    /// definition and the NavMesh exist only then); the composition passes this forwarder at S5 and sets
    /// <see cref="Spawner"/> at S6. With no spawner (S6 failed first, or a composition without a definition slot)
    /// <see cref="CancelAllSpawnTimers"/> does nothing and logs nothing. Tick thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// var timers = new ForwardingZoneMobSpawnTimers();   // S5: passed as ZoneInstancingWiringOptions.MobSpawnTimers
    /// timers.Spawner = spawner;                           // S6: after MobSpawningZoneLoader.TryLoad succeeded
    /// </code>
    /// </example>
    public sealed class ForwardingZoneMobSpawnTimers : IZoneMobSpawnTimers
    {
        /// <summary>The spawner calls go to; null until S6 builds it.</summary>
        public MobSpawningService Spawner { get; set; }

        /// <inheritdoc/>
        public void CancelAllSpawnTimers(ZoneID zone)
        {
            Spawner?.CancelAllSpawnTimers(zone);
        }
    }
}

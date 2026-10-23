using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The Mob Spawning seam of zone teardown (<c>zone-instancing.md</c> CR-ZI-12 step 1, Zone Instancing Story 012).
    /// Direct call (ADR-010), tick thread. <see cref="LoggingZoneMobSpawnTimers"/> until the Mob Spawning epic ships.
    /// </summary>
    /// <example>
    /// <code>
    /// timers.CancelAllSpawnTimers(zone); // teardown step 1: no spawn event fires after this
    /// </code>
    /// </example>
    public interface IZoneMobSpawnTimers
    {
        /// <summary>Cancels every pending spawn timer of the zone so no spawn event fires after teardown.</summary>
        /// <param name="zone">The zone being torn down.</param>
        void CancelAllSpawnTimers(ZoneID zone);
    }
}

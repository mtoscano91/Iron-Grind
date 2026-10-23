using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.ZoneServer;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Composition default for <see cref="IZoneMobSpawnTimers"/> until the Mob Spawning epic ships: logs once, cancels nothing.
    /// </summary>
    /// <example>
    /// <code>
    /// new LoggingZoneMobSpawnTimers().CancelAllSpawnTimers(zone); // logs once
    /// </code>
    /// </example>
    public sealed class LoggingZoneMobSpawnTimers : IZoneMobSpawnTimers
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("mob spawn timers");

        /// <inheritdoc/>
        public void CancelAllSpawnTimers(ZoneID zone)
        {
            _log.Log();
        }
    }
}

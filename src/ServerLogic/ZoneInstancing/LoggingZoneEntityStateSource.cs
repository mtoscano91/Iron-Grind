using IronGrind.CharacterStats;
using IronGrind.ServerLogic.ZoneServer;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Composition default for <see cref="IZoneEntityStateSource"/> until the real Character Stats / combat / Equipment
    /// sources exist (Zone Instancing Story 014): has no data for any entity and logs once per instance. The snapshot
    /// builder then skips every entity with one warning per call and <see cref="ZoneEntryCompletion"/> skips
    /// <c>PlayerJoinedZone</c> with one warning.
    /// </summary>
    /// <example>
    /// <code>
    /// bool found = new LoggingZoneEntityStateSource().TryGetPlayerState(id, out _); // false, logs once
    /// </code>
    /// </example>
    public sealed class LoggingZoneEntityStateSource : IZoneEntityStateSource
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("zone entity state source");

        /// <inheritdoc/>
        public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
        {
            _log.Log();
            view = default;
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
        {
            _log.Log();
            view = default;
            return false;
        }
    }
}

using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.ServerLogic.ZoneServer;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Composition default for <see cref="IZoneTeardownSessionSink"/> until TD-079 is paid: logs once per instance,
    /// persists nothing, and returns an already-completed success for the ghost snapshot save.
    /// </summary>
    /// <example>
    /// <code>
    /// var sink = new LoggingZoneTeardownSessionSink();
    /// Task&lt;CharacterSaveResult&gt; save = sink.SaveGhostSnapshotAndExpire(characterId, 5u); // completed, Success
    /// </code>
    /// </example>
    public sealed class LoggingZoneTeardownSessionSink : IZoneTeardownSessionSink
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("teardown session sink (TD-079)");

        /// <inheritdoc/>
        public Task<CharacterSaveResult> SaveGhostSnapshotAndExpire(CharacterID characterId, uint clientId)
        {
            _log.Log();
            return Task.FromResult(CharacterSaveResult.Success);
        }

        /// <inheritdoc/>
        public void ExpireConnectedSession(uint clientId)
        {
            _log.Log();
        }
    }
}

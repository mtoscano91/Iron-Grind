using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The session-layer seam of zone teardown (<c>zone-instancing.md</c> CR-ZI-12 steps 3 and 4, Zone Instancing
    /// Story 012; the real implementation pays TD-079). Direct call (ADR-010), tick thread.
    /// </summary>
    /// <example>
    /// <code>
    /// Task&lt;CharacterSaveResult&gt; save = sink.SaveGhostSnapshotAndExpire(characterId, clientId); // step 3, tracked by the caller
    /// sink.ExpireConnectedSession(otherClientId);                                                   // step 4
    /// </code>
    /// </example>
    public interface IZoneTeardownSessionSink
    {
        /// <summary>
        /// Contract: writes the ghost's CGS-3 pre-disconnect snapshot as its only teardown save, moves the session to
        /// <c>Disconnected_SessionExpired</c>, and deletes <c>ActiveSessionTokens[AccountID]</c> only if its
        /// <c>sessionId</c> is this session's. Emits nothing (no <c>GhostExpiredEvent</c>, no <c>PlayerLeftZone</c>).
        /// </summary>
        /// <param name="characterId">The ghost's character.</param>
        /// <param name="clientId">The ghost's last connection id.</param>
        /// <returns>The save; the caller passes it to <c>ITickCompletionQueue.Track</c>.</returns>
        Task<CharacterSaveResult> SaveGhostSnapshotAndExpire(CharacterID characterId, uint clientId);

        /// <summary>
        /// Contract: <c>Connected -> Disconnected_SessionExpired</c>, no resume, no persistence write, token deleted
        /// only if owned. Closes no transport connection. It must not remove the client from the outbox or close the
        /// connection: the queued <c>ZoneSessionEnded</c> is flushed by composition teardown step 5, after the close steps.
        /// </summary>
        /// <param name="clientId">The connection whose session ends.</param>
        void ExpireConnectedSession(uint clientId);
    }
}

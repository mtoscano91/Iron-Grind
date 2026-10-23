namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The session-layer seam of stale slot reclamation (<c>zone-instancing.md</c> CR-ZI-17, Zone Instancing Story 011).
    /// Called by <see cref="ZoneEntryReclamation"/> after it freed an entering slot. Direct call (ADR-010), tick thread.
    /// </summary>
    public interface IZoneEntryReclaimSessionSink
    {
        /// <summary>
        /// Ends the session of a connection whose entering slot was reclaimed. Contract: the session goes
        /// <c>Connected -> Disconnected_SessionExpired</c>; no persistence write; no TTL (no ghost). When
        /// <paramref name="deleteTokenIfOwned"/> is true, <c>ActiveSessionTokens[AccountID]</c> is deleted only when
        /// its <c>sessionId</c> is this session's (case b passes false: the new connection owns the token).
        /// </summary>
        /// <param name="clientId">The connection whose session ends.</param>
        /// <param name="deleteTokenIfOwned">Delete the account's active token when this session owns it.</param>
        void ExpireEnteringSession(uint clientId, bool deleteTokenIfOwned);
    }
}

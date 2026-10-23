namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Records which clients already got <c>ZoneSessionEnded</c> at the T-4 drain broadcast (<c>zone-instancing.md</c>
    /// CR-ZI-12 step 4, Zone Instancing Story 012). Story 014 implements it; <see cref="NoZoneDrainNoticeLog"/> is the
    /// default until then. Direct call (ADR-010), tick thread.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!log.WasNotifiedAtDrain(clientId)) { /* queue ZoneSessionEnded(ZoneClosed) */ }
    /// </code>
    /// </example>
    public interface IZoneDrainNoticeLog
    {
        /// <summary>True when the client was already sent <c>ZoneSessionEnded</c> at T-4.</summary>
        /// <param name="clientId">The connection.</param>
        bool WasNotifiedAtDrain(uint clientId);
    }
}

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Default <see cref="IZoneDrainNoticeLog"/>: nobody was notified at drain, so every client gets
    /// <c>ZoneSessionEnded</c> at teardown (correct for T-6 from Active).
    /// </summary>
    /// <example>
    /// <code>
    /// bool notified = new NoZoneDrainNoticeLog().WasNotifiedAtDrain(5u); // false
    /// </code>
    /// </example>
    public sealed class NoZoneDrainNoticeLog : IZoneDrainNoticeLog
    {
        /// <inheritdoc/>
        public bool WasNotifiedAtDrain(uint clientId) => false;
    }
}

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The stop side of the cross-zone relay listener (ADR-015 Decision 3), teardown step 2a.
    /// Story 044 makes <c>NpgsqlRelayListener</c> implement it. <see cref="Stop"/> must be idempotent.
    /// </summary>
    /// <example>
    /// <code>
    /// listener.Stop(); // joins the listener thread, bounded by RELAY_LISTENER_STOP_TIMEOUT_MS
    /// </code>
    /// </example>
    public interface IZoneRelayListener
    {
        /// <summary>Stops the listener and joins its thread.</summary>
        void Stop();
    }
}

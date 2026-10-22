namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The stop side of the inbound transport shell, teardown step 6 (before the NGO shutdown).
    /// Story 045 adapts <c>NgoInboundTransportShell</c> to it.
    /// </summary>
    /// <example>
    /// <code>
    /// transport.Stop();
    /// network.Shutdown();
    /// </code>
    /// </example>
    public interface IZoneInboundTransport
    {
        /// <summary>Stops receiving from NGO.</summary>
        void Stop();
    }
}

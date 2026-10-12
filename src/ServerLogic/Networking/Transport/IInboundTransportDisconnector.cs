namespace IronGrind.Networking
{
    /// <summary>
    /// The transport seam the inbound transport adapter uses to ask for a disconnect (ADR-014 Decision 4,
    /// Networking Core Story 038). The NGO shell implements it with the deferred <c>DisconnectClient</c>;
    /// tests implement it with a recorder.
    /// </summary>
    /// <example>
    /// <code>
    /// transport.DisconnectClient(7UL);
    /// </code>
    /// </example>
    public interface IInboundTransportDisconnector
    {
        /// <summary>
        /// Asks the transport to disconnect a client. The parameter is <c>ulong</c> because a transport id
        /// above <see cref="uint.MaxValue"/> must be disconnectable too. The transport may raise its
        /// disconnect event later, or inside this call.
        /// </summary>
        /// <param name="transportClientId">The transport's client id.</param>
        void DisconnectClient(ulong transportClientId);
    }
}

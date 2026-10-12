namespace IronGrind.Networking
{
    /// <summary>
    /// Asked to disconnect a client (Networking Core Story 039). <see cref="InboundTransportAdapter"/>
    /// implements it with its server-initiated <see cref="InboundTransportAdapter.Disconnect"/>;
    /// <see cref="ClientMessageOutbox"/> calls it when a client's queue overflows.
    /// </summary>
    /// <remarks>
    /// The call may come back into <see cref="IClientConnectionListener.RemoveClient"/> before it returns.
    /// </remarks>
    /// <example>
    /// <code>
    /// outbox.SetDisconnectRequester(adapter);
    /// </code>
    /// </example>
    public interface IClientDisconnectRequester
    {
        /// <summary>Disconnects a client.</summary>
        /// <param name="clientId">The connection to disconnect.</param>
        void Disconnect(uint clientId);
    }
}

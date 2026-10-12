namespace IronGrind.Networking
{
    /// <summary>
    /// Told of the clients that connect and disconnect (Networking Core Story 039). The
    /// <see cref="InboundTransportAdapter"/> calls it; <see cref="ClientMessageOutbox"/> implements it to
    /// create and free a client's send storage.
    /// </summary>
    /// <remarks>
    /// Main thread, outside the tick. The adapter catches and logs any exception from either method, so a
    /// listener cannot disturb the adapter's own behaviour.
    /// </remarks>
    /// <example>
    /// <code>
    /// var adapter = new InboundTransportAdapter(intake, dispatcher, transport, clientListener: outbox);
    /// </code>
    /// </example>
    public interface IClientConnectionListener
    {
        /// <summary>A client was added to the dispatcher. Adding a known id does nothing.</summary>
        /// <param name="clientId">The client's connection id.</param>
        void AddClient(uint clientId);

        /// <summary>A client was removed. An unknown id does nothing.</summary>
        /// <param name="clientId">The client's connection id.</param>
        void RemoveClient(uint clientId);
    }
}

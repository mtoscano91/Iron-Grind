namespace IronGrind.Networking
{
    /// <summary>
    /// Told once of every message of a known connection, before the message is decoded (ADR-014
    /// Decision 2, step 1; any packet resets the heartbeat timeout, CR-NET-7.10). Implemented by the
    /// session layer.
    /// </summary>
    /// <example>
    /// <code>
    /// public void OnInboundActivity(uint clientId, uint serverTick) { /* record only */ }
    /// </code>
    /// </example>
    public interface IConnectionActivitySink
    {
        /// <summary>Called from <c>TryAccept</c>, inside the receive callback. Records only.</summary>
        /// <param name="clientId">The connection.</param>
        /// <param name="serverTick"><see cref="IServerTickSource.ServerTickNumber"/> at receipt.</param>
        void OnInboundActivity(uint clientId, uint serverTick);
    }
}

using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Sends an S-to-ALL message to every connected client of every zone (ADR-015 Decision 1, Networking Core
    /// Story 040). Tick thread only. Never throws.
    /// </summary>
    /// <example>
    /// <code>
    /// bool sent = broadcaster.Broadcast(ServerBroadcast_Enhancement9.MessageTypeId, body);
    /// </code>
    /// </example>
    public interface IServerWideBroadcaster
    {
        /// <summary>
        /// Enqueues <paramref name="body"/> for every client of this zone, then publishes it to the other zones.
        /// Returns false (and sends nothing) if <paramref name="messageTypeId"/> is not on the relay allowlist.
        /// </summary>
        /// <param name="messageTypeId">The wire message type; must be on the relay allowlist.</param>
        /// <param name="body">The encoded body, exactly as the outbox takes it; valid only during the call.</param>
        /// <returns>True when the message was accepted (the local half ran); false when it was refused.</returns>
        bool Broadcast(ushort messageTypeId, ReadOnlySpan<byte> body);
    }
}

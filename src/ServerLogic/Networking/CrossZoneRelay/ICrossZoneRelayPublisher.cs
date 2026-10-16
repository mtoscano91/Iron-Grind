using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Publishes one relay envelope to the other zone processes (ADR-015 Decision 2). Tick thread only.
    /// Never blocks the tick, never throws (the broadcaster still guards the call).
    /// </summary>
    /// <remarks>
    /// An implementation may allocate (it builds a string per message); a relayed message is a few per hour.
    /// </remarks>
    /// <example>
    /// <code>
    /// publisher.Publish(ServerBroadcast_Enhancement9.MessageTypeId, body);
    /// </code>
    /// </example>
    public interface ICrossZoneRelayPublisher
    {
        /// <summary>Publishes the message to the other zones.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="body">The encoded body; valid only during the call.</param>
        void Publish(ushort messageTypeId, ReadOnlySpan<byte> body);
    }
}

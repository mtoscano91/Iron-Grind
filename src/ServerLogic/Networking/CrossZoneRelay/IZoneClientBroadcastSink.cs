using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Sends one message to every client of this zone (ADR-015 Decision 1). Tick thread only.
    /// Implemented by <see cref="ClientMessageOutbox"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// sink.EnqueueToAllClients(ServerBroadcast_Enhancement9.MessageTypeId, body, isCapExempt: false);
    /// </code>
    /// </example>
    public interface IZoneClientBroadcastSink
    {
        /// <summary>
        /// Calls <c>Enqueue(clientId, messageTypeId, body, isCapExempt)</c> for every client added to this zone's outbox.
        /// </summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="body">The encoded body; valid only during the call.</param>
        /// <param name="isCapExempt">True when the message is exempt from the priority-path cap.</param>
        void EnqueueToAllClients(ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt);
    }
}

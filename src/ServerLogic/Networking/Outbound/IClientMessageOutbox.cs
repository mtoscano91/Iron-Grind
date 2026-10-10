using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The server-to-client send seam (Enhancement Story 010, user decision 2026-10-09): a handler or
    /// system enqueues one message for one client's connection. Generic on purpose: Enhancement
    /// Story 015 and Inventory Story 012 reuse it, and the transport adapter story (ADR-014) writes
    /// the production implementation. Tick thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[NPCInteractionOpened.BodySize];
    /// int written = NPCInteractionOpenedCodec.WriteBody(buffer);
    /// outbox.Enqueue(clientId, NPCInteractionOpened.MessageTypeId, buffer.Slice(0, written), isCapExempt: false);
    /// </code>
    /// </example>
    public interface IClientMessageOutbox
    {
        /// <summary>
        /// Enqueues a message for a client. An implementation copies <paramref name="body"/> before it
        /// returns; the caller keeps nothing and may reuse or free the span at once (ADR-014).
        /// </summary>
        /// <param name="clientId">The connection that receives the message.</param>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c> of the message.</param>
        /// <param name="body">The encoded body (after the envelope); valid only during the call.</param>
        /// <param name="isCapExempt">True when the message is exempt from the priority-path cap (CR-NET-7.7).</param>
        void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt);
    }
}

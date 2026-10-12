using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The send seam of <see cref="ClientMessageOutbox"/> (Networking Core Story 039): hands one finished
    /// server message to the transport. The production implementation is the NGO sender; tests use a
    /// recording double. "Sent" means one call on this interface.
    /// </summary>
    /// <remarks>
    /// Tick thread only. The implementation must copy what it needs before it returns; the span is a
    /// view of the outbox's single send buffer and is overwritten by the next message. An implementation
    /// may throw: the outbox catches the exception and logs one error per client per flush.
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class CountingSendSeam : IClientMessageSendSeam
    /// {
    ///     public int Count;
    ///     public void Send(uint clientId, ReadOnlySpan&lt;byte&gt; message, NetworkChannel channel) { Count++; }
    /// }
    /// </code>
    /// </example>
    public interface IClientMessageSendSeam
    {
        /// <summary>Sends one message to one client.</summary>
        /// <param name="clientId">The connection that receives the message.</param>
        /// <param name="message">The 10-byte server envelope followed by the body; valid only during the call.</param>
        /// <param name="channel">The channel of the message type's routing row.</param>
        void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel);
    }
}

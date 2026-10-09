using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The one entry point a transport adapter calls for each received client message (ADR-014
    /// Decision 2). The adapter decodes nothing and calls no game system, guard or handler.
    /// </summary>
    /// <example>
    /// <code>
    /// bool accepted = intake.TryAccept(clientId, receivedBytes);
    /// </code>
    /// </example>
    public interface IInboundMessageIntake
    {
        /// <summary>
        /// Main thread, outside the tick. <paramref name="message"/> is the envelope, the optional
        /// <c>SenderEntityID</c> and the body, as received. Copies what it keeps. Returns false when
        /// the message was dropped. Never throws.
        /// </summary>
        /// <param name="clientId">The connection that sent the message.</param>
        /// <param name="message">The received bytes.</param>
        bool TryAccept(uint clientId, ReadOnlySpan<byte> message);
    }
}

using System;
using IronGrind.Networking;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The inbound registration of <see cref="MovementIntentMessage"/> (Movement Story 004; ADR-014 Decision 1 and 3):
    /// U-U, not held during an irreversible write, its own unlimited <see cref="RpcTypeTag.MovementIntent"/>, body bound 10.
    /// </summary>
    /// <remarks>Not called from the composition root yet (Movement Story 009).</remarks>
    /// <example>
    /// <code>
    /// MovementInboundRegistration.Register(dispatcher, intentBuffer);
    /// dispatcher.Seal();
    /// </code>
    /// </example>
    public static class MovementInboundRegistration
    {
        /// <summary>Registers <see cref="MovementIntentMessage"/>; does not call <c>Seal()</c>.</summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="buffer">The intent buffer whose handler receives each message.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public static void Register(IInboundRequestDispatcher dispatcher, MovementIntentBuffer buffer)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            dispatcher.Register(
                new InboundRequestDescriptor(MovementIntentMessage.MessageTypeId, RpcTypeTag.MovementIntent,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)MovementIntentMessage.BodySize),
                buffer.HandleMovementIntent);
        }
    }
}

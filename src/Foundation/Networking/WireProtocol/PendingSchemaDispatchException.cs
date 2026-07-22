using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Thrown at handler-registration time when a <c>MessageTypeID</c> has no
    /// <see cref="MessageRoutingRegistry"/> row and the running build is a debug build
    /// (<see cref="BuildConfiguration.IsDevelopmentBuild"/>) — EC-MCR-1 / EC-MCR-4 / AC-MCR-03. In a
    /// release build the same condition instead routes the message to R-U and logs an
    /// <c>UnclassifiedMessageType</c> anomaly (see <see cref="MessageRoutingRegistry.ValidateAndRoute"/>)
    /// rather than throwing — this exception is exclusively the debug-build "fail loudly during
    /// development" path.
    /// </summary>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     MessageRoutingRegistry.ValidateAndRoute(messageTypeId: 0x9999, isDevelopmentBuild: true);
    /// }
    /// catch (PendingSchemaDispatchException ex)
    /// {
    ///     // ex.MessageTypeId == 0x9999
    /// }
    /// </code>
    /// </example>
    public sealed class PendingSchemaDispatchException : Exception
    {
        /// <summary>The unregistered <c>MessageTypeID</c> that triggered this exception.</summary>
        public ushort MessageTypeId { get; }

        /// <summary>Initializes a new <see cref="PendingSchemaDispatchException"/> for <paramref name="messageTypeId"/>.</summary>
        public PendingSchemaDispatchException(ushort messageTypeId)
            : base($"PendingSchemaDispatch: MessageTypeID 0x{messageTypeId:X4} has no MessageRoutingRegistry row. " +
                   "Registration is rejected in debug builds per EC-MCR-1/EC-MCR-4 — add a row to " +
                   "MessageRoutingRegistry before this message type may be dispatched.")
        {
            MessageTypeId = messageTypeId;
        }
    }
}

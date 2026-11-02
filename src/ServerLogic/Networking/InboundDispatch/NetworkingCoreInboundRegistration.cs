using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The inbound registrations owned by Networking Core (Story 036, ADR-014 Decision 3, last
    /// bullet): <see cref="SetTarget"/> as a request and <see cref="HeartbeatMessage"/> as a
    /// connection-level message. The two types that have a routing row and a message type in code.
    /// </summary>
    /// <example>
    /// <code>
    /// NetworkingCoreInboundRegistration.Register(dispatcher, setTargetHandler, heartbeatSink);
    /// // ... other systems register ...
    /// dispatcher.Seal();
    /// </code>
    /// </example>
    public static class NetworkingCoreInboundRegistration
    {
        /// <summary>
        /// Registers <see cref="SetTarget"/> (tag <see cref="RpcTypeTag.SetTarget"/>, not held,
        /// body 4 bytes) and <see cref="HeartbeatMessage"/> (connection-level, body 0, no
        /// <c>SenderEntityID</c>). Does not call <c>Seal()</c>: the composition root seals after
        /// every system has registered.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="setTargetHandler">The <see cref="SetTarget"/> handler.</param>
        /// <param name="heartbeatSink">The sink that receives heartbeats.</param>
        public static void Register(IInboundRequestDispatcher dispatcher, SetTargetRequestHandler setTargetHandler, IConnectionMessageSink heartbeatSink)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (setTargetHandler == null) throw new ArgumentNullException(nameof(setTargetHandler));
            if (heartbeatSink == null) throw new ArgumentNullException(nameof(heartbeatSink));

            dispatcher.Register(
                new InboundRequestDescriptor(SetTarget.MessageTypeId, RpcTypeTag.SetTarget,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)SetTarget.BodySize),
                setTargetHandler.Handle);

            dispatcher.RegisterConnectionLevel(
                new ConnectionMessageDescriptor(HeartbeatMessage.MessageTypeId, maxBodyBytes: 0, carriesSenderEntityId: false),
                heartbeatSink);
        }

        /// <summary>
        /// Registers the two Combat input requests (Auto-Attack Combat Story 006, ADR-014 Decision 1):
        /// <see cref="SetTarget"/> (tag <see cref="RpcTypeTag.SetTarget"/>, body 4) and <see cref="SetAutoAttack"/>
        /// (tag <see cref="RpcTypeTag.SetAutoAttack"/>, body 1), both not held and without a rate limit. Use it
        /// instead of <see cref="Register"/> when no heartbeat sink exists (the zone composition); do not call both
        /// for the same dispatcher, the second would register <see cref="SetTarget"/> twice. Does not call <c>Seal()</c>.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="setTargetHandler">The <see cref="SetTarget"/> handler.</param>
        /// <param name="setAutoAttackHandler">The <see cref="SetAutoAttack"/> handler.</param>
        public static void RegisterCombatInput(IInboundRequestDispatcher dispatcher, SetTargetRequestHandler setTargetHandler,
            SetAutoAttackRequestHandler setAutoAttackHandler)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (setTargetHandler == null) throw new ArgumentNullException(nameof(setTargetHandler));
            if (setAutoAttackHandler == null) throw new ArgumentNullException(nameof(setAutoAttackHandler));

            dispatcher.Register(
                new InboundRequestDescriptor(SetTarget.MessageTypeId, RpcTypeTag.SetTarget,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)SetTarget.BodySize),
                setTargetHandler.Handle);
            dispatcher.Register(
                new InboundRequestDescriptor(SetAutoAttack.MessageTypeId, RpcTypeTag.SetAutoAttack,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)SetAutoAttack.BodySize),
                setAutoAttackHandler.Handle);
        }
    }
}

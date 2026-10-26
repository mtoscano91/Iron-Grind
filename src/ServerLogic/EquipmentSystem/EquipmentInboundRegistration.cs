using System;
using IronGrind.Networking;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// The inbound registration of <see cref="EquipRequest"/> (Equipment Story 008; ADR-014 Decision 1 and 3): held
    /// during an irreversible write, its own <see cref="RpcTypeTag"/> with no rate limit, body bound 10 bytes.
    /// </summary>
    /// <remarks>
    /// Not yet called from <c>ZoneServerComposition</c> (it runs on stand-ins): calling it from the composition root
    /// belongs to the server bootstrap work (readiness decision 2026-10-26).
    /// </remarks>
    /// <example>
    /// <code>
    /// EquipmentInboundRegistration.Register(dispatcher, equipmentHandlers);
    /// // ... other systems register ...
    /// dispatcher.Seal();
    /// </code>
    /// </example>
    public static class EquipmentInboundRegistration
    {
        /// <summary>
        /// Registers <see cref="EquipRequest"/> (10 bytes) with <c>heldDuringIrreversibleWrite: true</c>, so an equip
        /// that arrives during an irreversible write waits and runs in arrival order when the gate opens. Does not call
        /// <c>Seal()</c>: the composition root seals after every system has registered.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="handlers">The Equipment request handler.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public static void Register(IInboundRequestDispatcher dispatcher, EquipmentClientRequestHandlers handlers)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            dispatcher.Register(
                new InboundRequestDescriptor(EquipRequest.MessageTypeId, RpcTypeTag.EquipRequest,
                    heldDuringIrreversibleWrite: true, maxBodyBytes: (ushort)EquipRequest.BodySize),
                handlers.HandleEquipRequest);
        }
    }
}

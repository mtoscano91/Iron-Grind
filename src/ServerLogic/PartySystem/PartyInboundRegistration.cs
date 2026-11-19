using System;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The inbound registrations of the two Party System invite messages (Story 007, ADR-014 Decision 1 and 3):
    /// neither is held during an irreversible write, each has its own unlimited <see cref="RpcTypeTag"/> and a body
    /// bound equal to its codec <c>BodySize</c>.
    /// </summary>
    public static class PartyInboundRegistration
    {
        /// <summary>
        /// Registers <see cref="PartyInviteRequest"/> (body 4) and <see cref="PartyInviteResponse"/> (body 5).
        /// Does not call <c>Seal()</c>: the composition root seals after every system has registered.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="handlers">The invite handlers.</param>
        public static void Register(IInboundRequestDispatcher dispatcher, PartyInviteClientRequestHandlers handlers)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            dispatcher.Register(
                new InboundRequestDescriptor(PartyInviteRequest.MessageTypeId, RpcTypeTag.PartyInviteRequest,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)PartyInviteRequest.BodySize),
                handlers.HandleInviteRequest);
            dispatcher.Register(
                new InboundRequestDescriptor(PartyInviteResponse.MessageTypeId, RpcTypeTag.PartyInviteResponse,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)PartyInviteResponse.BodySize),
                handlers.HandleInviteResponse);
        }

        /// <summary>
        /// Registers <see cref="PartyLeaveRequest"/> (0xE175) and <see cref="PartyDisbandRequest"/> (0xE176), both
        /// body 0, not held, unlimited (Story 008). Does not call <c>Seal()</c>.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="handlers">The leave and disband handlers.</param>
        public static void RegisterLeaveAndDisband(IInboundRequestDispatcher dispatcher,
            PartyLeaveDisbandClientRequestHandlers handlers)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));

            dispatcher.Register(
                new InboundRequestDescriptor(PartyLeaveRequest.MessageTypeId, RpcTypeTag.PartyLeaveRequest,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)PartyLeaveRequest.BodySize),
                handlers.HandleLeave);
            dispatcher.Register(
                new InboundRequestDescriptor(PartyDisbandRequest.MessageTypeId, RpcTypeTag.PartyDisbandRequest,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)PartyDisbandRequest.BodySize),
                handlers.HandleDisband);
        }
    }
}

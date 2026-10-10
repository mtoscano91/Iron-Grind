using System;
using IronGrind.Networking;
using IronGrind.NpcInteraction;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// The inbound registrations of the five Enhancement Story 010 client-to-server types
    /// (ADR-014 Decision 1 and 3): none is held during an irreversible write, each has its own
    /// <see cref="RpcTypeTag"/> with no rate limit and the body bound of Decision 1.
    /// </summary>
    /// <example>
    /// <code>
    /// EnhancementInboundRegistration.Register(dispatcher, enhancementHandlers, npcHandlers);
    /// // ... other systems register ...
    /// dispatcher.Seal();
    /// </code>
    /// </example>
    public static class EnhancementInboundRegistration
    {
        /// <summary>
        /// Registers <see cref="EnhancementAttemptRequest"/> (6), <see cref="EnhancementPreviewRequest"/> (2),
        /// <see cref="CancelEnhancement"/> (0), <see cref="OpenNPCInteraction"/> (4) and
        /// <see cref="CloseNPCInteraction"/> (0), all with <c>heldDuringIrreversibleWrite: false</c>.
        /// Does not call <c>Seal()</c>: the composition root seals after every system has registered.
        /// </summary>
        /// <param name="dispatcher">The dispatcher to register on.</param>
        /// <param name="enhancementHandlers">The three Enhancement handlers.</param>
        /// <param name="npcHandlers">The two NPC interaction handlers.</param>
        public static void Register(
            IInboundRequestDispatcher dispatcher, EnhancementClientRequestHandlers enhancementHandlers, NpcInteractionRequestHandlers npcHandlers)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            if (enhancementHandlers == null) throw new ArgumentNullException(nameof(enhancementHandlers));
            if (npcHandlers == null) throw new ArgumentNullException(nameof(npcHandlers));

            dispatcher.Register(
                new InboundRequestDescriptor(EnhancementAttemptRequest.MessageTypeId, RpcTypeTag.EnhancementAttemptRequest,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)EnhancementAttemptRequest.BodySize),
                enhancementHandlers.HandleAttemptRequest);
            dispatcher.Register(
                new InboundRequestDescriptor(EnhancementPreviewRequest.MessageTypeId, RpcTypeTag.EnhancementPreviewRequest,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)EnhancementPreviewRequest.BodySize),
                enhancementHandlers.HandlePreviewRequest);
            dispatcher.Register(
                new InboundRequestDescriptor(CancelEnhancement.MessageTypeId, RpcTypeTag.CancelEnhancement,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)CancelEnhancement.BodySize),
                enhancementHandlers.HandleCancel);
            dispatcher.Register(
                new InboundRequestDescriptor(OpenNPCInteraction.MessageTypeId, RpcTypeTag.OpenNPCInteraction,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)OpenNPCInteraction.BodySize),
                npcHandlers.HandleOpen);
            dispatcher.Register(
                new InboundRequestDescriptor(CloseNPCInteraction.MessageTypeId, RpcTypeTag.CloseNPCInteraction,
                    heldDuringIrreversibleWrite: false, maxBodyBytes: (ushort)CloseNPCInteraction.BodySize),
                npcHandlers.HandleClose);
        }
    }
}

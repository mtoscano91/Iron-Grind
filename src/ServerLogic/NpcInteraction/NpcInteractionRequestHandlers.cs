using System;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.NpcInteraction
{
    /// <summary>
    /// The request handlers of <see cref="OpenNPCInteraction"/> and <see cref="CloseNPCInteraction"/>
    /// (Enhancement Story 010, CR-ENH-16, CR-ENH-17, ADR-014 Decision 3): decode the body, call
    /// <see cref="NpcInteractionSessionTracker"/> and answer an open with
    /// <see cref="NPCInteractionOpened"/> or <see cref="RejectedNotInTownHub"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var handlers = new NpcInteractionRequestHandlers(tracker, outbox);
    /// dispatcher.Register(descriptor, handlers.HandleOpen);
    /// </code>
    /// </example>
    public sealed class NpcInteractionRequestHandlers
    {
        private const string LOG_PREFIX = "[NpcInteractionRequestHandlers]";

        private readonly NpcInteractionSessionTracker _tracker;
        private readonly IClientMessageOutbox _outbox;

        /// <summary>Creates the handlers.</summary>
        /// <param name="tracker">The NPC session tracker (Story 006); not null.</param>
        /// <param name="outbox">Server-to-client send seam; not null.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public NpcInteractionRequestHandlers(NpcInteractionSessionTracker tracker, IClientMessageOutbox outbox)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        }

        /// <summary>
        /// Handles <see cref="OpenNPCInteraction"/>: opens the session and sends one
        /// <see cref="NPCInteractionOpened"/> or, outside the town hub, one
        /// <see cref="RejectedNotInTownHub"/> (both with an empty body, not cap-exempt). An open result this
        /// handler does not know fails closed: one <c>UnhandledOpenResult</c> warning and one
        /// <see cref="RejectedNotInTownHub"/>, so the client is never left waiting.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 4-byte body.</param>
        public void HandleOpen(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != OpenNPCInteraction.BodySize
                || !OpenNPCInteractionCodec.TryReadBody(body, out uint npcId))
            {
                LogMalformed(context, body.Length, OpenNPCInteraction.BodySize);
                return;
            }

            // npcId is not validated here: no NPC registry exists and the wire protocol defines no
            // rejection for an unknown id (deferred, TD-058 item 5, npc-shop.md OQ-NS-1).
            NpcInteractionOpenResult result = _tracker.Open(context.CharacterId, npcId);
            switch (result)
            {
                case NpcInteractionOpenResult.NPCInteractionOpened:
                    _outbox.Enqueue(context.ClientId, NPCInteractionOpened.MessageTypeId, ReadOnlySpan<byte>.Empty, false);
                    break;
                case NpcInteractionOpenResult.RejectedNotInTownHub:
                    _outbox.Enqueue(context.ClientId, RejectedNotInTownHub.MessageTypeId, ReadOnlySpan<byte>.Empty, false);
                    break;
                default:
                    // Fail closed: an open result this handler does not know is answered with a rejection so the
                    // client is never left waiting.
                    Debug.LogWarning($"{LOG_PREFIX} UnhandledOpenResult: clientId={context.ClientId} result={result}.");
                    _outbox.Enqueue(context.ClientId, RejectedNotInTownHub.MessageTypeId, ReadOnlySpan<byte>.Empty, false);
                    break;
            }
        }

        /// <summary>Handles <see cref="CloseNPCInteraction"/>: clears the session (AC-ENH-29); sends nothing.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The empty body.</param>
        public void HandleClose(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != CloseNPCInteraction.BodySize || !CloseNPCInteractionCodec.TryReadBody(body))
            {
                LogMalformed(context, body.Length, CloseNPCInteraction.BodySize);
                return;
            }

            _tracker.Close(context.CharacterId);
        }

        private static void LogMalformed(in InboundRequestContext context, int actualLength, int expectedLength)
        {
            Debug.LogWarning($"{LOG_PREFIX} InboundMessageMalformed: clientId={context.ClientId} " +
                $"messageType=0x{context.MessageTypeId:X4} - body length {actualLength}, expected {expectedLength}.");
        }
    }
}

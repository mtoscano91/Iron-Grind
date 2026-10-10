using System;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// The request handlers of the three Enhancement client messages (Enhancement Story 010,
    /// ADR-014 Decision 3: one descriptor and one handler per type): <see cref="EnhancementAttemptRequest"/>,
    /// <see cref="EnhancementPreviewRequest"/> and <see cref="CancelEnhancement"/>. A handler decodes the
    /// body with the message's codec, calls its collaborator on the tick thread and, where the story
    /// says so, enqueues a reply on <see cref="IClientMessageOutbox"/>. No handler keeps
    /// <c>body</c> after it returns, suspends, or reads the mutation gate (ADR-014, ADR-011).
    /// </summary>
    /// <remarks>
    /// This story never sends <see cref="EnhancementRequestReceived"/> and never writes the
    /// <c>LastEnhancementRequestID</c>: both belong to Story 015 (the accepted-attempt path).
    /// </remarks>
    /// <example>
    /// <code>
    /// var handlers = new EnhancementClientRequestHandlers(service, outbox, dedupLookup, starter);
    /// dispatcher.Register(descriptor, handlers.HandlePreviewRequest);
    /// </code>
    /// </example>
    public sealed class EnhancementClientRequestHandlers
    {
        private const string LOG_PREFIX = "[EnhancementClientRequestHandlers]";

        private readonly EnhancementService _service;
        private readonly IClientMessageOutbox _outbox;
        private readonly IEnhancementRequestDedupLookup _dedupLookup;
        private readonly IEnhancementAttemptStarter _starter;

        /// <summary>Creates the handlers over their collaborators.</summary>
        /// <param name="service">Validation and preview (CR-ENH-15 step 2); not null.</param>
        /// <param name="outbox">Server-to-client send seam; not null.</param>
        /// <param name="dedupLookup">Finds a character's deduplicator (EC-NET-9); not null.</param>
        /// <param name="starter">Attempt-start seam (Story 015's production implementation); not null.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EnhancementClientRequestHandlers(
            EnhancementService service, IClientMessageOutbox outbox,
            IEnhancementRequestDedupLookup dedupLookup, IEnhancementAttemptStarter starter)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _dedupLookup = dedupLookup ?? throw new ArgumentNullException(nameof(dedupLookup));
            _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        }

        /// <summary>
        /// Handles <see cref="EnhancementAttemptRequest"/> (UI-ENH-4, CR-ENH-15 steps 1-2, AC-NC-49).
        /// A body that is not exactly <see cref="EnhancementAttemptRequest.BodySize"/> bytes is dropped
        /// with an <c>InboundMessageMalformed</c> warning. A duplicate <c>requestId</c> (equal to the
        /// character's <c>LastEnhancementRequestID</c>) is checked before validation: one
        /// <c>DuplicateEnhancementRequest</c> warning, no reply, no start. A request that fails a step 2
        /// check gets one cap-exempt <see cref="EnhancementAttemptResultMessage"/> rejection (newLevel 0).
        /// A valid request reaches <see cref="IEnhancementAttemptStarter"/> once and gets no reply here.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 6-byte body.</param>
        public void HandleAttemptRequest(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != EnhancementAttemptRequest.BodySize
                || !EnhancementAttemptRequestCodec.TryReadBody(body, out uint requestId, out byte itemSlotIndex, out byte scrollSlotIndex))
            {
                LogMalformed(context, body.Length, EnhancementAttemptRequest.BodySize);
                return;
            }

            // The duplicate check runs before validation (AC-NC-49). No deduplicator = not a duplicate.
            if (_dedupLookup.TryGetDeduplicator(context.CharacterId, out EnhancementRequestDeduplicator deduplicator)
                && deduplicator != null
                && deduplicator.LastEnhancementRequestId.HasValue
                && deduplicator.LastEnhancementRequestId.Value == requestId)
            {
                Debug.LogWarning($"{LOG_PREFIX} DuplicateEnhancementRequest: clientId={context.ClientId} " +
                    $"character={context.CharacterId} requestId={requestId}.");
                return;
            }

            EnhancementPreview validation = _service.PreviewAttempt(context.CharacterId, itemSlotIndex, scrollSlotIndex);
            if (!validation.IsValid)
            {
                Span<byte> buffer = stackalloc byte[EnhancementAttemptResultMessage.BodySize];
                int written = EnhancementAttemptResultMessageCodec.WriteBody(buffer, requestId, validation.Code, 0);
                // Cap-exempt: the rejection releases the waiting client (CR-NET-7.7).
                _outbox.Enqueue(context.ClientId, EnhancementAttemptResultMessage.MessageTypeId, buffer.Slice(0, written), true);
                return;
            }

            _starter.StartAttempt(context.CharacterId, requestId, itemSlotIndex, scrollSlotIndex);
        }

        /// <summary>
        /// Handles <see cref="EnhancementPreviewRequest"/> (UI-ENH-1, AC-NC-48): one
        /// <see cref="EnhancementStateUpdate"/> for a valid selection, one <see cref="EnhancementPreviewRejected"/>
        /// otherwise; both not cap-exempt. Changes no state.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 2-byte body.</param>
        public void HandlePreviewRequest(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != EnhancementPreviewRequest.BodySize
                || !EnhancementPreviewRequestCodec.TryReadBody(body, out byte itemSlotIndex, out byte scrollSlotIndex))
            {
                LogMalformed(context, body.Length, EnhancementPreviewRequest.BodySize);
                return;
            }

            EnhancementPreview preview = _service.PreviewAttempt(context.CharacterId, itemSlotIndex, scrollSlotIndex);
            if (preview.IsValid)
            {
                Span<byte> buffer = stackalloc byte[EnhancementStateUpdate.BodySize];
                int written = EnhancementStateUpdateCodec.WriteBody(buffer, itemSlotIndex, scrollSlotIndex,
                    preview.CurrentLevel, (float)preview.SuccessProbability, (float)preview.DestructionProbability);
                _outbox.Enqueue(context.ClientId, EnhancementStateUpdate.MessageTypeId, buffer.Slice(0, written), false);
            }
            else
            {
                Span<byte> buffer = stackalloc byte[EnhancementPreviewRejected.BodySize];
                int written = EnhancementPreviewRejectedCodec.WriteBody(buffer, itemSlotIndex, scrollSlotIndex, preview.Code);
                _outbox.Enqueue(context.ClientId, EnhancementPreviewRejected.MessageTypeId, buffer.Slice(0, written), false);
            }
        }

        /// <summary>
        /// Handles <see cref="CancelEnhancement"/>. Does nothing beyond the length check: the first tap of
        /// the two-tap flow only selects, so nothing is locked, consumed or remembered on the server and
        /// there is nothing to undo or answer (CR-ENH-6, AC-ENH-6). Cancel after an accepted
        /// <see cref="EnhancementAttemptRequest"/> is likewise ignored: the attempt is irrevocable.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The empty body.</param>
        public void HandleCancel(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != CancelEnhancement.BodySize || !CancelEnhancementCodec.TryReadBody(body))
            {
                LogMalformed(context, body.Length, CancelEnhancement.BodySize);
            }
        }

        private static void LogMalformed(in InboundRequestContext context, int actualLength, int expectedLength)
        {
            Debug.LogWarning($"{LOG_PREFIX} InboundMessageMalformed: clientId={context.ClientId} " +
                $"messageType=0x{context.MessageTypeId:X4} - body length {actualLength}, expected {expectedLength}.");
        }
    }
}

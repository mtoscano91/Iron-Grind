using System;
using System.Collections.Generic;
using IronGrind.AutoAttackCombat;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The request handler of <see cref="SetTarget"/> (Story 036, ADR-014 Decision 3): decodes the
    /// 4-byte body and hands it to <see cref="TargetSlotTracker.ProcessSetTarget"/>. Sends no reply;
    /// what the tracker does with a rejected target is Story 029's behaviour.
    /// </summary>
    /// <example>
    /// <code>
    /// var handler = new SetTargetRequestHandler(tracker, validZoneEntityIds);
    /// dispatcher.Register(descriptor, handler.Handle);
    /// </code>
    /// </example>
    public sealed class SetTargetRequestHandler
    {
        private readonly TargetSlotTracker _tracker;
        private readonly IReadOnlyCollection<EntityID> _validZoneEntityIds;
        private readonly IAutoAttackInputSink _combat;

        /// <summary>Creates the handler.</summary>
        /// <param name="tracker">The target-slot tracker; not null.</param>
        /// <param name="validZoneEntityIds">The zone's live entity set, supplied by the composition root; may be null (then only a deselect succeeds).</param>
        /// <param name="combat">
        /// Auto-Attack Combat seam (Story 006); an accepted target is forwarded to it. Optional: null keeps the
        /// tracker-only behaviour of Networking Core Story 036.
        /// </param>
        public SetTargetRequestHandler(TargetSlotTracker tracker, IReadOnlyCollection<EntityID> validZoneEntityIds,
            IAutoAttackInputSink combat = null)
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            _validZoneEntityIds = validZoneEntityIds;
            _combat = combat;
        }

        /// <summary>
        /// Matches <see cref="InboundRequestHandler"/>. Drops (with a warning) a body that is not
        /// exactly <see cref="SetTarget.BodySize"/> bytes; otherwise sets the target.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 4-byte body.</param>
        public void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (body.Length != SetTarget.BodySize || !SetTargetCodec.TryReadBody(body, out uint targetEntityId))
            {
                Debug.LogWarning($"[SetTargetRequestHandler] InboundMessageMalformed: clientId={context.ClientId} " +
                    $"messageType=0x{context.MessageTypeId:X4} - body length {body.Length}, expected {SetTarget.BodySize}.");
                return;
            }

            var target = new EntityID(targetEntityId);
            SetTargetOutcome outcome = _tracker.ProcessSetTarget(context.ClientId, context.SenderEntityId, target, _validZoneEntityIds);

            // Auto-Attack Combat Story 006: only an accepted SetTarget (select or deselect) reaches Combat. The tracker
            // stays the owner of relevance; Combat applies its own validity check on top.
            if (outcome == SetTargetOutcome.Accepted)
            {
                _combat?.SetTarget(context.SenderEntityId, target);
            }
        }
    }
}

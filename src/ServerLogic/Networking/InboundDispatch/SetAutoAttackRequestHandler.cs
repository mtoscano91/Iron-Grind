using System;
using IronGrind.AutoAttackCombat;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The request handler of <see cref="SetAutoAttack"/> (Auto-Attack Combat Story 006, ADR-014 Decision 3,
    /// <c>design/gdd/auto-attack-combat.md</c> Rule 1): decodes the strict 1-byte body and forwards the toggle to
    /// Combat. Sends no reply. Runs in the dispatch step, so the toggle applies before the same tick's fire check (EC-8).
    /// A dead player is blocked by Combat's prohibited state, not here.
    /// </summary>
    /// <example>
    /// <code>
    /// var handler = new SetAutoAttackRequestHandler(combat);
    /// dispatcher.Register(descriptor, handler.Handle);
    /// </code>
    /// </example>
    public sealed class SetAutoAttackRequestHandler
    {
        private readonly IAutoAttackInputSink _combat;

        /// <summary>Creates the handler.</summary>
        /// <param name="combat">The Combat seam; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="combat"/> is null.</exception>
        public SetAutoAttackRequestHandler(IAutoAttackInputSink combat)
        {
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        }

        /// <summary>
        /// Matches <see cref="InboundRequestHandler"/>. A body that is not exactly one byte of 0 or 1 is dropped with
        /// a warning and changes no state; otherwise the toggle is forwarded. Allocates nothing on the accept path.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 1-byte body.</param>
        public void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (!SetAutoAttackCodec.TryReadBody(body, out bool enabled))
            {
                Debug.LogWarning($"[SetAutoAttackRequestHandler] InboundMessageMalformed: clientId={context.ClientId} " +
                    $"messageType=0x{context.MessageTypeId:X4} - body length {body.Length}, expected {SetAutoAttack.BodySize} byte of 0 or 1.");
                return;
            }

            _combat.SetAutoAttack(context.SenderEntityId, enabled);
        }
    }
}

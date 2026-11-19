using System;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Inbound handlers of <see cref="PartyLeaveRequest"/> and <see cref="PartyDisbandRequest"/> (Party System
    /// Story 008, ADR-014 Decision 3). Both bodies are empty; each handler resolves the sender character from the
    /// envelope entity and only queues the request in <see cref="PartyMembershipStep"/> (applied after the
    /// simulation phase). A sender that is not registered is ignored silently. The dispatcher already rejects any
    /// body longer than the registered maximum of 0.
    /// </summary>
    public sealed class PartyLeaveDisbandClientRequestHandlers
    {
        private readonly PartyRegistry _registry;
        private readonly PartyMembershipStep _step;

        /// <summary>Creates the handlers.</summary>
        /// <param name="registry">Resolves the sender entity to its character.</param>
        /// <param name="step">The membership step that queues the requests.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PartyLeaveDisbandClientRequestHandlers(PartyRegistry registry, PartyMembershipStep step)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _step = step ?? throw new ArgumentNullException(nameof(step));
        }

        /// <summary>Handles a <see cref="PartyLeaveRequest"/>.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The empty body.</param>
        public void HandleLeave(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (_registry.TryGetCharacter(context.SenderEntityId, out CharacterID sender))
            {
                _step.EnqueueLeave(sender);
            }
        }

        /// <summary>Handles a <see cref="PartyDisbandRequest"/>.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The empty body.</param>
        public void HandleDisband(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (_registry.TryGetCharacter(context.SenderEntityId, out CharacterID sender))
            {
                _step.EnqueueDisband(sender);
            }
        }
    }
}

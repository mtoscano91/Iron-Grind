using System;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Inbound handlers of <see cref="PartyInviteRequest"/> and <see cref="PartyInviteResponse"/> (Party System
    /// Story 007, ADR-014 Decision 3). Each decodes the body with the message codec and calls
    /// <see cref="PartyInviteService"/> with the envelope's sender entity and the dispatch tick. A body the codec
    /// rejects (too short, or a response whose accepted byte is not 0 or 1) logs one warning and changes nothing.
    /// The dispatcher already rejects a body longer than the registered maximum.
    /// </summary>
    public sealed class PartyInviteClientRequestHandlers
    {
        private readonly PartyInviteService _service;

        /// <summary>Creates the handlers.</summary>
        /// <param name="service">The invite service.</param>
        /// <exception cref="ArgumentNullException"><paramref name="service"/> is null.</exception>
        public PartyInviteClientRequestHandlers(PartyInviteService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>Handles a <see cref="PartyInviteRequest"/>.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 4-byte body.</param>
        public void HandleInviteRequest(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (!PartyInviteRequest.TryRead(body, out PartyInviteRequest request))
            {
                LogMalformed(context, body.Length);
                return;
            }

            _service.HandleInvite(context.SenderEntityId, new CharacterID(request.TargetCharacterId),
                context.DispatchTick);
        }

        /// <summary>Handles a <see cref="PartyInviteResponse"/>.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 5-byte body.</param>
        public void HandleInviteResponse(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (!PartyInviteResponse.TryRead(body, out PartyInviteResponse response))
            {
                LogMalformed(context, body.Length);
                return;
            }

            _service.HandleResponse(context.SenderEntityId, new CharacterID(response.InviterCharacterId),
                response.Accepted, context.DispatchTick);
        }

        private static void LogMalformed(in InboundRequestContext context, int length)
        {
            Debug.LogWarning(PartyInviteService.LOG_PREFIX + "malformed body: clientId=" + context.ClientId
                + " messageType=0x" + context.MessageTypeId.ToString("X4") + " length=" + length + ".");
        }
    }
}

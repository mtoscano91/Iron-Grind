using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// What a request handler is told about the request it receives (ADR-014 Decision 3). The body
    /// is passed separately, as a span valid only for the duration of the call.
    /// </summary>
    /// <remarks>
    /// <see cref="ArrivalTick"/> and <see cref="DispatchTick"/> are <c>ServerTickLoop.ServerTickNumber</c>
    /// values, never the transport's own tick counter.
    /// </remarks>
    /// <example>
    /// <code>
    /// void Handle(in InboundRequestContext context, ReadOnlySpan&lt;byte&gt; body)
    /// {
    ///     if (context.WasHeld) { /* released after an irreversible write */ }
    /// }
    /// </code>
    /// </example>
    public readonly struct InboundRequestContext
    {
        /// <summary>The connection the request came from.</summary>
        public readonly uint ClientId;

        /// <summary>The <c>SenderEntityID</c> of the envelope (already accepted by the guard chain).</summary>
        public readonly EntityID SenderEntityId;

        /// <summary>The character the connection plays, resolved through <see cref="IConnectionCharacterDirectory"/>.</summary>
        public readonly CharacterID CharacterId;

        /// <summary>The wire <c>MessageTypeID</c>.</summary>
        public readonly ushort MessageTypeId;

        /// <summary><see cref="IServerTickSource.ServerTickNumber"/> when <c>TryAccept</c> ran.</summary>
        public readonly uint ArrivalTick;

        /// <summary>The tick on which the handler runs (the argument of <c>DispatchTick</c>).</summary>
        public readonly uint DispatchTick;

        /// <summary>True when the request was held and released by Pass A.</summary>
        public readonly bool WasHeld;

        /// <summary>Creates a request context.</summary>
        public InboundRequestContext(uint clientId, EntityID senderEntityId, CharacterID characterId,
            ushort messageTypeId, uint arrivalTick, uint dispatchTick, bool wasHeld)
        {
            ClientId = clientId;
            SenderEntityId = senderEntityId;
            CharacterId = characterId;
            MessageTypeId = messageTypeId;
            ArrivalTick = arrivalTick;
            DispatchTick = dispatchTick;
            WasHeld = wasHeld;
        }
    }
}

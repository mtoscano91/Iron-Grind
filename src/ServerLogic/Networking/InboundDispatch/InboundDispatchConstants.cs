namespace IronGrind.Networking
{
    /// <summary>
    /// Structural constants of the inbound request dispatcher (ADR-014 Decisions 2, 3 and 5,
    /// Networking Core Story 036). Compile-time constants, in line with this folder's other
    /// structural constants. <c>MAX_HELD_REQUESTS_PER_CHARACTER</c> stays in
    /// <see cref="TickCompletionConstants"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// int largestRequestBody = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES - ClientEntityMessageEnvelope.WireSize; // 386
    /// </code>
    /// </example>
    public static class InboundDispatchConstants
    {
        /// <summary>Most undispatched requests one connection may have in the inbox (ADR-014 Decision 2, step 6).</summary>
        public const int MAX_INBOX_REQUESTS_PER_CONNECTION = 64;

        /// <summary>Size of one connection's segment of the inbox arena, in bytes (ADR-014 Decision 2, step 6).</summary>
        public const int MAX_INBOX_BYTES_PER_CONNECTION = 4096;

        /// <summary>
        /// Longest client-to-server message the adapter passes on: the 14-byte envelope with
        /// <c>SenderEntityID</c> plus the 386-byte <c>PartyChatRequest</c> body (ADR-014 Decision 2).
        /// </summary>
        public const int MAX_INBOUND_MESSAGE_BYTES = 400;

        /// <summary>Longest body of a held request type, in bytes (ADR-014 Decision 3 and Decision 5).</summary>
        public const int MAX_HELD_BODY_BYTES = 64;
    }
}

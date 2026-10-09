namespace IronGrind.Networking
{
    /// <summary>
    /// What the intake needs to know about one connection-level message type (ADR-014 Decisions 1
    /// and 3). A connection-level message goes to an <see cref="IConnectionMessageSink"/> inside
    /// <c>TryAccept</c>; it is never guarded, held or queued.
    /// </summary>
    /// <example>
    /// <code>
    /// var descriptor = new ConnectionMessageDescriptor(HeartbeatMessage.MessageTypeId, maxBodyBytes: 0, carriesSenderEntityId: false);
    /// </code>
    /// </example>
    public readonly struct ConnectionMessageDescriptor
    {
        /// <summary>The wire <c>MessageTypeID</c>.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>Longest body, in bytes, after the envelope.</summary>
        public readonly ushort MaxBodyBytes;

        /// <summary>True when the 4 bytes after the 10-byte envelope are a <c>SenderEntityID</c> (false: <c>HeartbeatMessage</c>, <c>SessionHandshake</c>).</summary>
        public readonly bool CarriesSenderEntityId;

        /// <summary>Creates a connection-level message descriptor.</summary>
        /// <param name="messageTypeId">The wire <c>MessageTypeID</c>.</param>
        /// <param name="maxBodyBytes">Longest body in bytes.</param>
        /// <param name="carriesSenderEntityId">Whether the envelope is the 14-byte one.</param>
        public ConnectionMessageDescriptor(ushort messageTypeId, ushort maxBodyBytes, bool carriesSenderEntityId)
        {
            MessageTypeId = messageTypeId;
            MaxBodyBytes = maxBodyBytes;
            CarriesSenderEntityId = carriesSenderEntityId;
        }
    }
}

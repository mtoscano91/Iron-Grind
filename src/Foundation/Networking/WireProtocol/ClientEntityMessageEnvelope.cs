using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server message envelope for messages that reference a runtime entity (CR-NET-7.1).
    /// Extends the base 10-byte envelope with <c>SenderEntityID</c>. Wire layout (little-endian,
    /// fixed offsets, <see cref="WireSize"/> = 14 bytes total): <c>MessageTypeID</c> (offset 0,
    /// 2 bytes), <c>SequenceNumber</c> (offset 2, 4 bytes), <c>ServerTickNumber</c> (offset 6,
    /// 4 bytes), <c>SenderEntityID</c> (offset 10, 4 bytes).
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="MessageEnvelopeCodec"/>. Use <see cref="ServerMessageEnvelope"/>
    /// instead for server-originated messages or client-to-server messages that do not reference a
    /// runtime entity (10 bytes total).
    /// </remarks>
    /// <example>
    /// <code>
    /// var envelope = new ClientEntityMessageEnvelope(messageTypeId: 0xE010, sequenceNumber: 7u, serverTickNumber: 1000u, senderEntityId: 501u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[ClientEntityMessageEnvelope.WireSize];
    /// MessageEnvelopeCodec.Write(buffer, in envelope);
    /// </code>
    /// </example>
    public readonly struct ClientEntityMessageEnvelope : IEquatable<ClientEntityMessageEnvelope>
    {
        /// <summary>Total wire size of this envelope, in bytes (CR-NET-7.1).</summary>
        public const int WireSize = 14;

        /// <summary>Identifies the message schema; dispatches to the correct deserializer.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>
        /// Monotonically increasing per-connection counter, shared across all message types sent
        /// by this endpoint. Starts at 1; 0 = uninitialized and must never appear in a valid message.
        /// </summary>
        public readonly uint SequenceNumber;

        /// <summary>The server tick during which this message was authored; never wall-clock time.</summary>
        public readonly uint ServerTickNumber;

        /// <summary>The entity ID of the sending client's controlled entity.</summary>
        public readonly uint SenderEntityId;

        /// <summary>Initializes a new <see cref="ClientEntityMessageEnvelope"/>.</summary>
        /// <example>
        /// <code>
        /// var envelope = new ClientEntityMessageEnvelope(0xE010, 7u, 1000u, 501u);
        /// </code>
        /// </example>
        public ClientEntityMessageEnvelope(ushort messageTypeId, uint sequenceNumber, uint serverTickNumber, uint senderEntityId)
        {
            MessageTypeId = messageTypeId;
            SequenceNumber = sequenceNumber;
            ServerTickNumber = serverTickNumber;
            SenderEntityId = senderEntityId;
        }

        /// <inheritdoc/>
        public bool Equals(ClientEntityMessageEnvelope other)
            => MessageTypeId == other.MessageTypeId
            && SequenceNumber == other.SequenceNumber
            && ServerTickNumber == other.ServerTickNumber
            && SenderEntityId == other.SenderEntityId;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ClientEntityMessageEnvelope other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = MessageTypeId.GetHashCode();
            h = (h * 397) ^ SequenceNumber.GetHashCode();
            h = (h * 397) ^ ServerTickNumber.GetHashCode();
            h = (h * 397) ^ SenderEntityId.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both envelopes have identical field values.</summary>
        public static bool operator ==(ClientEntityMessageEnvelope left, ClientEntityMessageEnvelope right) => left.Equals(right);

        /// <summary>Returns true if the envelopes differ in any field.</summary>
        public static bool operator !=(ClientEntityMessageEnvelope left, ClientEntityMessageEnvelope right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ClientEntityMessageEnvelope(TypeId={MessageTypeId}, Seq={SequenceNumber}, Tick={ServerTickNumber}, SenderEntityId={SenderEntityId})";
    }
}

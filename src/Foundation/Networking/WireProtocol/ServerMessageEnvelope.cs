using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-originated message envelope (CR-NET-7.1). Prepended to every message the server
    /// sends to a client. Wire layout (little-endian, fixed offsets, <see cref="WireSize"/> = 10
    /// bytes total): <c>MessageTypeID</c> (offset 0, 2 bytes), <c>SequenceNumber</c> (offset 2,
    /// 4 bytes), <c>ServerTickNumber</c> (offset 6, 4 bytes).
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> — zero heap allocation on the hot serialization path.
    /// Encoded/decoded via <see cref="MessageEnvelopeCodec"/>. For client-to-server messages that
    /// reference a runtime entity, use <see cref="ClientEntityMessageEnvelope"/> instead (adds
    /// <c>SenderEntityID</c>, 14 bytes total).
    /// </remarks>
    /// <example>
    /// <code>
    /// var envelope = new ServerMessageEnvelope(messageTypeId: 0x0201, sequenceNumber: 42u, serverTickNumber: 1000u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[ServerMessageEnvelope.WireSize];
    /// MessageEnvelopeCodec.Write(buffer, in envelope);
    /// </code>
    /// </example>
    public readonly struct ServerMessageEnvelope : IEquatable<ServerMessageEnvelope>
    {
        /// <summary>Total wire size of this envelope, in bytes (CR-NET-7.1).</summary>
        public const int WireSize = 10;

        /// <summary>Identifies the message schema; dispatches to the correct deserializer.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>
        /// Monotonically increasing per-connection counter, shared across all message types sent
        /// by this endpoint (one counter per connection, not one per message type). Starts at 1;
        /// 0 = uninitialized and must never appear in a valid message.
        /// </summary>
        public readonly uint SequenceNumber;

        /// <summary>The server tick during which this message was authored; never wall-clock time.</summary>
        public readonly uint ServerTickNumber;

        /// <summary>Initializes a new <see cref="ServerMessageEnvelope"/>.</summary>
        /// <example>
        /// <code>
        /// var envelope = new ServerMessageEnvelope(0x0201, 42u, 1000u);
        /// </code>
        /// </example>
        public ServerMessageEnvelope(ushort messageTypeId, uint sequenceNumber, uint serverTickNumber)
        {
            MessageTypeId = messageTypeId;
            SequenceNumber = sequenceNumber;
            ServerTickNumber = serverTickNumber;
        }

        /// <inheritdoc/>
        public bool Equals(ServerMessageEnvelope other)
            => MessageTypeId == other.MessageTypeId
            && SequenceNumber == other.SequenceNumber
            && ServerTickNumber == other.ServerTickNumber;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ServerMessageEnvelope other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = MessageTypeId.GetHashCode();
            h = (h * 397) ^ SequenceNumber.GetHashCode();
            h = (h * 397) ^ ServerTickNumber.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both envelopes have identical field values.</summary>
        public static bool operator ==(ServerMessageEnvelope left, ServerMessageEnvelope right) => left.Equals(right);

        /// <summary>Returns true if the envelopes differ in any field.</summary>
        public static bool operator !=(ServerMessageEnvelope left, ServerMessageEnvelope right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => $"ServerMessageEnvelope(TypeId={MessageTypeId}, Seq={SequenceNumber}, Tick={ServerTickNumber})";
    }
}

using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Allocation-free encode/decode for the CR-NET-7.1 message envelope
    /// (<see cref="ServerMessageEnvelope"/> and <see cref="ClientEntityMessageEnvelope"/>).
    /// Operates directly on <see cref="Span{T}"/>/<see cref="ReadOnlySpan{T}"/> byte buffers using
    /// <see cref="BinaryPrimitives"/> little-endian writers/readers — never <see cref="BitConverter"/>
    /// (endianness is runtime-dependent) and never manual bit-shifting.
    /// </summary>
    /// <remarks>
    /// <c>Write</c> assumes a correctly-sized destination and lets <see cref="BinaryPrimitives"/>
    /// throw its own <see cref="ArgumentOutOfRangeException"/> if the span is too small — an
    /// undersized destination is a caller sizing bug, not a recoverable network condition.
    /// <c>TryRead</c> instead returns <see langword="false"/> on a too-short source, because decode
    /// paths receive untrusted/possibly-truncated network data and must never throw on it.
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[ServerMessageEnvelope.WireSize];
    /// MessageEnvelopeCodec.Write(buffer, new ServerMessageEnvelope(0x0201, 42u, 1000u));
    /// if (MessageEnvelopeCodec.TryRead(buffer, out ServerMessageEnvelope decoded))
    /// {
    ///     // decoded.MessageTypeId == 0x0201
    /// }
    /// </code>
    /// </example>
    public static class MessageEnvelopeCodec
    {
        /// <summary>
        /// Writes a <see cref="ServerMessageEnvelope"/> to <paramref name="destination"/> at offset 0
        /// (<see cref="ServerMessageEnvelope.WireSize"/> bytes: MessageTypeID[0-1], SequenceNumber[2-5],
        /// ServerTickNumber[6-9], little-endian).
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[ServerMessageEnvelope.WireSize];
        /// MessageEnvelopeCodec.Write(buffer, new ServerMessageEnvelope(0x0201, 42u, 1000u));
        /// </code>
        /// </example>
        public static void Write(Span<byte> destination, in ServerMessageEnvelope envelope)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, envelope.MessageTypeId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(2, 4), envelope.SequenceNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(6, 4), envelope.ServerTickNumber);
        }

        /// <summary>
        /// Attempts to decode a <see cref="ServerMessageEnvelope"/> from <paramref name="source"/>.
        /// Returns <see langword="false"/> (and a default envelope) if <paramref name="source"/> is
        /// shorter than <see cref="ServerMessageEnvelope.WireSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// if (MessageEnvelopeCodec.TryRead(receivedBytes, out ServerMessageEnvelope envelope))
        /// {
        ///     Dispatch(envelope.MessageTypeId, receivedBytes.Slice(ServerMessageEnvelope.WireSize));
        /// }
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ServerMessageEnvelope envelope)
        {
            if (source.Length < ServerMessageEnvelope.WireSize)
            {
                envelope = default;
                return false;
            }

            ushort messageTypeId = BinaryPrimitives.ReadUInt16LittleEndian(source);
            uint sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(2, 4));
            uint serverTickNumber = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(6, 4));
            envelope = new ServerMessageEnvelope(messageTypeId, sequenceNumber, serverTickNumber);
            return true;
        }

        /// <summary>
        /// Writes a <see cref="ClientEntityMessageEnvelope"/> to <paramref name="destination"/> at
        /// offset 0 (<see cref="ClientEntityMessageEnvelope.WireSize"/> bytes: the 10-byte base
        /// envelope followed by SenderEntityID[10-13], little-endian).
        /// </summary>
        /// <example>
        /// <code>
        /// Span&lt;byte&gt; buffer = stackalloc byte[ClientEntityMessageEnvelope.WireSize];
        /// MessageEnvelopeCodec.Write(buffer, new ClientEntityMessageEnvelope(0xE010, 7u, 1000u, 501u));
        /// </code>
        /// </example>
        public static void Write(Span<byte> destination, in ClientEntityMessageEnvelope envelope)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, envelope.MessageTypeId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(2, 4), envelope.SequenceNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(6, 4), envelope.ServerTickNumber);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(10, 4), envelope.SenderEntityId);
        }

        /// <summary>
        /// Attempts to decode a <see cref="ClientEntityMessageEnvelope"/> from <paramref name="source"/>.
        /// Returns <see langword="false"/> (and a default envelope) if <paramref name="source"/> is
        /// shorter than <see cref="ClientEntityMessageEnvelope.WireSize"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// if (MessageEnvelopeCodec.TryRead(receivedBytes, out ClientEntityMessageEnvelope envelope))
        /// {
        ///     Dispatch(envelope.MessageTypeId, envelope.SenderEntityId, receivedBytes.Slice(ClientEntityMessageEnvelope.WireSize));
        /// }
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ClientEntityMessageEnvelope envelope)
        {
            if (source.Length < ClientEntityMessageEnvelope.WireSize)
            {
                envelope = default;
                return false;
            }

            ushort messageTypeId = BinaryPrimitives.ReadUInt16LittleEndian(source);
            uint sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(2, 4));
            uint serverTickNumber = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(6, 4));
            uint senderEntityId = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(10, 4));
            envelope = new ClientEntityMessageEnvelope(messageTypeId, sequenceNumber, serverTickNumber, senderEntityId);
            return true;
        }
    }
}

using System;
using System.Buffers.Binary;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encodes/decodes the standalone R-OD <see cref="SetTarget"/> RPC (Networking Core Story 029,
    /// RFR-3a): the 14-byte <see cref="ClientEntityMessageEnvelope"/> immediately followed by the
    /// 4-byte <c>targetEntityId</c> body field.
    /// </summary>
    /// <remarks>
    /// <b>CRITICAL — do NOT use <see cref="WireIdCodec.SerializeEntityId"/> for <c>targetEntityId</c>.</b>
    /// <see cref="WireIdCodec.SerializeEntityId"/> throws <see cref="InvalidOperationException"/> on a
    /// zero-valued <see cref="IronGrind.CharacterStats.EntityID"/> (CR-NET-7.3's zero-write guard) —
    /// but RFR-3a's wire contract explicitly makes <c>targetEntityId = 0</c> a legitimate, meaningful
    /// value ("EntityID = 0 means deselect current target"). This is the one field in the whole wire
    /// protocol where <c>0</c> is valid on the wire. <c>targetEntityId</c> is therefore written/read
    /// as a raw <see cref="uint"/> via <see cref="BinaryPrimitives"/> directly, bypassing
    /// <see cref="WireIdCodec"/> entirely for this one field — do not "fix" this to route through
    /// <see cref="WireIdCodec"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[SetTarget.WireSize];
    /// SetTargetCodec.Write(buffer, sequenceNumber: 7u, tickNumber: 1000u, senderEntityId: 501u, targetEntityId: 42u);
    /// bool ok = SetTargetCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out uint targetEntityId);
    /// </code>
    /// </example>
    public static class SetTargetCodec
    {
        /// <summary>
        /// Writes a <see cref="SetTarget"/> message to <paramref name="destination"/> at offset 0.
        /// </summary>
        /// <param name="destination">The destination buffer — must be at least <see cref="SetTarget.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this message.</param>
        /// <param name="tickNumber">The client-observed tick this <see cref="SetTarget"/> was authored on.</param>
        /// <param name="senderEntityId">The sending client's own <c>EntityID</c> (envelope field, ADR-004 Decision 4).</param>
        /// <param name="targetEntityId">
        /// The entity to target, or <c>0</c> to deselect (RFR-3a). Written as a raw <see cref="uint"/>
        /// — see type-level remarks on why <see cref="WireIdCodec"/> is not used here.
        /// </param>
        /// <returns><see cref="SetTarget.WireSize"/> (18), always.</returns>
        /// <example>
        /// <code>
        /// int written = SetTargetCodec.Write(buffer, sequenceNumber: 7u, tickNumber: 1000u, senderEntityId: 501u, targetEntityId: 42u);
        /// </code>
        /// </example>
        public static int Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, uint senderEntityId, uint targetEntityId)
        {
            var envelope = new ClientEntityMessageEnvelope(SetTarget.MessageTypeId, sequenceNumber, tickNumber, senderEntityId);
            MessageEnvelopeCodec.Write(destination, in envelope);
            int offset = ClientEntityMessageEnvelope.WireSize;

            // Deliberately NOT WireIdCodec.SerializeEntityId — see type-level remarks. targetEntityId
            // == 0 is a legitimate "deselect" value on this one field and must never throw.
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), targetEntityId);
            offset += 4;

            return offset;
        }

        /// <summary>
        /// Attempts to decode a <see cref="SetTarget"/> message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is too short, or if the
        /// decoded envelope's <c>MessageTypeId</c> does not match <see cref="SetTarget.MessageTypeId"/>
        /// (wrong dispatch, matching <see cref="SelfDamageEventCodec.TryRead"/>'s own convention).
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = SetTargetCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out uint targetEntityId);
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ClientEntityMessageEnvelope envelope, out uint targetEntityId)
        {
            if (!MessageEnvelopeCodec.TryRead(source, out envelope) || envelope.MessageTypeId != SetTarget.MessageTypeId)
            {
                envelope = default;
                targetEntityId = 0;
                return false;
            }

            ReadOnlySpan<byte> body = source.Slice(ClientEntityMessageEnvelope.WireSize);
            if (body.Length < SetTarget.BodySize)
            {
                targetEntityId = 0;
                return false;
            }

            // Deliberately NOT WireIdCodec.DeserializeEntityId — a raw uint read matches the raw
            // uint write above (0 must round-trip as 0, never rejected).
            targetEntityId = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(0, 4));
            return true;
        }
    }
}

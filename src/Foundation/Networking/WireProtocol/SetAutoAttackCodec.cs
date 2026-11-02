using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encodes/decodes the standalone R-OD <see cref="SetAutoAttack"/> RPC (Auto-Attack Combat Story 006,
    /// <c>design/gdd/auto-attack-combat.md</c> Rule 1): the 14-byte <see cref="ClientEntityMessageEnvelope"/>
    /// followed by one strict body byte (0 = OFF, 1 = ON; any other value is malformed).
    /// </summary>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[SetAutoAttack.WireSize];
    /// SetAutoAttackCodec.Write(buffer, 7u, 1000u, 501u, enabled: true);
    /// bool ok = SetAutoAttackCodec.TryRead(buffer, out ClientEntityMessageEnvelope envelope, out bool enabled);
    /// </code>
    /// </example>
    public static class SetAutoAttackCodec
    {
        private const byte OFF_BYTE = 0;
        private const byte ON_BYTE = 1;

        /// <summary>Writes a <see cref="SetAutoAttack"/> message to <paramref name="destination"/> at offset 0.</summary>
        /// <param name="destination">At least <see cref="SetAutoAttack.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">The outbound envelope sequence number.</param>
        /// <param name="tickNumber">The client-observed tick the message was authored on.</param>
        /// <param name="senderEntityId">The sending client's own <c>EntityID</c> (envelope field).</param>
        /// <param name="enabled">The new toggle value.</param>
        /// <returns><see cref="SetAutoAttack.WireSize"/> (15), always.</returns>
        public static int Write(Span<byte> destination, uint sequenceNumber, uint tickNumber, uint senderEntityId, bool enabled)
        {
            var envelope = new ClientEntityMessageEnvelope(SetAutoAttack.MessageTypeId, sequenceNumber, tickNumber, senderEntityId);
            MessageEnvelopeCodec.Write(destination, in envelope);
            destination[ClientEntityMessageEnvelope.WireSize] = enabled ? ON_BYTE : OFF_BYTE;
            return SetAutoAttack.WireSize;
        }

        /// <summary>
        /// Attempts to decode a whole <see cref="SetAutoAttack"/> message. Returns false when the envelope cannot be
        /// read, the type id differs, or the body is malformed (see <see cref="TryReadBody"/>).
        /// </summary>
        /// <param name="source">The message bytes.</param>
        /// <param name="envelope">The decoded envelope, default on failure.</param>
        /// <param name="enabled">The decoded toggle, false on failure.</param>
        /// <returns>True when the message is well formed.</returns>
        public static bool TryRead(ReadOnlySpan<byte> source, out ClientEntityMessageEnvelope envelope, out bool enabled)
        {
            if (!MessageEnvelopeCodec.TryRead(source, out envelope) || envelope.MessageTypeId != SetAutoAttack.MessageTypeId)
            {
                envelope = default;
                enabled = false;
                return false;
            }

            return TryReadBody(source.Slice(ClientEntityMessageEnvelope.WireSize), out enabled);
        }

        /// <summary>
        /// Attempts to decode the 1-byte body on its own (the inbound dispatcher hands the handler the body only).
        /// Strict: the length must be exactly <see cref="SetAutoAttack.BodySize"/> and the byte 0 or 1.
        /// </summary>
        /// <param name="body">The body bytes (after the envelope).</param>
        /// <param name="enabled">The decoded toggle, false on failure.</param>
        /// <returns>True when the body is exactly one byte of 0 or 1.</returns>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out bool enabled)
        {
            enabled = false;
            if (body.Length != SetAutoAttack.BodySize || body[0] > ON_BYTE)
            {
                return false;
            }

            enabled = body[0] == ON_BYTE;
            return true;
        }
    }
}

using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Encodes/decodes the Story 026 standalone R-OD forced-delivery
    /// <see cref="GoldSyncEventForcedDelivery"/> message (MCR-4): the 10-byte
    /// <see cref="ServerMessageEnvelope"/> immediately followed by the 13-byte
    /// <see cref="GoldSyncEvent"/> body — no batch header, no sub-message framing
    /// (<see cref="BatchSubMessageFraming"/> is never used here; contrast with
    /// <see cref="BatchSubMessageCodec.WriteGoldSyncEvent"/>'s R-U batch sub-message shape).
    /// </summary>
    /// <remarks>
    /// <c>newBalance</c> is written/read via the exact same
    /// <see cref="BatchSubMessageCodec.WriteGoldSyncEventBody"/> /
    /// <see cref="BatchSubMessageCodec.TryReadGoldSyncEventBody"/> helpers the R-U batch path uses —
    /// the absolute-value invariant (never a delta, AC-NC-19) is therefore structurally guaranteed to
    /// match the R-U path, not just documented to match it.
    /// </remarks>
    /// <example>
    /// <code>
    /// var goldSync = new GoldSyncEvent(new CharacterID(7), newBalance: 250u, version: 4u, GoldTransactionReason.MonsterDrop);
    /// Span&lt;byte&gt; buffer = stackalloc byte[GoldSyncEventForcedDelivery.WireSize];
    /// int written = GoldSyncForcedDeliveryCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in goldSync);
    /// bool ok = GoldSyncForcedDeliveryCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out GoldSyncEvent decoded);
    /// </code>
    /// </example>
    public static class GoldSyncForcedDeliveryCodec
    {
        /// <summary>
        /// Writes the standalone forced-delivery message to <paramref name="destination"/> at offset 0.
        /// </summary>
        /// <param name="destination">The destination buffer — must be at least <see cref="GoldSyncEventForcedDelivery.WireSize"/> bytes.</param>
        /// <param name="sequenceNumber">The outbound envelope <c>SequenceNumber</c> for this message.</param>
        /// <param name="tickNumber">The server tick this forced delivery was authored on.</param>
        /// <param name="goldSyncEvent">
        /// The character's current absolute gold balance and version (same fields as the R-U path;
        /// never re-encoded as a delta — AC-NC-19).
        /// </param>
        /// <returns><see cref="GoldSyncEventForcedDelivery.WireSize"/> (23), always.</returns>
        /// <example>
        /// <code>
        /// int written = GoldSyncForcedDeliveryCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in goldSync);
        /// </code>
        /// </example>
        public static int Write(
            Span<byte> destination,
            uint sequenceNumber,
            uint tickNumber,
            in GoldSyncEvent goldSyncEvent
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            var envelope = new ServerMessageEnvelope(GoldSyncEventForcedDelivery.MessageTypeId, sequenceNumber, tickNumber);
            MessageEnvelopeCodec.Write(destination, in envelope);
            int offset = ServerMessageEnvelope.WireSize;
            offset += BatchSubMessageCodec.WriteGoldSyncEventBody(destination.Slice(offset), in goldSyncEvent);

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnServerGoldSyncForcedDeliveryEmitted(goldSyncEvent.CharacterId.RawValue, goldSyncEvent.NewBalance, goldSyncEvent.Version, tickNumber);
#endif

            return offset;
        }

        /// <summary>
        /// Attempts to decode a standalone forced-delivery message from <paramref name="source"/>.
        /// Returns <see langword="false"/> if <paramref name="source"/> is too short, or if the
        /// decoded envelope's <c>MessageTypeId</c> does not match
        /// <see cref="GoldSyncEventForcedDelivery.MessageTypeId"/> (wrong dispatch — a caller routing
        /// the wrong buffer here rather than a network condition, but returned rather than thrown
        /// since decode paths receive untrusted/possibly-truncated data, matching
        /// <see cref="MessageEnvelopeCodec.TryRead"/>'s own convention).
        /// </summary>
        /// <example>
        /// <code>
        /// bool ok = GoldSyncForcedDeliveryCodec.TryRead(buffer, out ServerMessageEnvelope envelope, out GoldSyncEvent decoded);
        /// </code>
        /// </example>
        public static bool TryRead(ReadOnlySpan<byte> source, out ServerMessageEnvelope envelope, out GoldSyncEvent goldSyncEvent)
        {
            if (!MessageEnvelopeCodec.TryRead(source, out envelope) || envelope.MessageTypeId != GoldSyncEventForcedDelivery.MessageTypeId)
            {
                envelope = default;
                goldSyncEvent = default;
                return false;
            }

            return BatchSubMessageCodec.TryReadGoldSyncEventBody(
                source.Slice(ServerMessageEnvelope.WireSize), GoldSyncEventForcedDelivery.MessageTypeId, out goldSyncEvent);
        }
    }
}

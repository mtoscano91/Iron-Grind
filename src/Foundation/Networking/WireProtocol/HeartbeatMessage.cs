using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server keep-alive with no body fields (CR-NET-7.10, GAP-1). Wire size is exactly
    /// <see cref="ServerMessageEnvelope.WireSize"/> (10 bytes) — envelope only, no payload. Sent
    /// standalone on the U-U channel (never batched); mere receipt at the transport layer resets
    /// the server's inactivity timeout counter for the sending session. The client sends one
    /// <see cref="HeartbeatMessage"/> every <see cref="HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS"/>
    /// when no other outbound packet has been sent in the preceding interval — see
    /// <see cref="HeartbeatActivityTracker"/> for the skip-on-activity scheduling logic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="readonly struct"/> — zero heap allocation, matching every other message type
    /// in this folder, even though this type carries no fields. It exists as its own small type
    /// (rather than a bare <see cref="ushort"/> constant) purely for discoverability and to match
    /// this folder's one-type-per-message-type convention (see <see cref="DamageEvent"/> /
    /// <see cref="GoldSyncEvent"/> — Story 007 — even though both of those carry real bodies and
    /// this does not), and to serve as the template for future "standalone, unbatched, U-U"
    /// message types other stories may reuse the pattern from.
    /// </para>
    /// <para>
    /// <b>No dedicated codec:</b> because this message has no body, it is encoded/decoded directly
    /// via the existing <see cref="ServerMessageEnvelope"/> + <see cref="MessageEnvelopeCodec"/>
    /// pair — <see cref="CreateEnvelope"/> is a convenience factory, not a new wire format. Despite
    /// the "Server" name, <see cref="ServerMessageEnvelope"/> is explicitly documented (its own
    /// type-level remarks) as the correct envelope for "client-to-server messages that do not
    /// reference a runtime entity" — <see cref="HeartbeatMessage"/> carries no
    /// <c>SenderEntityID</c>, so <see cref="ClientEntityMessageEnvelope"/> (14 bytes) is not used
    /// here. That envelope choice is already resolved by the two existing envelope types' own doc
    /// comments, not a new judgment call introduced by this story.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> the GDD's CR-NET-7.10 schema block does
    /// not assign a concrete wire ID, and no de-facto convention exists elsewhere in this codebase
    /// for it — the same "genuinely unassigned" situation as <see cref="CycleTimerBroadcast"/> and
    /// <see cref="EntityPositionUpdate"/> (Story 007). <c>0x0210</c> is chosen deliberately apart
    /// from the per-gameplay-system clusters already claimed in this folder (<c>0x0301-0x0303</c>
    /// combat, <c>0x0520</c> currency): Heartbeat is core wire-protocol infrastructure, not owned
    /// by any one gameplay system, so it is placed early in the <c>0x0200-0xDFFF</c> application
    /// range with room reserved around it, and does not collide with the <c>0x0100-0x01FF</c>
    /// batch-header range (<see cref="BatchHeaderCodec"/> remarks) or the <c>0xE000-0xFFFF</c>
    /// reserved ranges. Flag for formal registration in a future Networking ADR amendment or ID
    /// registry before cross-team/client implementations rely on this exact value.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ServerMessageEnvelope envelope = HeartbeatMessage.CreateEnvelope(sequenceNumber: 12u, serverTickNumber: 500u);
    /// Span&lt;byte&gt; buffer = stackalloc byte[HeartbeatMessage.WireSize];
    /// MessageEnvelopeCodec.Write(buffer, in envelope);
    ///
    /// // ... later, on receipt ...
    /// if (MessageEnvelopeCodec.TryRead(buffer, out ServerMessageEnvelope decoded) && HeartbeatMessage.IsHeartbeatEnvelope(in decoded))
    /// {
    ///     // reset the session's inactivity timeout counter (networking-session.md, future story)
    /// }
    /// </code>
    /// </example>
    public readonly struct HeartbeatMessage : IEquatable<HeartbeatMessage>
    {
        /// <summary>Total wire size: envelope only, no body (CR-NET-7.10) — equal to <see cref="ServerMessageEnvelope.WireSize"/>.</summary>
        public const int WireSize = ServerMessageEnvelope.WireSize;

        /// <summary>Provisional wire <c>MessageTypeID</c> for <see cref="HeartbeatMessage"/> — see type-level remarks (genuinely unassigned in the source GDD).</summary>
        public const ushort MessageTypeId = 0x0210;

        /// <summary>
        /// Builds the <see cref="ServerMessageEnvelope"/> for a <see cref="HeartbeatMessage"/> —
        /// stamps <see cref="MessageTypeId"/>; there is no body to attach.
        /// </summary>
        /// <param name="sequenceNumber">The sending connection's next outbound <c>SequenceNumber</c> (CR-NET-7.1/7.5).</param>
        /// <param name="serverTickNumber">The tick this heartbeat is authored on (client-authored ticks mirror the last known server tick per CR-NET-7.1).</param>
        /// <example>
        /// <code>
        /// ServerMessageEnvelope envelope = HeartbeatMessage.CreateEnvelope(sequenceNumber: 12u, serverTickNumber: 500u);
        /// </code>
        /// </example>
        public static ServerMessageEnvelope CreateEnvelope(uint sequenceNumber, uint serverTickNumber)
            => new ServerMessageEnvelope(MessageTypeId, sequenceNumber, serverTickNumber);

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="envelope"/>'s <c>MessageTypeId</c>
        /// identifies it as a <see cref="HeartbeatMessage"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// if (HeartbeatMessage.IsHeartbeatEnvelope(in decodedEnvelope)) { /* reset timeout */ }
        /// </code>
        /// </example>
        public static bool IsHeartbeatEnvelope(in ServerMessageEnvelope envelope) => envelope.MessageTypeId == MessageTypeId;

        /// <inheritdoc/>
        /// <remarks>Always <see langword="true"/> — <see cref="HeartbeatMessage"/> carries no fields; every instance is equivalent.</remarks>
        public bool Equals(HeartbeatMessage other) => true;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is HeartbeatMessage;

        /// <inheritdoc/>
        public override int GetHashCode() => MessageTypeId;

        /// <summary>Always <see langword="true"/> — every <see cref="HeartbeatMessage"/> instance is equivalent (no fields).</summary>
        public static bool operator ==(HeartbeatMessage left, HeartbeatMessage right) => true;

        /// <summary>Always <see langword="false"/> — every <see cref="HeartbeatMessage"/> instance is equivalent (no fields).</summary>
        public static bool operator !=(HeartbeatMessage left, HeartbeatMessage right) => false;

        /// <inheritdoc/>
        public override string ToString() => "HeartbeatMessage()";
    }
}

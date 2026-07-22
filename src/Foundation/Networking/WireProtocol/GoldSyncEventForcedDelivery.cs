namespace IronGrind.Networking
{
    /// <summary>
    /// MCR-4's forced-delivery escalation for <see cref="GoldSyncEvent"/> (Story 026): a standalone
    /// R-OD priority-path message emitted after <c>GOLD_MAX_CONSECUTIVE_DROP</c> consecutive R-U
    /// overflow-drops (<see cref="RUBatchWriter"/>'s category-eviction policy, Story 007). This is a
    /// distinct wire schema from the batch-framed R-U <see cref="GoldSyncEvent"/>
    /// (<see cref="BatchSubMessageCodec.WriteGoldSyncEvent"/>) — same payload fields, but a distinct
    /// <see cref="MessageTypeId"/> and a distinct wire shape: the 10-byte
    /// <see cref="ServerMessageEnvelope"/> immediately followed by the 13-byte
    /// <see cref="GoldSyncEvent"/> body, with no 12-byte batch header and no 4-byte sub-message
    /// length-prefix/type-id framing (<see cref="BatchSubMessageFraming"/> is never used here).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own.</b> It exists solely to give this message's
    /// provisional <see cref="MessageTypeId"/> and wire-shape documentation a discoverable home,
    /// matching this folder's one-file-per-message-type convention (<see cref="DamageEvent"/>,
    /// <see cref="GoldSyncEvent"/>, <see cref="HeartbeatMessage"/>). The payload itself is an
    /// ordinary <see cref="GoldSyncEvent"/> value — see <see cref="GoldSyncForcedDeliveryCodec"/> for
    /// encode/decode. Reusing <see cref="GoldSyncEvent"/> verbatim (rather than a duplicate struct
    /// with the same four fields) means the absolute-balance-never-a-delta invariant (AC-NC-19) has
    /// exactly one field-owning type to keep correct, not two.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> MCR-4 requires a <c>MessageTypeID</c> within
    /// <c>0xE000–0xEFFF</c> (the priority/control range) but does not assign a concrete value — the
    /// same "genuinely unassigned" situation as <see cref="CycleTimerBroadcast"/>,
    /// <see cref="EntityPositionUpdate"/>, and <see cref="HeartbeatMessage"/> (Stories 007/008).
    /// <c>0xE020</c> is chosen to sit inside the required range while avoiding the illustrative
    /// (non-assigned) <c>0xE010</c>/<c>0xE050</c> values already used in doc-comment examples
    /// elsewhere in this folder (<see cref="ClientEntityMessageEnvelope"/>, <see cref="WireEnumCodec"/>).
    /// Not registered in any ADR or ID table (see TD-011's precedent for the other five provisional
    /// IDs already tracked); treat as provisional until formally registered.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var goldSync = new GoldSyncEvent(new CharacterID(7), newBalance: 250u, version: 4u, GoldTransactionReason.MonsterDrop);
    /// Span&lt;byte&gt; buffer = stackalloc byte[GoldSyncEventForcedDelivery.WireSize];
    /// int written = GoldSyncForcedDeliveryCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in goldSync);
    /// </code>
    /// </example>
    public readonly struct GoldSyncEventForcedDelivery
    {
        /// <summary>
        /// Provisional wire <c>MessageTypeID</c> for the standalone R-OD forced-delivery
        /// <see cref="GoldSyncEvent"/> — see type-level remarks (genuinely unassigned; chosen within
        /// the MCR-4-required <c>0xE000–0xEFFF</c> range).
        /// </summary>
        public const ushort MessageTypeId = 0xE020;

        /// <summary>
        /// Total wire size: <see cref="ServerMessageEnvelope.WireSize"/> (10) +
        /// <see cref="GoldSyncEvent.BodySize"/> (13) = 23 bytes. No batch header, no sub-message
        /// framing — see type-level remarks.
        /// </summary>
        public const int WireSize = ServerMessageEnvelope.WireSize + GoldSyncEvent.BodySize;
    }
}

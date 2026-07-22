namespace IronGrind.Networking
{
    /// <summary>
    /// MCR-2's exclusive-recipient R-OD sibling of <see cref="DamageEvent"/> (Story 027): delivered
    /// to the attacker's own client exclusively, via the R-OD priority path, carrying the same
    /// fields as the zone-wide R-U <see cref="DamageEvent"/> (CCR-3: "Same fields as DamageEvent").
    /// This is a distinct wire schema from <see cref="DamageEvent"/> — a distinct
    /// <see cref="MessageTypeId"/> and a distinct wire shape: the 10-byte
    /// <see cref="ServerMessageEnvelope"/> immediately followed by the 14-byte <see cref="DamageEvent"/>
    /// body, with no 4-byte sub-message length-prefix/type-id framing
    /// (<see cref="BatchSubMessageFraming"/> is never used here) and no R-U batch header — matching
    /// the shape Story 026 established for <see cref="GoldSyncEventForcedDelivery"/>. Confirmed
    /// against <c>networking-wire-protocol.md</c>'s own Message Schemas section: "Standalone size:
    /// 10 (envelope) + 14 = 24 bytes."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own.</b> It exists solely to give this message's
    /// provisional <see cref="MessageTypeId"/> and wire-shape documentation a discoverable home,
    /// matching this folder's one-file-per-message-type convention and
    /// <see cref="GoldSyncEventForcedDelivery"/>'s precedent. The payload itself is an ordinary
    /// <see cref="DamageEvent"/> value — see <see cref="SelfDamageEventCodec"/> for encode/decode.
    /// Reusing <see cref="DamageEvent"/> verbatim (rather than a duplicate struct with the same five
    /// fields) means there is exactly one field-owning type for the attacker/target/damage/crit/type
    /// tuple, not two independently-maintained ones.
    /// </para>
    /// <para>
    /// <b>Body encode/decode is intentionally NOT shared with <see cref="BatchSubMessageCodec"/>'s
    /// <c>WriteDamageEvent</c>/<c>TryReadDamageEvent</c></b> (unlike <see cref="GoldSyncEventForcedDelivery"/>,
    /// which does share its body codec with the R-U path via <c>WriteGoldSyncEventBody</c>). Story 027's
    /// Out of Scope explicitly excludes modifying <c>DamageEvent</c>'s existing R-U serialization/
    /// dispatch (Story 007) — factoring a shared body method out of <see cref="BatchSubMessageCodec"/>
    /// would touch that closed file. <see cref="SelfDamageEventCodec"/> therefore encodes/decodes the
    /// body directly, using the same shared low-level primitives (<see cref="WireIdCodec"/>,
    /// <see cref="WireEnumCodec"/>, <see cref="System.Buffers.Binary.BinaryPrimitives"/>) that
    /// <see cref="BatchSubMessageCodec"/> already uses — identical wire layout guaranteed by sharing
    /// those primitives, at the cost of a small amount of duplicated field-ordering code.
    /// </para>
    /// <para>
    /// <b>Recipient-set construction (EC-CCR-2, MCR-2):</b> the server must construct this message's
    /// recipient set as a singleton <c>{attackerEntityId}</c>, never by filtering the zone-wide
    /// <see cref="DamageEvent"/> broadcast list down to one entry — see
    /// <see cref="SelfDamageEventDispatcher"/>, whose only public method accepts a single attacker
    /// <see cref="EntityID"/> parameter (never a collection), making the exclusivity structural
    /// rather than conventional.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/> provisionality:</b> MCR-2 assigns this message the R-OD channel
    /// but no concrete <c>MessageTypeID</c> — the same "genuinely unassigned" situation as
    /// <see cref="GoldSyncEventForcedDelivery"/>, <see cref="CycleTimerBroadcast"/>,
    /// <see cref="EntityPositionUpdate"/>, and <see cref="HeartbeatMessage"/> (Stories 007/008/026).
    /// <c>0xE030</c> is chosen within the required <c>0xE000–0xEFFF</c> priority range; the only value
    /// already taken in that range is <see cref="GoldSyncEventForcedDelivery.MessageTypeId"/>
    /// (<c>0xE020</c>). Not registered in any ADR or ID table; treat as provisional until formally
    /// registered.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var selfDamage = new DamageEvent(attackerEntityId, targetEntityId, finalDamage: 42, isCrit: true, DamageType.Physical);
    /// Span&lt;byte&gt; buffer = stackalloc byte[SelfDamageEvent.WireSize];
    /// int written = SelfDamageEventCodec.Write(buffer, sequenceNumber: 42u, tickNumber: 1003u, in selfDamage);
    /// </code>
    /// </example>
    public readonly struct SelfDamageEvent
    {
        /// <summary>
        /// Provisional wire <c>MessageTypeID</c> for the standalone R-OD <see cref="SelfDamageEvent"/>
        /// — see type-level remarks (genuinely unassigned; chosen within the MCR-2-required
        /// <c>0xE000–0xEFFF</c> range).
        /// </summary>
        public const ushort MessageTypeId = 0xE030;

        /// <summary>
        /// Total wire size: <see cref="ServerMessageEnvelope.WireSize"/> (10) +
        /// <see cref="DamageEvent.BodySize"/> (14) = 24 bytes. No batch header, no sub-message
        /// framing — see type-level remarks.
        /// </summary>
        public const int WireSize = ServerMessageEnvelope.WireSize + DamageEvent.BodySize;
    }
}

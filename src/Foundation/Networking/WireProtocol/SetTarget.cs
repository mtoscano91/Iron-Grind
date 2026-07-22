namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server RPC selecting (or deselecting) the sending client's current target
    /// (Networking Core Story 029, RFR-3a): <c>SetTarget { EntityID targetEntityId; }</c> — 4 bytes,
    /// C→S, R-OD/P1. A dropped <c>SetTarget</c> leaves the target-slot <see cref="EntityHealthUpdate"/>
    /// stale indefinitely, which is why it needs guaranteed delivery unlike the per-tick
    /// self-correcting HP data it gates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own</b>, matching <see cref="SelfDamageEvent"/>'s
    /// precedent (Story 027) — it exists solely to give this message's <see cref="MessageTypeId"/>
    /// and wire-shape documentation a discoverable home. The single body field
    /// (<c>targetEntityId</c>) is encoded/decoded directly by <see cref="SetTargetCodec"/> as a raw
    /// <see cref="System.UInt32"/> — see that class's remarks for why <see cref="WireIdCodec"/> is
    /// deliberately NOT used for this field.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/>:</b> chosen within the required <c>0xE000–0xEFFF</c> R-OD
    /// priority range, the next free slot after <see cref="GoldSyncEventForcedDelivery.MessageTypeId"/>
    /// (<c>0xE020</c>) and <see cref="SelfDamageEvent.MessageTypeId"/> (<c>0xE030</c>). Not registered
    /// in any ADR or ID table; treat as provisional until formally registered (same convention as
    /// the messages it sits adjacent to).
    /// </para>
    /// <para>
    /// <b>Wire shape:</b> the 14-byte <see cref="ClientEntityMessageEnvelope"/> (C→S, carries the
    /// sender's own <c>SenderEntityID</c>) immediately followed by <see cref="BodySize"/> (4) bytes
    /// — a raw little-endian <c>uint</c> <c>targetEntityId</c>. No batch header, no sub-message
    /// framing (matches <see cref="SelfDamageEvent"/>'s standalone-message shape). Total
    /// <see cref="WireSize"/> = 18 bytes, matching the GDD's own OQ-RFR-2 resolution note ("18 bytes
    /// standalone").
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[SetTarget.WireSize];
    /// SetTargetCodec.Write(buffer, sequenceNumber: 7u, tickNumber: 1000u, senderEntityId: 501u, targetEntityId: 42u);
    /// </code>
    /// </example>
    public readonly struct SetTarget
    {
        /// <summary>
        /// Wire <c>MessageTypeID</c> for the standalone R-OD <see cref="SetTarget"/> RPC — see
        /// type-level remarks (next free slot in the <c>0xE0xx</c> range).
        /// </summary>
        public const ushort MessageTypeId = 0xE040;

        /// <summary>Wire size of the body only (excludes the envelope): just <c>targetEntityId</c>, 4 bytes.</summary>
        public const int BodySize = 4;

        /// <summary>
        /// Total wire size: <see cref="ClientEntityMessageEnvelope.WireSize"/> (14) +
        /// <see cref="BodySize"/> (4) = 18 bytes.
        /// </summary>
        public const int WireSize = ClientEntityMessageEnvelope.WireSize + BodySize;
    }
}

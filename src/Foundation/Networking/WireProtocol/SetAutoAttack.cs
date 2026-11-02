namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server RPC switching the sending client's auto-attack toggle
    /// (Auto-Attack Combat Story 006, <c>design/gdd/auto-attack-combat.md</c> Rule 1):
    /// <c>SetAutoAttack { bool enabled; }</c> — 1 byte body, C→S, R-OD/P1. A dropped toggle would leave the
    /// server and the client disagreeing about a persistent state, so it needs guaranteed delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own</b> (same precedent as <see cref="SetTarget"/>); the single body
    /// byte is encoded and decoded by <see cref="SetAutoAttackCodec"/>. The byte is strict: <c>0</c> is OFF, <c>1</c>
    /// is ON, anything else is malformed.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/>:</b> <c>0xE050</c>, the next slot on the <c>0xE0x0</c> stride after
    /// <see cref="SetTarget.MessageTypeId"/> (<c>0xE040</c>), inside the required <c>0xE000–0xEFFF</c> R-OD range.
    /// Provisional until formally registered, like the messages it sits beside.
    /// </para>
    /// <para>
    /// <b>Wire shape:</b> the 14-byte <see cref="ClientEntityMessageEnvelope"/> followed by <see cref="BodySize"/>
    /// (1) byte. Total <see cref="WireSize"/> = 15 bytes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Span&lt;byte&gt; buffer = stackalloc byte[SetAutoAttack.WireSize];
    /// SetAutoAttackCodec.Write(buffer, sequenceNumber: 7u, tickNumber: 1000u, senderEntityId: 501u, enabled: true);
    /// </code>
    /// </example>
    public readonly struct SetAutoAttack
    {
        /// <summary>Wire <c>MessageTypeID</c> of the standalone R-OD <see cref="SetAutoAttack"/> RPC.</summary>
        public const ushort MessageTypeId = 0xE050;

        /// <summary>Wire size of the body only (excludes the envelope): the <c>enabled</c> byte.</summary>
        public const int BodySize = 1;

        /// <summary>Total wire size: <see cref="ClientEntityMessageEnvelope.WireSize"/> (14) + <see cref="BodySize"/> (1) = 15 bytes.</summary>
        public const int WireSize = ClientEntityMessageEnvelope.WireSize + BodySize;
    }
}

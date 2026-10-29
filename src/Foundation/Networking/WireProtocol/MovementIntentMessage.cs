namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server per-tick movement intent (Movement System Story 002, CR-MOV-10): the player's
    /// desired unit direction on the XZ plane, the facing angle, and the client tick it was authored on.
    /// U-U (unreliable, unordered), C→S, 10-byte body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fields:</b> <see cref="DirX"/> / <see cref="DirZ"/> are the unit direction scaled by 32,767
    /// (see <see cref="MovementIntentMessageCodec.EncodeDirection"/>); <see cref="FacingAngle"/> is
    /// degrees × 10 (nominally 0–3599; a received out-of-range value is wrapped by
    /// <see cref="MovementIntentMessageCodec.NormalizeFacing"/>, never rejected); <see cref="TickNumber"/>
    /// is the client-observed tick.
    /// </para>
    /// <para>
    /// <b>SenderEntityID is NOT in the body.</b> It travels in the 14-byte
    /// <see cref="ClientEntityMessageEnvelope"/> (CR-NET-7.1); the body carries only the four fields above.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/>:</b> 0x0211 was assigned at story-readiness 2026-10-29 (next to
    /// <see cref="HeartbeatMessage.MessageTypeId"/> 0x0210) and is recorded in
    /// <c>networking-wire-protocol.md</c> (Movement System Messages note).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var body = new byte[MovementIntentMessage.BodySize];
    /// var msg = new MovementIntentMessage(dirX: 19660, dirZ: -26214, facingAngle: 1234, tickNumber: 1000u);
    /// MovementIntentMessageCodec.WriteBody(body, in msg);
    /// </code>
    /// </example>
    public readonly struct MovementIntentMessage
    {
        /// <summary>Wire <c>MessageTypeID</c> for <see cref="MovementIntentMessage"/> (U-U, C→S).</summary>
        public const ushort MessageTypeId = 0x0211;

        /// <summary>Wire size of the body only (excludes the envelope): 2 + 2 + 2 + 4 = 10 bytes.</summary>
        public const int BodySize = 10;

        /// <summary>Total wire size: envelope (14) + <see cref="BodySize"/> (10) = 24 bytes.</summary>
        public const int WireSize = ClientEntityMessageEnvelope.WireSize + BodySize;

        /// <summary>Unit-direction X component × 32,767.</summary>
        public readonly short DirX;

        /// <summary>Unit-direction Z component × 32,767.</summary>
        public readonly short DirZ;

        /// <summary>Facing angle in degrees × 10 (nominally 0–3599).</summary>
        public readonly short FacingAngle;

        /// <summary>The client-observed tick this intent was authored on.</summary>
        public readonly uint TickNumber;

        /// <summary>Creates a message from already-encoded wire values.</summary>
        public MovementIntentMessage(short dirX, short dirZ, short facingAngle, uint tickNumber)
        {
            DirX = dirX;
            DirZ = dirZ;
            FacingAngle = facingAngle;
            TickNumber = tickNumber;
        }
    }
}

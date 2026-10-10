namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client acknowledgment of an accepted enhancement request (networking-wire-protocol.md, TD-046
    /// amendment; enhancement-system.md CR-ENH-15 steps 2-4): <c>EnhancementRequestReceived { uint requestId; byte
    /// itemSlotIndex; }</c> — 5-byte body, S→C, R-OD. <b>Exempt from the priority-path cap (CR-NET-7.7).</b> Sent by
    /// Enhancement Story 015; the codec is written in Story 010 (AC-NC-40).
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range). <c>requestId</c> is a raw counter echo; no zero guard.
    /// </remarks>
    public readonly struct EnhancementRequestReceived
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE101;

        /// <summary>Wire size of the body only: 4 + 1 = 5 bytes.</summary>
        public const int BodySize = 5;
    }
}

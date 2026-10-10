namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to start an enhancement attempt (Enhancement Story 010; networking-wire-protocol.md
    /// "Priority-Path Messages", TD-046 amendment): <c>EnhancementAttemptRequest { uint requestId; byte itemSlotIndex;
    /// byte scrollSlotIndex; }</c> — 6-byte body, C→S, R-OD. This is the <c>ConfirmEnhancement</c> request of
    /// enhancement-system.md (CR-ENH-6, CR-ENH-15 step 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own</b> (same precedent as <see cref="SetTarget"/>); it is the home of
    /// <see cref="MessageTypeId"/> and the wire-shape documentation. <see cref="EnhancementAttemptRequestCodec"/>
    /// reads and writes the body. The requesting player is the envelope's <c>SenderEntityID</c>; the body carries no
    /// entity or item id.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/>:</b> provisional. No ADR assigns it; chosen in the free part of the
    /// <c>0xE000–0xEFFF</c> R-OD priority range (the <c>0xE1xx</c> block, Enhancement and NPC messages), following
    /// the convention of <see cref="SetTarget.MessageTypeId"/>. Treat as provisional until formally registered.
    /// </para>
    /// <para>
    /// <b>Slot indices</b> are plain bytes; the codec does not range-check them. Range rejection is the server's job
    /// (<c>RejectedItemNotFound</c> / <c>RejectedScrollNotFound</c>). <b>requestId</b> is a raw <see cref="uint"/>
    /// request counter, not an ID type: CR-NET-7.3 does not apply and no zero guard is applied.
    /// </para>
    /// </remarks>
    public readonly struct EnhancementAttemptRequest
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE100;

        /// <summary>Wire size of the body only (excludes the envelope): 4 + 1 + 1 = 6 bytes.</summary>
        public const int BodySize = 6;
    }
}

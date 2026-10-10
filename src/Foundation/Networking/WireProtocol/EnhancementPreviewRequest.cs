namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server read-only preview request (UI-ENH-1, EC-ENH-5, CR-ENH-15 step 2): <c>EnhancementPreviewRequest
    /// { byte itemSlotIndex; byte scrollSlotIndex; }</c> — 2-byte body, C→S, R-OD. Locks and changes nothing, so it
    /// carries no <c>requestId</c>.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range). Slot indices are not range-checked by the codec.
    /// </remarks>
    public readonly struct EnhancementPreviewRequest
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE105;

        /// <summary>Wire size of the body only: 1 + 1 = 2 bytes.</summary>
        public const int BodySize = 2;
    }
}

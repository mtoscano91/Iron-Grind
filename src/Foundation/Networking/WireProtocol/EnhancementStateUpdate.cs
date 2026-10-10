namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client response to a valid preview (UI-ENH-1): <c>EnhancementStateUpdate { byte itemSlotIndex;
    /// byte scrollSlotIndex; byte currentLevel; ushort pSuccess; ushort pDestruction; }</c> — 7-byte body, S→C, R-OD.
    /// The probabilities are display-only and use the CR-NET-7.2 fixed-point rule: <c>P × 10,000</c> as a
    /// <see cref="ushort"/> (0.65 → 6500, 1.0 → 10000, 0 → 0).
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range).
    /// </remarks>
    public readonly struct EnhancementStateUpdate
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE106;

        /// <summary>Wire size of the body only: 1 + 1 + 1 + 2 + 2 = 7 bytes.</summary>
        public const int BodySize = 7;

        /// <summary>Fixed-point scale of <c>pSuccess</c> and <c>pDestruction</c> (CR-NET-7.2): wire value = probability × 10,000.</summary>
        public const int ProbabilityScale = 10000;
    }
}

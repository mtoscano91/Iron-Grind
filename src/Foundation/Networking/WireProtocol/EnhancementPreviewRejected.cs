using IronGrind.EnhancementSystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client response to a preview whose selection fails a CR-ENH-15 step 2 check:
    /// <c>EnhancementPreviewRejected { byte itemSlotIndex; byte scrollSlotIndex; EnhancementResultCode resultCode; }</c>
    /// — 3-byte body, S→C, R-OD. <c>resultCode</c> is a <c>Rejected*</c> value, never <c>Success</c> or
    /// <c>Destruction</c>.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR
    /// assigns it; <c>0xE1xx</c> block of the R-OD priority range). Unknown <c>resultCode</c> bytes follow AC-NC-42
    /// (see <see cref="EnhancementPreviewRejectedCodec.TryReadBody"/>).
    /// </remarks>
    public readonly struct EnhancementPreviewRejected
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE107;

        /// <summary>Wire size of the body only: 1 + 1 + 1 = 3 bytes.</summary>
        public const int BodySize = 3;

        /// <summary>
        /// Lowest <see cref="EnhancementResultCode"/> value a preview rejection can carry (2, <c>RejectedAtMaxLevel</c>):
        /// <c>Success</c> (0) and <c>Destruction</c> (1) are never preview rejections.
        /// </summary>
        public const byte MinKnownResultCode = (byte)EnhancementResultCode.RejectedAtMaxLevel;

        /// <summary>Highest defined <see cref="EnhancementResultCode"/> value on the wire (9).</summary>
        public const byte MaxKnownResultCode = (byte)EnhancementResultCode.RejectedNotUpgradeable;

        /// <summary>The rejection code a decoder substitutes for an unknown <c>resultCode</c> byte (AC-NC-42).</summary>
        public const EnhancementResultCode UnknownResultCodeFallback = EnhancementResultCode.RejectedItemNotFound;
    }
}

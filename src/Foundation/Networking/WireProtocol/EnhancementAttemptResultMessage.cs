using IronGrind.EnhancementSystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client result of an enhancement request. <b>Wire name: <c>EnhancementAttemptResult</c></b>
    /// (networking-wire-protocol.md, TD-046 amendment): <c>{ uint requestId; EnhancementResultCode resultCode; byte
    /// newLevel; }</c> — 6-byte body, S→C, R-OD. <b>Exempt from the priority-path cap (CR-NET-7.7).</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code name carries the <c>Message</c> suffix because <c>IronGrind.EnhancementSystem.EnhancementAttemptResult</c>
    /// (the service's result struct) already exists. <b>The <c>outcome</c> field of UI-ENH-2 is not serialized</b>: the
    /// decoder derives it from <c>resultCode</c> (<see cref="IsRejection"/>).
    /// </para>
    /// <para>
    /// <see cref="MessageTypeId"/> is <b>provisional</b> (no ADR assigns it; <c>0xE1xx</c> block of the R-OD priority
    /// range). An unknown <c>resultCode</c> byte (greater than <see cref="MaxKnownResultCode"/>) is handled by
    /// <see cref="EnhancementAttemptResultMessageCodec.TryReadBody"/> per AC-NC-42 and CR-NET-7.4.
    /// </para>
    /// </remarks>
    public readonly struct EnhancementAttemptResultMessage
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE102;

        /// <summary>Wire size of the body only: 4 + 1 + 1 = 6 bytes.</summary>
        public const int BodySize = 6;

        /// <summary>Highest defined <see cref="EnhancementResultCode"/> value on the wire (9, <c>RejectedNotUpgradeable</c>).</summary>
        public const byte MaxKnownResultCode = (byte)EnhancementResultCode.RejectedNotUpgradeable;

        /// <summary>
        /// The rejection code a decoder substitutes for an unknown <c>resultCode</c> byte (AC-NC-42): a generic
        /// <c>Rejected*</c> value that clears the client's waiting state.
        /// </summary>
        public const EnhancementResultCode UnknownResultCodeFallback = EnhancementResultCode.RejectedItemNotFound;

        /// <summary>
        /// <see langword="true"/> if <paramref name="resultCode"/> is a <c>Rejected*</c> code (no outcome); <see langword="false"/>
        /// for <c>Success</c> and <c>Destruction</c> — the derived UI-ENH-2 <c>outcome</c>.
        /// </summary>
        public static bool IsRejection(EnhancementResultCode resultCode)
            => resultCode != EnhancementResultCode.Success && resultCode != EnhancementResultCode.Destruction;
    }
}

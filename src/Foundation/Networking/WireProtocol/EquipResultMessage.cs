namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client result of an <see cref="EquipRequest"/>. <b>Wire name: <c>EquipResult</c></b>
    /// (networking-wire-protocol.md; equipment-system.md CR-EQS-13, UI Requirements): <c>{ uint requestId; bool success;
    /// EquipFailReason failReason; byte affectedSlot; uint slotItemId; byte slotEnhancementLevel; }</c> — 12-byte body,
    /// S→C, R-OD priority path, not cap-exempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code name carries the <c>Message</c> suffix because <c>IronGrind.EquipmentSystem.EquipResult</c> (the
    /// service's result enum) already exists — the precedent is <see cref="EnhancementAttemptResultMessage"/>.
    /// </para>
    /// <para>
    /// The slot fields always describe <c>affectedSlot</c> <b>after</b> the operation: the new contents on success,
    /// <c>slotItemId = 0</c> and level 0 after an unequip, and the unchanged slot on <c>NoChange</c> and every failure.
    /// </para>
    /// <para><see cref="MessageTypeId"/> <c>0xE141</c> — readiness decision 2026-10-26.</para>
    /// </remarks>
    public readonly struct EquipResultMessage
    {
        /// <summary>Wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE141;

        /// <summary>Wire size of the body only: 4 + 1 + 1 + 1 + 4 + 1 = 12 bytes.</summary>
        public const int BodySize = 12;

        /// <summary>
        /// The reason a decoder substitutes for an unknown <c>failReason</c> byte (CR-NET-7.4): a generic failure that
        /// still clears the client's waiting state.
        /// </summary>
        public const EquipFailReason UnknownFailReasonFallback = EquipFailReason.CriticalFailure;
    }
}

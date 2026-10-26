namespace IronGrind.Networking
{
    /// <summary>
    /// Client-to-server request to equip or unequip a gear slot (Equipment Story 008; networking-wire-protocol.md
    /// <c>EquipRequest</c>; equipment-system.md CR-EQS-13): <c>EquipRequest { uint requestId; byte gearSlot; uint itemId;
    /// byte inventorySlot; }</c> — 10-byte body, C→S, R-OD priority path. <c>itemId = 0</c> means unequip
    /// (<c>inventorySlot</c> is then ignored; the client sends <c>0xFF</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This type declares no fields of its own</b> (same precedent as <see cref="EnhancementAttemptRequest"/>); it is
    /// the home of <see cref="MessageTypeId"/> and the wire-shape documentation. <see cref="EquipRequestCodec"/> reads
    /// and writes the body. The requesting player is the envelope's <c>SenderEntityID</c>.
    /// </para>
    /// <para>
    /// <b><see cref="MessageTypeId"/>:</b> <c>0xE140</c>, the first id of the equipment block after Inventory sync
    /// (<c>0xE120</c>) and zone (<c>0xE130</c>) — readiness decision 2026-10-26.
    /// </para>
    /// <para>
    /// <b>gearSlot</b>, <b>itemId</b> and <b>inventorySlot</b> are not range-checked by the codec: the Equipment System
    /// validates them (Story 003). <b>requestId</b> is the client's monotonic request counter, used by the server to
    /// answer a retransmit from its stored result; it is not an ID type and is not zero-guarded.
    /// </para>
    /// </remarks>
    public readonly struct EquipRequest
    {
        /// <summary>Wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE140;

        /// <summary>Wire size of the body only (excludes the envelope): 4 + 1 + 4 + 1 = 10 bytes.</summary>
        public const int BodySize = 10;

        /// <summary>The <c>itemId</c> value that turns the request into an unequip.</summary>
        public const uint UnequipItemId = 0u;

        /// <summary>The <c>inventorySlot</c> value a client sends with an unequip (ignored by the server).</summary>
        public const byte UnusedInventorySlot = 0xFF;
    }
}

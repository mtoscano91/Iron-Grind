using IronGrind.InventorySystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client bag change (TD-046): <c>InventorySlotUpdate { byte count; InventorySlotEntry[count]; }</c>
    /// — body <c>1 + 10 × count</c> bytes, <c>count</c> from 1 to <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>,
    /// S→C, R-OD. One message per server-side bag-change event, carrying the absolute post-change state of each changed
    /// slot; no slot index appears twice in one message. The client overwrites each listed slot.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b>: it is not
    /// assigned by any ADR; it sits in the <c>0xE1xx</c> block of the <c>0xE000–0xEFFF</c> R-OD priority range, next
    /// free after <c>0xE113</c>. The entry layout is <see cref="InventorySlotEntry"/>; encode and decode are in
    /// <see cref="InventorySlotUpdateCodec"/>.
    /// </remarks>
    public readonly struct InventorySlotUpdate
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE120;

        /// <summary>Size of the leading <c>count</c> field: 1 byte.</summary>
        public const int CountFieldSize = 1;

        /// <summary>Smallest valid body: one entry, 1 + 10 = 11 bytes.</summary>
        public const int MinBodySize = CountFieldSize + InventorySlotEntry.WireSize;

        /// <summary>Largest valid body: one entry per bag slot, <c>1 + 10 × INVENTORY_SLOT_COUNT</c> bytes.</summary>
        public const int MaxBodySize = CountFieldSize + InventorySlotEntry.WireSize * InventoryConstants.INVENTORY_SLOT_COUNT;

        /// <summary>Body size for <paramref name="count"/> entries: <c>1 + 10 × count</c>.</summary>
        public static int GetBodySize(int count) => CountFieldSize + InventorySlotEntry.WireSize * count;
    }
}

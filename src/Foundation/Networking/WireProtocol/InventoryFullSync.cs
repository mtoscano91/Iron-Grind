using IronGrind.InventorySystem;

namespace IronGrind.Networking
{
    /// <summary>
    /// Server-to-owning-client whole-bag replacement (TD-046): <c>InventoryFullSync { byte count; InventorySlotEntry[count]; }</c>
    /// — <c>count</c> is always <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>, entries in ascending
    /// <c>slotIndex</c> order, empty slots included. Body <c>1 + 10 × INVENTORY_SLOT_COUNT</c> bytes, S→C, R-OD.
    /// Sent once per zone entry, after <c>SessionReady</c> and before any <see cref="InventorySlotUpdate"/> of that session.
    /// </summary>
    /// <remarks>
    /// Declares no fields (see <see cref="SetTarget"/>). <see cref="MessageTypeId"/> is <b>provisional</b>: it is not
    /// assigned by any ADR; it sits in the <c>0xE1xx</c> block of the <c>0xE000–0xEFFF</c> R-OD priority range, next
    /// free after <see cref="InventorySlotUpdate.MessageTypeId"/>. If the slot count is ever raised above 51 the body
    /// exceeds the 512-byte cap of CR-NET-7.6 and this message must move to the bulk-transfer range.
    /// </remarks>
    public readonly struct InventoryFullSync
    {
        /// <summary>Provisional wire <c>MessageTypeID</c> — see type-level remarks.</summary>
        public const ushort MessageTypeId = 0xE121;

        /// <summary>Exact body size: <c>1 + 10 × INVENTORY_SLOT_COUNT</c> bytes.</summary>
        public const int BodySize = InventorySlotUpdate.CountFieldSize + InventorySlotEntry.WireSize * InventoryConstants.INVENTORY_SLOT_COUNT;
    }
}

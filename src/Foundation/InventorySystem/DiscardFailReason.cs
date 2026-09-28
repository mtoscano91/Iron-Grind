namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Why an <see cref="IInventoryService.Discard"/> call did not apply. <see cref="None"/>
    /// indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency (mirrors <see cref="PickupFailReason"/>).
    /// Mirrors `design/gdd/networking-wire-protocol.md`'s <c>DiscardFailReason</c> member-for-member
    /// and value-for-value so the future wire codec maps 1:1 — do not add members without updating
    /// the wire GDD first.
    /// </remarks>
    public enum DiscardFailReason : byte
    {
        /// <summary>The discard applied.</summary>
        None = 0,

        /// <summary>The slot is locked by the Enhancement System (GDD Rule 5) — discarding a locked slot is never allowed.</summary>
        SlotLocked = 1,

        /// <summary>The requested quantity was zero or negative, or exceeded the slot's current <c>Quantity</c>.</summary>
        InvalidQuantity = 2,

        /// <summary>
        /// The slot holds no item (<c>ItemID.Invalid</c>). Also returned — per the 2026-09-27
        /// decision — for an out-of-range <c>slotIndex</c> and for an unregistered <c>charId</c>:
        /// the wire enum has no dedicated invalid-slot value, and a slot that does not exist holds
        /// no item.
        /// </summary>
        SlotEmpty = 3,
    }
}

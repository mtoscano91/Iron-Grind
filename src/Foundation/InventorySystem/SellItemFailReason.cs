namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Why an <see cref="IInventoryService.SellItem"/> call did not apply. <see cref="None"/>
    /// indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency (mirrors <see cref="DiscardFailReason"/>).
    /// One member per NPC Shop GDD's CR-SHOP-7 step 15 inventory-side rejection
    /// (<c>RejectedInvalidSlot</c>, <c>RejectedItemMismatch</c>, <c>RejectedSlotLocked</c>,
    /// <c>RejectedInvalidQuantity</c>) — do not add members without updating that GDD first.
    /// </remarks>
    public enum SellItemFailReason : byte
    {
        /// <summary>The sell applied.</summary>
        None = 0,

        /// <summary>
        /// The requested <c>slotIndex</c> was out of range, or <c>charId</c> was not a registered
        /// character (both are Tier 1 caller bugs — NPC Shop range-validates before calling).
        /// </summary>
        InvalidSlot = 1,

        /// <summary>
        /// The slot's current <c>ItemID</c> does not match the requested item — including an
        /// empty slot (<c>ItemID.Invalid</c>) and a requested <c>itemId == ItemID.Invalid</c>.
        /// </summary>
        ItemMismatch = 2,

        /// <summary>The slot is locked by the Enhancement System (GDD Rule 5.12) — a locked slot can never be sold.</summary>
        SlotLocked = 3,

        /// <summary>The requested quantity was zero or negative, or exceeded the slot's current <c>Quantity</c>.</summary>
        InvalidQuantity = 4,
    }
}

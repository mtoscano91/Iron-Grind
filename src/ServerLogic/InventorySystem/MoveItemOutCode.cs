namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Outcome codes for <see cref="IInventoryService.MoveItemOut"/> (GDD Rule 8 — Unequip to
    /// Bag). Member names match the GDD.
    /// </summary>
    public enum MoveItemOutCode : byte
    {
        /// <summary>The slot was occupied and unlocked; its item was removed and the slot emptied.</summary>
        Success = 0,

        /// <summary>
        /// The slot was already empty, or the request was out of range, or <c>charId</c> was not a
        /// registered character.
        /// </summary>
        SlotEmpty = 1,

        /// <summary>The slot was occupied but locked (GDD Rule 5).</summary>
        SlotLocked = 2,
    }
}

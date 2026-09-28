namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Why an <see cref="IInventoryService.Move"/> call did not apply. <see cref="None"/>
    /// indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency (mirrors <see cref="DiscardFailReason"/>).
    /// Mirrors `design/gdd/networking-wire-protocol.md`'s <c>MoveFailReason</c> member-for-member
    /// and value-for-value so the future wire codec maps 1:1 — do not add members without updating
    /// the wire GDD first.
    /// </remarks>
    public enum MoveFailReason : byte
    {
        /// <summary>The move (merge, swap, or relocate) applied.</summary>
        None = 0,

        /// <summary>The source slot is locked by the Enhancement System (GDD Rule 5) — moving out of a locked slot is never allowed.</summary>
        SourceLocked = 1,

        /// <summary>The destination slot is locked by the Enhancement System (GDD Rule 5) — moving into a locked slot is never allowed.</summary>
        DestLocked = 2,

        /// <summary>
        /// Either slot index was out of range, the source slot held no item, or <c>charId</c> was
        /// not a registered character (decided 2026-09-27) — the wire enum has no dedicated
        /// reason for these distinct conditions, and each ultimately means there is nothing valid
        /// to move: an out-of-range or unregistered target names no real slot, and an empty
        /// source has no item to move.
        /// </summary>
        InvalidSlot = 3,
    }
}

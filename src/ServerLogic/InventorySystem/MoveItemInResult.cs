namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.MoveItemIn"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="DiscardResult"/>).
    /// </summary>
    /// <remarks>
    /// A move-in is atomic (GDD Rule 8): on failure no slot was written, no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired, and
    /// <see cref="IInventoryService.OnInventoryFull"/> never fires either — unlike
    /// <see cref="IInventoryService.ForceInsert"/>, a full bag is reported purely through this
    /// result.
    /// </remarks>
    public readonly struct MoveItemInResult
    {
        /// <summary>The canonical failure result.</summary>
        public static readonly MoveItemInResult Failed = new MoveItemInResult(false, -1);

        /// <summary><see langword="true"/> when the item was placed into an empty slot.</summary>
        public readonly bool Success;

        /// <summary>The slot index the item was placed into, or -1 on failure.</summary>
        public readonly int SlotIndex;

        /// <summary>Creates a new result.</summary>
        /// <param name="success">Whether the item was placed.</param>
        /// <param name="slotIndex">The slot index placed into; must be -1 iff <paramref name="success"/> is <see langword="false"/>.</param>
        public MoveItemInResult(bool success, int slotIndex)
        {
            Success = success;
            SlotIndex = slotIndex;
        }

        /// <summary>Creates a success result carrying the placed <paramref name="slotIndex"/>.</summary>
        /// <param name="slotIndex">The slot index the item was placed into.</param>
        public static MoveItemInResult Succeeded(int slotIndex)
        {
            return new MoveItemInResult(true, slotIndex);
        }
    }
}

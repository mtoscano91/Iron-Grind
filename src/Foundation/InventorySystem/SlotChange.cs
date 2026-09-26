using IronGrind.CharacterStats;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// A single slot's post-mutation state, as broadcast inside
    /// <see cref="InventoryChangedEventArgs"/> via <see cref="IInventoryService.OnInventoryChanged"/>.
    /// </summary>
    /// <remarks>
    /// <see langword="readonly struct"/> per ADR-010 Decision 3 (Tier 2 broadcast event argument
    /// element type) — never boxed, never a class. <see cref="Quantity"/> == 0 (paired with
    /// <see cref="ItemId"/> == <see cref="ItemID.Invalid"/>) means the slot became empty as a
    /// result of the mutation that produced this entry.
    /// </remarks>
    public readonly struct SlotChange
    {
        /// <summary>The slot index (0–19) this entry describes.</summary>
        public readonly int SlotIndex;

        /// <summary>The slot's item after the mutation. <see cref="ItemID.Invalid"/> if the slot became empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>The slot's quantity after the mutation. 0 means the slot became empty.</summary>
        public readonly int Quantity;

        /// <summary>Constructs a slot-change entry describing a single slot's post-mutation state.</summary>
        /// <param name="slotIndex">The slot index this entry describes.</param>
        /// <param name="itemId">The slot's item after the mutation.</param>
        /// <param name="quantity">The slot's quantity after the mutation.</param>
        public SlotChange(int slotIndex, ItemID itemId, int quantity)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}

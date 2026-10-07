#nullable enable

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Immutable save/load record for a single non-empty inventory slot (Story 009 — GDD Rule
    /// 1.2, Persistence and Load Edge Cases, AC-INV-3). An element of <see cref="InventorySnapshot.Slots"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ItemId"/> is the raw <see cref="uint"/> wire/storage value — convert to/from
    /// <see cref="IronGrind.CharacterStats.ItemID"/> at the boundary
    /// (<see cref="IronGrind.CharacterStats.ItemID.RawValue"/> on export, <c>new ItemID(uint)</c>
    /// on import), so this type carries no engine-facing dependency beyond the primitive. A
    /// snapshot produced by <see cref="IInventoryService.ExportSnapshot"/> holds non-empty slots
    /// only, but this type does not itself enforce that — <see cref="IInventoryService.ImportSnapshot"/>
    /// validates every entry it is given independently.
    /// </remarks>
    public readonly struct InventorySnapshotEntry
    {
        /// <summary>
        /// The slot index this entry occupies. Valid range on export:
        /// [0, <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>); an entry outside that
        /// range on import is rejected by <see cref="IInventoryService.ImportSnapshot"/>.
        /// </summary>
        public readonly byte SlotIndex;

        /// <summary>The raw <see cref="IronGrind.CharacterStats.ItemID"/> value occupying the slot.</summary>
        public readonly uint ItemId;

        /// <summary>The stack quantity held in the slot.</summary>
        public readonly int Quantity;

        /// <summary>The enhancement level of the item in the slot. Normalised (zeroed or clamped) by <see cref="IInventoryService.ImportSnapshot"/>.</summary>
        public readonly byte EnhancementLevel;

        /// <summary>Constructs a snapshot entry with the given slot index, item and quantity (enhancement level 0).</summary>
        /// <param name="slotIndex">The slot index this entry occupies.</param>
        /// <param name="itemId">The raw item identifier occupying the slot.</param>
        /// <param name="quantity">The stack quantity held in the slot.</param>
        public InventorySnapshotEntry(byte slotIndex, uint itemId, int quantity)
            : this(slotIndex, itemId, quantity, 0)
        {
        }

        /// <summary>Constructs a snapshot entry including the item's enhancement level.</summary>
        /// <param name="slotIndex">The slot index this entry occupies.</param>
        /// <param name="itemId">The raw item identifier occupying the slot.</param>
        /// <param name="quantity">The stack quantity held in the slot.</param>
        /// <param name="enhancementLevel">The item's enhancement level (GDD AC-INV-20).</param>
        public InventorySnapshotEntry(byte slotIndex, uint itemId, int quantity, byte enhancementLevel)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
            EnhancementLevel = enhancementLevel;
        }
    }
}

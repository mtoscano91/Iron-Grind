using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// One bag slot as it travels on the wire in <see cref="InventorySlotUpdate"/> and
    /// <see cref="InventoryFullSync"/> (TD-046): <c>byte slotIndex; ItemID itemId; int quantity; byte enhancementLevel;</c>
    /// — 10 bytes, little-endian. The state is absolute (the slot after the change), never a delta.
    /// </summary>
    /// <remarks>
    /// An empty slot is <c>itemId = ItemID.Invalid</c>, <c>quantity = 0</c>, <c>enhancementLevel = 0</c>.
    /// <c>itemId</c> is a nullable ID field (CR-NET-7.3 exception): <see cref="InventorySlotUpdateCodec"/> writes
    /// <c>0</c> without the zero-write assertion. This type references no server-only type; the server-side sender
    /// copies each of its own slot changes into one of these.
    /// </remarks>
    public readonly struct InventorySlotEntry
    {
        /// <summary>Wire size of one entry: 1 + 4 + 4 + 1 = 10 bytes.</summary>
        public const int WireSize = 10;

        /// <summary>Bag slot index.</summary>
        public readonly byte SlotIndex;

        /// <summary>The item in the slot, or <see cref="ItemID.Invalid"/> if the slot is empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>Stack quantity; <c>0</c> if the slot is empty.</summary>
        public readonly int Quantity;

        /// <summary>Enhancement level; <c>0</c> if the slot is empty or holds a stack.</summary>
        public readonly byte EnhancementLevel;

        /// <summary>Creates an entry.</summary>
        /// <param name="slotIndex">Bag slot index.</param>
        /// <param name="itemId">The item, or <see cref="ItemID.Invalid"/> for an empty slot.</param>
        /// <param name="quantity">Stack quantity; 0 for an empty slot.</param>
        /// <param name="enhancementLevel">Enhancement level; 0 for an empty slot or a stack.</param>
        public InventorySlotEntry(byte slotIndex, ItemID itemId, int quantity, byte enhancementLevel)
        {
            SlotIndex = slotIndex;
            ItemId = itemId;
            Quantity = quantity;
            EnhancementLevel = enhancementLevel;
        }
    }
}

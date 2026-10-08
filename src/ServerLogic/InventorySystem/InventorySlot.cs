using IronGrind.CharacterStats;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Immutable snapshot of a single inventory slot's contents at the moment it was read.
    /// </summary>
    /// <remarks>
    /// Inventory System GDD Rule 1.1: an empty slot is <see cref="ItemID.Invalid"/> with
    /// <see cref="Quantity"/> == 0. A valid <see cref="ItemID"/> paired with <see cref="Quantity"/>
    /// == 0 (or vice versa) is the forbidden "phantom slot" state (GDD Edge Cases) — this type
    /// does not itself enforce that invariant on construction; <see cref="InventoryService"/> is
    /// responsible for never producing a phantom slot (see
    /// <see cref="InventoryService.SeedSlotForTesting(IronGrind.Currency.CharacterID, int, ItemID, int, byte)"/>).
    /// </remarks>
    public readonly struct InventorySlot
    {
        /// <summary>The canonical empty slot value: <see cref="ItemID.Invalid"/>, <see cref="Quantity"/> = 0.</summary>
        public static readonly InventorySlot Empty = new InventorySlot(ItemID.Invalid, 0);

        /// <summary>The item occupying this slot. <see cref="ItemID.Invalid"/> when the slot is empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>The stack quantity held in this slot. 0 iff <see cref="ItemId"/> is <see cref="ItemID.Invalid"/> (GDD Rule 1.1).</summary>
        public readonly int Quantity;

        /// <summary>
        /// The enhancement level of the item in this slot (GDD Rule 1.1/1.4). Always 0 for an empty
        /// slot and for any stackable item (<c>StackLimit</c> &gt; 1, at any quantity) — only a single
        /// <c>StackLimit</c> = 1 item carries a level; the Inventory System stores and transports it
        /// but never computes with it.
        /// </summary>
        public readonly byte EnhancementLevel;

        /// <summary>True iff this slot holds no item (<see cref="ItemId"/> == <see cref="ItemID.Invalid"/>).</summary>
        public bool IsEmpty => ItemId == ItemID.Invalid;

        /// <summary>Constructs a slot snapshot with the given item and quantity.</summary>
        /// <param name="itemId">The item occupying the slot, or <see cref="ItemID.Invalid"/> for an empty slot.</param>
        /// <param name="quantity">The stack quantity. Must be 0 iff <paramref name="itemId"/> is <see cref="ItemID.Invalid"/>.</param>
        public InventorySlot(ItemID itemId, int quantity)
            : this(itemId, quantity, 0)
        {
        }

        /// <summary>Constructs a slot snapshot with the given item, quantity and enhancement level.</summary>
        /// <param name="itemId">The item occupying the slot, or <see cref="ItemID.Invalid"/> for an empty slot.</param>
        /// <param name="quantity">The stack quantity. Must be 0 iff <paramref name="itemId"/> is <see cref="ItemID.Invalid"/>.</param>
        /// <param name="enhancementLevel">The item's enhancement level. Must be 0 for an empty slot or a stackable item (GDD Rule 1.4).</param>
        public InventorySlot(ItemID itemId, int quantity, byte enhancementLevel)
        {
            ItemId = itemId;
            Quantity = quantity;
            EnhancementLevel = enhancementLevel;
        }
    }
}

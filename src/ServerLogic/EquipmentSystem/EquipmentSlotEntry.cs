using IronGrind.CharacterStats;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// One gear slot's contents as the Equipment System stores it (GDD CR-EQS-1).
    /// </summary>
    /// <remarks>
    /// An empty slot is <see cref="ItemID.Invalid"/> with <see cref="EnhancementLevel"/> 0 and
    /// <see cref="IsTransitioning"/> false (<see cref="Empty"/>). <see cref="IsTransitioning"/> is a
    /// runtime flag only — it is never saved (CR-EQS-15).
    /// </remarks>
    public readonly struct EquipmentSlotEntry
    {
        /// <summary>The canonical empty slot value: <see cref="ItemID.Invalid"/>, level 0, not transitioning.</summary>
        public static readonly EquipmentSlotEntry Empty = new EquipmentSlotEntry(ItemID.Invalid, false, 0);

        /// <summary>The equipped item, or <see cref="ItemID.Invalid"/> when the slot is empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>True while an equip, swap or unequip on this slot is mid-operation (CR-EQS-9).</summary>
        public readonly bool IsTransitioning;

        /// <summary>The equipped item's enhancement level; 0 when the slot is empty.</summary>
        public readonly byte EnhancementLevel;

        /// <summary>True iff no item occupies the slot (<see cref="ItemId"/> == <see cref="ItemID.Invalid"/>).</summary>
        public bool IsEmpty => ItemId == ItemID.Invalid;

        /// <summary>Constructs a slot entry.</summary>
        /// <param name="itemId">The equipped item, or <see cref="ItemID.Invalid"/> for an empty slot.</param>
        /// <param name="isTransitioning">Whether the slot is mid-operation.</param>
        /// <param name="enhancementLevel">The item's enhancement level; must be 0 for an empty slot.</param>
        public EquipmentSlotEntry(ItemID itemId, bool isTransitioning, byte enhancementLevel)
        {
            ItemId = itemId;
            IsTransitioning = isTransitioning;
            EnhancementLevel = enhancementLevel;
        }
    }
}

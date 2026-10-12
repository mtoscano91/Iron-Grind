using IronGrind.CharacterStats;

namespace IronGrind.CharacterPersistence
{
    /// <summary>One persisted gear slot: the equipped item and its enhancement level (CR-CP-2).</summary>
    public readonly struct GearSlotRecord
    {
        /// <summary>The empty slot: <see cref="ItemID.Invalid"/>, enhancement level 0.</summary>
        public static readonly GearSlotRecord Empty = new GearSlotRecord(ItemID.Invalid, 0);

        /// <summary>The equipped item; <see cref="ItemID.Invalid"/> when the slot is empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>The enhancement level of the equipped item.</summary>
        public readonly byte EnhancementLevel;

        /// <summary>Constructs a gear slot record.</summary>
        /// <param name="itemId">The equipped item, or <see cref="ItemID.Invalid"/>.</param>
        /// <param name="enhancementLevel">The item's enhancement level.</param>
        public GearSlotRecord(ItemID itemId, byte enhancementLevel)
        {
            ItemId = itemId;
            EnhancementLevel = enhancementLevel;
        }
    }
}

using IronGrind.CharacterStats;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// The value <see cref="IEquipmentService.GetEquipmentSlotState"/> returns: a slot's item and
    /// its <see cref="SlotState"/> (GDD UI Requirements).
    /// </summary>
    public readonly struct EquipmentSlotState
    {
        /// <summary>The empty value: <see cref="ItemID.Invalid"/>, <see cref="SlotState.Empty"/>.</summary>
        public static readonly EquipmentSlotState Empty = new EquipmentSlotState(ItemID.Invalid, SlotState.Empty);

        /// <summary>The equipped item, or <see cref="ItemID.Invalid"/> when the slot is empty.</summary>
        public readonly ItemID ItemId;

        /// <summary>The slot's state.</summary>
        public readonly SlotState State;

        /// <summary>Constructs a slot state value.</summary>
        /// <param name="itemId">The equipped item, or <see cref="ItemID.Invalid"/>.</param>
        /// <param name="state">The slot's state.</param>
        public EquipmentSlotState(ItemID itemId, SlotState state)
        {
            ItemId = itemId;
            State = state;
        }
    }
}

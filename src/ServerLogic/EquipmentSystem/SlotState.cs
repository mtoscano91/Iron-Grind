namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// A gear slot's state as the read surface reports it (GDD States and Transitions).
    /// </summary>
    public enum SlotState : byte
    {
        /// <summary>No item equipped (<c>ItemID.Invalid</c>, level 0).</summary>
        Empty = 0,

        /// <summary>An item is equipped and its modifiers are fully applied.</summary>
        Occupied = 1,

        /// <summary>An equip, swap or unequip is mid-operation; the slot still shows the old item (CR-EQS-9).</summary>
        Transitioning = 2,
    }
}

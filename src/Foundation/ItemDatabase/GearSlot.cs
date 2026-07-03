namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Body location where a piece of equipment is worn. Stored in
    /// <see cref="EquipmentData._gearSlot"/> and used by the equipment system to enforce
    /// one-item-per-slot constraints.
    /// </summary>
    /// <remarks>
    /// OQ-3 resolved: <see cref="Ring"/> (5) and <see cref="Necklace"/> (6) replace the
    /// former single <c>Accessory</c> slot, permitting two distinct accessory slots.
    /// Backed by <see cref="byte"/> for IL2CPP efficiency.
    /// </remarks>
    public enum GearSlot : byte
    {
        /// <summary>Main-hand weapon slot.</summary>
        Weapon   = 0,

        /// <summary>Head armour slot.</summary>
        Helmet   = 1,

        /// <summary>Torso armour slot.</summary>
        Chest    = 2,

        /// <summary>Leg armour slot.</summary>
        Legs     = 3,

        /// <summary>Foot armour slot.</summary>
        Boots    = 4,

        /// <summary>Ring accessory slot (OQ-3).</summary>
        Ring     = 5,

        /// <summary>Necklace accessory slot (OQ-3).</summary>
        Necklace = 6,
    }
}

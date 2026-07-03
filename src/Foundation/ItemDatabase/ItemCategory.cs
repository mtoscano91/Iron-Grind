namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Top-level classification of an item. Determines which sub-schema
    /// (<see cref="EquipmentData"/> or <see cref="ConsumableData"/>) is populated
    /// on an <see cref="ItemDefinition"/>.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> to keep the enum cheap on iOS IL2CPP and to
    /// allow safe storage in serialized data without sign-extension concerns.
    /// </remarks>
    public enum ItemCategory : byte
    {
        /// <summary>Wearable gear (weapons, armour, accessories). Sub-schema: <see cref="EquipmentData"/>.</summary>
        Equipment = 0,

        /// <summary>Single-use or stackable consumables (potions, food). Sub-schema: <see cref="ConsumableData"/>.</summary>
        Consumable = 1,
    }
}

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Outcome of <c>Equip</c> / <c>Unequip</c> (GDD Interactions, <c>EquipResult</c> → wire mapping).
    /// Member names match the GDD. The values are not the wire values: the request handler maps
    /// each code to the wire <c>success</c> flag and <c>EquipFailReason</c>.
    /// </summary>
    public enum EquipResult : byte
    {
        /// <summary>The equip, swap or unequip completed.</summary>
        Success = 0,

        /// <summary>The same item at the same enhancement level is already equipped (CR-EQS-4); nothing changed.</summary>
        NoChange = 1,

        /// <summary>The character's base stat is below the item's requirement (CR-EQS-3); nothing changed.</summary>
        StatRequirementNotMet = 2,

        /// <summary>No free inventory slot for the item leaving the gear slot (CR-EQS-6, CR-EQS-7); nothing changed.</summary>
        InventoryFull = 3,

        /// <summary>The item's <c>GearSlot</c> does not match the target slot (CR-EQS-2); nothing changed.</summary>
        SlotMismatch = 4,

        /// <summary>The inventory slot holding the item is locked — <c>MoveItemOut</c> returned <c>SlotLocked</c> (EC-EQS-4).</summary>
        ItemLocked = 5,

        /// <summary>
        /// The item is not in the given inventory slot — the stale-render guard (CR-EQS-13) or
        /// <c>MoveItemOut</c> returned <c>SlotEmpty</c>.
        /// </summary>
        ItemNotInInventory = 6,

        /// <summary>An inventory call failed mid-operation; the old item is still equipped (CR-EQS-7 step 5, CR-EQS-8).</summary>
        InventoryError = 7,

        /// <summary>Reserved for wire-enum stability; no GDD rule produces it.</summary>
        CriticalFailure = 8,
    }
}

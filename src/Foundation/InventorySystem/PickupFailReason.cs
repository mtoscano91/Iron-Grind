namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Why an <see cref="IInventoryService.Pickup"/> call did not apply. <see cref="None"/>
    /// indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency (mirrors <c>GoldMutationError</c>).
    /// Only <see cref="InventoryFull"/> is a player-facing outcome — Story 003's
    /// <c>InventoryFullNotification</c> fires for it alone; every other reason is an invalid
    /// request and must never trigger bag-full UI.
    /// </remarks>
    public enum PickupFailReason : byte
    {
        /// <summary>The pickup applied.</summary>
        None = 0,

        /// <summary>GDD Rule 3 Step 3 — a remainder was left after filling partial stacks and empty slots. Nothing was written.</summary>
        InventoryFull = 1,

        /// <summary>The requested quantity was zero or negative.</summary>
        InvalidQuantity = 2,

        /// <summary>The <c>ItemID</c> is <c>ItemID.Invalid</c>, is not in the Item Database, has an invalid stack limit, or the database is not ready.</summary>
        UnknownItem = 3,

        /// <summary>The <c>CharacterID</c> was never registered via <see cref="IInventoryService.RegisterCharacter"/>.</summary>
        CharacterNotRegistered = 4,
    }
}

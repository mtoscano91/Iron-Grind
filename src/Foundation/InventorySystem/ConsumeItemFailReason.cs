namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Why an <see cref="IInventoryService.ConsumeItem"/> call did not apply. <see cref="None"/>
    /// indicates success.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency (mirrors <see cref="DiscardFailReason"/>).
    /// </remarks>
    public enum ConsumeItemFailReason : byte
    {
        /// <summary>The consume applied.</summary>
        None = 0,

        /// <summary>The requested quantity was zero or negative.</summary>
        InvalidQuantity = 1,

        /// <summary><c>charId</c> was not a registered character — a Tier 1 caller bug.</summary>
        CharacterNotRegistered = 2,

        /// <summary>
        /// The total held across unlocked slots (locked slots are neither decremented nor
        /// counted — GDD Rule 5.12) was less than the requested quantity. Also returned for an
        /// item no longer in the bag and for <c>itemId == ItemID.Invalid</c>.
        /// </summary>
        InsufficientQuantity = 3,
    }
}

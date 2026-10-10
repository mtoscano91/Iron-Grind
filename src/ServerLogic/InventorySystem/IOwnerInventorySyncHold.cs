using IronGrind.Currency;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// The hold mechanism that Enhancement Story 015 injects for CR-ENH-11: while a hold is open for a
    /// character, no <c>InventorySlotUpdate</c> and no <c>InventoryFullSync</c> reaches that character's
    /// owning client. Inventory Story 012 provides the mechanism (<see cref="OwnerInventorySyncSender"/>);
    /// when <see cref="OpenHold"/>, <see cref="ReleaseHold"/> and <see cref="DiscardHold"/> are called is the
    /// rule of Enhancement Story 015, not of this interface. Tick thread only.
    /// </summary>
    public interface IOwnerInventorySyncHold
    {
        /// <summary>
        /// Opens a hold for <paramref name="characterId"/>. Bag changes for the character are collected, not sent,
        /// until <see cref="ReleaseHold"/> or <see cref="DiscardHold"/>.
        /// </summary>
        /// <param name="characterId">The character whose owner sync is held.</param>
        void OpenHold(CharacterID characterId);

        /// <summary>
        /// Closes the hold and sends what it held: the collected updates in the order raised, or one full sync
        /// when a zone entry completed during the hold or the hold overflowed. Sends nothing when no client is connected.
        /// </summary>
        /// <param name="characterId">The character whose hold is released.</param>
        void ReleaseHold(CharacterID characterId);

        /// <summary>
        /// Closes the hold and sends nothing: collected updates, a deferred full sync and an overflow are all dropped.
        /// </summary>
        /// <param name="characterId">The character whose hold is discarded.</param>
        void DiscardHold(CharacterID characterId);
    }
}

using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Told by <see cref="ZoneEntryAdmission"/> when a player slot was allocated for a character (CR-ZI-8 step 4), which
    /// ends any gateway reservation held for it (Authentication Story 013). Tick thread only.
    /// </summary>
    public interface IZoneEntryReservationSink
    {
        /// <summary>A slot was allocated for <paramref name="character"/>.</summary>
        /// <param name="character">The character that now holds a slot.</param>
        /// <param name="serverTick">The server tick of the allocation.</param>
        void OnSlotAllocated(CharacterID character, uint serverTick);
    }
}

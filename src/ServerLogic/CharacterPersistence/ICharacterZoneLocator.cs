using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Narrow read seam through which Character Persistence learns which zone a character is in when it saves
    /// (CR-CP-10 step 6, Story 007). Zone Instancing implements it later.
    /// </summary>
    public interface ICharacterZoneLocator
    {
        /// <summary>
        /// Returns the zone the character is currently in. Called on the tick thread, in the synchronous prefix
        /// of <c>SaveSession</c>.
        /// </summary>
        /// <param name="charId">The character being saved.</param>
        /// <returns>The current zone, or <see cref="ZoneID.Invalid"/> when unknown (the save then keeps the loaded zone).</returns>
        ZoneID GetCurrentZone(CharacterID charId);
    }
}

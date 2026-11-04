using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The zone server's <see cref="ICharacterZoneLocator"/> (Character Persistence Story 020, CR-CP-10 step 6): every
    /// character this zone's persistence service saves is in this zone, so the locator returns the zone's own
    /// <see cref="ZoneID"/> for any character.
    /// </summary>
    /// <remarks>
    /// A zone process saves only characters admitted to it (session end, zone close, irreversible outcomes), so no
    /// lookup is needed. Built by the composition root at S5 with the zone id read at S0. Stateless and thread-safe.
    /// </remarks>
    public sealed class ZoneServerCharacterZoneLocator : ICharacterZoneLocator
    {
        private readonly ZoneID _zone;

        /// <summary>Creates the locator for one zone.</summary>
        /// <param name="zone">The zone this process runs.</param>
        public ZoneServerCharacterZoneLocator(ZoneID zone)
        {
            _zone = zone;
        }

        /// <summary>The zone this locator reports.</summary>
        public ZoneID Zone => _zone;

        /// <inheritdoc/>
        public ZoneID GetCurrentZone(CharacterID charId)
        {
            return _zone;
        }
    }
}

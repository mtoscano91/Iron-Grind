using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Owner of the zone <c>State</c> (Zone Instancing). The composition records the T-2 and T-3 transitions
    /// through it. <see cref="StandInZoneStateOwner"/> until the Zone Instancing epic ships.
    /// </summary>
    /// <example>
    /// <code>
    /// owner.MarkActive(zone);                 // S9, T-2
    /// owner.MarkClosedInvalidBounds(zone);    // S6 when bounds validation fails, T-3
    /// </code>
    /// </example>
    public interface IZoneStateOwner
    {
        /// <summary>Zone becomes <c>Active</c> (T-2), startup S9.</summary>
        /// <param name="zone">The running zone.</param>
        void MarkActive(ZoneID zone);

        /// <summary>Zone is closed because its bounds are invalid (T-3), startup S6.</summary>
        /// <param name="zone">The zone whose bounds failed validation.</param>
        void MarkClosedInvalidBounds(ZoneID zone);
    }
}

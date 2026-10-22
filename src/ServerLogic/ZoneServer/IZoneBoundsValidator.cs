using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Validates a loaded zone's bounds (startup S6, ADR-009 Amendment 1). Replaced by the real validator
    /// when its epic ships; <see cref="StandInZoneBoundsValidator"/> until then.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!validator.Validate(zone)) { /* S-fail */ }
    /// </code>
    /// </example>
    public interface IZoneBoundsValidator
    {
        /// <summary>Checks the zone's bounds.</summary>
        /// <param name="zone">The zone to check.</param>
        /// <returns>True when the bounds are valid.</returns>
        bool Validate(ZoneID zone);
    }
}

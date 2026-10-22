namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Loads and validates the static data of startup S2 (ADR-009 Amendment 1). Story 043 calls it;
    /// <see cref="StandInZoneStaticDataLoader"/> until the real loaders are wired.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!loader.LoadAndValidate()) { /* S-fail */ }
    /// </code>
    /// </example>
    public interface IZoneStaticDataLoader
    {
        /// <summary>Loads and validates all static data.</summary>
        /// <returns>True when everything loaded and validated.</returns>
        bool LoadAndValidate();
    }
}

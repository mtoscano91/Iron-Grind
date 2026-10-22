namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Reads the server config file named by <c>-serverConfig</c> (startup S0). A seam so unit tests pass JSON
    /// text and never touch the file system. Story 045 implements it over <c>System.IO.File</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// string json = configSource.ReadAllText(args.ServerConfigPath);
    /// ZoneServerConfig.TryLoad(json, out var config, out var error);
    /// </code>
    /// </example>
    public interface IZoneConfigSource
    {
        /// <summary>Returns the whole text of the config file. May throw (I/O); startup treats that as S-fail.</summary>
        /// <param name="path">The <c>-serverConfig</c> value.</param>
        /// <returns>The file text.</returns>
        string ReadAllText(string path);
    }
}

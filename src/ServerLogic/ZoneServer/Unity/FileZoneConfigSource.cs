using System.IO;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneConfigSource"/> over <see cref="File.ReadAllText(string)"/>: the only file I/O of the zone
    /// server start (ADR-009 Amendment 1 Decision 2 S0). The file holds no password (ADR-006 Amendment 3).
    /// </summary>
    /// <example>
    /// <code>
    /// IZoneConfigSource source = new FileZoneConfigSource();
    /// string json = source.ReadAllText("Builds/ZoneServer/zone-config.json");
    /// </code>
    /// </example>
    public sealed class FileZoneConfigSource : IZoneConfigSource
    {
        /// <inheritdoc/>
        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }
    }
}

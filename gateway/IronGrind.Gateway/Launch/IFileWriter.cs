namespace IronGrind.Gateway.Launch;

/// <summary>File-system seam for <see cref="SystemdZoneLauncher"/> (Gateway Story 008), so tests assert the env file content.</summary>
public interface IFileWriter
{
    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, replacing any existing file, then sets its mode.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="content">Full file content (UTF-8, no BOM).</param>
    /// <param name="mode">Unix permission bits; ignored on Windows.</param>
    void WriteAllText(string path, string content, UnixFileMode mode);
}

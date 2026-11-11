using System.Text;

namespace IronGrind.Gateway.Launch;

/// <summary>
/// <see cref="IFileWriter"/> over <see cref="File"/>. On Unix a new file is created with the mode (no looser umask
/// window), and the mode is set again after the write so an existing file gets it too.
/// </summary>
public sealed class SystemFileWriter : IFileWriter
{
    private static readonly UTF8Encoding UTF8_NO_BOM = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public void WriteAllText(string path, string content, UnixFileMode mode)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = mode;
        }

        using (var writer = new StreamWriter(path, UTF8_NO_BOM, options))
        {
            writer.Write(content);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}

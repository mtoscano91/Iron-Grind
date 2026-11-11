using System.Globalization;
using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Launch;

/// <summary>
/// Production zone launcher (ADR-016 Decision 3, zone process launch; Gateway Story 008). Writes
/// <c>/run/irongrind/zone-&lt;id&gt;.env</c> (<c>ZONE_TEMPLATE</c>, <c>ZONE_PORT</c>, mode 0640, replacing any earlier file),
/// then runs <c>systemctl --no-ask-password start irongrind-zone@&lt;id&gt;.service</c> (killed after
/// <see cref="SYSTEMCTL_TIMEOUT"/>). The template unit
/// (<c>tools/deploy/systemd/irongrind-zone@.service</c>) reads the file; the zone is not a child of the gateway.
/// </summary>
/// <remarks>
/// The env file holds no token, password or connection string. On a failed start the file stays (ZoneIDs are never
/// reused, so nothing else reads it); <c>/run</c> is a tmpfs and is emptied at reboot.
/// </remarks>
public sealed class SystemdZoneLauncher : IZoneLauncher
{
    /// <summary>Directory of the per-zone env files; created at boot by <c>tools/deploy/tmpfiles/irongrind.conf</c>.</summary>
    public const string ENV_DIRECTORY = "/run/irongrind";

    /// <summary>The <c>systemctl</c> executable, resolved through PATH.</summary>
    public const string SYSTEMCTL = "systemctl";

    /// <summary>
    /// Longest wait for <c>systemctl start</c> (Type=simple units return once the process is forked). Past it the
    /// <c>systemctl</c> process is killed and the start counts as failed, so a stuck call cannot pile up processes.
    /// </summary>
    public static readonly TimeSpan SYSTEMCTL_TIMEOUT = TimeSpan.FromSeconds(10);

    /// <summary>Env file mode: owner read/write, group read (0640).</summary>
    public const UnixFileMode ENV_FILE_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;

    private const string STARTED_MESSAGE = "Zone {ZoneId} start requested via {Unit} (template {ZoneTemplate}, port {Port})";

    private readonly IProcessRunner _runner;
    private readonly IFileWriter _files;
    private readonly ILogger _logger;

    /// <summary>Creates the launcher.</summary>
    /// <param name="runner">Runs <c>systemctl</c>.</param>
    /// <param name="files">Writes the env file.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public SystemdZoneLauncher(IProcessRunner runner, IFileWriter files, ILoggerFactory loggerFactory)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<SystemdZoneLauncher>();
    }

    /// <summary>The env file path for a zone, for example <c>/run/irongrind/zone-12.env</c>.</summary>
    /// <param name="zone">The zone.</param>
    /// <returns>The absolute path (always with '/' separators).</returns>
    public static string EnvFilePath(ZoneID zone) => $"{ENV_DIRECTORY}/zone-{zone.RawValue.ToString(CultureInfo.InvariantCulture)}.env";

    /// <summary>The systemd unit instance for a zone, for example <c>irongrind-zone@12.service</c>.</summary>
    /// <param name="zone">The zone.</param>
    /// <returns>The unit name.</returns>
    public static string UnitName(ZoneID zone) => $"irongrind-zone@{zone.RawValue.ToString(CultureInfo.InvariantCulture)}.service";

    /// <summary>The env file content: <c>ZONE_TEMPLATE=&lt;t&gt;\nZONE_PORT=&lt;p&gt;\n</c>.</summary>
    /// <param name="zoneTemplate">Template name.</param>
    /// <param name="port">UDP game port.</param>
    /// <returns>The content.</returns>
    public static string EnvFileContent(string zoneTemplate, ushort port) =>
        $"ZONE_TEMPLATE={zoneTemplate}\nZONE_PORT={port.ToString(CultureInfo.InvariantCulture)}\n";

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException"><c>systemctl</c> exited non-zero; the message holds the exit code and stderr.</exception>
    /// <exception cref="TimeoutException"><c>systemctl</c> did not exit within <see cref="SYSTEMCTL_TIMEOUT"/>.</exception>
    public async Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct)
    {
        ZoneLaunchArguments.Check(zone, zoneTemplate, port);
        ct.ThrowIfCancellationRequested();

        // A write failure propagates before systemctl runs.
        _files.WriteAllText(EnvFilePath(zone), EnvFileContent(zoneTemplate, port), ENV_FILE_MODE);

        string unit = UnitName(zone);
        // --no-ask-password: never wait for an interactive polkit prompt; only the polkit rule may allow the start.
        ProcessRunResult result = await _runner
            .RunAsync(SYSTEMCTL, new[] { "--no-ask-password", "start", unit }, SYSTEMCTL_TIMEOUT, ct)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"systemctl --no-ask-password start {unit} failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.StandardError.Trim()}");
        }

        _logger.LogInformation(STARTED_MESSAGE, zone.RawValue, unit, zoneTemplate, port);
    }
}

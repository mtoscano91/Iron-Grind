using System.Globalization;
using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Hosting;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Launch;

/// <summary>
/// Development zone launcher (ADR-016 Decision 3, dev; Gateway Story 008). Starts the configured zone build
/// (<c>ZONE_PROCESS_PATH</c>) with <c>-zoneId &lt;id&gt; -zoneTemplate &lt;t&gt; -port &lt;p&gt; -serverConfig &lt;path&gt;</c>,
/// no shell and no window, without the gateway's database variable, and logs the PID. Not for production: there the zone would be the gateway's child.
/// </summary>
public sealed class ProcessZoneLauncher : IZoneLauncher
{
    /// <summary>
    /// Gateway variables the zone must not inherit: the gateway connection string uses the <c>gateway</c> role, which has
    /// rights on <c>accounts</c>; zone roles get none (ADR-016). The zone's own <c>IRONGRIND_PG_PASSWORD</c> is kept.
    /// </summary>
    public static readonly IReadOnlyCollection<string> REMOVED_ENVIRONMENT = new[] { GatewayHost.DATABASE_CONFIG_KEY };

    private const string STARTED_MESSAGE = "Zone {ZoneId} process started: PID {Pid}, {Executable} (template {ZoneTemplate}, port {Port})";

    private readonly IProcessRunner _runner;
    private readonly string _executable;
    private readonly string _serverConfigPath;
    private readonly ILogger _logger;

    /// <summary>Creates the launcher.</summary>
    /// <param name="runner">Starts the process.</param>
    /// <param name="executable">Zone build path (<c>ZONE_PROCESS_PATH</c>).</param>
    /// <param name="serverConfigPath">Value passed as <c>-serverConfig</c> (<c>ZONE_SERVER_CONFIG</c>).</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public ProcessZoneLauncher(IProcessRunner runner, string executable, string serverConfigPath, ILoggerFactory loggerFactory)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverConfigPath);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _executable = executable;
        _serverConfigPath = serverConfigPath;
        _logger = loggerFactory.CreateLogger<ProcessZoneLauncher>();
    }

    /// <summary>The zone command-line arguments, in the order <c>ZoneStartupArgs</c> documents.</summary>
    /// <param name="zone">The zone.</param>
    /// <param name="zoneTemplate">Template name.</param>
    /// <param name="port">UDP game port.</param>
    /// <param name="serverConfigPath">Server config path.</param>
    /// <returns>One element per argument.</returns>
    public static IReadOnlyList<string> Arguments(ZoneID zone, string zoneTemplate, ushort port, string serverConfigPath) => new[]
    {
        "-zoneId", zone.RawValue.ToString(CultureInfo.InvariantCulture),
        "-zoneTemplate", zoneTemplate,
        "-port", port.ToString(CultureInfo.InvariantCulture),
        "-serverConfig", serverConfigPath,
    };

    /// <inheritdoc/>
    public Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct)
    {
        ZoneLaunchArguments.Check(zone, zoneTemplate, port);
        ct.ThrowIfCancellationRequested();
        int pid = _runner.Start(_executable, Arguments(zone, zoneTemplate, port, _serverConfigPath), REMOVED_ENVIRONMENT);
        _logger.LogInformation(STARTED_MESSAGE, zone.RawValue, pid, _executable, zoneTemplate, port);
        return Task.CompletedTask;
    }
}

using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Launch;

/// <summary>Builds the <see cref="IZoneLauncher"/> named by <c>ZONE_LAUNCHER</c> (Gateway Story 008; Story 009 registers it).</summary>
public static class ZoneLauncherFactory
{
    /// <summary><c>ZONE_LAUNCHER</c> value for <see cref="SystemdZoneLauncher"/> (production).</summary>
    public const string SYSTEMD = "systemd";

    /// <summary><c>ZONE_LAUNCHER</c> value for <see cref="ProcessZoneLauncher"/> (dev).</summary>
    public const string PROCESS = "process";

    /// <summary><c>ZONE_LAUNCHER</c> value for <see cref="NoOpZoneLauncher"/> (dev, zones started by hand).</summary>
    public const string NONE = "none";

    /// <summary>The accepted <c>ZONE_LAUNCHER</c> values (exact, lower case).</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { SYSTEMD, PROCESS, NONE };

    /// <summary>Creates the configured launcher. Options are validated at startup, so an unknown name here is a bug.</summary>
    /// <param name="options">Source of <c>ZONE_LAUNCHER</c>, <c>ZONE_PROCESS_PATH</c> and <c>ZONE_SERVER_CONFIG</c>.</param>
    /// <param name="runner">Process seam.</param>
    /// <param name="files">File seam.</param>
    /// <param name="loggerFactory">Source of the loggers.</param>
    /// <returns>The launcher.</returns>
    /// <exception cref="InvalidOperationException">Unknown <c>ZONE_LAUNCHER</c> value.</exception>
    public static IZoneLauncher Create(GatewayOptions options, IProcessRunner runner, IFileWriter files, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ZoneLauncher switch
        {
            SYSTEMD => new SystemdZoneLauncher(runner, files, loggerFactory),
            PROCESS => new ProcessZoneLauncher(runner, options.ZoneProcessPath, options.ZoneServerConfig, loggerFactory),
            NONE => new NoOpZoneLauncher(loggerFactory),
            _ => throw new InvalidOperationException($"ZONE_LAUNCHER must be one of {string.Join(", ", Names)} (was \"{options.ZoneLauncher}\")"),
        };
    }
}

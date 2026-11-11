using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Launch;

/// <summary>
/// Development launcher for zones started by hand or from the Editor (ADR-016 Decision 3, dev; Gateway Story 008): logs
/// the request and returns. A hand-started zone passes its own <c>-zoneId</c>, so the requested ZoneID may never register
/// and the warm-spare keeper's load timeout applies.
/// </summary>
public sealed class NoOpZoneLauncher : IZoneLauncher
{
    private const string REQUEST_MESSAGE =
        "ZONE_LAUNCHER=none: zone {ZoneId} not started (template {ZoneTemplate}, port {Port}); start a zone by hand";

    private readonly ILogger _logger;

    /// <summary>Creates the launcher.</summary>
    /// <param name="loggerFactory">Source of the logger.</param>
    public NoOpZoneLauncher(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<NoOpZoneLauncher>();
    }

    /// <inheritdoc/>
    public Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct)
    {
        _logger.LogWarning(REQUEST_MESSAGE, zone.RawValue, zoneTemplate, port);
        return Task.CompletedTask;
    }
}

using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Routing;

/// <summary>
/// Starts a zone server process (ADR-016 Decision 3 and Key Interfaces). The gateway never owns zone processes as children:
/// production starts a systemd unit (<c>systemctl</c>), development uses <c>Process.Start</c> or a no-op (Gateway Story 008).
/// Returning means the start was requested, not that the zone is ready; readiness is the zone's <c>RegisterZone</c>.
/// </summary>
public interface IZoneLauncher
{
    /// <summary>Requests a zone process start.</summary>
    /// <param name="zone">The ZoneID the process must register with.</param>
    /// <param name="zoneTemplate">The template name, for example <see cref="FillFirstRouter.STARTING_TEMPLATE_NAME"/>.</param>
    /// <param name="port">The UDP game port the zone must listen on.</param>
    /// <param name="ct">Cancels the start request.</param>
    /// <returns>A task that completes when the start was requested.</returns>
    /// <exception cref="Exception">Any failure; the caller treats it as a failed zone start.</exception>
    Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct);
}

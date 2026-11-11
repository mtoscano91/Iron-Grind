using IronGrind.CharacterPersistence;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// The one subscription point for zone-link events (Gateway Story 009, ADR-016 Decision 3): the listener raises each event
/// here, and this class passes it to every handler in a fixed order:
/// <list type="number">
/// <item><description><c>RoutingTable</c>: the row is current before anyone else reads it.</description></item>
/// <item><description><c>AccountZoneMap</c>: it has seen a registration (live accounts, link-up) before pending requests settle.</description></item>
/// <item><description><c>LinkZoneLink</c>: settles pending Admit / Evict / Reconnect requests, after the map is current.</description></item>
/// <item><description><c>WarmSpareKeeper</c>: last, because it reads the routing table's updated counts to decide on a start.</description></item>
/// </list>
/// An exception from one handler is logged and does not stop the handlers after it. The handlers are resolved lazily on the
/// first event (or when <see cref="Handlers"/> is read): the listener, the link and the map reference each other, and the
/// lazy list breaks that cycle. Handlers must not block (see <see cref="IZoneLinkEvents"/>).
/// </summary>
public sealed class ZoneLinkEventFanOut : IZoneLinkEvents
{
    private readonly Lazy<IReadOnlyList<IZoneLinkEvents>> _handlers;
    private readonly ILogger _logger;

    /// <summary>Creates the fan-out.</summary>
    /// <param name="handlers">Resolves the handlers, in call order, the first time they are needed.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public ZoneLinkEventFanOut(Func<IReadOnlyList<IZoneLinkEvents>> handlers, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _handlers = new Lazy<IReadOnlyList<IZoneLinkEvents>>(handlers, LazyThreadSafetyMode.ExecutionAndPublication);
        _logger = loggerFactory.CreateLogger<ZoneLinkEventFanOut>();
    }

    /// <summary>The handlers in call order (resolves them on first read).</summary>
    public IReadOnlyList<IZoneLinkEvents> Handlers => _handlers.Value;

    /// <inheritdoc/>
    public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message) =>
        Dispatch(nameof(OnZoneRegistered), zone, handler => handler.OnZoneRegistered(zone, message));

    /// <inheritdoc/>
    public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message) =>
        Dispatch(nameof(OnMessageReceived), zone, handler => handler.OnMessageReceived(zone, message));

    /// <inheritdoc/>
    public void OnLinkLost(ZoneID zone) =>
        Dispatch(nameof(OnLinkLost), zone, handler => handler.OnLinkLost(zone));

    private void Dispatch(string eventName, ZoneID zone, Action<IZoneLinkEvents> call)
    {
        foreach (IZoneLinkEvents handler in _handlers.Value)
        {
            try
            {
                call(handler);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Zone link handler {Handler} threw in {Event} for zone {Zone}", handler.GetType().Name, eventName, zone.RawValue);
            }
        }
    }
}

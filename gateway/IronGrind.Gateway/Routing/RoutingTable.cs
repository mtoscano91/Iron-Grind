using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Routing;

/// <summary>Lifecycle state of a zone as the gateway knows it.</summary>
public enum ZoneRouteState
{
    /// <summary>The zone accepts new players.</summary>
    Active,

    /// <summary>The zone announced <c>ZoneDraining</c> and takes no new players.</summary>
    Draining,
}

/// <summary>
/// Immutable copy of one routing-table row (Gateway Story 006).
/// </summary>
/// <param name="Zone">The zone instance id.</param>
/// <param name="Template">The zone template id from <c>RegisterZone</c>.</param>
/// <param name="UdpPort">The zone's UDP game port from <c>RegisterZone</c>.</param>
/// <param name="Capacity">The zone's registered capacity (0 means never routable).</param>
/// <param name="PlayerCount">Latest known player count (occupied plus admitted); the live-account count until the first <c>PlayerCount</c>.</param>
/// <param name="State">Active or Draining.</param>
/// <param name="LinkUp">False after the zone's link was lost and until it registers again.</param>
/// <param name="HasHeldPlayers">True once the player count has been above 0 at any time (Story 007 emptied-instance tie-break).</param>
/// <param name="HardCap">The zone's hard cap: the lower of the registered capacity and <c>MAX_PLAYERS_PER_ZONE</c>.</param>
public readonly record struct ZoneRoute(
    ZoneID Zone,
    byte Template,
    ushort UdpPort,
    int Capacity,
    int PlayerCount,
    ZoneRouteState State,
    bool LinkUp,
    bool HasHeldPlayers,
    int HardCap)
{
    /// <summary>True when the zone is registered, Active, linked and below its hard cap.</summary>
    public bool IsOpenForPlayers => State == ZoneRouteState.Active && LinkUp && PlayerCount < HardCap;
}

/// <summary>
/// The gateway's view of every zone instance (CR-ZI-6, ADR-016 Decision 3), built from the zone-link events:
/// <c>RegisterZone</c>, <c>PlayerCount</c> and <c>ZoneDraining</c>. Thread-safe: one lock guards all rows; queries return copies.
/// A lost link keeps the row (marked down) so the warm-spare keeper can count it and its port stays reserved;
/// <see cref="Sweep"/> prunes a row whose link stayed down for <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> (Gateway Story 007).
/// </summary>
public sealed class RoutingTable : IZoneLinkEvents
{
    private sealed class Row
    {
        public byte Template;
        public ushort UdpPort;
        public int Capacity;
        public int PlayerCount;
        public ZoneRouteState State;
        public bool LinkUp;
        public bool HasHeldPlayers;
        public DateTimeOffset DownSince;
    }

    private readonly object _lock = new();
    private readonly Dictionary<ZoneID, Row> _rows = new();
    private readonly int _maxPlayersPerZone;
    private readonly TimeProvider _time;
    private readonly TimeSpan _pruneAfter;
    private readonly ILogger _logger;

    /// <summary>Creates an empty table.</summary>
    /// <param name="options">Source of <c>MAX_PLAYERS_PER_ZONE</c> and <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock for the link-down pruning.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public RoutingTable(GatewayOptions options, TimeProvider time, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _pruneAfter = TimeSpan.FromSeconds(2.0 * options.LinkRetrySeconds);
        _maxPlayersPerZone = options.MaxPlayersPerZone;
        _logger = loggerFactory.CreateLogger<RoutingTable>();
    }

    /// <summary>Looks up one zone's row.</summary>
    /// <param name="zone">The zone.</param>
    /// <param name="route">A copy of the row.</param>
    /// <returns>False when the zone never registered.</returns>
    public bool TryGet(ZoneID zone, out ZoneRoute route)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(zone, out Row? row))
            {
                route = ToRoute(zone, row);
                return true;
            }
        }

        route = default;
        return false;
    }

    /// <summary>Copies the rows of every zone of a template, in ascending ZoneID order.</summary>
    /// <param name="template">The zone template id.</param>
    /// <returns>A snapshot; later events do not change it.</returns>
    public IReadOnlyList<ZoneRoute> GetTemplateRoutes(byte template)
    {
        var result = new List<ZoneRoute>();
        lock (_lock)
        {
            foreach (KeyValuePair<ZoneID, Row> entry in _rows)
            {
                if (entry.Value.Template == template)
                {
                    result.Add(ToRoute(entry.Key, entry.Value));
                }
            }
        }

        result.Sort((a, b) => a.Zone.RawValue.CompareTo(b.Zone.RawValue));
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A re-registration (link reconnect) replaces the row but keeps <see cref="ZoneRoute.HasHeldPlayers"/>: an instance
    /// whose count fell to 0 after being above 0 is still idle-closing (ADR-016 Decision 3, T-7) after its link comes back.
    /// <c>RegisterZone</c> carries no state, so the row returns to Active; a zone that is not Active re-sends
    /// <c>ZoneDraining</c> after registering (zone side, Gateway Story 011).
    /// </remarks>
    public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message)
    {
        int live = message.LiveAccounts?.Length ?? 0;
        lock (_lock)
        {
            bool heldBefore = _rows.TryGetValue(zone, out Row? previous) && previous.HasHeldPlayers;
            _rows[zone] = new Row
            {
                Template = message.TemplateId,
                UdpPort = message.UdpPort,
                Capacity = message.Capacity,
                PlayerCount = live,
                State = ZoneRouteState.Active,
                LinkUp = true,
                HasHeldPlayers = heldBefore || live > 0,
            };
        }
    }

    /// <inheritdoc/>
    public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message)
    {
        switch (message.Type)
        {
            case GatewayLinkMessageType.PlayerCount:
                lock (_lock)
                {
                    if (_rows.TryGetValue(zone, out Row? row))
                    {
                        row.PlayerCount = message.PlayerCount.Count;
                        row.HasHeldPlayers |= row.PlayerCount > 0;
                    }
                    else
                    {
                        _logger.LogWarning("PlayerCount from unregistered zone {ZoneId} ignored.", zone);
                    }
                }

                break;
            case GatewayLinkMessageType.ZoneDraining:
                lock (_lock)
                {
                    if (_rows.TryGetValue(zone, out Row? row))
                    {
                        row.State = ZoneRouteState.Draining;
                    }
                }

                break;
            default:
                // SessionReleased and everything else belong to other consumers.
                break;
        }
    }

    /// <inheritdoc/>
    public void OnLinkLost(ZoneID zone)
    {
        lock (_lock)
        {
            if (_rows.TryGetValue(zone, out Row? row) && row.LinkUp)
            {
                row.LinkUp = false;
                row.DownSince = _time.GetUtcNow();
            }
        }
    }

    /// <summary>
    /// Removes every row whose link has stayed down for <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> (inclusive); its UDP port
    /// becomes free for the warm-spare keeper. A row that registered again is not removed. Story 009 schedules this next to
    /// <c>AccountZoneMap.Sweep()</c>.
    /// </summary>
    public void Sweep()
    {
        lock (_lock)
        {
            DateTimeOffset now = _time.GetUtcNow();
            List<ZoneID>? expired = null;
            foreach (KeyValuePair<ZoneID, Row> entry in _rows)
            {
                if (!entry.Value.LinkUp && now - entry.Value.DownSince >= _pruneAfter)
                {
                    (expired ??= new List<ZoneID>()).Add(entry.Key);
                }
            }

            if (expired is null)
            {
                return;
            }

            foreach (ZoneID zone in expired)
            {
                _rows.Remove(zone);
            }
        }
    }

    /// <summary>Copies the rows of every template (link-down rows included), in ascending ZoneID order.</summary>
    /// <returns>A snapshot; later events do not change it.</returns>
    public IReadOnlyList<ZoneRoute> GetAllRoutes()
    {
        var result = new List<ZoneRoute>();
        lock (_lock)
        {
            foreach (KeyValuePair<ZoneID, Row> entry in _rows)
            {
                result.Add(ToRoute(entry.Key, entry.Value));
            }
        }

        result.Sort((a, b) => a.Zone.RawValue.CompareTo(b.Zone.RawValue));
        return result;
    }

    // Caller holds _lock.
    private ZoneRoute ToRoute(ZoneID zone, Row row) =>
        new(zone, row.Template, row.UdpPort, row.Capacity, row.PlayerCount, row.State, row.LinkUp, row.HasHeldPlayers,
            Math.Min(row.Capacity, _maxPlayersPerZone));
}

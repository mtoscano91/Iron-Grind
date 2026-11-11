using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;

namespace IronGrind.Gateway.Routing;

/// <summary>
/// Fill-first zone router (CR-ZI-6 rules 2 to 5, Gateway Story 006). Reads the <see cref="RoutingTable"/>; each call works on
/// a snapshot, so it is safe from any HTTP thread. "No pick" is mapped to <c>ServerStarting</c> by SessionEstablishment.
/// </summary>
public sealed class FillFirstRouter : IZoneRouter
{
    /// <summary>Template id of the MVP starting zone; mirrors <c>ZoneTemplateID.StartingZone</c> (the gateway does not reference ServerLogic).</summary>
    public const byte STARTING_TEMPLATE = 0;

    /// <summary>Launch name of the starting template, passed to <c>IZoneLauncher.StartAsync</c> (Gateway Story 007; one template at MVP).</summary>
    public const string STARTING_TEMPLATE_NAME = "StartingZone";

    private readonly RoutingTable _table;
    private readonly int _openThreshold;

    /// <summary>Creates the router.</summary>
    /// <param name="table">The routing table.</param>
    /// <param name="options">Source of <c>ZONE_OPEN_THRESHOLD</c>.</param>
    public FillFirstRouter(RoutingTable table, GatewayOptions options)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        ArgumentNullException.ThrowIfNull(options);
        _openThreshold = options.ZoneOpenThreshold;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Rule 1 (party co-location) is deferred at MVP: see OQ-ZI-9. Routing starts at rule 2. At MVP the template is always
    /// the starting template; rule 2 still honours a <paramref name="lastZone"/> of another template, because that zone is
    /// where the character was.
    /// </remarks>
    public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port)
    {
        // Rule 2: the character's last zone, when routable.
        if (!lastZone.Equals(ZoneID.Invalid) && !lastZone.Equals(excluded)
            && _table.TryGet(lastZone, out ZoneRoute last) && last.IsOpenForPlayers)
        {
            zone = last.Zone;
            port = last.UdpPort;
            return true;
        }

        IReadOnlyList<ZoneRoute> routes = _table.GetTemplateRoutes(STARTING_TEMPLATE);
        bool haveOpen = false;
        ZoneRoute open = default;
        bool haveOverflow = false;
        ZoneRoute overflow = default;

        // Routes arrive in ascending ZoneID order, so strict comparisons keep the lower ZoneID on a tie.
        foreach (ZoneRoute route in routes)
        {
            if (!route.IsOpenForPlayers || route.Zone.Equals(excluded))
            {
                continue;
            }

            // Rule 3: highest count below the open threshold. Ties go to the emptied instance (HasHeldPlayers and count 0,
            // idle-closing: ADR-016 Decision 3), then to the lower ZoneID (strict comparisons keep the earlier route).
            // Only two zones at count 0 can differ in "emptied"; at any other equal count both flags are false.
            bool emptied = route.HasHeldPlayers && route.PlayerCount == 0;
            if (route.PlayerCount < _openThreshold
                && (!haveOpen
                    || route.PlayerCount > open.PlayerCount
                    || (route.PlayerCount == open.PlayerCount && emptied && !(open.HasHeldPlayers && open.PlayerCount == 0))))
            {
                open = route;
                haveOpen = true;
            }

            // Rule 4 candidate: lowest count below the hard cap.
            if (!haveOverflow || route.PlayerCount < overflow.PlayerCount)
            {
                overflow = route;
                haveOverflow = true;
            }
        }

        if (haveOpen)
        {
            zone = open.Zone;
            port = open.UdpPort;
            return true;
        }

        // Rule 4: overflow.
        if (haveOverflow)
        {
            zone = overflow.Zone;
            port = overflow.UdpPort;
            return true;
        }

        // Rule 5: no pick.
        zone = ZoneID.Invalid;
        port = 0;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the registered port for any known zone, whatever its state: the token path reconnects a player to the zone
    /// that holds the session even if that zone is Draining, at cap or its link is down.
    /// </remarks>
    public bool TryGetPort(ZoneID zone, out ushort port)
    {
        if (_table.TryGet(zone, out ZoneRoute route))
        {
            port = route.UdpPort;
            return true;
        }

        port = 0;
        return false;
    }
}

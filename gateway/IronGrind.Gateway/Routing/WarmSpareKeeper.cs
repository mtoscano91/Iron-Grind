using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Routing;

/// <summary>
/// Keeps one warm-spare zone instance per template, Active or Loading, below <c>ZONE_OPEN_THRESHOLD</c> (CR-ZI-6, ADR-016
/// Decision 3, Gateway Story 007). At MVP the only template is <see cref="FillFirstRouter.STARTING_TEMPLATE"/>.
/// </summary>
/// <remarks>
/// <para>
/// Ordering contract for Story 009: fan every zone-link event to the <see cref="RoutingTable"/> first and to this keeper
/// second, so the keeper reads the updated counts. Also call <see cref="Tick"/> on a timer next to
/// <c>AccountZoneMap.Sweep()</c> and <c>RoutingTable.Sweep()</c>; <see cref="Tick"/> handles the boot wait, load timeouts and
/// retry spacing.
/// </para>
/// <para>
/// A template "has a spare" when a row is Active, linked, has capacity, is below the open threshold and is not emptied
/// (<c>HasHeldPlayers &amp;&amp; PlayerCount == 0</c>, idle-closing), or when a start is in progress. Nothing starts before the
/// boot wait is over. Event handlers never block: the start (allocate, launch) runs without being awaited, but the Loading
/// record is set under the lock first so a second start cannot begin. A failed start (including no free port and a load
/// timeout) logs critical and is retried no sooner than <c>ZONE_LOAD_TIMEOUT_SECONDS</c> after the attempt began; a start
/// whose zone registered leaves no spacing, so the next spare can start at once.
/// Thread-safe: one lock guards the state; the routing table is read while holding it (the table never calls back).
/// </para>
/// </remarks>
public sealed class WarmSpareKeeper : IZoneLinkEvents
{
    private const string LOAD_TIMEOUT_MESSAGE =
        "ZoneDefinitionLoadFailed: zone {ZoneId} on port {Port} did not register within {Timeout} s.";

    private const string START_FAILED_MESSAGE = "ZoneDefinitionLoadFailed: starting zone {ZoneId} on port {Port} failed.";

    private sealed class StartPlan
    {
        public int Attempt;
        public ushort Port;
    }

    private readonly object _lock = new();
    private readonly RoutingTable _table;
    private readonly IZoneIdAllocator _allocator;
    private readonly IZoneLauncher _launcher;
    private readonly GatewayBootWait _bootWait;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly int _openThreshold;
    private readonly int _portFirst;
    private readonly int _portCount;
    private readonly TimeSpan _loadTimeout;

    private bool _loading;
    private int _attempt;
    private ZoneID _loadingZone = ZoneID.Invalid;
    private ushort _loadingPort;
    private DateTimeOffset _loadingStart;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;

    /// <summary>Creates the keeper; nothing starts until the shared boot wait is over.</summary>
    /// <param name="table">The routing table (updated before this keeper sees an event).</param>
    /// <param name="allocator">Hands out the ZoneID of a started zone.</param>
    /// <param name="launcher">Starts the zone process.</param>
    /// <param name="options">Source of the threshold, port range and load timeout.</param>
    /// <param name="bootWait">The boot wait shared with the account map.</param>
    /// <param name="time">Clock.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public WarmSpareKeeper(
        RoutingTable table,
        IZoneIdAllocator allocator,
        IZoneLauncher launcher,
        GatewayOptions options,
        GatewayBootWait bootWait,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        ArgumentNullException.ThrowIfNull(options);
        _bootWait = bootWait ?? throw new ArgumentNullException(nameof(bootWait));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<WarmSpareKeeper>();
        _openThreshold = options.ZoneOpenThreshold;
        _portFirst = options.ZoneUdpPortFirst;
        _portCount = options.ZoneUdpPortCount;
        _loadTimeout = TimeSpan.FromSeconds(options.ZoneLoadTimeoutSeconds);
    }

    /// <summary>True while a started instance has not yet sent its <c>RegisterZone</c>.</summary>
    public bool IsLoading
    {
        get
        {
            lock (_lock)
            {
                return _loading;
            }
        }
    }

    /// <summary>ZoneID of the Loading instance; <see cref="ZoneID.Invalid"/> when none is loading or the allocator has not answered yet.</summary>
    public ZoneID LoadingZone
    {
        get
        {
            lock (_lock)
            {
                return _loading ? _loadingZone : ZoneID.Invalid;
            }
        }
    }

    /// <summary>
    /// Timer entry point: fails a Loading instance that did not register within <c>ZONE_LOAD_TIMEOUT_SECONDS</c>, then
    /// re-evaluates the spare rule (covers the end of the boot wait and the retry spacing).
    /// </summary>
    public void Tick()
    {
        StartPlan? plan;
        lock (_lock)
        {
            if (_loading && _time.GetUtcNow() - _loadingStart >= _loadTimeout)
            {
                FailLocked(_attempt, _loadingZone, _loadingPort, null);
            }

            plan = EvaluateLocked();
        }

        Launch(plan);
    }

    /// <inheritdoc/>
    public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message)
    {
        StartPlan? plan;
        lock (_lock)
        {
            if (_loading && _loadingZone.Equals(zone))
            {
                _loading = false;
                _loadingZone = ZoneID.Invalid;

                // Retry spacing applies to failed starts only; a spare that registered may be needed again at once.
                // A zone that registered without giving the template a spare (for example capacity 0) is not a spare:
                // keep the spacing so the keeper does not start a zone on every event.
                if (HasSpareLocked())
                {
                    _nextAttemptAt = DateTimeOffset.MinValue;
                }
                else
                {
                    _logger.LogCritical(
                        "ZoneDefinitionLoadFailed: zone {ZoneId} registered but the template has no spare (capacity {Capacity}); the next start waits for the retry spacing.",
                        zone, message.Capacity);
                }
            }

            plan = EvaluateLocked();
        }

        Launch(plan);
    }

    /// <inheritdoc/>
    public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message) => Reevaluate();

    /// <inheritdoc/>
    public void OnLinkLost(ZoneID zone) => Reevaluate();

    private void Reevaluate()
    {
        StartPlan? plan;
        lock (_lock)
        {
            plan = EvaluateLocked();
        }

        Launch(plan);
    }

    // Caller holds _lock. Returns a plan when a start must now run; the Loading record is already set.
    private StartPlan? EvaluateLocked()
    {
        if (!_bootWait.IsOver || _loading)
        {
            return null;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (now < _nextAttemptAt || HasSpareLocked())
        {
            return null;
        }

        _attempt++;
        _nextAttemptAt = now + _loadTimeout;

        if (!TryPickPortLocked(out ushort port))
        {
            _logger.LogCritical(
                "ZonePortsExhausted: no free UDP port in {First} to {Last}; the zone start is retried in {Timeout} s.",
                _portFirst, _portFirst + _portCount - 1, _loadTimeout.TotalSeconds);
            return null;
        }

        _loading = true;
        _loadingZone = ZoneID.Invalid;
        _loadingPort = port;
        _loadingStart = now;
        return new StartPlan { Attempt = _attempt, Port = port };
    }

    // Caller holds _lock.
    private bool HasSpareLocked()
    {
        foreach (ZoneRoute route in _table.GetTemplateRoutes(FillFirstRouter.STARTING_TEMPLATE))
        {
            bool emptied = route.HasHeldPlayers && route.PlayerCount == 0;
            if (route.State == ZoneRouteState.Active && route.LinkUp && route.Capacity > 0
                && route.PlayerCount < _openThreshold && !emptied)
            {
                return true;
            }
        }

        return false;
    }

    // Caller holds _lock. The lowest port in the range that no row holds. Only called when nothing is Loading (one start
    // at a time), so the Loading port needs no check here.
    private bool TryPickPortLocked(out ushort port)
    {
        var held = new HashSet<int>();
        foreach (ZoneRoute route in _table.GetAllRoutes())
        {
            held.Add(route.UdpPort);
        }

        for (int candidate = _portFirst; candidate < _portFirst + _portCount; candidate++)
        {
            if (!held.Contains(candidate))
            {
                port = (ushort)candidate;
                return true;
            }
        }

        port = 0;
        return false;
    }

    // Runs the start outside the lock. The async method runs synchronously until its first real await and never throws.
    private void Launch(StartPlan? plan)
    {
        if (plan is not null)
        {
            _ = RunStartAsync(plan);
        }
    }

    private async Task RunStartAsync(StartPlan plan)
    {
        ZoneID zone = ZoneID.Invalid;
        try
        {
            zone = await _allocator.AllocateAsync(CancellationToken.None).ConfigureAwait(false);
            lock (_lock)
            {
                if (!_loading || _attempt != plan.Attempt)
                {
                    return; // Timed out while the allocator was working; the attempt is abandoned.
                }

                _loadingZone = zone;
            }

            await _launcher.StartAsync(zone, FillFirstRouter.STARTING_TEMPLATE_NAME, plan.Port, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                FailLocked(plan.Attempt, zone, plan.Port, ex);
            }
        }
    }

    // Caller holds _lock. Ends the given attempt if it is still the Loading one; the next attempt waits for
    // attemptStart + ZONE_LOAD_TIMEOUT_SECONDS (already set when the attempt began). No exception = load timeout.
    private void FailLocked(int attempt, ZoneID zone, ushort port, Exception? exception)
    {
        if (!_loading || _attempt != attempt)
        {
            return; // Already registered or already failed; a late failure changes nothing.
        }

        _loading = false;
        _loadingZone = ZoneID.Invalid;
        if (exception is null)
        {
            _logger.LogCritical(LOAD_TIMEOUT_MESSAGE, zone, port, _loadTimeout.TotalSeconds);
        }
        else
        {
            _logger.LogCritical(exception, START_FAILED_MESSAGE, zone, port);
        }
    }
}

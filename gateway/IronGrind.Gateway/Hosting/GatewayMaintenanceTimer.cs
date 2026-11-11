using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// The gateway's one maintenance timer (Gateway Story 009): every <c>GATEWAY_MAINTENANCE_INTERVAL_MS</c> it runs
/// <see cref="WarmSpareKeeper.Tick"/>, <see cref="RoutingTable.Sweep"/> and <see cref="AccountZoneMap.Sweep"/>, in that order.
/// It uses the injected <see cref="TimeProvider"/> so tests can drive it. A run that is still going when the next period
/// arrives makes that period skip; one step that throws is logged and does not stop the others.
/// </summary>
public sealed class GatewayMaintenanceTimer : IHostedService, IDisposable
{
    private readonly WarmSpareKeeper? _keeper;
    private readonly RoutingTable? _table;
    private readonly AccountZoneMap? _map;
    private readonly TimeProvider _time;
    private readonly TimeSpan _period;
    private readonly ILogger _logger;
    private ITimer? _timer;
    private readonly Action _tick;
    private readonly Action _sweepTable;
    private readonly Action _sweepMap;
    private int _running;
    private int _stopping;

    /// <summary>Creates the timer service.</summary>
    /// <param name="keeper">Warm-spare keeper.</param>
    /// <param name="table">Routing table.</param>
    /// <param name="map">Account to zone map.</param>
    /// <param name="options">Source of <c>GATEWAY_MAINTENANCE_INTERVAL_MS</c>.</param>
    /// <param name="time">Timer source.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public GatewayMaintenanceTimer(
        WarmSpareKeeper keeper, RoutingTable table, AccountZoneMap map, GatewayOptions options, TimeProvider time, ILoggerFactory loggerFactory)
    {
        _keeper = keeper ?? throw new ArgumentNullException(nameof(keeper));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(options);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        if (options.MaintenanceIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "GATEWAY_MAINTENANCE_INTERVAL_MS must be greater than 0.");
        }

        _period = TimeSpan.FromMilliseconds(options.MaintenanceIntervalMs);
        _tick = _keeper.Tick;
        _sweepTable = _table.Sweep;
        _sweepMap = _map.Sweep;
        _logger = loggerFactory.CreateLogger<GatewayMaintenanceTimer>();
    }

    /// <summary>Creates a timer with explicit steps (tests: a step that throws).</summary>
    internal GatewayMaintenanceTimer(Action tick, Action sweepTable, Action sweepMap, TimeSpan period, TimeProvider time, ILoggerFactory loggerFactory)
    {
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _sweepTable = sweepTable ?? throw new ArgumentNullException(nameof(sweepTable));
        _sweepMap = sweepMap ?? throw new ArgumentNullException(nameof(sweepMap));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _period = period;
        _logger = loggerFactory.CreateLogger<GatewayMaintenanceTimer>();
        _keeper = null;
        _table = null;
        _map = null;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer ??= _time.CreateTimer(_ => RunOnce(), null, _period, _period);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopping, 1);
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>Runs one maintenance pass now (what each timer period does). Skipped if a pass is still running or the timer was stopped.</summary>
    public void RunOnce()
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return;
        }

        try
        {
            Step("WarmSpareKeeper.Tick", _tick);
            Step("RoutingTable.Sweep", _sweepTable);
            Step("AccountZoneMap.Sweep", _sweepMap);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private void Step(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway maintenance step {Step} failed", name);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _timer?.Dispose();
}

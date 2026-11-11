using System.Net;
using System.Net.Sockets;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Hosting;
using IronGrind.Gateway.Launch;
using IronGrind.Gateway.Routing;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 009: the composed gateway. Graph, fan-out order, maintenance timer and boot-wait cases run on a plain
/// service provider with a manual clock; the end-to-end cases (AC-4, AC-5, AC-6) run a real host with a real zone-link
/// listener on a free loopback port, the test database and a fake zone client that speaks the real link codec.
/// Non-parallelizable: the end-to-end cases read the process environment variable <c>IRONGRIND_GATEWAY_DB</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
internal sealed class Gateway_Composition_tests
{
    private const string VALID_CONNECTION = "Host=127.0.0.1;Database=irongrind_auth_test;Username=gateway;Password=unused";
    private const ushort ZONE_PORT = 7801;
    private const uint ZONE_RAW = 5;
    private const uint OTHER_ZONE_RAW = 6;
    private static readonly TimeSpan POLL_LIMIT = TimeSpan.FromSeconds(20);

    private ServiceProvider? _provider;
    private GwTestHost? _host;
    private HttpClient? _client;

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _host?.Dispose();
        _provider?.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    private ServiceProvider Build(ManualTime time, Action<IServiceCollection>? tweak = null, params (string Key, string Value)[] settings) =>
        BuildCore(true, time, tweak, settings);

    // The running gateway starts the boot wait when the listener has bound; most tests start it at once instead.
    private ServiceProvider BuildCore(bool startBootWait, ManualTime time, Action<IServiceCollection>? tweak, (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            [GatewayHost.DATABASE_CONFIG_KEY] = VALID_CONNECTION,
            ["GATEWAY_ZONE_LINK_PORT"] = GwPorts.Free().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach ((string key, string value) in settings)
        {
            values[key] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGatewayServices(configuration);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(time);
        tweak?.Invoke(services);
        _provider = services.BuildServiceProvider();
        if (startBootWait)
        {
            _provider.GetRequiredService<GatewayBootWait>().Start();
        }

        return _provider;
    }

    private static void UseFakes(IServiceCollection services, RecordingLauncher launcher)
    {
        services.RemoveAll<IZoneLauncher>();
        services.AddSingleton<IZoneLauncher>(launcher);
        services.RemoveAll<IZoneIdAllocator>();
        services.AddSingleton<IZoneIdAllocator>(new GwFakeAllocator());
        services.RemoveAll<IAccountRepository>();
        services.AddSingleton<IAccountRepository>(new GwFakeRepo());
    }

    private static RegisterZoneMessage Register(uint zone, ushort capacity = 50) =>
        new(new ZoneID(zone), FillFirstRouter.STARTING_TEMPLATE, (ushort)(7776 + zone), capacity, Array.Empty<AccountID>());

    private static async Task PollAsync(Func<bool> condition, string what)
    {
        using var guard = new CancellationTokenSource(POLL_LIMIT);
        while (!condition())
        {
            if (guard.IsCancellationRequested)
            {
                Assert.Fail("Timed out waiting for: " + what);
            }

            await Task.Delay(20);
        }
    }

    // ---------------------------------------------------------------- AC-1: graph

    [Test]
    public void Test_Graph_EachInterfaceResolvesToTheRealTypeAndTheSameInstance()
    {
        ServiceProvider sp = Build(new ManualTime());

        Assert.That(sp.GetRequiredService<IZoneLink>(), Is.TypeOf<LinkZoneLink>().And.SameAs(sp.GetRequiredService<LinkZoneLink>()));
        Assert.That(sp.GetRequiredService<IAccountZoneMap>(), Is.TypeOf<AccountZoneMap>().And.SameAs(sp.GetRequiredService<AccountZoneMap>()));
        Assert.That(sp.GetRequiredService<IZoneRouter>(), Is.TypeOf<FillFirstRouter>().And.SameAs(sp.GetRequiredService<FillFirstRouter>()));
        Assert.That(sp.GetRequiredService<IZoneLinkSender>(), Is.SameAs(sp.GetRequiredService<ZoneLinkListener>()));
        Assert.That(sp.GetRequiredService<IZoneLinkEvents>(), Is.SameAs(sp.GetRequiredService<ZoneLinkEventFanOut>()));
        Assert.That(sp.GetRequiredService<IZoneEvictSender>(), Is.TypeOf<ZoneLinkEvictSender>());
        Assert.That(sp.GetRequiredService<IZoneIdAllocator>(), Is.TypeOf<NpgsqlZoneIdAllocator>());
        Assert.That(sp.GetRequiredService<IZoneLauncher>(), Is.TypeOf<NoOpZoneLauncher>());
        Assert.That(sp.GetRequiredService<IZoneLauncher>(), Is.SameAs(sp.GetRequiredService<IZoneLauncher>()));
        Assert.That(sp.GetRequiredService<RoutingTable>(), Is.SameAs(sp.GetRequiredService<RoutingTable>()));
        Assert.That(sp.GetRequiredService<WarmSpareKeeper>(), Is.SameAs(sp.GetRequiredService<WarmSpareKeeper>()));
        Assert.That(sp.GetRequiredService<GatewayBootWait>(), Is.SameAs(sp.GetRequiredService<GatewayBootWait>()));
    }

    [Test]
    public void Test_Graph_HostedServices_AreTheOneListenerAndOneTimer()
    {
        ServiceProvider sp = Build(new ManualTime());

        IHostedService[] hosted = sp.GetServices<IHostedService>().ToArray();

        Assert.That(hosted.OfType<ZoneLinkListener>().Single(), Is.SameAs(sp.GetRequiredService<ZoneLinkListener>()));
        Assert.That(hosted.OfType<GatewayMaintenanceTimer>().Count(), Is.EqualTo(1));
        Assert.That(hosted.OfType<GatewayStartupCheck>().Count(), Is.EqualTo(1));
        Assert.That(hosted.OfType<GatewayStartupCheck>().Single(), Is.SameAs(hosted[0]), "the check starts first");
    }

    [Test]
    public void Test_Graph_ListenerAndLinkAndMap_ShareTheSameInstancesThroughTheFanOut()
    {
        ServiceProvider sp = Build(new ManualTime());

        IReadOnlyList<IZoneLinkEvents> handlers = sp.GetRequiredService<ZoneLinkEventFanOut>().Handlers;

        Assert.That(handlers[1], Is.SameAs(sp.GetRequiredService<IAccountZoneMap>()));
        Assert.That(handlers[2], Is.SameAs(sp.GetRequiredService<IZoneLink>()));
    }

    // ---------------------------------------------------------------- AC-2: one subscription point

    [Test]
    public void Test_FanOut_Handlers_AreTableThenMapThenLinkThenKeeper()
    {
        ServiceProvider sp = Build(new ManualTime());

        IReadOnlyList<IZoneLinkEvents> handlers = sp.GetRequiredService<ZoneLinkEventFanOut>().Handlers;

        Assert.That(handlers, Has.Count.EqualTo(4));
        Assert.That(handlers[0], Is.SameAs(sp.GetRequiredService<RoutingTable>()));
        Assert.That(handlers[1], Is.TypeOf<AccountZoneMap>());
        Assert.That(handlers[2], Is.TypeOf<LinkZoneLink>());
        Assert.That(handlers[3], Is.SameAs(sp.GetRequiredService<WarmSpareKeeper>()));
    }

    [Test]
    public void Test_FanOut_ThrowingHandler_LogsOneErrorNamingHandlerEventAndZone()
    {
        var log = new List<string>();
        var logs = new GwLogProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var fanOut = new ZoneLinkEventFanOut(
            () => new IZoneLinkEvents[] { new Recorder("a", log, throws: true), new Recorder("b", log) }, loggerFactory);

        fanOut.OnZoneRegistered(new ZoneID(41u), Register(41));

        GwLogEntry[] errors = logs.Entries.Where(e => e.Level == LogLevel.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Message, Does.Contain(nameof(Recorder)));
        Assert.That(errors[0].Message, Does.Contain(nameof(IZoneLinkEvents.OnZoneRegistered)));
        Assert.That(errors[0].Message, Does.Contain("41"));
        Assert.That(log, Is.EqualTo(new[] { "a:reg", "b:reg" }));
    }

    [Test]
    public void Test_FanOut_EveryEvent_ReachesHandlersInOrder_AndAThrowingHandlerDoesNotStopTheRest()
    {
        var log = new List<string>();
        var fanOut = new ZoneLinkEventFanOut(
            () => new IZoneLinkEvents[] { new Recorder("a", log, throws: true), new Recorder("b", log), new Recorder("c", log) },
            NullLoggerFactory.Instance);
        ZoneToGatewayMessage message = default;

        fanOut.OnZoneRegistered(new ZoneID(1u), Register(1));
        fanOut.OnMessageReceived(new ZoneID(1u), message);
        fanOut.OnLinkLost(new ZoneID(1u));

        Assert.That(
            log,
            Is.EqualTo(new[] { "a:reg", "b:reg", "c:reg", "a:msg", "b:msg", "c:msg", "a:lost", "b:lost", "c:lost" }));
    }

    [Test]
    public void Test_FanOut_HandlersResolvedOnce_AndOnlyWhenFirstNeeded()
    {
        int resolved = 0;
        var fanOut = new ZoneLinkEventFanOut(
            () =>
            {
                resolved++;
                return Array.Empty<IZoneLinkEvents>();
            },
            NullLoggerFactory.Instance);
        Assert.That(resolved, Is.EqualTo(0));

        fanOut.OnLinkLost(new ZoneID(1u));
        fanOut.OnLinkLost(new ZoneID(2u));

        Assert.That(resolved, Is.EqualTo(1));
    }

    [Test]
    public void Test_FanOut_RegistrationThroughTheRealGraph_UpdatesTableMapAndKeeper()
    {
        var launcher = new RecordingLauncher();
        ServiceProvider sp = Build(new ManualTime(), s => UseFakes(s, launcher));

        sp.GetRequiredService<ZoneLinkEventFanOut>().OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));

        Assert.That(sp.GetRequiredService<RoutingTable>().TryGet(new ZoneID(ZONE_RAW), out ZoneRoute route), Is.True);
        Assert.That(route.LinkUp, Is.True);
        Assert.That(sp.GetRequiredService<IAccountZoneMap>().IsLinkUp(new ZoneID(ZONE_RAW)), Is.True);
        Assert.That(sp.GetRequiredService<IZoneRouter>().TryPick(ZoneID.Invalid, ZoneID.Invalid, out ZoneID picked, out _), Is.True);
        Assert.That(picked, Is.EqualTo(new ZoneID(ZONE_RAW)));
    }

    // ---------------------------------------------------------------- maintenance timer

    [TestCase(1000)]
    [TestCase(250)]
    public async Task Test_Timer_StartsWithTheConfiguredPeriod(int intervalMs)
    {
        var time = new ManualTime();
        ServiceProvider sp = Build(
            time, s => UseFakes(s, new RecordingLauncher()), ("GATEWAY_MAINTENANCE_INTERVAL_MS", intervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        await sp.GetServices<IHostedService>().OfType<GatewayMaintenanceTimer>().Single().StartAsync(CancellationToken.None);

        Assert.That(time.Period, Is.EqualTo(TimeSpan.FromMilliseconds(intervalMs)));
    }

    [Test]
    public async Task Test_Timer_Fire_RunsKeeperTickAndRoutingTableSweep()
    {
        var time = new ManualTime();
        var launcher = new RecordingLauncher();
        ServiceProvider sp = Build(time, s => UseFakes(s, launcher));
        var fanOut = sp.GetRequiredService<ZoneLinkEventFanOut>();
        var table = sp.GetRequiredService<RoutingTable>();
        await sp.GetServices<IHostedService>().OfType<GatewayMaintenanceTimer>().Single().StartAsync(CancellationToken.None);

        // A zone registers and loses its link during the boot wait (default 2 x 2 s); nothing may start yet.
        fanOut.OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));
        fanOut.OnLinkLost(new ZoneID(ZONE_RAW));
        time.Timer!.Fire();
        Assert.That(launcher.Calls, Is.Empty);
        Assert.That(table.TryGet(new ZoneID(ZONE_RAW), out _), Is.True);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.That(table.TryGet(new ZoneID(ZONE_RAW), out _), Is.True, "nothing pruned the row before the timer fired");
        time.Timer.Fire();

        Assert.That(table.TryGet(new ZoneID(ZONE_RAW), out _), Is.False, "RoutingTable.Sweep ran");
        await PollAsync(() => launcher.Calls.Count == 1, "WarmSpareKeeper.Tick starts a spare");
    }

    [Test]
    public void Test_Timer_ThrowingStep_IsLoggedOnce_AndTheOtherStepsStillRun()
    {
        var time = new ManualTime();
        var logs = new GwLogProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        ServiceProvider sp = Build(time, s => UseFakes(s, new RecordingLauncher()));
        var fanOut = sp.GetRequiredService<ZoneLinkEventFanOut>();
        var table = sp.GetRequiredService<RoutingTable>();
        var map = sp.GetRequiredService<AccountZoneMap>();
        fanOut.OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));
        fanOut.OnLinkLost(new ZoneID(ZONE_RAW));
        var timer = new GatewayMaintenanceTimer(
            () => throw new InvalidOperationException("tick failed on purpose"), table.Sweep, map.Sweep, TimeSpan.FromSeconds(1), time, loggerFactory);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.That(table.TryGet(new ZoneID(ZONE_RAW), out _), Is.True);

        Assert.DoesNotThrow(timer.RunOnce);

        Assert.That(table.TryGet(new ZoneID(ZONE_RAW), out _), Is.False, "RoutingTable.Sweep still ran");
        GwLogEntry[] errors = logs.Entries.Where(e => e.Level == LogLevel.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Category, Does.EndWith(nameof(GatewayMaintenanceTimer)));
        Assert.That(errors[0].Message, Does.Contain("WarmSpareKeeper.Tick"));
    }

    [Test]
    public async Task Test_Timer_Fire_RunsAccountZoneMapSweep_WhichOnlySweepRemovesFromStorage()
    {
        var time = new ManualTime();
        ServiceProvider sp = Build(time, s => UseFakes(s, new RecordingLauncher()));
        var fanOut = sp.GetRequiredService<ZoneLinkEventFanOut>();
        var map = sp.GetRequiredService<AccountZoneMap>();
        fanOut.OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));
        map.SetSession(new AccountID(1u), new ZoneID(ZONE_RAW));
        fanOut.OnLinkLost(new ZoneID(ZONE_RAW));
        GatewayMaintenanceTimer timer = sp.GetServices<IHostedService>().OfType<GatewayMaintenanceTimer>().Single();
        await timer.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.That(map.StoredEntryCount, Is.EqualTo(1), "the grace passed but nothing has purged the entry yet");

        time.Timer!.Fire();

        Assert.That(map.StoredEntryCount, Is.EqualTo(0), "AccountZoneMap.Sweep ran");
    }

    [Test]
    public async Task Test_Timer_AfterStop_RunOnceDoesNothing()
    {
        var time = new ManualTime();
        var launcher = new RecordingLauncher();
        ServiceProvider sp = Build(time, s => UseFakes(s, launcher));
        GatewayMaintenanceTimer timer = sp.GetServices<IHostedService>().OfType<GatewayMaintenanceTimer>().Single();
        await timer.StartAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(5));

        await timer.StopAsync(CancellationToken.None);
        timer.RunOnce();
        time.Timer!.Fire();

        Assert.That(launcher.Calls, Is.Empty, "the boot wait ended with no spare, but no pass may start after stop");
    }

    // ---------------------------------------------------------------- AC-3: boot wait

    [Test]
    public async Task Test_BootWait_LoginBeforeItEnds_IsServerStarting_EvenWithARoutableZone()
    {
        var time = new ManualTime();
        var launcher = new RecordingLauncher();
        ServiceProvider sp = Build(time, s => UseFakes(s, launcher));
        sp.GetRequiredService<ZoneLinkEventFanOut>().OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));
        var sessions = sp.GetRequiredService<SessionEstablishment>();

        time.Advance(TimeSpan.FromSeconds(4) - TimeSpan.FromMilliseconds(1));
        LoginResponse response = await GwHttp.Within(sessions.RunAsync(new AccountID(1u)));

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        Assert.That(launcher.Calls, Is.Empty);
    }

    [Test]
    public async Task Test_BootWait_AfterItEnds_NoRoutableZone_IsServerStarting_AndASpareIsRequested()
    {
        var time = new ManualTime();
        var launcher = new RecordingLauncher();
        ServiceProvider sp = Build(time, s => UseFakes(s, launcher));
        var sessions = sp.GetRequiredService<SessionEstablishment>();
        GatewayMaintenanceTimer timer = sp.GetServices<IHostedService>().OfType<GatewayMaintenanceTimer>().Single();

        time.Advance(TimeSpan.FromSeconds(4));
        LoginResponse response = await GwHttp.Within(sessions.RunAsync(new AccountID(1u)));
        timer.RunOnce();

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        await PollAsync(() => launcher.Calls.Count == 1, "a spare start is requested");
        Assert.That(launcher.Calls[0].Template, Is.EqualTo("StartingZone"));
    }

    [Test]
    public async Task Test_BootWait_AfterItEnds_WithARegisteredZone_LoginSucceedsWithZoneAndPort()
    {
        var time = new ManualTime();
        ServiceProvider sp = Build(time, s =>
        {
            UseFakes(s, new RecordingLauncher());
            s.RemoveAll<IZoneLink>();
            s.AddSingleton<IZoneLink>(new AcceptingLink());
        });
        sp.GetRequiredService<ZoneLinkEventFanOut>().OnZoneRegistered(new ZoneID(ZONE_RAW), Register(ZONE_RAW));
        var sessions = sp.GetRequiredService<SessionEstablishment>();
        time.Advance(TimeSpan.FromSeconds(4));

        LoginResponse response = await GwHttp.Within(sessions.RunAsync(new AccountID(1u)));

        Assert.That(response.Success, Is.True, response.ErrorCode.ToString());
        Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(ZONE_RAW)));
        Assert.That(response.ZoneServerPort, Is.EqualTo((ushort)(7776 + ZONE_RAW)));
        Assert.That(response.SessionToken, Has.Length.EqualTo(16));
    }

    [Test]
    public void Test_BootWait_NotStarted_IsNeverOver_StartOpensTheWindow_SecondStartDoesNotMoveIt()
    {
        var time = new ManualTime();
        var options = new GatewayOptions { LinkRetrySeconds = 2 };
        GatewayBootWait wait = GatewayBootWait.NotStarted(options, time);

        time.Advance(TimeSpan.FromDays(1));
        Assert.That(wait.IsStarted, Is.False);
        Assert.That(wait.IsOver, Is.False, "not started: never over");

        wait.Start();
        time.Advance(TimeSpan.FromSeconds(3));
        wait.Start();
        Assert.That(wait.IsOver, Is.False, "a second Start must not move the end");
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(wait.IsOver, Is.True, "over after 2 x retry from the first Start");
    }

    [Test]
    public async Task Test_BootWait_ThroughTheGraph_StartsWhenTheListenerHasBound()
    {
        var time = new ManualTime();
        ServiceProvider sp = BuildCore(false, time, s => UseFakes(s, new RecordingLauncher()), Array.Empty<(string, string)>());
        var wait = sp.GetRequiredService<GatewayBootWait>();
        var listener = sp.GetRequiredService<ZoneLinkListener>();
        time.Advance(TimeSpan.FromDays(1));
        Assert.That(wait.IsStarted, Is.False, "not running before the listener starts");
        Assert.That(wait.IsOver, Is.False);

        await listener.StartAsync(CancellationToken.None);
        try
        {
            Assert.That(wait.IsStarted, Is.True, "running once the listener has bound");
            Assert.That(wait.IsOver, Is.False);
            time.Advance(TimeSpan.FromSeconds(4));
            Assert.That(wait.IsOver, Is.True);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public void Test_Assembly_HasNoNoZoneStandIns()
    {
        string[] banned = { "UnavailableZoneLink", "NoZoneRouter", "EmptyAccountZoneMap" };

        string[] found = typeof(GatewayHost).Assembly.GetTypes().Select(t => t.Name).Where(banned.Contains).ToArray();

        Assert.That(found, Is.Empty);
    }

    // ---------------------------------------------------------------- AC-7: startup check and options

    [TestCase(0)]
    [TestCase(-5)]
    public void Test_Validator_MaintenanceIntervalNotPositive_Fails(int value)
    {
        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, new GatewayOptions { MaintenanceIntervalMs = value });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("GATEWAY_MAINTENANCE_INTERVAL_MS"));
    }

    [Test]
    public void Test_Validator_DefaultMaintenanceInterval_Is1000AndValid()
    {
        var options = new GatewayOptions();

        Assert.That(options.MaintenanceIntervalMs, Is.EqualTo(1000));
        Assert.That(new GatewayOptionsValidator().Validate(null, options).Succeeded, Is.True);
    }

    [Test]
    public void Test_Startup_InvalidMaintenanceInterval_FailsHostStart()
    {
        _host = new GwTestHost();
        _host.Settings["GATEWAY_MAINTENANCE_INTERVAL_MS"] = "0";

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<OptionsValidationException>(thrown), Is.Not.Null, thrown?.ToString());
    }

    [Test]
    public void Test_Startup_UnknownZoneLauncher_FailsHostStart()
    {
        _host = new GwTestHost();
        _host.Settings["ZONE_LAUNCHER"] = "docker";

        Exception? thrown = Assert.Catch(() => _client = _host.CreateClient());

        Assert.That(FindInChain<OptionsValidationException>(thrown), Is.Not.Null, thrown?.ToString());
    }

    [Test]
    public void Test_Startup_ValidHost_StartsListenerOnTheConfiguredPort()
    {
        _host = new GwTestHost();
        _client = _host.CreateClient();

        var listener = _host.Services().GetRequiredService<ZoneLinkListener>();

        Assert.That(listener.LocalEndpoint, Is.Not.Null);
        Assert.That(listener.LocalEndpoint!.Address, Is.EqualTo(IPAddress.Loopback));
        Assert.That(listener.LocalEndpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(_host.Settings["GATEWAY_ZONE_LINK_PORT"]));
    }

    private static TException? FindInChain<TException>(Exception? ex) where TException : Exception
    {
        while (ex is not null)
        {
            if (ex is TException match)
            {
                return match;
            }

            ex = ex.InnerException;
        }

        return null;
    }

    // ---------------------------------------------------------------- AC-4 / AC-5 / AC-6: end to end (Database)

    private async Task<(AuthTestDatabase Db, FakeZoneClient Zone)> StartEndToEndAsync(bool acceptAdmits = true)
    {
        AuthTestDatabase db = await AuthTestDatabase.CreateAsync();
        _host = new GwTestHost();
        _host.ReplaceRepository = false;
        _host.Settings["GATEWAY_LINK_RETRY_SECONDS"] = "1";
        _client = _host.CreateClient();
        RegistrationResult registered = await GwHttp.Within(GwHttp.PostRegisterAsync(_client, "alice", GwHttp.CORRECT_PASSWORD, "Aldric"));
        Assert.That(registered.Success, Is.True);
        FakeZoneClient zone = await ConnectZoneAsync(ZONE_RAW, ZONE_PORT);
        zone.AcceptAdmits = acceptAdmits;
        return (db, zone);
    }

    private async Task<FakeZoneClient> ConnectZoneAsync(uint zoneId, ushort udpPort)
    {
        int port = int.Parse(_host!.Settings["GATEWAY_ZONE_LINK_PORT"]!, System.Globalization.CultureInfo.InvariantCulture);
        FakeZoneClient zone = await FakeZoneClient.ConnectAsync(port, new RegisterZoneMessage(
            new ZoneID(zoneId), FillFirstRouter.STARTING_TEMPLATE, udpPort, 50, Array.Empty<AccountID>()));
        var table = _host.Services().GetRequiredService<RoutingTable>();
        await PollAsync(() => table.TryGet(new ZoneID(zoneId), out _), $"zone {zoneId} registered");
        return zone;
    }

    /// <summary>Logs in until the boot wait is over (ServerStarting is the answer until then). Bounded polling.</summary>
    private async Task<LoginResponse> LoginPastBootWaitAsync()
    {
        using var guard = new CancellationTokenSource(POLL_LIMIT);
        while (true)
        {
            LoginResponse response = await GwHttp.Within(GwHttp.PostLoginAsync(_client!, "alice", GwHttp.CORRECT_PASSWORD));
            if (response.Success || response.ErrorCode != AuthFailureReason.ServerStarting)
            {
                return response;
            }

            if (guard.IsCancellationRequested)
            {
                Assert.Fail("The boot wait did not end in time.");
            }

            await Task.Delay(100);
        }
    }

    [Test]
    [Category("Database")]
    [CancelAfter(60000)]
    public async Task Test_EndToEnd_Login_AdmitsAtTheFakeZone_AndReturnsTheSameToken()
    {
        (AuthTestDatabase db, FakeZoneClient zone) = await StartEndToEndAsync();
        await using (db)
        await using (zone)
        {
            LoginResponse response = await LoginPastBootWaitAsync();

            Assert.That(response.Success, Is.True, response.ErrorCode.ToString());
            AdmitMessage admit = zone.Admits.Single();
            Assert.That(admit.Token, Has.Length.EqualTo(16));
            Assert.That(response.SessionToken, Is.EqualTo(admit.Token));
            Assert.That(response.CharacterId, Is.EqualTo(admit.CharacterId));
            Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(ZONE_RAW)));
            Assert.That(response.ZoneServerPort, Is.EqualTo(ZONE_PORT));
            var map = _host!.Services().GetRequiredService<IAccountZoneMap>();
            Assert.That(map.TryGetZone(admit.AccountId, out ZoneID held), Is.True);
            Assert.That(held, Is.EqualTo(new ZoneID(ZONE_RAW)));
        }
    }

    [Test]
    [Category("Database")]
    [CancelAfter(60000)]
    public async Task Test_EndToEnd_AdmitRejected_NoOtherZone_IsServerError()
    {
        (AuthTestDatabase db, FakeZoneClient zone) = await StartEndToEndAsync(acceptAdmits: false);
        await using (db)
        await using (zone)
        {
            object? failuresBefore = await db.AdminScalarAsync("SELECT login_failure_count FROM accounts WHERE username_lower = @u", ("u", "alice"));
            LoginResponse response = await LoginPastBootWaitAsync();

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerError));
            Assert.That(zone.Admits, Has.Count.EqualTo(1), "one attempt; the re-route found nothing to pick");
            var map = _host!.Services().GetRequiredService<IAccountZoneMap>();
            Assert.That(map.TryGetZone(zone.Admits[0].AccountId, out _), Is.False);
            object? failuresAfter = await db.AdminScalarAsync("SELECT login_failure_count FROM accounts WHERE username_lower = @u", ("u", "alice"));
            Assert.That(failuresAfter, Is.EqualTo(failuresBefore), "a rejected admission is not a credential failure");
        }
    }

    [Test]
    [Category("Database")]
    [CancelAfter(60000)]
    public async Task Test_EndToEnd_SessionReleased_ClearsTheMap_AndTheNextLoginSendsNoEvict()
    {
        (AuthTestDatabase db, FakeZoneClient zone) = await StartEndToEndAsync();
        await using (db)
        await using (zone)
        {
            LoginResponse first = await LoginPastBootWaitAsync();
            Assert.That(first.Success, Is.True, first.ErrorCode.ToString());
            AccountID account = zone.Admits.Single().AccountId;
            var map = _host!.Services().GetRequiredService<IAccountZoneMap>();
            Assert.That(map.TryGetZone(account, out _), Is.True);

            await zone.SendAsync(GatewayLinkCodec.Encode(new SessionReleasedMessage(account)));
            await PollAsync(() => !map.TryGetZone(account, out _), "map entry removed");
            LoginResponse second = await GwHttp.Within(GwHttp.PostLoginAsync(_client!, "alice", GwHttp.CORRECT_PASSWORD));

            Assert.That(second.Success, Is.True, second.ErrorCode.ToString());
            Assert.That(zone.Evicts, Is.Empty);
        }
    }

    [Test]
    [Category("Database")]
    [CancelAfter(60000)]
    public async Task Test_EndToEnd_SessionReleasedFromAnotherZone_KeepsTheEntry_AndTheNextLoginSendsEvict()
    {
        (AuthTestDatabase db, FakeZoneClient zone) = await StartEndToEndAsync();
        await using (db)
        await using (zone)
        {
            await using FakeZoneClient other = await ConnectZoneAsync(OTHER_ZONE_RAW, ZONE_PORT + 1);
            LoginResponse first = await LoginPastBootWaitAsync();
            Assert.That(first.Success, Is.True, first.ErrorCode.ToString());
            AccountID account = (zone.Admits.Concat(other.Admits)).Single().AccountId;
            ZoneID admittedZone = first.ZoneId;
            FakeZoneClient admitting = admittedZone == new ZoneID(ZONE_RAW) ? zone : other;
            FakeZoneClient bystander = admitting == zone ? other : zone;

            // The bystander releases an account it never held. Its PlayerCount follows on the same link, so once the
            // table shows that count the release was already dispatched.
            await bystander.SendAsync(GatewayLinkCodec.Encode(new SessionReleasedMessage(account)));
            await bystander.SendAsync(GatewayLinkCodec.Encode(new PlayerCountMessage(7)));
            var table = _host!.Services().GetRequiredService<RoutingTable>();
            ZoneID bystanderZone = bystander == zone ? new ZoneID(ZONE_RAW) : new ZoneID(OTHER_ZONE_RAW);
            await PollAsync(() => table.TryGet(bystanderZone, out ZoneRoute r) && r.PlayerCount == 7, "bystander count seen");

            var map = _host.Services().GetRequiredService<IAccountZoneMap>();
            Assert.That(map.TryGetZone(account, out ZoneID still), Is.True);
            Assert.That(still, Is.EqualTo(admittedZone));
            LoginResponse second = await GwHttp.Within(GwHttp.PostLoginAsync(_client!, "alice", GwHttp.CORRECT_PASSWORD));

            Assert.That(second.Success, Is.True, second.ErrorCode.ToString());
            Assert.That(admitting.Evicts.Select(e => e.AccountId), Is.EqualTo(new[] { account }));
            Assert.That(bystander.Evicts, Is.Empty);
        }
    }

    // ---------------------------------------------------------------- fakes

    private sealed class Recorder : IZoneLinkEvents
    {
        private readonly string _name;
        private readonly List<string> _log;
        private readonly bool _throws;

        public Recorder(string name, List<string> log, bool throws = false)
        {
            _name = name;
            _log = log;
            _throws = throws;
        }

        public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message) => Record("reg");

        public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message) => Record("msg");

        public void OnLinkLost(ZoneID zone) => Record("lost");

        private void Record(string what)
        {
            _log.Add($"{_name}:{what}");
            if (_throws)
            {
                throw new InvalidOperationException("handler failed on purpose");
            }
        }
    }

    private sealed class AcceptingLink : IZoneLink
    {
        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct) => Task.FromResult(true);

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct) => Task.CompletedTask;

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLauncher : IZoneLauncher
    {
        private readonly object _lock = new();
        private readonly List<(ZoneID Zone, string Template, ushort Port)> _calls = new();

        public IReadOnlyList<(ZoneID Zone, string Template, ushort Port)> Calls
        {
            get
            {
                lock (_lock)
                {
                    return _calls.ToArray();
                }
            }
        }

        public Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct)
        {
            lock (_lock)
            {
                _calls.Add((zone, zoneTemplate, port));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public ManualTimer? Timer { get; private set; }

        public TimeSpan Period { get; private set; }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Period = period;
            Timer = new ManualTimer(callback, state);
            return Timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A zone client on the real link codec: registers, records Admit and Evict, and answers them.</summary>
    private sealed class FakeZoneClient : IAsyncDisposable
    {
        private const int MAX_FRAME = 65536;
        private static readonly TimeSpan IO_LIMIT = TimeSpan.FromSeconds(10);
        private readonly CancellationTokenSource _readStop = new();
        private readonly TcpClient _client;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly object _lock = new();
        private readonly List<AdmitMessage> _admits = new();
        private readonly List<EvictMessage> _evicts = new();
        private Task _reader = Task.CompletedTask;

        private FakeZoneClient(TcpClient client) => _client = client;

        public volatile bool AcceptAdmits = true;

        public List<AdmitMessage> Admits
        {
            get
            {
                lock (_lock)
                {
                    return _admits.ToList();
                }
            }
        }

        public List<EvictMessage> Evicts
        {
            get
            {
                lock (_lock)
                {
                    return _evicts.ToList();
                }
            }
        }

        public static async Task<FakeZoneClient> ConnectAsync(int port, RegisterZoneMessage register)
        {
            var tcp = new TcpClient { NoDelay = true };
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(IPAddress.Loopback, port, guard.Token);
            var zone = new FakeZoneClient(tcp);
            await zone.SendAsync(GatewayLinkCodec.Encode(register));
            zone._reader = Task.Run(zone.ReadLoopAsync);
            return zone;
        }

        public async Task SendAsync(byte[] frame)
        {
            await _writeLock.WaitAsync(IO_LIMIT);
            try
            {
                using var cts = new CancellationTokenSource(IO_LIMIT);
                await _client.GetStream().WriteAsync(frame, cts.Token);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[MAX_FRAME * 2];
            int filled = 0;
            try
            {
                while (true)
                {
                    int read = await _client.GetStream().ReadAsync(buffer.AsMemory(filled), _readStop.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    filled += read;
                    int offset = 0;
                    while (offset < filled
                        && GatewayLinkCodec.TryReadFrame(buffer.AsSpan(offset, filled - offset), MAX_FRAME, out byte type, out ReadOnlySpan<byte> body, out int consumed)
                            == GatewayLinkFrameStatus.Frame)
                    {
                        if (GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message))
                        {
                            await HandleAsync(message);
                        }

                        offset += consumed;
                    }

                    Buffer.BlockCopy(buffer, offset, buffer, 0, filled - offset);
                    filled -= offset;
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException or OperationCanceledException)
            {
                // The test ended and closed the socket.
            }
        }

        private async Task HandleAsync(GatewayToZoneMessage message)
        {
            if (message.Type == GatewayLinkMessageType.Admit)
            {
                lock (_lock)
                {
                    _admits.Add(message.Admit);
                }

                bool accept = AcceptAdmits;
                await SendAsync(GatewayLinkCodec.Encode(new AdmitResultMessage(
                    message.Admit.RequestId, accept, accept ? AdmitRejectReason.None : AdmitRejectReason.NotActive)));
            }
            else if (message.Type == GatewayLinkMessageType.Evict)
            {
                lock (_lock)
                {
                    _evicts.Add(message.Evict);
                }

                await SendAsync(GatewayLinkCodec.Encode(new EvictResultMessage(message.Evict.RequestId)));
            }
        }

        public async ValueTask DisposeAsync()
        {
            _readStop.Cancel();
            _client.Dispose();
            await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(5)));
            _writeLock.Dispose();
        }
    }
}

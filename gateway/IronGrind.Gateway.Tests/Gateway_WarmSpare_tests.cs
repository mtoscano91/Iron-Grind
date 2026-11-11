using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 007: <see cref="WarmSpareKeeper"/>, <see cref="RoutingTable.Sweep"/>, the rule 3 emptied-instance
/// tie-break and the shared <see cref="GatewayBootWait"/> (AC-ZI-1, AC-ZI-19, AC-ZI-33a to f). Fake clock, launcher and
/// allocator; no real waits.
/// </summary>
[TestFixture]
internal sealed class Gateway_WarmSpare_tests
{
    private static readonly TimeSpan _boot = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan _loadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _ms = TimeSpan.FromMilliseconds(1);

    private FakeClock _clock = null!;
    private GwLogProvider _logs = null!;
    private ILoggerFactory _loggerFactory = null!;
    private GatewayOptions _options = null!;
    private RoutingTable _table = null!;
    private GatewayBootWait _bootWait = null!;
    private FakeLauncher _launcher = null!;
    private FakeAllocator _allocator = null!;
    private WarmSpareKeeper _keeper = null!;
    private FillFirstRouter _router = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeClock();
        _logs = new GwLogProvider();
        _loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        Build(new GatewayOptions());
    }

    [TearDown]
    public void TearDown() => _loggerFactory.Dispose();

    private void Build(GatewayOptions options)
    {
        _options = options;
        _table = new RoutingTable(_options, _clock, _loggerFactory);
        _bootWait = new GatewayBootWait(_options, _clock);
        _launcher = new FakeLauncher();
        _allocator = new FakeAllocator();
        _keeper = new WarmSpareKeeper(_table, _allocator, _launcher, _options, _bootWait, _clock, _loggerFactory);
        _router = new FillFirstRouter(_table, _options);
    }

    private static ZoneID Z(uint id) => new(id);

    // Story 009 order: routing table first, then the keeper.
    private void Register(uint id, int count, ushort port = 0, ushort capacity = 50, byte template = 0)
    {
        ushort udp = port == 0 ? (ushort)(7776 + id) : port;
        var message = new RegisterZoneMessage(Z(id), template, udp, capacity, Array.Empty<AccountID>());
        _table.OnZoneRegistered(Z(id), message);
        _keeper.OnZoneRegistered(Z(id), message);
        if (count > 0)
        {
            SetCount(id, count);
        }
    }

    private void SetCount(uint id, int count)
    {
        ZoneToGatewayMessage message = Decode(GatewayLinkCodec.Encode(new PlayerCountMessage((ushort)count)));
        _table.OnMessageReceived(Z(id), message);
        _keeper.OnMessageReceived(Z(id), message);
    }

    private void Drain(uint id)
    {
        ZoneToGatewayMessage message = Decode(GatewayLinkCodec.EncodeZoneDraining());
        _table.OnMessageReceived(Z(id), message);
        _keeper.OnMessageReceived(Z(id), message);
    }

    private void LoseLink(uint id)
    {
        _table.OnLinkLost(Z(id));
        _keeper.OnLinkLost(Z(id));
    }

    private static ZoneToGatewayMessage Decode(byte[] frame)
    {
        Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(frame[4], frame.AsSpan(5), out ZoneToGatewayMessage message), Is.True);
        return message;
    }

    private uint? Pick(uint last = 0)
    {
        return _router.TryPick(Z(last), ZoneID.Invalid, out ZoneID zone, out _) ? zone.RawValue : null;
    }

    private int CriticalLogs(string text) =>
        _logs.Entries.Count(e => e.Level == LogLevel.Critical && e.Message.Contains(text, StringComparison.Ordinal));

    private void EndBootWait() => _clock.Advance(_boot);

    // ---------------------------------------------------------------- AC-ZI-33(a)

    [Test]
    public void BootWait_OneMillisecondEarly_NoStart_ThenExactlyOneStartingZoneStart()
    {
        _clock.Advance(_boot - _ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);
        Assert.That(_keeper.IsLoading, Is.False);

        _clock.Advance(_ms);
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Template, Is.EqualTo("StartingZone"));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7777));
        Assert.That(_launcher.Calls[0].Zone, Is.EqualTo(Z(100)));
        Assert.That(_keeper.IsLoading, Is.True);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(100)));
    }

    [Test]
    public void BootWait_EventsBeforeItEnds_NeverStart()
    {
        _clock.Advance(_boot - _ms);
        Register(1, 10);
        LoseLink(1);
        Drain(1);
        SetCount(1, 0);

        Assert.That(_launcher.Calls, Is.Empty);
    }

    [Test]
    public void BootWait_SpareLostBeforeItEnds_NoStartUntilBootEnd_ThenExactlyOne()
    {
        Register(1, 34, port: 7777);
        _clock.Advance(_boot - _ms);
        SetCount(1, 35);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        _clock.Advance(_ms);
        _keeper.Tick();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7778));
    }

    [Test]
    public void Tick_RepeatedWhileLoading_StartsOnlyOnce()
    {
        EndBootWait();
        _keeper.Tick();
        _keeper.Tick();
        _clock.Advance(TimeSpan.FromSeconds(10));
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- AC-ZI-33(d)

    [Test]
    public void BootWait_InstanceAt20RegisteredBeforeItEnds_NoStart()
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        Register(1, 20);

        EndBootWait();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Is.Empty);
        Assert.That(_keeper.IsLoading, Is.False);
    }

    // ---------------------------------------------------------------- AC-ZI-19 / 33(b)

    [Test]
    public void PlayerCount_Crossing35_StartsOneSpare_NoSecondWhileLoading_RoutesToSpareAfterRegistration()
    {
        Register(1, 34, port: 7777);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        SetCount(1, 35);

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        ZoneID spare = _launcher.Calls[0].Zone;
        Assert.That(spare, Is.Not.EqualTo(Z(1)));
        Assert.That(spare, Is.Not.EqualTo(ZoneID.Invalid));
        Assert.That(spare.RawValue, Is.Not.EqualTo(0u));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7778));

        SetCount(1, 36);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        // Rule 4 while the spare loads: the only routable zone is the 36-player instance.
        Assert.That(Pick(), Is.EqualTo(1u));

        Register(spare.RawValue, 0, port: 7778);

        Assert.That(_keeper.IsLoading, Is.False);
        Assert.That(Pick(), Is.EqualTo(spare.RawValue));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- AC-ZI-33(c)

    [Test]
    public void FailedStart_LauncherThrows_CriticalLog_RetriedNoSoonerThanLoadTimeout()
    {
        _launcher.Throw = new InvalidOperationException("systemctl failed");
        EndBootWait();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_keeper.IsLoading, Is.False);

        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        SetCount(1, 0);
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        _clock.Advance(_ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
        Assert.That(_launcher.Calls[1].Zone, Is.Not.EqualTo(_launcher.Calls[0].Zone));
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(2));

        // The second failure waits another full timeout.
        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public void FailedStart_AllocatorThrows_CriticalLog_LauncherNotCalled_RetriedAfterLoadTimeout()
    {
        _allocator.Throw = new InvalidOperationException("db down");
        EndBootWait();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Is.Empty);
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_keeper.IsLoading, Is.False);

        _allocator.Throw = null;
        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        _clock.Advance(_ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7777));
    }

    [Test]
    public void LoadTimeout_NoRegisterZone_CriticalLog_AndRetryAtOnce()
    {
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(0));
        Assert.That(_keeper.IsLoading, Is.True);

        _clock.Advance(_ms);
        _keeper.Tick();

        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
        Assert.That(_launcher.Calls[1].Zone, Is.EqualTo(Z(101)));
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(101)));
    }

    [Test]
    public void StaleAttempt_FirstLauncherFaultsAfterRetry_RetryStaysLoading_NoExtraCriticalLog()
    {
        var first = new TaskCompletionSource();
        _launcher.PendingCalls.Enqueue(first.Task);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(100)));

        _clock.Advance(_loadTimeout);
        _keeper.Tick();
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(101)));

        first.SetException(new InvalidOperationException("late systemctl failure"));

        Assert.That(_keeper.IsLoading, Is.True);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(101)));
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public void StaleAttempt_AllocatorAnswersAfterTimeout_LauncherNotCalledForIt()
    {
        var first = new TaskCompletionSource<ZoneID>();
        _allocator.PendingCalls.Enqueue(first.Task);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        _clock.Advance(_loadTimeout);
        _keeper.Tick();
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
        Assert.That(_allocator.Calls, Is.EqualTo(2));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Zone, Is.EqualTo(Z(100)));

        first.SetResult(Z(500));

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(100)));
        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(1));
    }

    [Test]
    public void LateLauncherFault_AfterRegisterZone_NoLog_NoRestart()
    {
        var first = new TaskCompletionSource();
        _launcher.PendingCalls.Enqueue(first.Task);
        EndBootWait();
        _keeper.Tick();
        Register(100, 0, port: 7777);
        Assert.That(_keeper.IsLoading, Is.False);

        first.SetException(new InvalidOperationException("late"));
        _keeper.Tick();

        Assert.That(CriticalLogs("ZoneDefinitionLoadFailed"), Is.EqualTo(0));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_keeper.IsLoading, Is.False);
    }

    [Test]
    public void LateRegistration_OfAbandonedZone_IsAnOrdinaryRegistration()
    {
        EndBootWait();
        _keeper.Tick();
        _clock.Advance(_loadTimeout);
        _keeper.Tick();
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(101)));

        Register(100, 0, port: 7777);

        Assert.That(_keeper.IsLoading, Is.True);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(101)));
        Assert.That(Pick(), Is.EqualTo(100u));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
    }

    // ---------------------------------------------------------------- AC-ZI-33(e) / 33(f)

    [Test]
    public void PlayerCount_OneToZero_EmptiedInstanceIsNotTheSpare_ExactlyOneStart()
    {
        Register(1, 1, port: 7777);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        SetCount(1, 0);

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        SetCount(1, 0);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void EmptiedInstanceAndNewSpare_BothAtZero_NewCharacterGoesToEmptied_ThenTieIsGone()
    {
        Register(1, 1, port: 7777);
        EndBootWait();
        SetCount(1, 0);
        ZoneID spare = _launcher.Calls[0].Zone;
        Register(spare.RawValue, 0, port: _launcher.Calls[0].Port);

        Assert.That(Pick(), Is.EqualTo(1u));

        SetCount(1, 1);
        Assert.That(Pick(), Is.EqualTo(1u));
        SetCount(1, 0);
        SetCount(1, 1);
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Router_EmptiedInstanceBeatsLowerZoneId_AtEqualZeroCount()
    {
        Register(2, 0);
        Register(5, 3);
        SetCount(5, 0);

        Assert.That(Pick(), Is.EqualTo(5u));
    }

    [Test]
    public void Router_TwoEmptiedInstances_LowerZoneIdWins()
    {
        Register(6, 2);
        Register(4, 2);
        SetCount(6, 0);
        SetCount(4, 0);

        Assert.That(Pick(), Is.EqualTo(4u));
    }

    [Test]
    public void Router_NeitherEmptiedAtZero_LowerZoneIdWins()
    {
        Register(7, 0);
        Register(3, 0);

        Assert.That(Pick(), Is.EqualTo(3u));
    }

    [Test]
    public void Router_EqualNonZeroCounts_LowerZoneIdWins()
    {
        Register(9, 12);
        Register(8, 12);
        Register(2, 1);
        SetCount(2, 12);

        Assert.That(Pick(), Is.EqualTo(2u));
    }

    // ---------------------------------------------------------------- AC-ZI-1

    [Test]
    public async Task SessionEstablishment_OnlyLoadingSpare_ServerStartingNoAdmitOneStart_ThenRoutedToSpare()
    {
        EndBootWait();
        _keeper.Tick();
        var repo = new FakeRepo { Character = new CharacterID(77u) };
        var link = new FakeLink();
        var sessions = new SessionEstablishment(
            repo, new FakeMap(), _router, link, new GatewayOptions(), _clock, _loggerFactory);

        LoginResponse first = await sessions.RunAsync(new AccountID(1u));

        Assert.That(first.Success, Is.False);
        Assert.That(first.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        Assert.That(link.Admits, Is.EqualTo(0));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        Register(_launcher.Calls[0].Zone.RawValue, 0, port: _launcher.Calls[0].Port);
        LoginResponse retried = await sessions.RunAsync(new AccountID(1u));

        Assert.That(retried.Success, Is.True);
        Assert.That(retried.ZoneId, Is.EqualTo(_launcher.Calls[0].Zone));
        Assert.That(retried.ZoneServerPort, Is.EqualTo((ushort)7777));
        Assert.That(link.Admits, Is.EqualTo(1));
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- draining and link loss

    [Test]
    public void ZoneDraining_LeavingNoInstanceBelowThreshold_StartsOne()
    {
        Register(1, 10);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        Drain(1);

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Draining_OnlyActiveZoneWhileSpareLoading_NoSecondStart_ThenDrainingRegisteredSpare_StartsAtOnce()
    {
        Register(1, 35, port: 7777);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7778));

        Drain(1);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        Register(_launcher.Calls[0].Zone.RawValue, 0, port: 7778);
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        // Same instant as the first start: a successful start does not impose retry spacing.
        Drain(_launcher.Calls[0].Zone.RawValue);

        Assert.That(_launcher.Calls, Has.Count.EqualTo(2));
        Assert.That(_launcher.Calls[1].Zone, Is.Not.EqualTo(_launcher.Calls[0].Zone));
        Assert.That(_launcher.Calls[1].Zone, Is.Not.EqualTo(Z(1)));
        Assert.That(_launcher.Calls[1].Port, Is.EqualTo((ushort)7779));
    }

    [Test]
    public void LinkLoss_LeavingNoInstanceBelowThreshold_StartsOne()
    {
        Register(1, 10);
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Is.Empty);

        LoseLink(1);

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void ZoneWithCapacityZero_DoesNotCountAsSpare()
    {
        Register(1, 0, capacity: 0);
        EndBootWait();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void OtherTemplateZone_DoesNotCountAsSpare()
    {
        Register(1, 0, template: 1);
        EndBootWait();
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Registration_OfLoadingZone_ClearsLoading()
    {
        EndBootWait();
        _keeper.Tick();
        Assert.That(_keeper.IsLoading, Is.True);

        Register(100, 0, port: 7777);

        Assert.That(_keeper.IsLoading, Is.False);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(ZoneID.Invalid));
    }

    // ---------------------------------------------------------------- handlers do not block

    [Test]
    public void Handlers_DoNotBlockOnLauncher_AndNoSecondStartWhileItIsPending()
    {
        var pending = new TaskCompletionSource();
        _launcher.Pending = pending.Task;
        EndBootWait();

        _keeper.Tick();
        Register(1, 40);
        SetCount(1, 41);
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_keeper.IsLoading, Is.True);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(100)));

        pending.SetResult();
        Assert.That(_keeper.IsLoading, Is.True);
    }

    [Test]
    public void Handlers_DoNotBlockOnAllocator_LoadingIsSetBeforeItAnswers()
    {
        var pending = new TaskCompletionSource<ZoneID>();
        _allocator.Pending = pending.Task;
        EndBootWait();

        _keeper.Tick();
        _keeper.Tick();
        LoseLink(9);

        Assert.That(_allocator.Calls, Is.EqualTo(1));
        Assert.That(_keeper.IsLoading, Is.True);
        Assert.That(_keeper.LoadingZone, Is.EqualTo(ZoneID.Invalid));

        pending.SetResult(Z(500));

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Zone, Is.EqualTo(Z(500)));
        Assert.That(_keeper.LoadingZone, Is.EqualTo(Z(500)));
    }

    // ---------------------------------------------------------------- ports

    [Test]
    public void Ports_SpareGetsLowestFreePort_SkippingHeldOnesIncludingLinkDownRows()
    {
        Register(1, 40, port: 7777);
        Register(2, 40, port: 7778);
        LoseLink(2);
        Register(3, 40, port: 7780);
        EndBootWait();

        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7779));
    }

    [Test]
    public void Ports_NoneFree_NoStart_CriticalLog_NoRetryBeforeLoadTimeout()
    {
        Build(new GatewayOptions { ZoneUdpPortCount = 2 });
        Register(1, 40, port: 7777);
        Register(2, 40, port: 7778);
        EndBootWait();

        _keeper.Tick();

        Assert.That(_launcher.Calls, Is.Empty);
        Assert.That(_allocator.Calls, Is.EqualTo(0));
        Assert.That(CriticalLogs("ZonePortsExhausted"), Is.EqualTo(1));

        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        SetCount(1, 41);
        Assert.That(CriticalLogs("ZonePortsExhausted"), Is.EqualTo(1));

        _clock.Advance(_ms);
        _keeper.Tick();
        Assert.That(CriticalLogs("ZonePortsExhausted"), Is.EqualTo(2));
        Assert.That(_launcher.Calls, Is.Empty);
    }

    [Test]
    public void Ports_SweepOfLongDownRow_FreesItsPortForTheNextStart()
    {
        Build(new GatewayOptions { ZoneUdpPortCount = 2 });
        Register(1, 40, port: 7777);
        Register(2, 40, port: 7778);
        EndBootWait();
        _keeper.Tick();
        Assert.That(CriticalLogs("ZonePortsExhausted"), Is.EqualTo(1));

        LoseLink(1);
        _clock.Advance(_boot);
        _table.Sweep();
        Assert.That(_table.TryGet(Z(1), out _), Is.False);

        // A port is free again, but an event before attempt + 30 s still does not start (retry spacing).
        SetCount(2, 41);
        Assert.That(_allocator.Calls, Is.EqualTo(0));
        Assert.That(_launcher.Calls, Is.Empty);

        // The retry waits for attempt + 30 s; a link-down row that was not swept would have kept 7777 held.
        _clock.Advance(_loadTimeout - _boot);
        _keeper.Tick();

        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));
        Assert.That(_launcher.Calls[0].Port, Is.EqualTo((ushort)7777));
    }

    [Test]
    public void Ports_LinkDownRowNotYetSwept_StillHoldsItsPort()
    {
        Build(new GatewayOptions { ZoneUdpPortCount = 1 });
        Register(1, 40, port: 7777);
        LoseLink(1);
        EndBootWait();

        _keeper.Tick();

        Assert.That(_launcher.Calls, Is.Empty);
        Assert.That(CriticalLogs("ZonePortsExhausted"), Is.EqualTo(1));
    }

    // ---------------------------------------------------------------- RoutingTable.Sweep

    [Test]
    public void Sweep_RemovesRowDownForTwiceRetry_KeepsRowDownForLess()
    {
        Register(1, 5);
        Register(2, 5);
        LoseLink(1);
        _clock.Advance(_boot - _ms);
        LoseLink(2);
        _table.Sweep();
        Assert.That(_table.TryGet(Z(1), out _), Is.True);
        Assert.That(_table.TryGet(Z(2), out _), Is.True);

        _clock.Advance(_ms);
        _table.Sweep();

        Assert.That(_table.TryGet(Z(1), out _), Is.False);
        Assert.That(_table.TryGet(Z(2), out _), Is.True);
        Assert.That(_table.GetAllRoutes().Select(r => r.Zone.RawValue), Is.EqualTo(new[] { 2u }));
    }

    [Test]
    public void Sweep_KeepsRowThatRegisteredAgain_AndRestartsTheDownTimerOnNextLoss()
    {
        Register(1, 5);
        LoseLink(1);
        _clock.Advance(TimeSpan.FromSeconds(3));
        Register(1, 0);
        _clock.Advance(TimeSpan.FromSeconds(2));
        _table.Sweep();
        Assert.That(_table.TryGet(Z(1), out ZoneRoute route), Is.True);
        Assert.That(route.LinkUp, Is.True);

        LoseLink(1);
        _clock.Advance(_boot - _ms);
        _table.Sweep();
        Assert.That(_table.TryGet(Z(1), out _), Is.True);

        _clock.Advance(_ms);
        _table.Sweep();
        Assert.That(_table.TryGet(Z(1), out _), Is.False);
    }

    [Test]
    public void Sweep_RepeatedLinkLostEvent_DoesNotRestartTheDownTimer()
    {
        Register(1, 5);
        LoseLink(1);
        _clock.Advance(TimeSpan.FromSeconds(3));
        LoseLink(1);
        _clock.Advance(TimeSpan.FromSeconds(1));
        _table.Sweep();

        Assert.That(_table.TryGet(Z(1), out _), Is.False);
    }

    // ---------------------------------------------------------------- shared boot wait

    [Test]
    public void AccountZoneMap_WithSharedBootWait_ReadsTheSameBootEnd()
    {
        var shared = new GatewayBootWait(_options, _clock);
        _clock.Advance(TimeSpan.FromSeconds(3));
        var map = new AccountZoneMap(_options, _clock, new NoopEvictSender(), _loggerFactory, shared);

        Assert.That(map.IsBootWaitOver, Is.False);
        Assert.That(shared.IsOver, Is.False);

        _clock.Advance(TimeSpan.FromSeconds(1));

        // 4 s after the shared start; the map's own start (3 s in) would still have 3 s to go.
        Assert.That(shared.IsOver, Is.True);
        Assert.That(map.IsBootWaitOver, Is.True);
    }

    [Test]
    public void AccountZoneMap_WithoutSharedBootWait_CreatesItsOwn()
    {
        var map = new AccountZoneMap(_options, _clock, new NoopEvictSender(), _loggerFactory);

        _clock.Advance(_boot - _ms);
        Assert.That(map.IsBootWaitOver, Is.False);
        _clock.Advance(_ms);
        Assert.That(map.IsBootWaitOver, Is.True);
    }

    [Test]
    public void Constructors_RejectNulls()
    {
        Assert.Throws<ArgumentNullException>(() => new GatewayBootWait(null!, _clock));
        Assert.Throws<ArgumentNullException>(() => new GatewayBootWait(_options, null!));
        Assert.Throws<ArgumentNullException>(() => new RoutingTable(_options, null!, _loggerFactory));
        Assert.Throws<ArgumentNullException>(
            () => new WarmSpareKeeper(null!, _allocator, _launcher, _options, _bootWait, _clock, _loggerFactory));
        Assert.Throws<ArgumentNullException>(
            () => new WarmSpareKeeper(_table, null!, _launcher, _options, _bootWait, _clock, _loggerFactory));
        Assert.Throws<ArgumentNullException>(
            () => new WarmSpareKeeper(_table, _allocator, null!, _options, _bootWait, _clock, _loggerFactory));
        Assert.Throws<ArgumentNullException>(
            () => new WarmSpareKeeper(_table, _allocator, _launcher, _options, null!, _clock, _loggerFactory));
    }

    // ---------------------------------------------------------------- Story 007 follow-up S1

    [Test]
    public void StartedZone_RegistersWithCapacityZero_KeepsRetrySpacing_AndLogsCritical()
    {
        EndBootWait();
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1));

        Register(100, 0, port: 7777, capacity: 0);

        Assert.That(_keeper.IsLoading, Is.False);
        Assert.That(CriticalLogs("registered but the template has no spare"), Is.EqualTo(1));
        _clock.Advance(_loadTimeout - _ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(1), "no new start before the retry spacing has elapsed");

        _clock.Advance(_ms);
        _keeper.Tick();
        Assert.That(_launcher.Calls, Has.Count.EqualTo(2), "the next start runs once the spacing has elapsed");
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeLauncher : IZoneLauncher
    {
        public List<(ZoneID Zone, string Template, ushort Port)> Calls { get; } = new();

        public Exception? Throw { get; set; }

        public Task? Pending { get; set; }

        // Answers for the next calls, in order; once empty, Pending (or a completed task) is used.
        public Queue<Task> PendingCalls { get; } = new();

        public Task StartAsync(ZoneID zone, string zoneTemplate, ushort port, CancellationToken ct)
        {
            Calls.Add((zone, zoneTemplate, port));
            if (PendingCalls.TryDequeue(out Task? queued))
            {
                return queued;
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            return Pending ?? Task.CompletedTask;
        }
    }

    private sealed class FakeAllocator : IZoneIdAllocator
    {
        private uint _next = 100;

        public int Calls { get; private set; }

        public Exception? Throw { get; set; }

        public Task<ZoneID>? Pending { get; set; }

        // Answers for the next calls, in order; once empty, Pending (or the next id) is used.
        public Queue<Task<ZoneID>> PendingCalls { get; } = new();

        public Task<ZoneID> AllocateAsync(CancellationToken ct)
        {
            Calls++;
            if (PendingCalls.TryDequeue(out Task<ZoneID>? queued))
            {
                return queued;
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            return Pending ?? Task.FromResult(Z(_next++));
        }
    }

    private sealed class NoopEvictSender : IZoneEvictSender
    {
        public void SendEvict(ZoneID zone, AccountID account)
        {
        }
    }

    private sealed class FakeRepo : IAccountRepository
    {
        public CharacterID Character { get; set; }

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => Task.FromResult(Character);

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => Task.FromResult(ZoneID.Invalid);

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct) => Task.FromResult<AccountRecord?>(null);

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct) => Task.CompletedTask;

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMap : IAccountZoneMap
    {
        public bool TryGetZone(AccountID account, out ZoneID zone)
        {
            zone = ZoneID.Invalid;
            return false;
        }

        public bool IsLinkUp(ZoneID zone) => true;

        public void SetSession(AccountID account, ZoneID zone)
        {
        }

        public bool Remove(AccountID account, ZoneID fromZone) => false;
    }

    private sealed class FakeLink : IZoneLink
    {
        public int Admits { get; private set; }

        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
        {
            Admits++;
            return Task.FromResult(true);
        }

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct) => Task.CompletedTask;

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

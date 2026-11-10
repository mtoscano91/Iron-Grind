using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Routing;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 006: <see cref="RoutingTable"/> and <see cref="FillFirstRouter"/> driven through the zone-link events
/// (CR-ZI-6 rules 2 to 5; AC-ZI-2, AC-ZI-3, AC-ZI-20, AC-ZI-34). No real waits, no randomness.
/// </summary>
[TestFixture]
internal sealed class Gateway_Routing_tests
{
    private static readonly ZoneID _none = ZoneID.Invalid;

    private GatewayOptions _options = null!;
    private RoutingTable _table = null!;
    private FillFirstRouter _router = null!;

    [SetUp]
    public void SetUp()
    {
        _options = new GatewayOptions();
        Rebuild();
    }

    private void Rebuild()
    {
        _table = new RoutingTable(_options, NullLoggerFactory.Instance);
        _router = new FillFirstRouter(_table, _options);
    }

    private static ZoneID Z(uint id) => new(id);

    private void Register(uint id, int count, ushort capacity = 50, ushort port = 0, byte template = 0)
    {
        ushort udp = port == 0 ? (ushort)(7000 + id) : port;
        _table.OnZoneRegistered(Z(id), new RegisterZoneMessage(Z(id), template, udp, capacity, Array.Empty<AccountID>()));
        if (count > 0)
        {
            SetCount(id, count);
        }
    }

    private void SetCount(uint id, int count)
    {
        byte[] frame = GatewayLinkCodec.Encode(new PlayerCountMessage((ushort)count));
        _table.OnMessageReceived(Z(id), Decode(frame));
    }

    private void Drain(uint id) => _table.OnMessageReceived(Z(id), Decode(GatewayLinkCodec.EncodeZoneDraining()));

    private static ZoneToGatewayMessage Decode(byte[] frame)
    {
        Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(frame[4], frame.AsSpan(5), out ZoneToGatewayMessage message), Is.True);
        return message;
    }

    private uint? Pick(uint last = 0, uint excluded = 0)
    {
        return _router.TryPick(Z(last), Z(excluded), out ZoneID zone, out _) ? zone.RawValue : null;
    }

    // ---------------------------------------------------------------- AC-ZI-2 rule 2

    [Test]
    public void TryPick_LastZoneActive_ReturnsThatZoneAndItsPort()
    {
        Register(7, 10, port: 7777);
        Register(3, 30);

        bool picked = _router.TryPick(Z(7), _none, out ZoneID zone, out ushort port);

        Assert.That(picked, Is.True);
        Assert.That(zone, Is.EqualTo(Z(7)));
        Assert.That(port, Is.EqualTo((ushort)7777));
    }

    [Test]
    public void TryPick_LastZoneAtHardCap_FallsToRule3()
    {
        Register(7, 50);
        Register(3, 30);

        Assert.That(Pick(last: 7), Is.EqualTo(3u));
    }

    // ---------------------------------------------------------------- AC-ZI-3

    [Test]
    public void TryPick_LastZoneNeverRegistered_UsesOtherZone()
    {
        Register(7, 10);

        Assert.That(Pick(last: 99), Is.EqualTo(7u));
    }

    [Test]
    public void TryPick_LastZoneUnknownAndNoActiveZone_NoPick()
    {
        Assert.That(Pick(last: 99), Is.Null);
    }

    [Test]
    public void TryPick_LastZoneBelongsToOtherTemplate_RuleTwoStillApplies()
    {
        Register(8, 5, template: 1);
        Register(3, 30);

        Assert.That(Pick(last: 8), Is.EqualTo(8u));
    }

    // ---------------------------------------------------------------- AC-ZI-20 rule 3

    [Test]
    public void TryPick_TwoOpenZones_PicksTheFullerOne()
    {
        Register(1, 20);
        Register(2, 30);

        Assert.That(Pick(), Is.EqualTo(2u));
        Assert.That(_table.TryGet(Z(1), out ZoneRoute a), Is.True);
        Assert.That(a.PlayerCount, Is.EqualTo(20));
    }

    [Test]
    public void TryPick_Rule3Tie_PrefersLowerZoneId()
    {
        Register(5, 30);
        Register(2, 30);
        Register(9, 30);

        Assert.That(Pick(), Is.EqualTo(2u));
    }

    [Test]
    public void TryPick_ZoneAtThreshold_IsNotARule3Candidate()
    {
        Register(1, 35);
        Register(2, 20);

        // 20 is below the threshold, so rule 3 picks it; 35 is not "below" even though it is fuller.
        Assert.That(Pick(), Is.EqualTo(2u));
    }

    [Test]
    public void TryPick_SingleZoneAtThreshold_IsPickedByRule4()
    {
        Register(1, 35);

        Assert.That(Pick(), Is.EqualTo(1u));
    }

    [Test]
    public void TryPick_OtherTemplateZones_AreNeverPickedByRules3And4()
    {
        // Template 1 zones would win rule 3 (34 beats 10) and rule 4; only the starting template is routable.
        Register(1, 10);
        Register(2, 34, template: 1);
        Assert.That(Pick(), Is.EqualTo(1u));

        SetCount(1, 50);
        SetCount(2, 0);
        Assert.That(Pick(), Is.Null);
    }

    [Test]
    public void TryPick_ZoneCapacityBelowThreshold_UsesItsHardCap()
    {
        Register(1, 29, capacity: 30);
        Assert.That(Pick(), Is.EqualTo(1u));

        SetCount(1, 30);
        Assert.That(Pick(), Is.Null);
        Assert.That(Pick(last: 1), Is.Null);
    }

    [Test]
    public void TryPick_LastZoneOneBelowHardCap_Rule2StillApplies()
    {
        Register(7, 49);
        Register(3, 30);

        Assert.That(Pick(last: 7), Is.EqualTo(7u));
    }

    [Test]
    public void TryPick_LastZoneIsExcluded_FallsToRule3()
    {
        Register(1, 30);
        Register(2, 10);

        Assert.That(Pick(last: 1, excluded: 1), Is.EqualTo(2u));
    }

    [Test]
    public void TryPick_ExcludedWasTheOnlyRule3Candidate_FallsToRule4()
    {
        Register(1, 30);
        Register(2, 40);

        Assert.That(Pick(excluded: 1), Is.EqualTo(2u));
    }

    // ---------------------------------------------------------------- AC-ZI-34 rule 4 and rule 5

    [Test]
    public void TryPick_NoZoneBelowThreshold_PicksLowestCountBelowCap()
    {
        Register(1, 40);
        Register(2, 45);

        Assert.That(Pick(), Is.EqualTo(1u));
    }

    [Test]
    public void TryPick_Rule4Tie_PrefersLowerZoneId()
    {
        Register(6, 40);
        Register(4, 40);

        Assert.That(Pick(), Is.EqualTo(4u));
    }

    [Test]
    public void TryPick_EveryZoneAtHardCap_NoPick()
    {
        Register(1, 50);
        Register(2, 50);

        Assert.That(Pick(), Is.Null);
    }

    // ---------------------------------------------------------------- AC-7 unroutable zones

    [Test]
    public void TryPick_DrainingLinkDownAndUnregistered_NoPick()
    {
        Register(1, 10);
        Drain(1);
        Register(2, 10);
        _table.OnLinkLost(Z(2));

        Assert.That(Pick(last: 1), Is.Null);
        Assert.That(Pick(last: 3), Is.Null);
    }

    [Test]
    public void TryPick_ExcludedIsOnlyCandidate_NoPick()
    {
        Register(1, 10);

        Assert.That(Pick(excluded: 1), Is.Null);
        Assert.That(Pick(last: 1, excluded: 1), Is.Null);
    }

    [Test]
    public void TryPick_ExcludedZoneSkipped_PicksTheOther()
    {
        Register(1, 30);
        Register(2, 20);

        Assert.That(Pick(excluded: 1), Is.EqualTo(2u));
    }

    // ---------------------------------------------------------------- hard cap

    [Test]
    public void HardCap_IsMinOfRegisteredCapacityAndConfig()
    {
        Register(1, 30, capacity: 30);
        Register(2, 60, capacity: 200);

        Assert.That(_table.TryGet(Z(1), out ZoneRoute small), Is.True);
        Assert.That(small.HardCap, Is.EqualTo(30));
        Assert.That(_table.TryGet(Z(2), out ZoneRoute big), Is.True);
        Assert.That(big.HardCap, Is.EqualTo(50));
        Assert.That(Pick(), Is.Null);
    }

    [Test]
    public void TryPick_CapacityZero_NeverRoutable()
    {
        Register(1, 0, capacity: 0);

        Assert.That(Pick(), Is.Null);
        Assert.That(Pick(last: 1), Is.Null);
    }

    // ---------------------------------------------------------------- events

    [Test]
    public void OnZoneRegistered_AfterLinkLoss_MakesZoneRoutableAgain()
    {
        Register(1, 10);
        _table.OnLinkLost(Z(1));
        Assert.That(Pick(), Is.Null);

        Register(1, 0);

        Assert.That(Pick(), Is.EqualTo(1u));
    }

    [Test]
    public void OnZoneRegistered_AfterDraining_ResetsToActive()
    {
        Register(1, 10);
        Drain(1);

        Register(1, 0);

        Assert.That(Pick(), Is.EqualTo(1u));
    }

    [Test]
    public void OnZoneRegistered_Again_KeepsHasHeldPlayersAndTakesTheNewLiveCount()
    {
        Register(1, 10);
        Register(2, 0);
        SetCount(1, 0);

        // Link reconnects: zone 1 held players before, zone 2 never did.
        _table.OnZoneRegistered(Z(1), new RegisterZoneMessage(Z(1), 0, 7001, 50, Array.Empty<AccountID>()));
        _table.OnZoneRegistered(Z(2), new RegisterZoneMessage(Z(2), 0, 7002, 50, Array.Empty<AccountID>()));

        Assert.That(_table.TryGet(Z(1), out ZoneRoute one), Is.True);
        Assert.That(one.HasHeldPlayers, Is.True);
        Assert.That(one.PlayerCount, Is.EqualTo(0));
        Assert.That(_table.TryGet(Z(2), out ZoneRoute two), Is.True);
        Assert.That(two.HasHeldPlayers, Is.False);
    }

    [Test]
    public void OnZoneRegistered_UsesLiveAccountCountUntilFirstPlayerCount()
    {
        _table.OnZoneRegistered(Z(1), new RegisterZoneMessage(Z(1), 0, 7001, 50, new[] { new AccountID(1u), new AccountID(2u), new AccountID(3u) }));

        Assert.That(_table.TryGet(Z(1), out ZoneRoute route), Is.True);
        Assert.That(route.PlayerCount, Is.EqualTo(3));
        Assert.That(route.HasHeldPlayers, Is.True);

        SetCount(1, 0);

        Assert.That(_table.TryGet(Z(1), out route), Is.True);
        Assert.That(route.PlayerCount, Is.EqualTo(0));
        Assert.That(route.HasHeldPlayers, Is.True);
    }

    [Test]
    public void PlayerCount_UpdatesRouting()
    {
        Register(1, 20);
        Register(2, 30);
        Assert.That(Pick(), Is.EqualTo(2u));

        SetCount(2, 50);
        Assert.That(Pick(), Is.EqualTo(1u));

        SetCount(2, 10);
        SetCount(1, 5);
        Assert.That(Pick(), Is.EqualTo(2u));
    }

    [Test]
    public void OnLinkLost_KeepsTheRow()
    {
        Register(1, 10);

        _table.OnLinkLost(Z(1));

        Assert.That(_table.TryGet(Z(1), out ZoneRoute route), Is.True);
        Assert.That(route.LinkUp, Is.False);
        Assert.That(route.PlayerCount, Is.EqualTo(10));
    }

    [Test]
    public void OnMessageReceived_OtherMessageType_IsIgnored()
    {
        Register(1, 10);

        byte[] frame = GatewayLinkCodec.Encode(new SessionReleasedMessage(new AccountID(5u)));
        _table.OnMessageReceived(Z(1), Decode(frame));

        Assert.That(_table.TryGet(Z(1), out ZoneRoute route), Is.True);
        Assert.That(route.PlayerCount, Is.EqualTo(10));
        Assert.That(route.State, Is.EqualTo(ZoneRouteState.Active));
    }

    [Test]
    public void Events_ForUnregisteredZone_AreIgnored()
    {
        SetCount(9, 4);
        Drain(9);
        _table.OnLinkLost(Z(9));

        Assert.That(_table.TryGet(Z(9), out _), Is.False);
    }

    [Test]
    public void GetTemplateRoutes_ReturnsOnlyThatTemplateInZoneIdOrder()
    {
        Register(5, 1);
        Register(2, 1);
        Register(8, 1, template: 1);

        IReadOnlyList<ZoneRoute> routes = _table.GetTemplateRoutes(0);

        Assert.That(routes.Select(r => r.Zone.RawValue), Is.EqualTo(new[] { 2u, 5u }));
        Assert.That(_table.GetTemplateRoutes(1), Has.Count.EqualTo(1));
        Assert.That(_table.GetTemplateRoutes(2), Is.Empty);
    }

    // ---------------------------------------------------------------- TryGetPort

    [Test]
    public void TryGetPort_KnownZone_ReturnsRegisteredPortWhateverTheState()
    {
        Register(1, 50, port: 7123);
        Drain(1);
        _table.OnLinkLost(Z(1));

        Assert.That(_router.TryGetPort(Z(1), out ushort port), Is.True);
        Assert.That(port, Is.EqualTo((ushort)7123));
    }

    [Test]
    public void TryGetPort_UnknownZone_ReturnsFalse()
    {
        Assert.That(_router.TryGetPort(Z(99), out ushort port), Is.False);
        Assert.That(port, Is.EqualTo((ushort)0));
    }

    // ---------------------------------------------------------------- options

    [Test]
    public void Options_ThresholdAndCapComeFromConfiguration()
    {
        _options = new GatewayOptions { ZoneOpenThreshold = 25, MaxPlayersPerZone = 28 };
        Rebuild();
        Register(1, 24, capacity: 100);
        Register(2, 26, capacity: 100);

        // Threshold 25: zone 1 (24) is open and wins over the fuller zone 2 (26, not open).
        Assert.That(Pick(), Is.EqualTo(1u));

        // Cap 28 bounds the registered capacity of 100.
        SetCount(1, 28);
        Assert.That(Pick(), Is.EqualTo(2u));
        SetCount(2, 28);
        Assert.That(Pick(), Is.Null);
    }

    // ---------------------------------------------------------------- AC-ZI-34 through SessionEstablishment

    [Test]
    public async Task SessionEstablishment_AllZonesAtHardCap_ServerStartingNoAdmitNoFailureWrite()
    {
        Register(1, 50);
        Register(2, 50);
        var repo = new FakeRepo { Character = new CharacterID(77u) };
        var link = new FakeLink();
        var sessions = new SessionEstablishment(
            repo, new FakeMap(), _router, link, new GatewayOptions(), TimeProvider.System, NullLoggerFactory.Instance);

        LoginResponse response = await sessions.RunAsync(new AccountID(1u));

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        Assert.That(link.Admits, Is.EqualTo(0));
        Assert.That(repo.FailureWrites, Is.EqualTo(0));

        // Positive control: the same setup with one zone below its hard cap admits through the router.
        SetCount(2, 49);
        LoginResponse admitted = await sessions.RunAsync(new AccountID(1u));

        Assert.That(admitted.Success, Is.True);
        Assert.That(admitted.ZoneId, Is.EqualTo(Z(2)));
        Assert.That(admitted.ZoneServerPort, Is.EqualTo((ushort)7002));
        Assert.That(link.Admits, Is.EqualTo(1));
        Assert.That(repo.FailureWrites, Is.EqualTo(0));
    }

    // ---------------------------------------------------------------- thread safety

    [Test]
    public void Concurrency_ParallelEventsAndPicks_NoExceptionsAndExactFinalState()
    {
        const int ZONES = 40;
        const int ROUNDS = 200;
        var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        Parallel.For(1, ZONES + 1, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            try
            {
                uint id = (uint)i;
                Register(id, 0);
                for (int round = 1; round <= ROUNDS; round++)
                {
                    SetCount(id, round % 51);
                    _router.TryPick(_none, _none, out _, out _);
                    _router.TryPick(Z(id), _none, out _, out _);
                    _router.TryGetPort(Z(id), out _);
                }

                SetCount(id, (int)id);
                if (id % 2 == 0)
                {
                    Drain(id);
                }
                else if (id % 5 == 0)
                {
                    _table.OnLinkLost(Z(id));
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        });

        Assert.That(errors, Is.Empty);
        IReadOnlyList<ZoneRoute> routes = _table.GetTemplateRoutes(0);
        Assert.That(routes, Has.Count.EqualTo(ZONES));
        foreach (ZoneRoute route in routes)
        {
            uint id = route.Zone.RawValue;
            Assert.That(route.PlayerCount, Is.EqualTo((int)id));
            Assert.That(route.State, Is.EqualTo(id % 2 == 0 ? ZoneRouteState.Draining : ZoneRouteState.Active));
            Assert.That(route.LinkUp, Is.EqualTo(!(id % 2 != 0 && id % 5 == 0)));
        }

        // Active, linked zones are the odd ids not divisible by 5; the highest of those below 35 is 33.
        Assert.That(Pick(), Is.EqualTo(33u));
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeRepo : IAccountRepository
    {
        public CharacterID Character { get; set; }

        public int FailureWrites { get; private set; }

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => Task.FromResult(Character);

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => Task.FromResult(ZoneID.Invalid);

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct) => Task.FromResult<AccountRecord?>(null);

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct)
        {
            FailureWrites++;
            return Task.CompletedTask;
        }

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
        {
            FailureWrites++;
            throw new NotSupportedException();
        }

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

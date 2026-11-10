using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 004: the real <see cref="LinkZoneLink"/> with a fake sender, a fake map and a manual clock
/// (reply matching, timeouts, cancellation, link loss, late admission, map ordering). No real-time waits.
/// </summary>
[TestFixture]
internal sealed class Gateway_ZoneLink_tests
{
    private const uint ZONE_RAW = 7;
    private const uint OTHER_ZONE_RAW = 8;
    private const uint ACCOUNT_RAW = 42;
    private const uint OTHER_ACCOUNT_RAW = 43;
    private const uint CHARACTER_RAW = 5;
    private const int REQUEST_TIMEOUT_MS = 2000;
    private const int EVICT_TIMEOUT_MS = 11000;
    private static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(5);

    private static readonly ZoneID _zone = new(ZONE_RAW);
    private static readonly ZoneID _otherZone = new(OTHER_ZONE_RAW);
    private static readonly AccountID _account = new(ACCOUNT_RAW);
    private static readonly AccountID _otherAccount = new(OTHER_ACCOUNT_RAW);
    private static readonly CharacterID _character = new(CHARACTER_RAW);

    private FakeSender _sender = null!;
    private FakeMap _map = null!;
    private ManualTimeProvider _time = null!;
    private LinkZoneLink _link = null!;

    [SetUp]
    public void SetUp()
    {
        _sender = new FakeSender();
        _sender.SetLink(_zone, true);
        _sender.SetLink(_otherZone, true);
        _map = new FakeMap();
        _time = new ManualTimeProvider();
        _link = new LinkZoneLink(
            _sender,
            _map,
            Options.Create(new GatewayOptions { ZoneRequestTimeoutMs = REQUEST_TIMEOUT_MS, EvictTimeoutMs = EVICT_TIMEOUT_MS }),
            _time,
            NullLogger<LinkZoneLink>.Instance);
    }

    // ---------------------------------------------------------------- Helpers

    private static byte[] NewToken(byte fill) => Enumerable.Repeat(fill, 16).ToArray();

    private static async Task Settled(Task task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(HANG_GUARD));
        Assert.That(first, Is.SameAs(task), "the task did not complete (hang guard)");
    }

    private static async Task<T> ResultOf<T>(Task<T> task)
    {
        await Settled(task);
        return await task;
    }

    private GatewayToZoneMessage Sent(int index)
    {
        byte[] frame = _sender.Frames[index].Frame;
        Assert.That(GatewayLinkCodec.TryDecodeGatewayToZone(frame[4], frame.AsSpan(5), out GatewayToZoneMessage message), Is.True);
        return message;
    }

    private uint AdmitIdAt(int index) => Sent(index).Admit.RequestId;

    private void Reply(ZoneID zone, byte[] frame)
    {
        Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(frame[4], frame.AsSpan(5), out ZoneToGatewayMessage message), Is.True);
        _link.OnMessageReceived(zone, message);
    }

    private static byte[] AdmitAccepted(uint id) => GatewayLinkCodec.Encode(new AdmitResultMessage(id, true, AdmitRejectReason.None));

    private static byte[] AdmitRejected(uint id) => GatewayLinkCodec.Encode(new AdmitResultMessage(id, false, AdmitRejectReason.Full));

    private Task<bool> StartAdmit(AccountID account, CancellationToken ct = default) =>
        _link.AdmitAsync(_zone, account, _character, NewToken(0xAB), ct);

    // ---------------------------------------------------------------- AC-1 / AC-2

    [Test]
    public async Task Admit_TwoConcurrentToSameZoneAnsweredInReverse_EachGetsItsOwnResult()
    {
        Task<bool> first = StartAdmit(_account);
        Task<bool> second = StartAdmit(_otherAccount);
        Assert.That(_sender.Frames, Has.Count.EqualTo(2));
        uint firstId = AdmitIdAt(0);
        uint secondId = AdmitIdAt(1);
        Assert.That(firstId, Is.Not.EqualTo(secondId));

        Reply(_zone, AdmitRejected(secondId));
        Reply(_zone, AdmitAccepted(firstId));

        Assert.That(await ResultOf(second), Is.False);
        Assert.That(await ResultOf(first), Is.True);
        Assert.That(_map.Sessions, Is.EqualTo(new[] { (_account, _zone) }));
    }

    [Test]
    public async Task Admit_Accepted_ReturnsTrueAndFrameCarriesAccountCharacterAndToken()
    {
        Task<bool> admit = StartAdmit(_account);

        GatewayToZoneMessage sent = Sent(0);
        Assert.That(sent.Type, Is.EqualTo(GatewayLinkMessageType.Admit));
        Assert.That(sent.Admit.AccountId, Is.EqualTo(_account));
        Assert.That(sent.Admit.CharacterId, Is.EqualTo(_character));
        Assert.That(sent.Admit.Token, Is.EqualTo(NewToken(0xAB)));
        Assert.That(_sender.Frames[0].Zone, Is.EqualTo(_zone));

        Reply(_zone, AdmitAccepted(sent.Admit.RequestId));
        Assert.That(await ResultOf(admit), Is.True);
    }

    [Test]
    public async Task Admit_Rejected_ReturnsFalseAndDoesNotSetSession()
    {
        Task<bool> admit = StartAdmit(_account);
        Reply(_zone, AdmitRejected(AdmitIdAt(0)));

        Assert.That(await ResultOf(admit), Is.False);
        Assert.That(_map.Sessions, Is.Empty);
    }

    [Test]
    public async Task Reconnect_Accepted_ReturnsOutcomeWithNewToken()
    {
        Task<ReconnectOutcome> reconnect = _link.ReconnectAsync(_zone, _account, NewToken(0x11), CancellationToken.None);
        GatewayToZoneMessage sent = Sent(0);
        Assert.That(sent.Type, Is.EqualTo(GatewayLinkMessageType.Reconnect));
        Assert.That(sent.Reconnect.AccountId, Is.EqualTo(_account));
        Assert.That(sent.Reconnect.Token, Is.EqualTo(NewToken(0x11)));

        Reply(_zone, GatewayLinkCodec.Encode(new ReconnectResultMessage(sent.Reconnect.RequestId, true, NewToken(0x22))));

        ReconnectOutcome outcome = await ResultOf(reconnect);
        Assert.That(outcome.Accepted, Is.True);
        Assert.That(outcome.NewToken, Is.EqualTo(NewToken(0x22)));
    }

    [Test]
    public async Task Reconnect_Rejected_ReturnsOutcomeWithoutToken()
    {
        Task<ReconnectOutcome> reconnect = _link.ReconnectAsync(_zone, _account, NewToken(0x11), CancellationToken.None);
        uint id = Sent(0).Reconnect.RequestId;

        Reply(_zone, GatewayLinkCodec.Encode(new ReconnectResultMessage(id, false, new byte[16])));

        ReconnectOutcome outcome = await ResultOf(reconnect);
        Assert.That(outcome.Accepted, Is.False);
        Assert.That(outcome.NewToken, Is.Null);
    }

    [Test]
    public async Task Evict_EvictResult_CompletesTheTask()
    {
        Task evict = _link.EvictAsync(_zone, _account, CancellationToken.None);
        GatewayToZoneMessage sent = Sent(0);
        Assert.That(sent.Type, Is.EqualTo(GatewayLinkMessageType.Evict));
        Assert.That(sent.Evict.AccountId, Is.EqualTo(_account));

        Reply(_zone, GatewayLinkCodec.Encode(new EvictResultMessage(sent.Evict.RequestId)));

        await Settled(evict);
        Assert.That(evict.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void RequestIds_AcrossRequestTypes_AreNonZeroAndIncreasing()
    {
        _ = StartAdmit(_account);
        _ = _link.EvictAsync(_zone, _account, CancellationToken.None);
        _ = _link.ReconnectAsync(_zone, _account, NewToken(1), CancellationToken.None);
        _ = StartAdmit(_otherAccount);

        uint[] ids =
        {
            Sent(0).Admit.RequestId,
            Sent(1).Evict.RequestId,
            Sent(2).Reconnect.RequestId,
            Sent(3).Admit.RequestId,
        };

        Assert.That(ids, Is.All.Not.EqualTo(0u));
        Assert.That(ids, Is.Ordered.Ascending);
        Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
    }

    // ---------------------------------------------------------------- AC-3

    [Test]
    public async Task Admit_NoReply_StillPendingAt1999MsAndTimesOutAt2000Ms()
    {
        Task<bool> admit = StartAdmit(_account);

        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS - 1));
        Assert.That(admit.IsCompleted, Is.False);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await Settled(admit);
        TimeoutException ex = Assert.ThrowsAsync<TimeoutException>(async () => await admit)!;
        Assert.That(ex.Message, Does.Contain("Admit").And.Contain("7"));
        Assert.That(ex.Message, Does.Not.Contain("AB"));
    }

    [Test]
    public async Task Reconnect_NoReply_TimesOutAt2000Ms()
    {
        Task<ReconnectOutcome> reconnect = _link.ReconnectAsync(_zone, _account, NewToken(1), CancellationToken.None);

        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS - 1));
        Assert.That(reconnect.IsCompleted, Is.False);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await Settled(reconnect);
        Assert.ThrowsAsync<TimeoutException>(async () => await reconnect);
    }

    [Test]
    public async Task Evict_NoReply_StillPendingAt10999MsAndTimesOutAt11000Ms()
    {
        Task evict = _link.EvictAsync(_zone, _account, CancellationToken.None);

        _time.Advance(TimeSpan.FromMilliseconds(EVICT_TIMEOUT_MS - 1));
        Assert.That(evict.IsCompleted, Is.False);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await Settled(evict);
        Assert.ThrowsAsync<TimeoutException>(async () => await evict);
    }

    [Test]
    public async Task Admit_ReplyBeforeTimeout_TimerFiringLaterDoesNothing()
    {
        Task<bool> admit = StartAdmit(_account);
        Reply(_zone, AdmitAccepted(AdmitIdAt(0)));
        Assert.That(await ResultOf(admit), Is.True);

        Assert.DoesNotThrow(() => _time.Advance(TimeSpan.FromMilliseconds(EVICT_TIMEOUT_MS * 2)));
        Assert.That(_time.ActiveTimerCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Admit_CallerCancels_FailsWithOperationCanceledAndStopsWaiting()
    {
        using var cts = new CancellationTokenSource();
        Task<bool> admit = StartAdmit(_account, cts.Token);
        Assert.That(admit.IsCompleted, Is.False);

        cts.Cancel();

        await Settled(admit);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await admit);
        Assert.That(_time.ActiveTimerCount, Is.EqualTo(0));
    }

    // ---------------------------------------------------------------- AC-4

    [Test]
    public void Admit_LinkDownAtSendTime_ThrowsAtOnceAndSendsNothing()
    {
        _sender.SetLink(_zone, false);

        Assert.ThrowsAsync<ZoneLinkDownException>(async () => await StartAdmit(_account));
        Assert.ThrowsAsync<ZoneLinkDownException>(async () => await _link.EvictAsync(_zone, _account, CancellationToken.None));
        Assert.ThrowsAsync<ZoneLinkDownException>(
            async () => await _link.ReconnectAsync(_zone, _account, NewToken(1), CancellationToken.None));
        Assert.That(_sender.Frames, Is.Empty);
    }

    [Test]
    public void Admit_SendReturnsFalse_ThrowsLinkDown()
    {
        _sender.SendResult = false;

        Assert.ThrowsAsync<ZoneLinkDownException>(async () => await StartAdmit(_account));
        Assert.That(_time.ActiveTimerCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Reconnect_LinkLostWhilePending_FailsAtOnceAndLaterReplyIsIgnored()
    {
        Task<ReconnectOutcome> reconnect = _link.ReconnectAsync(_zone, _account, NewToken(1), CancellationToken.None);
        uint id = Sent(0).Reconnect.RequestId;

        _link.OnLinkLost(_zone);

        await Settled(reconnect);
        Assert.ThrowsAsync<ZoneLinkDownException>(async () => await reconnect);

        Assert.DoesNotThrow(() => Reply(_zone, GatewayLinkCodec.Encode(new ReconnectResultMessage(id, true, NewToken(9)))));
        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Admit_LinkLostWhilePending_LaterAcceptIsDroppedWithoutEvictOrSetSession()
    {
        Task<bool> admit = StartAdmit(_account);
        uint id = AdmitIdAt(0);

        _link.OnLinkLost(_zone);
        await Settled(admit);
        Assert.ThrowsAsync<ZoneLinkDownException>(async () => await admit);

        Reply(_zone, AdmitAccepted(id));

        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
        Assert.That(_map.Sessions, Is.Empty);
    }

    [Test]
    public async Task OnLinkLost_OtherZone_LeavesPendingRequestsAlone()
    {
        Task<bool> admit = StartAdmit(_account);

        _link.OnLinkLost(_otherZone);
        Assert.That(admit.IsCompleted, Is.False);

        Reply(_zone, AdmitAccepted(AdmitIdAt(0)));
        Assert.That(await ResultOf(admit), Is.True);
    }

    // ---------------------------------------------------------------- AC-5

    [Test]
    public async Task Admit_AcceptedAfterTimeout_SendsEvictForThatAccountAndSetsNoSession()
    {
        Task<bool> admit = StartAdmit(_account);
        uint admitId = AdmitIdAt(0);
        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));
        await Settled(admit);
        Assert.ThrowsAsync<TimeoutException>(async () => await admit);

        Reply(_zone, AdmitAccepted(admitId));

        Assert.That(_sender.Frames, Has.Count.EqualTo(2));
        Assert.That(_sender.Frames[1].Zone, Is.EqualTo(_zone));
        GatewayToZoneMessage evict = Sent(1);
        Assert.That(evict.Type, Is.EqualTo(GatewayLinkMessageType.Evict));
        Assert.That(evict.Evict.AccountId, Is.EqualTo(_account));
        Assert.That(evict.Evict.RequestId, Is.Not.EqualTo(0u).And.Not.EqualTo(admitId));
        Assert.That(_map.Sessions, Is.Empty);

        // The zone's EvictResult is matched normally and does not throw.
        Assert.DoesNotThrow(() => Reply(_zone, GatewayLinkCodec.Encode(new EvictResultMessage(evict.Evict.RequestId))));
        Assert.That(_time.ActiveTimerCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Admit_AcceptedAfterCallerCancelled_SendsEvict()
    {
        using var cts = new CancellationTokenSource();
        Task<bool> admit = StartAdmit(_account, cts.Token);
        uint admitId = AdmitIdAt(0);
        cts.Cancel();
        await Settled(admit);

        Reply(_zone, AdmitAccepted(admitId));

        Assert.That(_sender.Frames, Has.Count.EqualTo(2));
        Assert.That(Sent(1).Evict.AccountId, Is.EqualTo(_account));
        Assert.That(_map.Sessions, Is.Empty);
    }

    [Test]
    public async Task Admit_RejectedAfterTimeout_IsDroppedWithoutEvict()
    {
        Task<bool> admit = StartAdmit(_account);
        uint admitId = AdmitIdAt(0);
        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));
        await Settled(admit);

        Reply(_zone, AdmitRejected(admitId));

        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Admit_AcceptedLongAfterTimeout_ExpiredEntryIsPrunedAndNothingIsSent()
    {
        Task<bool> admit = StartAdmit(_account);
        uint admitId = AdmitIdAt(0);
        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));
        await Settled(admit);
        _time.Advance(TimeSpan.FromMilliseconds(EVICT_TIMEOUT_MS));

        Reply(_zone, AdmitAccepted(admitId));

        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
        Assert.That(_map.Sessions, Is.Empty);
    }

    [Test]
    public async Task Admit_LateAcceptFromOtherZone_DoesNotTriggerEvict()
    {
        Task<bool> admit = StartAdmit(_account);
        uint admitId = AdmitIdAt(0);
        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));
        await Settled(admit);

        Reply(_otherZone, AdmitAccepted(admitId));

        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnknownReplies_AreDroppedWithoutThrowing()
    {
        Assert.DoesNotThrow(() => Reply(_zone, AdmitAccepted(999)));
        Assert.DoesNotThrow(() => Reply(_zone, AdmitRejected(999)));
        Assert.DoesNotThrow(() => Reply(_zone, GatewayLinkCodec.Encode(new EvictResultMessage(999))));
        Assert.DoesNotThrow(() => Reply(_zone, GatewayLinkCodec.Encode(new ReconnectResultMessage(999, true, NewToken(1)))));

        Assert.That(_sender.Frames, Is.Empty);
        Assert.That(_map.Sessions, Is.Empty);
    }

    [Test]
    public async Task Admit_ReplyFromWrongZone_DoesNotCompleteTheRequest()
    {
        Task<bool> admit = StartAdmit(_account);
        uint id = AdmitIdAt(0);

        Reply(_otherZone, AdmitAccepted(id));

        Assert.That(admit.IsCompleted, Is.False);
        Assert.That(_map.Sessions, Is.Empty);

        Reply(_zone, AdmitAccepted(id));
        Assert.That(await ResultOf(admit), Is.True);
    }

    [Test]
    public async Task Evict_ReplyOfWrongKind_DoesNotCompleteTheRequest()
    {
        Task evict = _link.EvictAsync(_zone, _account, CancellationToken.None);
        uint id = Sent(0).Evict.RequestId;

        Reply(_zone, AdmitAccepted(id));
        Assert.That(evict.IsCompleted, Is.False);
        Assert.That(_map.Sessions, Is.Empty);

        Reply(_zone, GatewayLinkCodec.Encode(new EvictResultMessage(id)));
        await Settled(evict);
    }

    [Test]
    public void OtherMessageTypes_AreIgnored()
    {
        Task<bool> admit = StartAdmit(_account);

        Reply(_zone, GatewayLinkCodec.Encode(new PlayerCountMessage(3)));
        Reply(_zone, GatewayLinkCodec.Encode(new SessionReleasedMessage(_account)));
        Reply(_zone, GatewayLinkCodec.EncodeZoneDraining());

        Assert.That(admit.IsCompleted, Is.False);
    }

    // ---------------------------------------------------------------- Review fixes (R1, S1, S2)

    [Test]
    public async Task Admit_AcceptArrivesWhileTimeoutIsSettling_ReplyWinsAndNoEvictFollows()
    {
        Task<bool> admit = StartAdmit(_account);
        uint admitId = AdmitIdAt(0);

        // The timeout's settle reads the clock while recording the expired Admit, before it removes the pending entry:
        // deliver the accept exactly there.
        _time.OnNextGetUtcNow = () => Reply(_zone, AdmitAccepted(admitId));
        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));

        Assert.That(await ResultOf(admit), Is.True);
        Assert.That(_map.Sessions, Is.EqualTo(new[] { (_account, _zone) }));

        // The settle that lost must have taken its expired record back out: a duplicate accept sends no Evict.
        Reply(_zone, AdmitAccepted(admitId));
        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Admit_SlowWrite_TimesOutWithoutWaitingForTheWrite()
    {
        _sender.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> admit = StartAdmit(_account);

        _time.Advance(TimeSpan.FromMilliseconds(REQUEST_TIMEOUT_MS));

        await Settled(admit);
        Assert.ThrowsAsync<TimeoutException>(async () => await admit);
        _sender.Gate.SetResult(true);
    }

    [Test]
    public async Task Admit_SendThrows_CallerSeesThatErrorAndLateAcceptIsEvicted()
    {
        const uint FIRST_REQUEST_ID = 1; // ids start at 1 for a fresh link (see RequestIds test)
        IOException failure = new("write failed");
        _sender.Throw = failure;

        Task<bool> admit = StartAdmit(_account);
        await Settled(admit);
        IOException? seen = Assert.ThrowsAsync<IOException>(async () => await admit);
        Assert.That(seen, Is.SameAs(failure));

        // The frame may have reached the zone, so its late accept is answered with Evict.
        _sender.Throw = null;
        Reply(_zone, AdmitAccepted(FIRST_REQUEST_ID));

        Assert.That(_sender.Frames, Has.Count.EqualTo(1));
        Assert.That(Sent(0).Type, Is.EqualTo(GatewayLinkMessageType.Evict));
        Assert.That(Sent(0).Evict.AccountId, Is.EqualTo(_account));
        Assert.That(_map.Sessions, Is.Empty);
    }

    // ---------------------------------------------------------------- AC-6

    [Test]
    public async Task Admit_Accepted_SetsSessionBeforeTheCallersTaskCompletes()
    {
        Task<bool> admit = StartAdmit(_account);
        _map.Watched = admit;

        Reply(_zone, AdmitAccepted(AdmitIdAt(0)));

        Assert.That(await ResultOf(admit), Is.True);
        Assert.That(_map.Sessions, Is.EqualTo(new[] { (_account, _zone) }));
        Assert.That(_map.CompletedAtSetSession, Is.EqualTo(new[] { false }));
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        IOptions<GatewayOptions> options = Options.Create(new GatewayOptions());
        ILogger<LinkZoneLink> logger = NullLogger<LinkZoneLink>.Instance;

        Assert.Throws<ArgumentNullException>(() => new LinkZoneLink(null!, _map, options, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new LinkZoneLink(_sender, null!, options, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new LinkZoneLink(_sender, _map, null!, _time, logger));
        Assert.Throws<ArgumentNullException>(() => new LinkZoneLink(_sender, _map, options, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new LinkZoneLink(_sender, _map, options, _time, null!));
    }

    // ---------------------------------------------------------------- Fakes

    private sealed class FakeSender : IZoneLinkSender
    {
        private readonly Dictionary<uint, bool> _up = new();

        public List<(ZoneID Zone, byte[] Frame)> Frames { get; } = new();

        public bool SendResult { get; set; } = true;

        /// <summary>When set, a frame is recorded at once but the send completes only when the test completes the gate.</summary>
        public TaskCompletionSource<bool>? Gate { get; set; }

        /// <summary>When set, the send fails with this exception and records nothing.</summary>
        public Exception? Throw { get; set; }

        public void SetLink(ZoneID zone, bool up) => _up[zone.RawValue] = up;

        public bool IsLinkUp(ZoneID zone) => _up.TryGetValue(zone.RawValue, out bool up) && up;

        public Task<bool> SendAsync(ZoneID zone, byte[] frame, CancellationToken ct)
        {
            if (Throw != null)
            {
                return Task.FromException<bool>(Throw);
            }

            if (SendResult)
            {
                Frames.Add((zone, frame));
            }

            return Gate?.Task ?? Task.FromResult(SendResult);
        }
    }

    private sealed class FakeMap : IAccountZoneMap
    {
        public List<(AccountID Account, ZoneID Zone)> Sessions { get; } = new();

        public List<bool> CompletedAtSetSession { get; } = new();

        public Task? Watched { get; set; }

        public bool TryGetZone(AccountID account, out ZoneID zone)
        {
            zone = ZoneID.Invalid;
            return false;
        }

        public bool IsLinkUp(ZoneID zone) => true;

        public void SetSession(AccountID account, ZoneID zone)
        {
            CompletedAtSetSession.Add(Watched?.IsCompleted ?? false);
            Sessions.Add((account, zone));
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public int ActiveTimerCount => _timers.Count(t => t.Due.HasValue);

        /// <summary>One-shot hook run inside the next <see cref="GetUtcNow"/> call (used to interleave a reply with a settle).</summary>
        public Action? OnNextGetUtcNow { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            Action? hook = OnNextGetUtcNow;
            OnNextGetUtcNow = null;
            hook?.Invoke();
            return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target = _now + by;
            while (true)
            {
                ManualTimer? next = null;
                foreach (ManualTimer timer in _timers)
                {
                    if (timer.Due.HasValue && timer.Due.Value <= target && (next == null || timer.Due.Value < next.Due!.Value))
                    {
                        next = timer;
                    }
                }

                if (next == null)
                {
                    break;
                }

                if (next.Due!.Value > _now)
                {
                    _now = next.Due.Value;
                }

                next.Fire();
            }

            _now = target;
        }

        private void Remove(ManualTimer timer) => _timers.Remove(timer);

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;

            public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
            }

            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
                _period = period;
                return true;
            }

            public void Fire()
            {
                Due = _period > TimeSpan.Zero ? Due + _period : null;
                _callback(_state);
            }

            public void Dispose()
            {
                Due = null;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}

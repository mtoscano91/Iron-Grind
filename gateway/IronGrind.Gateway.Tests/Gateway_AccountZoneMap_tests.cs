using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 005: <see cref="AccountZoneMap"/> and <see cref="ZoneLinkEvictSender"/> with a manual clock and a
/// recording evict sender (zone-matched removal, link-loss grace, registration, boot wait, memory). No real waits.
/// </summary>
[TestFixture]
internal sealed class Gateway_AccountZoneMap_tests
{
    private const int RETRY_SECONDS = 2;
    private static readonly TimeSpan _grace = TimeSpan.FromSeconds(2 * RETRY_SECONDS);
    private static readonly TimeSpan _oneMs = TimeSpan.FromMilliseconds(1);

    private static readonly ZoneID _zone7 = new(7u);
    private static readonly ZoneID _zone8 = new(8u);
    private static readonly AccountID _a42 = new(42u);
    private static readonly AccountID _a43 = new(43u);

    private ManualClock _clock = null!;
    private RecordingEvictSender _sender = null!;
    private RecordingLoggerFactory _logs = null!;
    private AccountZoneMap _map = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new ManualClock();
        _sender = new RecordingEvictSender();
        _logs = new RecordingLoggerFactory();
        _map = Create(RETRY_SECONDS);
    }

    private AccountZoneMap Create(int retrySeconds) =>
        new(new GatewayOptions { LinkRetrySeconds = retrySeconds }, _clock, _sender, _logs);

    private static RegisterZoneMessage Register(ZoneID zone, params uint[] live) =>
        new(zone, 1, 7777, 50, live.Select(v => new AccountID(v)).ToArray());

    private static ZoneToGatewayMessage Released(AccountID account)
    {
        byte[] frame = GatewayLinkCodec.Encode(new SessionReleasedMessage(account));
        Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(frame[4], frame.AsSpan(5), out ZoneToGatewayMessage message), Is.True);
        return message;
    }

    private static ZoneToGatewayMessage PlayerCount()
    {
        byte[] frame = GatewayLinkCodec.Encode(new PlayerCountMessage(3));
        Assert.That(GatewayLinkCodec.TryDecodeZoneToGateway(frame[4], frame.AsSpan(5), out ZoneToGatewayMessage message), Is.True);
        return message;
    }

    private bool Has(AccountID account, ZoneID zone) => _map.TryGetZone(account, out ZoneID found) && found.Equals(zone);

    // ---------------------------------------------------------------- AC-2 zone-matched removal

    [Test]
    public void SessionReleased_FromOtherZone_ChangesNothing()
    {
        _map.SetSession(_a42, _zone7);

        _map.OnMessageReceived(_zone8, Released(_a42));

        Assert.That(Has(_a42, _zone7), Is.True);
    }

    [Test]
    public void SessionReleased_FromOwningZone_RemovesTheEntry()
    {
        _map.SetSession(_a42, _zone7);

        _map.OnMessageReceived(_zone7, Released(_a42));

        Assert.That(_map.TryGetZone(_a42, out ZoneID zone), Is.False);
        Assert.That(zone, Is.EqualTo(ZoneID.Invalid));
    }

    [Test]
    public void OnMessageReceived_OtherMessageType_IsIgnored()
    {
        _map.SetSession(_a42, _zone7);

        _map.OnMessageReceived(_zone7, PlayerCount());

        Assert.That(Has(_a42, _zone7), Is.True);
    }

    [Test]
    public void Remove_FromOtherZone_ReturnsFalseAndKeepsEntry()
    {
        _map.SetSession(_a42, _zone7);

        Assert.That(_map.Remove(_a42, _zone8), Is.False);
        Assert.That(Has(_a42, _zone7), Is.True);
    }

    [Test]
    public void Remove_FromOwningZone_ReturnsTrueAndRemoves()
    {
        _map.SetSession(_a42, _zone7);

        Assert.That(_map.Remove(_a42, _zone7), Is.True);
        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
        Assert.That(_map.Remove(_a42, _zone7), Is.False);
    }

    [Test]
    public void SetSession_ExistingEntry_IsReplaced()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(_a42, _zone8);

        Assert.That(Has(_a42, _zone8), Is.True);
    }

    [Test]
    public void NewMap_IsEmptyAndUnknownZoneLinkIsDown()
    {
        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
        Assert.That(_map.IsLinkUp(_zone7), Is.False);
    }

    // ---------------------------------------------------------------- AC-3 grace

    [Test]
    public void LinkLost_BeforeGraceEnds_KeepsEntriesAndLinkIsDown()
    {
        _map.OnZoneRegistered(_zone7, Register(_zone7, 42, 43));
        Assert.That(_map.IsLinkUp(_zone7), Is.True);

        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace - _oneMs);

        Assert.That(_map.IsLinkUp(_zone7), Is.False);
        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(Has(_a43, _zone7), Is.True);
    }

    [Test]
    public void LinkLost_AfterGrace_RemovesEveryEntryOfTheZoneOnly()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(_a43, _zone8);
        _map.OnLinkLost(_zone7);

        _clock.Advance(_grace - _oneMs);
        _clock.Advance(_oneMs);

        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
        Assert.That(Has(_a43, _zone8), Is.True);
        Assert.That(_map.IsLinkUp(_zone7), Is.False);
    }

    [Test]
    public void ReRegistration_WithinGrace_ReplacesEntriesAndCancelsTheRemoval()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(_a43, _zone7);
        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace - _oneMs);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 42));

        Assert.That(_map.IsLinkUp(_zone7), Is.True);
        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(_map.TryGetZone(_a43, out _), Is.False);

        _clock.Advance(_grace * 3);
        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(_map.IsLinkUp(_zone7), Is.True);
    }

    [Test]
    public void SessionReleased_FromZoneInGrace_RemovesTheEntry()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(_a43, _zone7);
        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace - _oneMs);

        _map.OnMessageReceived(_zone7, Released(_a42));

        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
        Assert.That(Has(_a43, _zone7), Is.True);
    }

    [Test]
    public void SetSession_OntoZoneInGrace_IsRemovedAtTheOriginalDeadline()
    {
        _map.SetSession(_a42, _zone7);
        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace - _oneMs);

        _map.SetSession(_a43, _zone7);
        Assert.That(Has(_a43, _zone7), Is.True);
        Assert.That(_map.IsLinkUp(_zone7), Is.False);

        _clock.Advance(_oneMs);
        Assert.That(_map.TryGetZone(_a43, out _), Is.False);
        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
    }

    [Test]
    public void SetSession_OntoUnknownZone_IsKeptAndLinkIsDown()
    {
        _map.SetSession(_a42, _zone7);
        _clock.Advance(_grace * 3);

        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(_map.IsLinkUp(_zone7), Is.False);
    }

    [Test]
    public void Sweep_RemovesExpiredEntries()
    {
        _map.SetSession(_a42, _zone7);
        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace);

        _map.Sweep();

        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
    }

    [Test]
    public void Sweep_BeforeDeadline_KeepsEntries()
    {
        _map.SetSession(_a42, _zone7);
        _map.OnLinkLost(_zone7);
        _clock.Advance(_grace - _oneMs);

        _map.Sweep();

        Assert.That(Has(_a42, _zone7), Is.True);
    }

    [Test]
    public void Grace_UsesTheConfiguredRetryValue()
    {
        AccountZoneMap map = Create(5);
        map.SetSession(_a42, _zone7);
        map.OnLinkLost(_zone7);

        _clock.Advance(TimeSpan.FromSeconds(10) - _oneMs);
        Assert.That(map.TryGetZone(_a42, out _), Is.True);

        _clock.Advance(_oneMs);
        Assert.That(map.TryGetZone(_a42, out _), Is.False);
    }

    // ---------------------------------------------------------------- AC-4 registration

    [Test]
    public void Registration_ReplacesTheZonesEntriesWithLiveAccounts()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(new AccountID(44u), _zone7);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 42, 43));

        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(Has(_a43, _zone7), Is.True);
        Assert.That(_map.TryGetZone(new AccountID(44u), out _), Is.False);
        Assert.That(_sender.Calls, Is.Empty);
    }

    [Test]
    public void Registration_AccountMappedToAnotherZone_KeepsThatMappingAndSendsOneEvict()
    {
        _map.SetSession(_a43, _zone8);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 42, 43));

        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(Has(_a43, _zone8), Is.True);
        Assert.That(_sender.Calls, Is.EqualTo(new[] { (_zone7, _a43) }));
    }

    [Test]
    public void Registration_EmptyLiveAccounts_ClearsTheZonesEntriesOnly()
    {
        _map.SetSession(_a42, _zone7);
        _map.SetSession(_a43, _zone8);

        _map.OnZoneRegistered(_zone7, Register(_zone7));

        Assert.That(_map.TryGetZone(_a42, out _), Is.False);
        Assert.That(Has(_a43, _zone8), Is.True);
        Assert.That(_map.IsLinkUp(_zone7), Is.True);
        Assert.That(_sender.Calls, Is.Empty);
    }

    [Test]
    public void Registration_ConflictWithZoneInGrace_KeepsThatMappingAndSendsEvict()
    {
        _map.SetSession(_a43, _zone8);
        _map.OnLinkLost(_zone8);
        _clock.Advance(_grace - _oneMs);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 43));

        Assert.That(Has(_a43, _zone8), Is.True);
        Assert.That(_sender.Calls, Is.EqualTo(new[] { (_zone7, _a43) }));
    }

    [Test]
    public void Registration_ConflictWithExpiredZone_AddsTheAccountWithoutEvict()
    {
        _map.SetSession(_a43, _zone8);
        _map.OnLinkLost(_zone8);
        _clock.Advance(_grace);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 43));

        Assert.That(Has(_a43, _zone7), Is.True);
        Assert.That(_sender.Calls, Is.Empty);
    }

    [Test]
    public void Registration_DuplicateConflictingAccount_EvictsOnce()
    {
        _map.SetSession(_a43, _zone8);

        _map.OnZoneRegistered(_zone7, Register(_zone7, 43, 43, 43));

        Assert.That(_sender.Calls, Is.EqualTo(new[] { (_zone7, _a43) }));
    }

    [Test]
    public void Registration_DuplicateOwnAccount_IsIdempotent()
    {
        _map.OnZoneRegistered(_zone7, Register(_zone7, 42, 42));

        Assert.That(Has(_a42, _zone7), Is.True);
        Assert.That(_sender.Calls, Is.Empty);
    }

    [Test]
    public void Registration_SenderThrows_IsLoggedAndOtherAccountsAreStillProcessed()
    {
        _map.SetSession(_a42, _zone8);
        _map.SetSession(_a43, _zone8);
        _sender.ThrowFor = _a42;

        _map.OnZoneRegistered(_zone7, Register(_zone7, 42, 43, 44));

        Assert.That(_sender.Calls, Is.EqualTo(new[] { (_zone7, _a42), (_zone7, _a43) }));
        Assert.That(Has(new AccountID(44u), _zone7), Is.True);
        Assert.That(_logs.Entries.Count(e => e.Level == LogLevel.Error), Is.EqualTo(1));
    }

    [Test]
    public void Registration_SendsEvictAfterReleasingTheLock()
    {
        _map.SetSession(_a43, _zone8);
        bool reentered = false;
        _sender.OnSend = () =>
        {
            // Re-entering the map from the sender must not deadlock; the other thread would block if the lock were held.
            Task<bool> probe = Task.Run(() => _map.IsLinkUp(_zone7));
            reentered = probe.Wait(TimeSpan.FromSeconds(5)) && probe.Result;
        };

        _map.OnZoneRegistered(_zone7, Register(_zone7, 43));

        Assert.That(reentered, Is.True);
    }

    // ---------------------------------------------------------------- AC-5 boot

    [Test]
    public void BootWait_FalseAtStart_TrueAtExactlyTwiceRetry()
    {
        Assert.That(_map.IsBootWaitOver, Is.False);

        _clock.Advance(_grace - _oneMs);
        Assert.That(_map.IsBootWaitOver, Is.False);

        _clock.Advance(_oneMs);
        Assert.That(_map.IsBootWaitOver, Is.True);
    }

    [Test]
    public void BootWait_UsesTheConfiguredRetryValue()
    {
        AccountZoneMap map = Create(5);

        _clock.Advance(TimeSpan.FromSeconds(10) - _oneMs);
        Assert.That(map.IsBootWaitOver, Is.False);

        _clock.Advance(_oneMs);
        Assert.That(map.IsBootWaitOver, Is.True);
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        GatewayOptions options = new();
        Assert.Throws<ArgumentNullException>(() => new AccountZoneMap(null!, _clock, _sender, _logs));
        Assert.Throws<ArgumentNullException>(() => new AccountZoneMap(options, null!, _sender, _logs));
        Assert.Throws<ArgumentNullException>(() => new AccountZoneMap(options, _clock, null!, _logs));
        Assert.Throws<ArgumentNullException>(() => new AccountZoneMap(options, _clock, _sender, null!));
    }

    // ---------------------------------------------------------------- Thread safety

    [Test]
    public void ParallelOperations_NoExceptionsAndConsistentFinalState()
    {
        // Each worker owns its accounts and two zones, so the final state is fully predictable while all workers contend
        // for the one lock. The churn zone is registered and lost repeatedly; the clock never moves, so nothing expires.
        const int WORKERS = 4;
        const uint ACCOUNTS = 500;
        int misreads = 0;
        Parallel.For(0, WORKERS, worker =>
        {
            uint first = (uint)worker * 1000u + 1u;
            ZoneID home = new(100u + (uint)worker);
            ZoneID churn = new(200u + (uint)worker);
            for (uint i = first; i < first + ACCOUNTS; i++)
            {
                AccountID account = new(i);
                _map.SetSession(account, home);
                if (!_map.TryGetZone(account, out ZoneID read) || !read.Equals(home))
                {
                    Interlocked.Increment(ref misreads);
                }

                if (i % 2 == 1)
                {
                    _map.Remove(account, home);
                }

                if (i % 50 == 0)
                {
                    _map.OnZoneRegistered(churn, Register(churn, 90000u + i));
                    _map.OnLinkLost(churn);
                }
            }
        });

        Assert.That(misreads, Is.Zero);
        Assert.That(_sender.Calls, Is.Empty);
        for (int worker = 0; worker < WORKERS; worker++)
        {
            uint first = (uint)worker * 1000u + 1u;
            ZoneID home = new(100u + (uint)worker);
            for (uint i = first; i < first + ACCOUNTS; i++)
            {
                Assert.That(Has(new AccountID(i), home), Is.EqualTo(i % 2 == 0), $"account {i}");
            }

            // Only the churn zone's last registration survives; its earlier live accounts were replaced.
            uint lastChurn = first + ACCOUNTS - 1u - ((first + ACCOUNTS - 1u) % 50u);
            ZoneID churnZone = new(200u + (uint)worker);
            Assert.That(Has(new AccountID(90000u + lastChurn), churnZone), Is.True);
            Assert.That(_map.TryGetZone(new AccountID(90000u + lastChurn - 50u), out _), Is.False);
            Assert.That(_map.IsLinkUp(churnZone), Is.False);
        }
    }

    // ---------------------------------------------------------------- AC-AUTH-24

    [Test]
    [Category("Advisory")]
    public void Memory_TwoHundredEntries_StaysWithinBudgetOverThreeRuns()
    {
        const int ENTRIES = 200;
        const long BUDGET_BYTES = 12_800;
        for (int run = 0; run < 3; run++)
        {
            // Each run takes the median of three attempts: GetTotalMemory counts allocations made by any thread, so a
            // background allocation inside the window would otherwise add noise unrelated to the map.
            long[] deltas = new long[3];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                AccountZoneMap map = Create(RETRY_SECONDS);
                long before = Settle();
                for (uint i = 1; i <= ENTRIES; i++)
                {
                    map.SetSession(new AccountID(i), _zone7);
                }

                deltas[attempt] = Settle() - before;
                GC.KeepAlive(map);
            }

            long delta = deltas.OrderBy(d => d).ElementAt(1);
            TestContext.Out.WriteLine($"AC-AUTH-24 run {run + 1}: delta = {delta} bytes (budget {BUDGET_BYTES}).");
            Assert.That(delta, Is.LessThanOrEqualTo(BUDGET_BYTES), $"run {run + 1}");
        }
    }

    private static long Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(true);
    }

    // ---------------------------------------------------------------- ZoneLinkEvictSender

    [Test]
    public void EvictSender_CallsEvictAsyncOnTheLinkTheFuncReturns()
    {
        RecordingLink link = new();
        ZoneLinkEvictSender sender = new(() => link, _logs);

        sender.SendEvict(_zone7, _a42);

        Assert.That(link.Evicts, Is.EqualTo(new[] { (_zone7, _a42) }));
    }

    [Test]
    public void EvictSender_FaultedEvict_IsLoggedWithoutThrowing()
    {
        RecordingLink link = new() { Fault = new IOException("down") };
        ZoneLinkEvictSender sender = new(() => link, _logs);

        Assert.DoesNotThrow(() => sender.SendEvict(_zone7, _a42));

        Assert.That(_logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Exception is IOException), Is.EqualTo(1));
    }

    [Test]
    public void EvictSender_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ZoneLinkEvictSender(null!, _logs));
        Assert.Throws<ArgumentNullException>(() => new ZoneLinkEvictSender(() => new RecordingLink(), null!));
    }

    // ---------------------------------------------------------------- Fakes

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingEvictSender : IZoneEvictSender
    {
        private readonly object _lock = new();
        private readonly List<(ZoneID Zone, AccountID Account)> _calls = new();

        public AccountID? ThrowFor { get; set; }

        public Action? OnSend { get; set; }

        public IReadOnlyList<(ZoneID Zone, AccountID Account)> Calls
        {
            get
            {
                lock (_lock)
                {
                    return _calls.ToList();
                }
            }
        }

        public void SendEvict(ZoneID zone, AccountID account)
        {
            lock (_lock)
            {
                _calls.Add((zone, account));
            }

            OnSend?.Invoke();
            if (ThrowFor.HasValue && ThrowFor.Value.Equals(account))
            {
                throw new InvalidOperationException("sender failed");
            }
        }
    }

    private sealed class RecordingLink : IZoneLink
    {
        public List<(ZoneID Zone, AccountID Account)> Evicts { get; } = new();

        public Exception? Fault { get; set; }

        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
        {
            Evicts.Add((zone, account));
            return Fault is null ? Task.CompletedTask : Task.FromException(Fault);
        }

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private readonly record struct LogEntry(LogLevel Level, Exception? Exception);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly object _lock = new();
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Recorder(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Recorder : ILogger
        {
            private readonly RecordingLoggerFactory _owner;

            public Recorder(RecordingLoggerFactory owner)
            {
                _owner = owner;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_owner._lock)
                {
                    _owner._entries.Add(new LogEntry(logLevel, exception));
                }
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

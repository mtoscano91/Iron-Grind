using System.Collections.Concurrent;
using System.Reflection;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 010: login session establishment and AuthenticateAsync (AC-AUTH-01, 23, 27, 29 ii, 37; EC-AUTH-5/6).</summary>
[TestFixture]
internal sealed class Auth_SessionEstablishment_tests
{
    private const string PHC = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNo";
    private static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(10);
    private static readonly DateTime NOW = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly AccountID ACCOUNT = new(1u);

    private List<string> _events = null!;
    private FakeRepo _repo = null!;
    private FakeMap _map = null!;
    private FakeRouter _router = null!;
    private FakeLink _link = null!;
    private FakeHasher _hasher = null!;
    private RecordingLoggerFactory _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _events = new List<string>();
        _repo = new FakeRepo { Character = new CharacterID(77u) };
        _map = new FakeMap();
        _router = new FakeRouter();
        _router.Picks.Add((5u, 7777));
        _link = new FakeLink(_events) { Map = _map };
        _hasher = new FakeHasher();
        _logs = new RecordingLoggerFactory();
    }

    private SessionEstablishment Sessions()
    {
        GatewayOptions options = new() { ZoneRequestTimeoutMs = 50, EvictTimeoutMs = 50 };
        return new SessionEstablishment(_repo, _map, _router, _link, options, new FixedTime(NOW), _logs);
    }

    private AuthenticationService Service()
    {
        AuthHashGate gate = new(10);
        CredentialCheck credentials = new(_repo, _hasher, gate, new DummyHash(new FakeHasher()), new FixedTime(NOW), _logs);
        return new AuthenticationService(
            _repo, _hasher, gate, new RegistrationValidator(new AuthOptions()), credentials, Sessions(), Tokens(), _logs);
    }

    private TokenPath Tokens()
    {
        GatewayOptions options = new() { ZoneRequestTimeoutMs = 50, EvictTimeoutMs = 50 };
        return new TokenPath(_repo, _map, _router, _link, options, new FixedTime(NOW), _logs);
    }

    private static async Task<T> Within<T>(Task<T> task)
    {
        Task finished = await Task.WhenAny(task, Task.Delay(HANG_GUARD));
        if (finished != task)
        {
            Assert.Fail("Task did not complete within the hang guard.");
        }

        return await task;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using CancellationTokenSource guard = new(HANG_GUARD);
        while (!condition())
        {
            if (guard.IsCancellationRequested)
            {
                Assert.Fail("Condition not reached within the hang guard.");
            }

            await Task.Yield();
        }
    }

    private void AssertServerError(LoginResponse response)
    {
        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerError));
    }

    private void AssertOneInfrastructureCritical()
    {
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        AssertNoTokenInLogs();
    }

    private void AssertNoTokenInLogs()
    {
        foreach (byte[] token in _link.Tokens)
        {
            string hex = Convert.ToHexString(token);
            string b64 = Convert.ToBase64String(token);
            foreach (LogEntry entry in _logs.Entries)
            {
                string text = entry.Message + " " + entry.Exception;
                Assert.That(text, Does.Not.Contain(hex).IgnoreCase);
                Assert.That(text, Does.Not.Contain(b64));
            }
        }
    }

    // AC-1 (AC-AUTH-01)

    [Test]
    public async Task RunAsync_CorrectLogin_AdmitsWithSameTokenAndRespondsWithZoneAndPort()
    {
        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(response.Success, Is.True);
        Assert.That(response.SessionToken, Has.Length.EqualTo(16));
        Assert.That(response.SessionToken, Is.Not.All.EqualTo((byte)0));
        Assert.That(response.CharacterId, Is.EqualTo(new CharacterID(77u)));
        Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(5u)));
        Assert.That(response.ZoneServerPort, Is.EqualTo((ushort)7777));
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)" }));
        Assert.That(_link.Tokens, Has.Count.EqualTo(1));
        Assert.That(_link.Tokens[0], Is.EqualTo(response.SessionToken));
        Assert.That(_map.SetCalls, Is.EqualTo(new[] { (ACCOUNT, new ZoneID(5u)) }));
        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public async Task RunAsync_Accepted_DoesNotWriteTheMapItself()
    {
        // Gateway Story 005: the link records the entry on AdmitResult(accepted); SessionEstablishment must not, or a
        // SessionReleased landing between the two writes would bring a removed entry back.
        _link.Map = null;

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(response.Success, Is.True);
        Assert.That(_map.SetCalls, Is.Empty);
        Assert.That(_map.Entries, Is.Empty);
    }

    [Test]
    public async Task RunAsync_RepositoryReads_UseCancellationTokenNone()
    {
        await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(_repo.Tokens, Is.All.EqualTo(CancellationToken.None));
        Assert.That(_repo.Tokens, Has.Count.EqualTo(2));
    }

    // AC-2 (AC-AUTH-23 / EC-AUTH-5)

    [Test]
    public async Task RunAsync_CharacterInvalid_ServerErrorWithCriticalNamingAccountAndTimeAndNoLinkCalls()
    {
        _repo.Character = CharacterID.Invalid;

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.Empty);
        Assert.That(_router.Calls, Is.Empty);
        AssertOneInfrastructureCritical();
        Assert.That(_logs.Entries[0].Message, Does.Contain("AccountID(1)"));
        Assert.That(_logs.Entries[0].Message, Does.Contain(NOW.ToString("O")));
    }

    // AC-3 (AC-AUTH-27 a, d / EC-AUTH-6)

    [Test]
    public async Task RunAsync_MapHoldsSession_EvictCompletesBeforeAdmitThenSucceeds()
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        _link.EvictGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<LoginResponse> pending = Sessions().RunAsync(ACCOUNT);
        await WaitUntil(() => _events.Count == 1);

        Assert.That(_events, Is.EqualTo(new[] { "Evict(3,1)" }));
        Assert.That(pending.IsCompleted, Is.False);

        _link.EvictGate.SetResult();
        LoginResponse response = await Within(pending);

        Assert.That(response.Success, Is.True);
        Assert.That(_events, Is.EqualTo(new[] { "Evict(3,1)", "Admit(5,1,77)" }));
        Assert.That(_map.Entries[ACCOUNT], Is.EqualTo(new ZoneID(5u)));
    }

    [Test]
    public async Task RunAsync_EvictNeverCompletesAndIgnoresToken_ServerErrorWithOneCriticalAndNoAdmit()
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        _link.EvictHangs = true;

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.EqualTo(new[] { "Evict(3,1)" }));
        Assert.That(_map.SetCalls, Is.Empty);
        AssertOneInfrastructureCritical();
        Assert.That(_logs.Entries[0].Exception, Is.TypeOf<TimeoutException>());
        Assert.That(_logs.Entries[0].Exception!.Message, Does.Contain("Evict from zone"));
    }

    [Test]
    public async Task RunAsync_SessionZoneLinkDown_ServerErrorAtOnceWithoutEvict()
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        _map.Down.Add(new ZoneID(3u));

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.Empty);
        Assert.That(_router.Calls, Is.Empty);
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        Assert.That(_logs.Entries[0].Message, Does.Contain(new ZoneID(3u).ToString()));
        Assert.That(_logs.Entries[0].Message, Does.Contain(ACCOUNT.ToString()));
    }

    [Test]
    public async Task RunAsync_EvictThrows_ServerErrorWithOneCriticalAndNoAdmit()
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        _link.EvictThrows = new InvalidOperationException("link broke");

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.EqualTo(new[] { "Evict(3,1)" }));
        AssertOneInfrastructureCritical();
    }

    [Test]
    public async Task RunAsync_LinkCalls_ReceiveOnlyTheTimeoutAsCancellationToken()
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);

        await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(_link.TokensSeen, Has.Count.EqualTo(2));
        Assert.That(_link.TokensSeen, Is.All.Not.EqualTo(CancellationToken.None));
        Assert.That(_link.TokensSeen[0], Is.Not.EqualTo(_link.TokensSeen[1]));
    }

    // AC-4 (AC-AUTH-37)

    [Test]
    public async Task RunAsync_FirstZoneRejects_ReroutesOnceExcludingItAndSucceeds()
    {
        _router.Picks.Add((6u, 7778));
        _link.Rejecting.Add(5u);
        _repo.LastZone = new ZoneID(4u);

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(response.Success, Is.True);
        Assert.That(response.ZoneId, Is.EqualTo(new ZoneID(6u)));
        Assert.That(response.ZoneServerPort, Is.EqualTo((ushort)7778));
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)", "Admit(6,1,77)" }));
        Assert.That(_router.Calls, Is.EqualTo(new[] { (4u, 0u), (4u, 5u) }));
        Assert.That(_map.SetCalls, Is.EqualTo(new[] { (ACCOUNT, new ZoneID(6u)) }));
        Assert.That(_link.Tokens, Has.Count.EqualTo(2));
        Assert.That(_link.Tokens[0], Is.Not.EqualTo(_link.Tokens[1]), "each Admit gets a fresh token");
        Assert.That(response.SessionToken, Is.EqualTo(_link.Tokens[1]));
    }

    [Test]
    public async Task RunAsync_BothZonesReject_ServerErrorAfterExactlyTwoAdmitsAndNoMapEntry()
    {
        _router.Picks.Add((6u, 7778));
        _link.Rejecting.Add(5u);
        _link.Rejecting.Add(6u);

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)", "Admit(6,1,77)" }));
        Assert.That(_map.SetCalls, Is.Empty);
        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public async Task RunAsync_RejectedAndNoOtherZone_ServerErrorNotServerStarting()
    {
        _link.Rejecting.Add(5u);

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)" }));
        Assert.That(_map.SetCalls, Is.Empty);
    }

    // AC-5 (AC-AUTH-29 ii)

    [Test]
    public async Task RunAsync_AdmitNeverCompletesAndIgnoresToken_ServerErrorWithOneCriticalNoRerouteNoMapEntry()
    {
        _router.Picks.Add((6u, 7778));
        _link.AdmitHangs = true;

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)" }));
        Assert.That(_router.Calls, Has.Count.EqualTo(1));
        Assert.That(_map.SetCalls, Is.Empty);
        AssertOneInfrastructureCritical();
        Assert.That(_logs.Entries[0].Exception, Is.TypeOf<TimeoutException>());
        Assert.That(_logs.Entries[0].Exception!.Message, Does.Contain("Admit to zone"));
    }

    // AC-6

    [Test]
    public async Task RunAsync_NoRoutableZone_ServerStartingWithNoLinkCalls()
    {
        _router.Picks.Clear();

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        Assert.That(_events, Is.Empty);
        Assert.That(_map.SetCalls, Is.Empty);
        Assert.That(_logs.Entries, Is.Empty);
    }

    // AC-7

    [Test]
    public async Task RunAsync_Success_NoTokenStoredInServiceOrMap()
    {
        await Within(Sessions().RunAsync(ACCOUNT));

        FieldInfo[] serviceFields = typeof(SessionEstablishment).GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (FieldInfo field in serviceFields)
        {
            Assert.That(field.FieldType, Is.Not.EqualTo(typeof(byte[])), field.Name);
        }

        Assert.That(_map.StoredTypes, Is.All.AnyOf(typeof(AccountID), typeof(ZoneID)));
        Assert.That(_map.Entries.Values, Is.All.TypeOf<ZoneID>());
    }

    // AC-8

    [Test]
    public async Task RunAsync_LastZoneFromRepository_PassedToRouterWithInvalidExcluded()
    {
        _repo.LastZone = new ZoneID(4u);

        await Within(Sessions().RunAsync(ACCOUNT));

        Assert.That(_router.Calls, Is.EqualTo(new[] { (4u, 0u) }));
    }

    [TestCase("character")]
    [TestCase("lastzone")]
    [TestCase("evict")]
    [TestCase("admit")]
    public async Task RunAsync_DependencyThrows_ServerErrorWithOneCriticalAndNoMapEntry(string which)
    {
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        InvalidOperationException boom = new("dependency failed");
        switch (which)
        {
            case "character":
                _repo.CharacterThrows = boom;
                break;
            case "lastzone":
                _repo.LastZoneThrows = boom;
                break;
            case "evict":
                _link.EvictThrows = boom;
                break;
            default:
                _link.AdmitThrows = boom;
                break;
        }

        LoginResponse response = await Within(Sessions().RunAsync(ACCOUNT));

        AssertServerError(response);
        Assert.That(_map.SetCalls, Is.Empty);
        Assert.That(_events.Count(e => e.StartsWith("Admit", StringComparison.Ordinal)), Is.LessThanOrEqualTo(1));
        AssertOneInfrastructureCritical();
    }

    // AC-9: AuthenticateAsync

    [Test]
    public async Task AuthenticateAsync_UnknownUser_InvalidCredentialsWithNoRouterOrLinkCalls()
    {
        LoginResponse response = await Within(Service().AuthenticateAsync(Request(), CancellationToken.None));

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(AuthFailureReason.InvalidCredentials));
        Assert.That(_router.Calls, Is.Empty);
        Assert.That(_events, Is.Empty);
    }

    [Test]
    public async Task AuthenticateAsync_CredentialServerError_ServerErrorWithNoRouterOrLinkCalls()
    {
        _repo.FindThrows = new InvalidOperationException("db down");

        LoginResponse response = await Within(Service().AuthenticateAsync(Request(), CancellationToken.None));

        AssertServerError(response);
        Assert.That(_router.Calls, Is.Empty);
        Assert.That(_events, Is.Empty);
    }

    [Test]
    public async Task AuthenticateAsync_Passed_RunsSessionEstablishmentForThatAccount()
    {
        _repo.Found = new AccountRecord(ACCOUNT, PHC, false, 0, null);
        _hasher.VerifyResult = true;

        LoginResponse response = await Within(Service().AuthenticateAsync(Request(), CancellationToken.None));

        Assert.That(response.Success, Is.True);
        Assert.That(response.CharacterId, Is.EqualTo(new CharacterID(77u)));
        Assert.That(_events, Is.EqualTo(new[] { "Admit(5,1,77)" }));
        Assert.That(_repo.CharacterAccounts, Is.EqualTo(new[] { ACCOUNT }));
    }

    [Test]
    public async Task AuthenticateAsync_NonZeroTokenField_RunsTokenPathNotCredentialOrAdmit()
    {
        byte[] token = new byte[16];
        token[3] = 9;
        _repo.Found = new AccountRecord(ACCOUNT, PHC, false, 0, null);
        _map.Entries[ACCOUNT] = new ZoneID(3u);
        _router.Ports[new ZoneID(3u)] = 7779;
        byte[] rotated = new byte[16];
        rotated[0] = 0xBB;
        _link.ReconnectResult = new ReconnectOutcome(true, rotated);
        ConnectionRequest request = new("alice", string.Empty, token);

        LoginResponse response = await Within(Service().AuthenticateAsync(request, CancellationToken.None));

        Assert.That(response.Success, Is.True);
        Assert.That(response.SessionToken, Is.EqualTo(rotated));
        Assert.That(_events, Is.EqualTo(new[] { "Reconnect(3,1)" }));
        Assert.That(_map.SetCalls, Is.Empty);
    }

    [Test]
    public void AuthenticateAsync_CancelledDuringCredentialCheck_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => Service().AuthenticateAsync(Request(), cts.Token));

        Assert.That(_events, Is.Empty);
    }

    private static ConnectionRequest Request() => new("alice", "correct horse", new byte[16]);

    // Fakes

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeRepo : IAccountRepository
    {
        public CharacterID Character { get; set; }

        public ZoneID LastZone { get; set; } = ZoneID.Invalid;

        public Exception? CharacterThrows { get; set; }

        public Exception? LastZoneThrows { get; set; }

        public Exception? FindThrows { get; set; }

        public AccountRecord? Found { get; set; }

        public List<CancellationToken> Tokens { get; } = new();

        public List<AccountID> CharacterAccounts { get; } = new();

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct)
        {
            Tokens.Add(ct);
            CharacterAccounts.Add(account);
            if (CharacterThrows is not null)
            {
                throw CharacterThrows;
            }

            return Task.FromResult(Character);
        }

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct)
        {
            Tokens.Add(ct);
            if (LastZoneThrows is not null)
            {
                throw LastZoneThrows;
            }

            return Task.FromResult(LastZone);
        }

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct)
        {
            if (FindThrows is not null)
            {
                throw FindThrows;
            }

            return Task.FromResult(Found);
        }

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct) => Task.CompletedTask;

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct) => throw new NotSupportedException();

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeMap : IAccountZoneMap
    {
        public Dictionary<AccountID, ZoneID> Entries { get; } = new();

        public HashSet<ZoneID> Down { get; } = new();

        public List<(AccountID, ZoneID)> SetCalls { get; } = new();

        public IEnumerable<Type> StoredTypes => typeof(Dictionary<AccountID, ZoneID>).GetGenericArguments();

        public bool TryGetZone(AccountID account, out ZoneID zone)
        {
            return Entries.TryGetValue(account, out zone);
        }

        public bool IsLinkUp(ZoneID zone) => !Down.Contains(zone);

        public void SetSession(AccountID account, ZoneID zone)
        {
            SetCalls.Add((account, zone));
            Entries[account] = zone;
        }

        public bool Remove(AccountID account, ZoneID fromZone) =>
            Entries.TryGetValue(account, out ZoneID current) && current.Equals(fromZone) && Entries.Remove(account);
    }

    private sealed class FakeRouter : IZoneRouter
    {
        public Dictionary<ZoneID, ushort> Ports { get; } = new();

        public bool TryGetPort(ZoneID zone, out ushort port) => Ports.TryGetValue(zone, out port);

        public List<(uint Zone, ushort Port)> Picks { get; } = new();

        public List<(uint Last, uint Excluded)> Calls { get; } = new();

        public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port)
        {
            Calls.Add((lastZone.RawValue, excluded.RawValue));
            int index = Calls.Count - 1;
            if (index < Picks.Count)
            {
                zone = new ZoneID(Picks[index].Zone);
                port = Picks[index].Port;
                return true;
            }

            zone = ZoneID.Invalid;
            port = 0;
            return false;
        }
    }

    private sealed class FakeLink : IZoneLink
    {
        private readonly List<string> _events;

        public FakeLink(List<string> events)
        {
            _events = events;
        }

        public HashSet<uint> Rejecting { get; } = new();

        public List<byte[]> Tokens { get; } = new();

        public List<CancellationToken> TokensSeen { get; } = new();

        public TaskCompletionSource? EvictGate { get; set; }

        public bool EvictHangs { get; set; }

        public bool AdmitHangs { get; set; }

        public Exception? EvictThrows { get; set; }

        public Exception? AdmitThrows { get; set; }

        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
        {
            _events.Add($"Admit({zone.RawValue},{account.RawValue},{character.RawValue})");
            Tokens.Add((byte[])token.Clone());
            TokensSeen.Add(ct);
            if (AdmitThrows is not null)
            {
                throw AdmitThrows;
            }

            if (AdmitHangs)
            {
                return new TaskCompletionSource<bool>().Task;
            }

            bool accepted = !Rejecting.Contains(zone.RawValue);
            if (accepted)
            {
                // The real link records the entry when the zone accepts (Story 004/005); SessionEstablishment does not.
                Map?.SetSession(account, zone);
            }

            return Task.FromResult(accepted);
        }

        public FakeMap? Map { get; set; }

        public ReconnectOutcome ReconnectResult { get; set; }

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct)
        {
            _events.Add($"Reconnect({zone.RawValue},{account.RawValue})");
            return Task.FromResult(ReconnectResult);
        }

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
        {
            _events.Add($"Evict({zone.RawValue},{account.RawValue})");
            TokensSeen.Add(ct);
            if (EvictThrows is not null)
            {
                throw EvictThrows;
            }

            if (EvictHangs)
            {
                return new TaskCompletionSource().Task;
            }

            return EvictGate?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public bool VerifyResult { get; set; }

        public string Hash(string password) => PHC;

        public bool Verify(string password, string phc) => VerifyResult;
    }

    private readonly record struct LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries.ToList();

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, _entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CategoryLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<LogEntry> _entries;

            public CategoryLogger(string category, ConcurrentQueue<LogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _entries.Enqueue(new LogEntry(_category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}

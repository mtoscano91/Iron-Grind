using System.Collections.Concurrent;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 011: login token path (AC-AUTH-15, 38; EC-AUTH-5/7).</summary>
[TestFixture]
internal sealed class Auth_TokenPath_tests
{
    private const string PHC = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNo";
    private const string USERNAME = "Alice";
    private static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(10);
    private static readonly AccountID ACCOUNT = new(1u);
    private static readonly ZoneID ZONE = new(3u);
    private static readonly CharacterID CHARACTER = new(77u);
    private static readonly DateTime NOW = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    private byte[] _requestToken = null!;
    private byte[] _rotated = null!;
    private FakeRepo _repo = null!;
    private FakeMap _map = null!;
    private FakeRouter _router = null!;
    private FakeLink _link = null!;
    private CountingHasher _hasher = null!;
    private CountingGate _gate = null!;
    private RecordingLoggerFactory _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _requestToken = Filled(0xAA);
        _rotated = Filled(0xBB);
        _repo = new FakeRepo { Found = new AccountRecord(ACCOUNT, PHC, false, 2, null), Character = CHARACTER };
        _map = new FakeMap();
        _map.Entries[ACCOUNT] = ZONE;
        _router = new FakeRouter();
        _router.Ports[ZONE] = 7779;
        _link = new FakeLink { Result = new ReconnectOutcome(true, _rotated) };
        _hasher = new CountingHasher();
        _gate = new CountingGate();
        _logs = new RecordingLoggerFactory();
    }

    private static byte[] Filled(byte value)
    {
        byte[] bytes = new byte[16];
        Array.Fill(bytes, value);
        return bytes;
    }

    private TokenPath Path()
    {
        GatewayOptions options = new() { ZoneRequestTimeoutMs = 50, EvictTimeoutMs = 50 };
        return new TokenPath(_repo, _map, _router, _link, options, new FixedTime(NOW), _logs);
    }

    private AuthenticationService Service()
    {
        GatewayOptions options = new() { ZoneRequestTimeoutMs = 50, EvictTimeoutMs = 50 };
        CredentialCheck credentials = new(_repo, _hasher, _gate, new DummyHash(new CountingHasher()), TimeProvider.System, _logs);
        SessionEstablishment sessions = new(_repo, _map, _router, _link, options, TimeProvider.System, _logs);
        return new AuthenticationService(
            _repo, _hasher, _gate, new RegistrationValidator(new AuthOptions()), credentials, sessions, Path(), _logs);
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

    private Task<LoginResponse> Run() => Within(Path().RunAsync(USERNAME, _requestToken, CancellationToken.None));

    private static void AssertFailure(LoginResponse response, AuthFailureReason reason)
    {
        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorCode, Is.EqualTo(reason));
    }

    private void AssertOneInfrastructureCritical()
    {
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        AssertNoSecretsInLogs();
    }

    private void AssertNoSecretsInLogs()
    {
        string[] secrets =
        {
            Convert.ToHexString(_requestToken), Convert.ToBase64String(_requestToken),
            Convert.ToHexString(_rotated), Convert.ToBase64String(_rotated),
            USERNAME, USERNAME.ToLowerInvariant(),
        };
        foreach (LogEntry entry in _logs.Entries)
        {
            string text = entry.Message + " " + entry.Exception;
            foreach (string secret in secrets)
            {
                Assert.That(text, Does.Not.Contain(secret).IgnoreCase);
            }
        }
    }

    private void AssertNoCredentialWork()
    {
        Assert.That(_hasher.Calls, Is.EqualTo(0));
        Assert.That(_gate.EnterCalls, Is.EqualTo(0));
        Assert.That(_repo.WriteCalls, Is.EqualTo(0));
        Assert.That(_map.SetCalls, Is.EqualTo(0));
    }

    // AC-1 (AC-AUTH-15)

    [Test]
    public async Task AuthenticateAsync_TokenWithWrongPassword_ReconnectsAndNeverEvaluatesCredentials()
    {
        ConnectionRequest request = new(USERNAME, "wrong", _requestToken);

        LoginResponse response = await Within(Service().AuthenticateAsync(request, CancellationToken.None));

        Assert.That(response.Success, Is.True);
        Assert.That(_link.Log, Is.EqualTo(new[] { "Reconnect(3,1)" }));
        Assert.That(_link.TokensPresented[0], Is.EqualTo(_requestToken));
        AssertNoCredentialWork();
        Assert.That(_repo.Found!.LoginFailureCount, Is.EqualTo(2));
    }

    // AC-2 (AC-AUTH-38 i-iii)

    [TestCase("unknown")]
    [TestCase("nomap")]
    [TestCase("banned")]
    public async Task RunAsync_NoUsableSession_InvalidCredentialsWithNoZoneTraffic(string which)
    {
        switch (which)
        {
            case "unknown":
                _repo.Found = null;
                break;
            case "nomap":
                _map.Entries.Clear();
                break;
            default:
                _repo.Found = new AccountRecord(ACCOUNT, PHC, true, 0, null);
                break;
        }

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.InvalidCredentials);
        Assert.That(_link.Log, Is.Empty);
        AssertNoCredentialWork();
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(typeof(TokenPath).FullName));
        if (which != "unknown")
        {
            Assert.That(_logs.Entries[0].Message, Does.Contain(ACCOUNT.ToString()));
        }

        AssertNoSecretsInLogs();
    }

    // AC-3

    [Test]
    public async Task RunAsync_LiveSession_ExactlyOneReconnectAndRelaysRotatedToken()
    {
        LoginResponse response = await Run();

        Assert.That(response.Success, Is.True);
        Assert.That(response.SessionToken, Is.EqualTo(_rotated));
        Assert.That(response.CharacterId, Is.EqualTo(CHARACTER));
        Assert.That(response.ZoneId, Is.EqualTo(ZONE));
        Assert.That(response.ZoneServerPort, Is.EqualTo((ushort)7779));
        Assert.That(_link.Log, Is.EqualTo(new[] { "Reconnect(3,1)" }));
        Assert.That(_logs.Entries, Is.Empty);
        Assert.That(_map.SetCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_UsernameIsNormalizedBeforeLookup()
    {
        await Run();

        Assert.That(_repo.LookedUp, Is.EqualTo(new[] { "alice" }));
    }

    [Test]
    public async Task RunAsync_ZoneRejects_InvalidCredentialsWithOneInformationEntry()
    {
        _link.Result = new ReconnectOutcome(false, null);

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.InvalidCredentials);
        Assert.That(_link.Log, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(_logs.Entries[0].Message, Does.Contain(ACCOUNT.ToString()));
        AssertNoSecretsInLogs();
    }

    [Test]
    public async Task RunAsync_ReconnectNeverCompletesAndIgnoresToken_ServerErrorWithOneCritical()
    {
        _link.Hangs = true;

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        AssertOneInfrastructureCritical();
        Assert.That(_logs.Entries[0].Exception, Is.TypeOf<TimeoutException>());
        Assert.That(_logs.Entries[0].Exception!.Message, Does.Contain("Reconnect to zone"));
    }

    // AC-4

    [Test]
    public async Task RunAsync_AccountInActiveLockout_StillReconnects()
    {
        _repo.Found = new AccountRecord(ACCOUNT, PHC, false, 5, DateTime.UtcNow.AddSeconds(600));

        LoginResponse response = await Run();

        Assert.That(response.Success, Is.True);
        Assert.That(_link.Log, Has.Count.EqualTo(1));
        Assert.That(_repo.WriteCalls, Is.EqualTo(0));
    }

    // AC-5

    [Test]
    public async Task RunAsync_SessionZoneLinkDown_ServerErrorWithOneWarningAndNoReconnect()
    {
        _map.Down.Add(ZONE);

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        Assert.That(_link.Log, Is.Empty);
        Assert.That(_repo.CharacterCalls, Is.EqualTo(0));
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        Assert.That(_logs.Entries[0].Message, Does.Contain(ACCOUNT.ToString()));
        Assert.That(_logs.Entries[0].Message, Does.Contain(ZONE.ToString()));
    }

    [Test]
    public async Task RunAsync_RouterHasNoPortForZone_ServerErrorWithNoReconnect()
    {
        _router.Ports.Clear();

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        Assert.That(_link.Log, Is.Empty);
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        Assert.That(_logs.Entries[0].Message, Does.Contain(ZONE.ToString()));
    }

    [Test]
    public async Task RunAsync_CharacterInvalid_ServerErrorWithOneCriticalAndNoReconnect()
    {
        _repo.Character = CharacterID.Invalid;

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        Assert.That(_link.Log, Is.Empty);
        AssertOneInfrastructureCritical();
        Assert.That(_logs.Entries[0].Message, Does.Contain(ACCOUNT.ToString()));
        Assert.That(_logs.Entries[0].Message, Does.Contain(NOW.ToString("O")));
    }

    [TestCase("find")]
    [TestCase("character")]
    [TestCase("reconnect")]
    public async Task RunAsync_DependencyThrows_ServerErrorWithOneCritical(string which)
    {
        InvalidOperationException boom = new("dependency failed");
        switch (which)
        {
            case "find":
                _repo.FindThrows = boom;
                break;
            case "character":
                _repo.CharacterThrows = boom;
                break;
            default:
                _link.Throws = boom;
                break;
        }

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        AssertOneInfrastructureCritical();
        Assert.That(_map.SetCalls, Is.EqualTo(0));
    }

    [TestCase("null")]
    [TestCase("short")]
    [TestCase("zero")]
    public async Task RunAsync_AcceptedWithBadToken_ServerErrorWithOneCritical(string which)
    {
        byte[]? bad = which switch
        {
            "null" => null,
            "short" => new byte[15],
            _ => new byte[16],
        };
        if (which == "short")
        {
            Array.Fill(bad!, (byte)0xCC);
        }

        _link.Result = new ReconnectOutcome(true, bad);

        LoginResponse response = await Run();

        AssertFailure(response, AuthFailureReason.ServerError);
        AssertOneInfrastructureCritical();
    }

    // AC-6

    [Test]
    public void RunAsync_AlreadyCancelled_ThrowsAndSendsNoReconnect()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => Path().RunAsync(USERNAME, _requestToken, cts.Token));

        Assert.That(_link.Log, Is.Empty);
        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public async Task RunAsync_RequestToken_ReachesFindButNotReconnect()
    {
        using CancellationTokenSource cts = new();

        await Within(Path().RunAsync(USERNAME, _requestToken, cts.Token));

        Assert.That(_repo.FindTokens, Is.EqualTo(new[] { cts.Token }));
        Assert.That(_link.LinkTokens, Has.Count.EqualTo(1));
        Assert.That(_link.LinkTokens[0], Is.Not.EqualTo(cts.Token));
        Assert.That(_link.LinkTokens[0], Is.Not.EqualTo(CancellationToken.None));
    }

    [Test]
    public async Task RunAsync_CancelledAfterReconnectSent_StillDeliversRotatedToken()
    {
        using CancellationTokenSource cts = new();
        _link.OnReconnect = cts.Cancel;

        LoginResponse response = await Within(Path().RunAsync(USERNAME, _requestToken, cts.Token));

        Assert.That(response.Success, Is.True);
        Assert.That(response.SessionToken, Is.EqualTo(_rotated));
    }

    [Test]
    public async Task AuthenticateAsync_NonZeroToken_RunsTokenPathWithoutGateHasherOrSetSession()
    {
        ConnectionRequest request = new(USERNAME, "anything", _requestToken);

        LoginResponse response = await Within(Service().AuthenticateAsync(request, CancellationToken.None));

        Assert.That(response.Success, Is.True);
        Assert.That(_link.Log, Is.EqualTo(new[] { "Reconnect(3,1)" }));
        AssertNoCredentialWork();
    }

    // Fakes

    private sealed class FakeRepo : IAccountRepository
    {
        private int _writes;

        public AccountRecord? Found { get; set; }

        public CharacterID Character { get; set; }

        public Exception? FindThrows { get; set; }

        public Exception? CharacterThrows { get; set; }

        public int CharacterCalls { get; private set; }

        public int WriteCalls => Volatile.Read(ref _writes);

        public List<string> LookedUp { get; } = new();

        public List<CancellationToken> FindTokens { get; } = new();

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct)
        {
            LookedUp.Add(usernameLower);
            FindTokens.Add(ct);
            if (FindThrows is not null)
            {
                throw FindThrows;
            }

            return Task.FromResult(Found);
        }

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct)
        {
            CharacterCalls++;
            if (CharacterThrows is not null)
            {
                throw CharacterThrows;
            }

            return Task.FromResult(Character);
        }

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
        {
            Interlocked.Increment(ref _writes);
            throw new NotSupportedException();
        }

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct)
        {
            Interlocked.Increment(ref _writes);
            throw new NotSupportedException();
        }

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct)
        {
            Interlocked.Increment(ref _writes);
            throw new NotSupportedException();
        }

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeMap : IAccountZoneMap
    {
        public Dictionary<AccountID, ZoneID> Entries { get; } = new();

        public HashSet<ZoneID> Down { get; } = new();

        public int SetCalls { get; private set; }

        public bool TryGetZone(AccountID account, out ZoneID zone) => Entries.TryGetValue(account, out zone);

        public bool IsLinkUp(ZoneID zone) => !Down.Contains(zone);

        public void SetSession(AccountID account, ZoneID zone)
        {
            SetCalls++;
        }

        public bool Remove(AccountID account, ZoneID fromZone) => false;
    }

    private sealed class FakeRouter : IZoneRouter
    {
        public Dictionary<ZoneID, ushort> Ports { get; } = new();

        public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port) => throw new NotSupportedException();

        public bool TryGetPort(ZoneID zone, out ushort port) => Ports.TryGetValue(zone, out port);
    }

    private sealed class FakeLink : IZoneLink
    {
        public List<string> Log { get; } = new();

        public List<byte[]> TokensPresented { get; } = new();

        public List<CancellationToken> LinkTokens { get; } = new();

        public ReconnectOutcome Result { get; set; }

        public bool Hangs { get; set; }

        public Exception? Throws { get; set; }

        public Action? OnReconnect { get; set; }

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct)
        {
            Log.Add($"Reconnect({zone.RawValue},{account.RawValue})");
            TokensPresented.Add((byte[])token.Clone());
            LinkTokens.Add(ct);
            OnReconnect?.Invoke();
            if (Throws is not null)
            {
                throw Throws;
            }

            return Hangs ? new TaskCompletionSource<ReconnectOutcome>().Task : Task.FromResult(Result);
        }

        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
        {
            Log.Add("Admit");
            throw new NotSupportedException();
        }

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
        {
            Log.Add("Evict");
            throw new NotSupportedException();
        }
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Hash(string password)
        {
            Interlocked.Increment(ref _calls);
            return PHC;
        }

        public bool Verify(string password, string phc)
        {
            Interlocked.Increment(ref _calls);
            return false;
        }
    }

    private sealed class CountingGate : IAuthHashGate
    {
        private int _enters;

        public int EnterCalls => Volatile.Read(ref _enters);

        public bool TryEnter()
        {
            Interlocked.Increment(ref _enters);
            return true;
        }

        public void Exit()
        {
        }
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

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

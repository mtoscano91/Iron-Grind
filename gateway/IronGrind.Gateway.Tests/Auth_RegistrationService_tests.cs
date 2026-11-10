using System.Collections.Concurrent;
using System.Reflection;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 008: registration service and hash gate (AC-AUTH-12 c/d, 34, 36; EC-AUTH-4/10).</summary>
[TestFixture]
internal sealed class Auth_RegistrationService_tests
{
    private const string PASSWORD = "correct horse";
    private const string FAKE_PHC = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNo";
    private static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(10);

    private FakeAccountRepository _repo = null!;
    private FakePasswordHasher _hasher = null!;
    private RecordingLoggerFactory _logs = null!;
    private StrictZoneLink _link = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new FakeAccountRepository();
        _hasher = new FakePasswordHasher(FAKE_PHC);
        _logs = new RecordingLoggerFactory();
        _link = new StrictZoneLink();
    }

    private AuthenticationService Service(int maxPending = 10)
    {
        AuthHashGate gate = new(maxPending);
        CredentialCheck credentials = new(_repo, _hasher, gate, new DummyHash(new FakePasswordHasher(FAKE_PHC)), TimeProvider.System, _logs);
        SessionEstablishment sessions = new(_repo, new StrictZoneMap(), new StrictZoneRouter(), _link, new GatewayOptions(), TimeProvider.System, _logs);
        TokenPath tokenPath = new(_repo, new StrictZoneMap(), new StrictZoneRouter(), _link, new GatewayOptions(), TimeProvider.System, _logs);
        return new AuthenticationService(_repo, _hasher, gate, new RegistrationValidator(new AuthOptions()), credentials, sessions, tokenPath, _logs);
    }

    private static RegistrationRequest Request(string user = "Alice", string pass = PASSWORD, string name = "Grim_Iron7", byte cls = 0)
    {
        return new RegistrationRequest(user, pass, name, cls);
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

    // AC-1

    [Test]
    public async Task RegisterAsync_ValidRequest_SucceedsAndPassesNormalizedArgsToRepository()
    {
        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(_hasher.CallCount, Is.EqualTo(1));
        Assert.That(_repo.RegisterCalls, Has.Count.EqualTo(1));
        FakeAccountRepository.RegisterCall call = _repo.RegisterCalls[0];
        Assert.That(call.UsernameLower, Is.EqualTo("alice"));
        Assert.That(call.Phc, Is.EqualTo(FAKE_PHC));
        Assert.That(call.CharacterName, Is.EqualTo("Grim_Iron7"));
        Assert.That(call.ClassType, Is.EqualTo((byte)0));
        Assert.That(call.Token, Is.EqualTo(CancellationToken.None));
        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public async Task RegisterAsync_CancellableToken_RepositoryStillReceivesNone()
    {
        using CancellationTokenSource cts = new();

        await Service().RegisterAsync(Request(), cts.Token);

        Assert.That(_repo.RegisterCalls[0].Token, Is.EqualTo(CancellationToken.None));
    }

    // AC-2

    [Test]
    public async Task RegisterAsync_PasswordLength7_ReturnsPasswordTooShortWithNoHashOrRepository()
    {
        RegistrationResult result = await Service().RegisterAsync(Request(pass: "1234567"), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(RegistrationError.PasswordTooShort));
        Assert.That(_hasher.CallCount, Is.EqualTo(0));
        Assert.That(_repo.RegisterCalls, Is.Empty);
    }

    [TestCase((byte)2)]
    [TestCase((byte)255)]
    public async Task RegisterAsync_UnknownClass_ReturnsClassInvalidWithNoHashOrRepository(byte cls)
    {
        RegistrationResult result = await Service().RegisterAsync(Request(cls: cls), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(RegistrationError.ClassInvalid));
        Assert.That(_hasher.CallCount, Is.EqualTo(0));
        Assert.That(_repo.RegisterCalls, Is.Empty);
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    public async Task RegisterAsync_PlayableClass_Succeeds(byte cls)
    {
        RegistrationResult result = await Service().RegisterAsync(Request(cls: cls), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(_repo.RegisterCalls[0].ClassType, Is.EqualTo(cls));
    }

    // AC-3 (AC-AUTH-36)

    [Test]
    public async Task RegisterAsync_GateFull_ReturnsServerErrorWithoutHashingThenPermitsReturn()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _hasher.BlockOn = release;
        AuthenticationService service = Service(maxPending: 2);

        Task<RegistrationResult> first = service.RegisterAsync(Request("user_one", name: "NameOne"), CancellationToken.None);
        Task<RegistrationResult> second = service.RegisterAsync(Request("user_two", name: "NameTwo"), CancellationToken.None);
        await WaitUntil(() => _hasher.CallCount == 2);

        RegistrationResult third = await Within(service.RegisterAsync(Request("user_three", name: "NameThree"), CancellationToken.None));

        Assert.That(third.Success, Is.False);
        Assert.That(third.ErrorCode, Is.EqualTo(RegistrationError.ServerError));
        Assert.That(first.IsCompleted, Is.False);
        Assert.That(second.IsCompleted, Is.False);
        Assert.That(_hasher.CallCount, Is.EqualTo(2));
        Assert.That(_repo.RegisterCalls, Is.Empty);
        Assert.That(_logs.Entries, Is.Empty);

        release.SetResult();
        Assert.That((await Within(first)).Success, Is.True);
        Assert.That((await Within(second)).Success, Is.True);

        RegistrationResult fourth = await Within(service.RegisterAsync(Request("user_four", name: "NameFour"), CancellationToken.None));
        Assert.That(fourth.Success, Is.True);
        Assert.That(_repo.RegisterCalls, Has.Count.EqualTo(3));
    }

    // AC-4 (EC-AUTH-4 / EC-AUTH-10)

    [Test]
    public async Task RegisterAsync_RepositoryServerErrorOutcome_ReturnsServerErrorWithOneCritical()
    {
        _repo.Outcome = RegistrationOutcome.Failed(RegistrationError.ServerError);

        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(RegistrationError.ServerError));
        AssertOneInfrastructureCritical();
    }

    [Test]
    public async Task RegisterAsync_RepositoryThrows_ReturnsServerErrorWithOneCritical()
    {
        _repo.Throws = new NpgsqlException("connection reset by peer");

        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(RegistrationError.ServerError));
        AssertOneInfrastructureCritical();
    }

    [TestCase(RegistrationError.UsernameTaken)]
    [TestCase(RegistrationError.CharacterNameTaken)]
    public async Task RegisterAsync_RepositoryUniquenessOutcome_PassesThroughWithoutCritical(RegistrationError error)
    {
        _repo.Outcome = RegistrationOutcome.Failed(error);

        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(error));
        Assert.That(_logs.Entries, Is.Empty);
    }

    // AC-5

    [Test]
    public async Task RegisterAsync_HasherThrows_ReturnsServerErrorWithOneCriticalAndReturnsPermit()
    {
        _hasher.Throws = new InvalidOperationException("hasher exploded");
        AuthenticationService service = Service(maxPending: 1);

        RegistrationResult failed = await service.RegisterAsync(Request(), CancellationToken.None);

        Assert.That(failed.ErrorCode, Is.EqualTo(RegistrationError.ServerError));
        Assert.That(_repo.RegisterCalls, Is.Empty);
        AssertOneInfrastructureCritical();

        _hasher.Throws = null;
        RegistrationResult next = await service.RegisterAsync(Request(), CancellationToken.None);
        Assert.That(next.Success, Is.True);
    }

    [Test]
    public async Task RegisterAsync_CancelledWhileHashBlocked_ThrowsWithoutRepositoryAndReturnsPermit()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _hasher.BlockOn = release;
        AuthenticationService service = Service(maxPending: 1);
        using CancellationTokenSource cts = new();

        Task<RegistrationResult> pending = service.RegisterAsync(Request(), cts.Token);
        await WaitUntil(() => _hasher.CallCount == 1);
        cts.Cancel();
        release.SetResult();

        Task finished = await Task.WhenAny(pending, Task.Delay(HANG_GUARD));
        Assert.That(finished, Is.SameAs(pending));
        Assert.That(pending.IsCanceled, Is.True);
        Assert.That(_repo.RegisterCalls, Is.Empty);

        _hasher.BlockOn = null;
        RegistrationResult next = await Within(service.RegisterAsync(Request(), CancellationToken.None));
        Assert.That(next.Success, Is.True);
    }

    [Test]
    public async Task RegisterAsync_AlreadyCancelled_ThrowsBeforeGateAndHash()
    {
        AuthenticationService service = Service(maxPending: 1);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(() => service.RegisterAsync(Request(), cts.Token));

        Assert.That(_hasher.CallCount, Is.EqualTo(0));
        Assert.That(_repo.RegisterCalls, Is.Empty);

        RegistrationResult next = await Within(service.RegisterAsync(Request(), CancellationToken.None));
        Assert.That(next.Success, Is.True);
    }

    [Test]
    public async Task RegisterAsync_HasherThrowsCancellationWithoutRequestCancel_ReturnsServerErrorWithOneCritical()
    {
        _hasher.Throws = new OperationCanceledException("hasher-internal cancellation");
        AuthenticationService service = Service(maxPending: 1);

        RegistrationResult result = await service.RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.ErrorCode, Is.EqualTo(RegistrationError.ServerError));
        Assert.That(_repo.RegisterCalls, Is.Empty);
        AssertOneInfrastructureCritical();

        _hasher.Throws = null;
        Assert.That((await Within(service.RegisterAsync(Request(), CancellationToken.None))).Success, Is.True);
    }

    // AC-AUTH-12 (c, d): structural

    [Test]
    public async Task RegisterAsync_Success_MakesNoZoneLinkCalls()
    {
        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(_link.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RegisterAsync_Failure_MakesNoZoneLinkCalls()
    {
        _repo.Outcome = RegistrationOutcome.Failed(RegistrationError.UsernameTaken);

        RegistrationResult result = await Service().RegisterAsync(Request(), CancellationToken.None);

        Assert.That(result.Success, Is.False);
        Assert.That(_link.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void RegistrationResult_HasNoTokenField()
    {
        foreach (FieldInfo field in typeof(RegistrationResult).GetFields())
        {
            Assert.That(field.Name, Does.Not.Contain("Token"));
        }
    }

    // AuthHashGate

    [TestCase(0)]
    [TestCase(-1)]
    public void AuthHashGate_NonPositiveMax_Throws(int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthHashGate(max));
    }

    [Test]
    public void AuthHashGate_TryEnterWhenFull_ReturnsFalseWithoutBlocking()
    {
        using AuthHashGate gate = new(2);

        Assert.That(gate.TryEnter(), Is.True);
        Assert.That(gate.TryEnter(), Is.True);
        Assert.That(gate.TryEnter(), Is.False);
    }

    [Test]
    public void AuthHashGate_Exit_ReturnsPermit()
    {
        using AuthHashGate gate = new(1);
        Assert.That(gate.TryEnter(), Is.True);
        Assert.That(gate.TryEnter(), Is.False);

        gate.Exit();

        Assert.That(gate.TryEnter(), Is.True);
    }

    private void AssertOneInfrastructureCritical()
    {
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo("IronGrind.Gateway.Infrastructure"));
        foreach (LogEntry entry in _logs.Entries)
        {
            string text = entry.Message + " " + entry.Exception;
            Assert.That(text, Does.Not.Contain(PASSWORD));
            Assert.That(text, Does.Not.Contain(FAKE_PHC));
        }
    }

    // Fakes

    private sealed class StrictZoneLink : IZoneLink
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Registration must not call the zone link.");
        }

        public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Registration must not call the zone link.");
        }

        public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            throw new InvalidOperationException("Registration must not call the zone link.");
        }
    }

    private sealed class StrictZoneMap : IAccountZoneMap
    {
        public bool TryGetZone(AccountID account, out ZoneID zone) => throw new NotSupportedException();

        public bool IsLinkUp(ZoneID zone) => throw new NotSupportedException();

        public void SetSession(AccountID account, ZoneID zone) => throw new NotSupportedException();

        public bool Remove(AccountID account, ZoneID fromZone) => throw new NotSupportedException();
    }

    private sealed class StrictZoneRouter : IZoneRouter
    {
        public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port) => throw new NotSupportedException();

        public bool TryGetPort(ZoneID zone, out ushort port) => throw new NotSupportedException();
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        internal readonly record struct RegisterCall(string UsernameLower, string Phc, string CharacterName, byte ClassType, CancellationToken Token);

        private readonly object _lock = new();
        private readonly List<RegisterCall> _calls = new();

        public RegistrationOutcome Outcome { get; set; } = RegistrationOutcome.Succeeded(new AccountID(1u), new CharacterID(1u));

        public Exception? Throws { get; set; }

        public IReadOnlyList<RegisterCall> RegisterCalls
        {
            get
            {
                lock (_lock)
                {
                    return _calls.ToList();
                }
            }
        }

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct)
        {
            lock (_lock)
            {
                _calls.Add(new RegisterCall(usernameLower, passwordHashPhc, characterName, classType, ct));
            }

            if (Throws is not null)
            {
                throw Throws;
            }

            return Task.FromResult(Outcome);
        }

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct) => throw new NotSupportedException();

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct) => throw new NotSupportedException();

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        private readonly string _phc;
        private int _callCount;

        public FakePasswordHasher(string phc)
        {
            _phc = phc;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public TaskCompletionSource? BlockOn { get; set; }

        public Exception? Throws { get; set; }

        public string Hash(string password)
        {
            Interlocked.Increment(ref _callCount);
            TaskCompletionSource? block = BlockOn;
            if (block is not null)
            {
                block.Task.GetAwaiter().GetResult();
            }

            if (Throws is not null)
            {
                throw Throws;
            }

            return _phc;
        }

        public bool Verify(string password, string phc) => throw new NotSupportedException();
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

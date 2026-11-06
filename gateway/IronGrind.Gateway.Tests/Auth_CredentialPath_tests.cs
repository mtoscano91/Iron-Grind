using System.Collections.Concurrent;
using System.Diagnostics;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>Authentication Story 009: login credential path (AC-AUTH-10, 17, 19 a, 20, 29 i; EC-AUTH-7).</summary>
[TestFixture]
internal sealed class Auth_CredentialPath_tests
{
    private const string PASSWORD = "correct horse";
    private const string WRONG_PASSWORD = "battery staple";
    private const string USERNAME = "alice";
    private static readonly DateTime NOW = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan HANG_GUARD = TimeSpan.FromSeconds(10);
    private static readonly AccountID ALICE_ID = new(7u);

    private FakeAccountRepository _repo = null!;
    private FakePasswordHasher _hasher = null!;
    private FakeTimeProvider _time = null!;
    private RecordingLoggerFactory _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(NOW);
        _repo = new FakeAccountRepository(new LockoutPolicy(new AuthOptions()));
        _hasher = new FakePasswordHasher();
        _logs = new RecordingLoggerFactory();
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 0, null));
    }

    private CredentialCheck Check(int maxPending = 10, IAuthHashGate? gate = null)
    {
        return new CredentialCheck(_repo, _hasher, gate ?? new AuthHashGate(maxPending), new DummyHash(_hasher), _time, _logs);
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

    // AC-1 (AC-AUTH-10)

    [TestCase("alice")]
    [TestCase("Alice")]
    [TestCase("ALICE")]
    public async Task RunAsync_UsernameAnyCase_PassesWithSameAccountID(string typed)
    {
        CredentialOutcome outcome = await Check().RunAsync(typed, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.Passed));
        Assert.That(outcome.AccountID, Is.EqualTo(ALICE_ID));
    }

    // AC-2 (AC-AUTH-17)

    [Test]
    public async Task RunAsync_CountFourAndCorrectPassword_ResetsBeforeReturning()
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 4, null));

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.Passed));
        Assert.That(_repo.ResetCalls, Has.Count.EqualTo(1));
        Assert.That(_repo.ResetCalls[0], Is.EqualTo(ALICE_ID));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.Get(USERNAME).LoginFailureCount, Is.EqualTo(0));
        Assert.That(_repo.Get(USERNAME).LockoutUntilUtc, Is.Null);
    }

    // AC-3 (AC-AUTH-19 a). The < 10 ms check is deliberately wall-clock (Stopwatch) after a warm-up call on another
    // locked account; it guards against a hash or a blocking call on the locked path, not against CI load. The
    // end-to-end measurement against a local database is Story 012.

    [Test]
    public async Task RunAsync_LockedAccount_ReturnsInvalidFastWithoutHashAndRecordsOnce()
    {
        _repo.Add("bob", new AccountRecord(new AccountID(8u), FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        CredentialCheck check = Check();
        await check.RunAsync("bob", PASSWORD, CancellationToken.None);

        Stopwatch watch = Stopwatch.StartNew();
        CredentialOutcome outcome = await check.RunAsync(USERNAME, PASSWORD, CancellationToken.None);
        watch.Stop();

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.InvalidCredentials));
        Assert.That(outcome.AccountID, Is.EqualTo(default(AccountID)));
        Assert.That(watch.Elapsed.TotalMilliseconds, Is.LessThan(10));
        Assert.That(_hasher.VerifyCount, Is.EqualTo(0));
        Assert.That(_repo.RecordCalls.Count(c => c.Account == ALICE_ID), Is.EqualTo(1));
        Assert.That(_repo.RecordCalls.First(c => c.Account == ALICE_ID).UtcNow, Is.EqualTo(NOW));
    }

    [Test]
    public async Task RunAsync_LockoutExactlyExpired_HashesAndPasses()
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW));

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.Passed));
        Assert.That(_hasher.VerifyCount, Is.EqualTo(1));
    }

    // AC-4 (AC-AUTH-20)

    [TestCase(9)]
    [TestCase(50)]
    public async Task RunAsync_WrongPasswordAtHighCount_RecordsFailureWithClockAndLocksSixHundredSeconds(int count)
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, count, null));

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, WRONG_PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.InvalidCredentials));
        Assert.That(_repo.RecordCalls, Has.Count.EqualTo(1));
        Assert.That(_repo.RecordCalls[0].UtcNow, Is.EqualTo(NOW));
        Assert.That(_repo.Get(USERNAME).LoginFailureCount, Is.EqualTo(count + 1));
        Assert.That(_repo.Get(USERNAME).LockoutUntilUtc, Is.EqualTo(NOW.AddSeconds(600)));
        Assert.That(_repo.ResetCalls, Is.Empty);
    }

    // AC-5 (EC-AUTH-7)

    [Test]
    public async Task RunAsync_BannedAccount_ReturnsInvalidWithoutHashOrWrites()
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), true, 2, null));

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.InvalidCredentials));
        Assert.That(_hasher.VerifyCount, Is.EqualTo(0));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
        Assert.That(_repo.Get(USERNAME).LoginFailureCount, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_AllCredentialFailureKinds_ReturnIdenticalOutcome()
    {
        _repo.Add("user_l", new AccountRecord(new AccountID(2u), FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        _repo.Add("user_b", new AccountRecord(new AccountID(3u), FakePasswordHasher.PhcFor(PASSWORD), true, 0, null));
        CredentialCheck check = Check();

        CredentialOutcome unknown = await check.RunAsync("nobody", PASSWORD, CancellationToken.None);
        CredentialOutcome wrong = await check.RunAsync(USERNAME, WRONG_PASSWORD, CancellationToken.None);
        CredentialOutcome locked = await check.RunAsync("user_l", PASSWORD, CancellationToken.None);
        CredentialOutcome banned = await check.RunAsync("user_b", PASSWORD, CancellationToken.None);

        Assert.That(unknown, Is.EqualTo(new CredentialOutcome(CredentialResult.InvalidCredentials, default)));
        Assert.That(wrong, Is.EqualTo(unknown));
        Assert.That(locked, Is.EqualTo(unknown));
        Assert.That(banned, Is.EqualTo(unknown));
    }

    [Test]
    public async Task RunAsync_UnknownUsername_VerifiesAgainstDummyHashAndWritesNothing()
    {
        CredentialCheck check = Check();
        int hashesAtSetup = _hasher.VerifyCount;

        CredentialOutcome outcome = await check.RunAsync("nobody", PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.InvalidCredentials));
        Assert.That(_hasher.VerifyCount - hashesAtSetup, Is.EqualTo(1));
        Assert.That(_hasher.LastVerifiedPhc, Is.Not.EqualTo(FakePasswordHasher.PhcFor(PASSWORD)));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
    }

    // AC-7 (AC-AUTH-29 i)

    [Test]
    public async Task RunAsync_FindThrows_ReturnsServerErrorWithNoWritesAndOneCritical()
    {
        _repo.FindThrows = new NpgsqlException("connection refused");

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
        Assert.That(_hasher.VerifyCount, Is.EqualTo(0));
        AssertOneInfrastructureCritical();
    }

    // AC-8

    [TestCase(USERNAME)]
    [TestCase("nobody")]
    public async Task RunAsync_GateFull_ReturnsServerErrorWithoutHashOrWrite(string username)
    {
        using AuthHashGate gate = new(1);
        CredentialCheck check = Check(gate: gate);
        Assert.That(gate.TryEnter(), Is.True);

        CredentialOutcome outcome = await check.RunAsync(username, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        Assert.That(_hasher.VerifyCount, Is.EqualTo(0));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
        Assert.That(_logs.Entries.Where(e => e.Level == LogLevel.Critical), Is.Empty);

        gate.Exit();
        Assert.That((await check.RunAsync(USERNAME, PASSWORD, CancellationToken.None)).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [Test]
    public async Task RunAsync_GateHeldByBlockedHash_NextCallIsServerErrorThenPermitsReturn()
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CredentialCheck check = Check(maxPending: 1);
        int baseline = _hasher.VerifyCount;
        _hasher.BlockOn = release;

        Task<CredentialOutcome> first = check.RunAsync(USERNAME, PASSWORD, CancellationToken.None);
        await WaitUntil(() => _hasher.VerifyCount == baseline + 1);
        CredentialOutcome second = await Within(check.RunAsync("nobody", PASSWORD, CancellationToken.None));

        Assert.That(second.Result, Is.EqualTo(CredentialResult.ServerError));
        release.SetResult();
        Assert.That((await Within(first)).Result, Is.EqualTo(CredentialResult.Passed));
    }

    // AC-9: errors

    [TestCase(USERNAME)]
    [TestCase("nobody")]
    public async Task RunAsync_HasherThrows_ReturnsServerErrorWithOneCriticalAndReturnsPermit(string username)
    {
        CredentialCheck check = Check(maxPending: 1);
        _hasher.Throws = new InvalidOperationException("hasher exploded");

        CredentialOutcome outcome = await check.RunAsync(username, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
        AssertOneInfrastructureCritical();

        _hasher.Throws = null;
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [Test]
    public async Task RunAsync_HasherThrowsInternalCancellation_ReturnsServerErrorWithOneCritical()
    {
        CredentialCheck check = Check(maxPending: 1);
        _hasher.Throws = new OperationCanceledException("hasher-internal cancellation");

        CredentialOutcome outcome = await check.RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        AssertOneInfrastructureCritical();
        _hasher.Throws = null;
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [Test]
    public async Task RunAsync_RecordFailureThrows_ReturnsServerErrorWithOneCriticalAndReturnsPermit()
    {
        CredentialCheck check = Check(maxPending: 1);
        _repo.RecordThrows = new NpgsqlException("write failed");

        CredentialOutcome outcome = await check.RunAsync(USERNAME, WRONG_PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        AssertOneInfrastructureCritical();
        _repo.RecordThrows = null;
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [Test]
    public async Task RunAsync_RecordFailureThrowsOnLockedPath_ReturnsServerErrorWithOneCritical()
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        _repo.RecordThrows = new NpgsqlException("write failed");

        CredentialOutcome outcome = await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        AssertOneInfrastructureCritical();
    }

    [Test]
    public async Task RunAsync_ResetFailuresThrows_ReturnsServerErrorWithOneCriticalAndReturnsPermit()
    {
        CredentialCheck check = Check(maxPending: 1);
        _repo.ResetThrows = new NpgsqlException("write failed");

        CredentialOutcome outcome = await check.RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(outcome.Result, Is.EqualTo(CredentialResult.ServerError));
        Assert.That(outcome.AccountID, Is.EqualTo(default(AccountID)));
        AssertOneInfrastructureCritical();
        _repo.ResetThrows = null;
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    // AC-9: cancellation and tokens

    [TestCase(USERNAME)]
    [TestCase("nobody")]
    public async Task RunAsync_AlreadyCancelled_ThrowsBeforeGateAndHash(string username)
    {
        CredentialCheck check = Check(maxPending: 1);
        using CancellationTokenSource cts = new();
        cts.Cancel();
        int baseline = _hasher.VerifyCount;

        Assert.ThrowsAsync<OperationCanceledException>(() => check.RunAsync(username, PASSWORD, cts.Token));

        Assert.That(_hasher.VerifyCount, Is.EqualTo(baseline));
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [TestCase(USERNAME, PASSWORD)]
    [TestCase(USERNAME, WRONG_PASSWORD)]
    [TestCase("nobody", PASSWORD)]
    public async Task RunAsync_CancelledWhileHashBlocked_ThrowsWithoutWritesAndReturnsPermit(string username, string password)
    {
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CredentialCheck check = Check(maxPending: 1);
        int baseline = _hasher.VerifyCount;
        _hasher.BlockOn = release;
        using CancellationTokenSource cts = new();

        Task<CredentialOutcome> pending = check.RunAsync(username, password, cts.Token);
        await WaitUntil(() => _hasher.VerifyCount == baseline + 1);
        cts.Cancel();
        release.SetResult();

        Task finished = await Task.WhenAny(pending, Task.Delay(HANG_GUARD));
        Assert.That(finished, Is.SameAs(pending));
        Assert.That(pending.IsCanceled, Is.True);
        Assert.That(_repo.RecordCalls, Is.Empty);
        Assert.That(_repo.ResetCalls, Is.Empty);

        _hasher.BlockOn = null;
        Assert.That((await Within(check.RunAsync(USERNAME, PASSWORD, CancellationToken.None))).Result, Is.EqualTo(CredentialResult.Passed));
    }

    [Test]
    public async Task RunAsync_CancellableToken_FindReceivesTokenAndWritesReceiveNone()
    {
        _repo.Add("carol", new AccountRecord(new AccountID(9u), FakePasswordHasher.PhcFor(PASSWORD), false, 0, null));
        CredentialCheck check = Check();
        using CancellationTokenSource cts = new();

        await check.RunAsync("carol", WRONG_PASSWORD, cts.Token);
        await check.RunAsync("carol", PASSWORD, cts.Token);

        Assert.That(_repo.FindTokens, Is.All.EqualTo(cts.Token));
        Assert.That(_repo.RecordCalls[0].Token, Is.EqualTo(CancellationToken.None));
        Assert.That(_repo.ResetTokens, Has.Count.EqualTo(1));
        Assert.That(_repo.ResetTokens[0], Is.EqualTo(CancellationToken.None));
    }

    [Test]
    public async Task RunAsync_LockedPathWithCancellableToken_RecordFailureReceivesNone()
    {
        _repo.Add(USERNAME, new AccountRecord(ALICE_ID, FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        using CancellationTokenSource cts = new();

        await Check().RunAsync(USERNAME, PASSWORD, cts.Token);

        Assert.That(_repo.RecordCalls[0].Token, Is.EqualTo(CancellationToken.None));
    }

    // Logging

    [Test]
    public async Task RunAsync_KnownAccountFailures_LogInformationWithAccountIDAndNoSecrets()
    {
        _repo.Add("user_l", new AccountRecord(new AccountID(2u), FakePasswordHasher.PhcFor(PASSWORD), false, 5, NOW.AddSeconds(30)));
        _repo.Add("user_b", new AccountRecord(new AccountID(3u), FakePasswordHasher.PhcFor(PASSWORD), true, 0, null));
        CredentialCheck check = Check();

        await check.RunAsync(USERNAME, WRONG_PASSWORD, CancellationToken.None);
        await check.RunAsync("user_l", PASSWORD, CancellationToken.None);
        await check.RunAsync("user_b", PASSWORD, CancellationToken.None);

        Assert.That(_logs.Entries, Has.Count.EqualTo(3));
        foreach (LogEntry entry in _logs.Entries)
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Category, Does.EndWith("CredentialCheck"));
        }

        Assert.That(_logs.Entries[0].Message, Does.Contain(ALICE_ID.ToString()));
        Assert.That(_logs.Entries[1].Message, Does.Contain(new AccountID(2u).ToString()));
        Assert.That(_logs.Entries[2].Message, Does.Contain(new AccountID(3u).ToString()));
        AssertNoSecrets(USERNAME, "user_l", "user_b");
    }

    [Test]
    public async Task RunAsync_UnknownUsername_LogsInformationWithoutAnyIdentifier()
    {
        await Check().RunAsync("nobody", PASSWORD, CancellationToken.None);

        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(_logs.Entries[0].Category, Does.EndWith("CredentialCheck"));
        Assert.That(_logs.Entries[0].Message, Does.Not.Contain("AccountID"));
        AssertNoSecrets("nobody");
    }

    [Test]
    public async Task RunAsync_Success_LogsNothing()
    {
        await Check().RunAsync(USERNAME, PASSWORD, CancellationToken.None);

        Assert.That(_logs.Entries, Is.Empty);
    }

    private void AssertNoSecrets(params string[] usernames)
    {
        foreach (LogEntry entry in _logs.Entries)
        {
            string text = entry.Message + " " + entry.Exception;
            Assert.That(text, Does.Not.Contain(PASSWORD));
            Assert.That(text, Does.Not.Contain(WRONG_PASSWORD));
            Assert.That(text, Does.Not.Contain("PHC:"));
            foreach (string username in usernames)
            {
                Assert.That(text, Does.Not.Contain(username));
            }
        }
    }

    private void AssertOneInfrastructureCritical()
    {
        Assert.That(_logs.Entries, Has.Count.EqualTo(1));
        Assert.That(_logs.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
        Assert.That(_logs.Entries[0].Category, Is.EqualTo(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY));
        AssertNoSecrets();
    }

    // Fakes

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTime utcNow)
        {
            _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        internal readonly record struct RecordCall(AccountID Account, DateTime UtcNow, CancellationToken Token);

        private readonly object _lock = new();
        private readonly LockoutPolicy _policy;
        private readonly Dictionary<string, AccountRecord> _byName = new();
        private readonly List<RecordCall> _recordCalls = new();
        private readonly List<AccountID> _resetCalls = new();
        private readonly List<CancellationToken> _resetTokens = new();
        private readonly List<CancellationToken> _findTokens = new();

        public FakeAccountRepository(LockoutPolicy policy)
        {
            _policy = policy;
        }

        public Exception? FindThrows { get; set; }

        public Exception? RecordThrows { get; set; }

        public Exception? ResetThrows { get; set; }

        public IReadOnlyList<RecordCall> RecordCalls => Snapshot(_recordCalls);

        public IReadOnlyList<AccountID> ResetCalls => Snapshot(_resetCalls);

        public IReadOnlyList<CancellationToken> ResetTokens => Snapshot(_resetTokens);

        public IReadOnlyList<CancellationToken> FindTokens => Snapshot(_findTokens);

        public void Add(string usernameLower, AccountRecord record)
        {
            lock (_lock)
            {
                _byName[usernameLower] = record;
            }
        }

        public AccountRecord Get(string usernameLower)
        {
            lock (_lock)
            {
                return _byName[usernameLower];
            }
        }

        public Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct)
        {
            lock (_lock)
            {
                _findTokens.Add(ct);
            }

            if (FindThrows is not null)
            {
                throw FindThrows;
            }

            lock (_lock)
            {
                return Task.FromResult(_byName.TryGetValue(usernameLower, out AccountRecord? record) ? record : null);
            }
        }

        public Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
        {
            lock (_lock)
            {
                _recordCalls.Add(new RecordCall(account, utcNow, ct));
            }

            if (RecordThrows is not null)
            {
                throw RecordThrows;
            }

            lock (_lock)
            {
                string key = _byName.First(p => p.Value.AccountID == account).Key;
                AccountRecord current = _byName[key];
                int count = current.LoginFailureCount + 1;
                DateTime? until = _policy.LockoutUntil(count, utcNow);
                _byName[key] = current with { LoginFailureCount = count, LockoutUntilUtc = until };
                return Task.FromResult(new LoginFailureState(count, until));
            }
        }

        public Task ResetFailuresAsync(AccountID account, CancellationToken ct)
        {
            lock (_lock)
            {
                _resetCalls.Add(account);
                _resetTokens.Add(ct);
            }

            if (ResetThrows is not null)
            {
                throw ResetThrows;
            }

            lock (_lock)
            {
                string key = _byName.First(p => p.Value.AccountID == account).Key;
                _byName[key] = _byName[key] with { LoginFailureCount = 0, LockoutUntilUtc = null };
            }

            return Task.CompletedTask;
        }

        public Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct) => throw new NotSupportedException();

        public Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();

        public Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct) => throw new NotSupportedException();

        private IReadOnlyList<T> Snapshot<T>(List<T> source)
        {
            lock (_lock)
            {
                return source.ToList();
            }
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        private int _verifyCount;
        private string? _lastVerifiedPhc;

        public static string PhcFor(string password) => "PHC:" + password;

        public int VerifyCount => Volatile.Read(ref _verifyCount);

        public string? LastVerifiedPhc => Volatile.Read(ref _lastVerifiedPhc);

        public TaskCompletionSource? BlockOn { get; set; }

        public Exception? Throws { get; set; }

        public string Hash(string password) => PhcFor(password);

        public bool Verify(string password, string phc)
        {
            Volatile.Write(ref _lastVerifiedPhc, phc);
            Interlocked.Increment(ref _verifyCount);
            TaskCompletionSource? block = BlockOn;
            if (block is not null)
            {
                block.Task.GetAwaiter().GetResult();
            }

            if (Throws is not null)
            {
                throw Throws;
            }

            return phc == PhcFor(password);
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
}

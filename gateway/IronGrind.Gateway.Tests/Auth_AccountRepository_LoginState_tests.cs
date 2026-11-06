using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using IronGrind.Gateway.Config;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 007 (ADR-016 Decision 5): proves the login-state methods of
/// <see cref="NpgsqlAccountRepository"/> against a real PostgreSQL. Same environment variables, dedicated test
/// database and refusal rules as Story 006 (see <see cref="AuthTestDatabase"/>). Without the variables every test is ignored.
/// </summary>
/// <example>
/// <c>dotnet test gateway/IronGrind.Gateway.sln --filter "FullyQualifiedName~Auth_AccountRepository_LoginState_tests"</c>
/// </example>
[TestFixture]
[Category("Database")]
internal sealed class Auth_AccountRepository_LoginState_tests
{
    private const string FAKE_PHC = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHQ$aGFzaGhhc2g";
    private const string CHARACTER_NAME = "Grim_Iron7";
    private const byte CLASS_ONE = 1;

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private AuthTestDatabase _db = null!;
    private NpgsqlAccountRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = await AuthTestDatabase.CreateAsync();
        _repository = new NpgsqlAccountRepository(
            _db.DataSource,
            new LockoutPolicy(new AuthOptions()),
            NoRegistrationFaultHook.Instance,
            new RecordingLogger<NpgsqlAccountRepository>());
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_db != null)
        {
            await _db.DisposeAsync();
            _db = null!;
        }
    }

    private async Task<RegistrationOutcome> RegisterAsync(string username = "alice", string characterName = CHARACTER_NAME)
    {
        RegistrationOutcome outcome = await _repository.RegisterAsync(username, FAKE_PHC, characterName, CLASS_ONE, CancellationToken.None);
        Assert.That(outcome.Success, Is.True);
        return outcome;
    }

    private Task SetStateAsync(AccountID account, int count, DateTime? lockout) =>
        _db.AdminExecuteAsync(
            "UPDATE accounts SET login_failure_count = @c, lockout_until_utc = @l WHERE account_id = @id",
            ("c", count),
            ("l", (object?)lockout ?? DBNull.Value),
            ("id", (long)account.RawValue));

    private async Task<(long Count, DateTime? Lockout)> ReadStateAsync(AccountID account)
    {
        long count = await _db.AdminCountAsync("SELECT login_failure_count FROM accounts WHERE account_id = @id", ("id", (long)account.RawValue));
        object? lockout = await _db.AdminScalarAsync("SELECT lockout_until_utc FROM accounts WHERE account_id = @id", ("id", (long)account.RawValue));
        return (count, (DateTime?)lockout);
    }

    [Test]
    public async Task FindByUsernameAsync_RegisteredAccount_ReturnsRecord()
    {
        RegistrationOutcome outcome = await RegisterAsync();

        AccountRecord? record = await _repository.FindByUsernameAsync("alice", CancellationToken.None);

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.AccountID, Is.EqualTo(outcome.AccountID));
        Assert.That(record.AccountID.RawValue, Is.EqualTo(1u));
        Assert.That(record.PasswordHashPhc, Is.EqualTo(FAKE_PHC));
        Assert.That(record.LoginFailureCount, Is.EqualTo(0));
        Assert.That(record.IsBanned, Is.False);
        Assert.That(record.LockoutUntilUtc, Is.Null);
    }

    [Test]
    public async Task FindByUsernameAsync_UnknownUsername_ReturnsNull()
    {
        await RegisterAsync();

        AccountRecord? record = await _repository.FindByUsernameAsync("nobody", CancellationToken.None);

        Assert.That(record, Is.Null);
    }

    [Test]
    public async Task FindByUsernameAsync_BannedAndLockedAccount_ReturnsStoredValuesWithUtcKind()
    {
        RegistrationOutcome outcome = await RegisterAsync();
        DateTime lockout = T0.AddSeconds(30);
        await _db.AdminExecuteAsync("UPDATE accounts SET is_banned = true WHERE account_id = 1");
        await SetStateAsync(outcome.AccountID, 3, lockout);

        AccountRecord? record = await _repository.FindByUsernameAsync("alice", CancellationToken.None);

        Assert.That(record, Is.Not.Null);
        Assert.That(record!.IsBanned, Is.True);
        Assert.That(record.LoginFailureCount, Is.EqualTo(3));
        Assert.That(record.LockoutUntilUtc, Is.EqualTo(lockout));
        Assert.That(record.LockoutUntilUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public async Task RecordFailureAsync_DuringLockout_IncrementsAndReplacesLockout()
    {
        RegistrationOutcome outcome = await RegisterAsync();
        await SetStateAsync(outcome.AccountID, 5, T0.AddSeconds(30));
        DateTime now = T0.AddSeconds(1);

        LoginFailureState state = await _repository.RecordFailureAsync(outcome.AccountID, now, CancellationToken.None);

        DateTime expected = now.AddSeconds(60);
        Assert.That(state.LoginFailureCount, Is.EqualTo(6));
        Assert.That(state.LockoutUntilUtc, Is.EqualTo(expected));
        var (count, lockout) = await ReadStateAsync(outcome.AccountID);
        Assert.That(count, Is.EqualTo(6));
        Assert.That(lockout, Is.EqualTo(expected));
    }

    [TestCase(0, 1, 0)]
    [TestCase(4, 5, 30)]
    [TestCase(9, 10, 600)]
    [TestCase(50, 51, 600)]
    public async Task RecordFailureAsync_FromCount_WritesNewCountAndLockout(int from, int expectedCount, int expectedSeconds)
    {
        RegistrationOutcome outcome = await RegisterAsync();
        await SetStateAsync(outcome.AccountID, from, null);

        LoginFailureState state = await _repository.RecordFailureAsync(outcome.AccountID, T0, CancellationToken.None);

        DateTime? expectedLockout = expectedSeconds == 0 ? null : T0.AddSeconds(expectedSeconds);
        Assert.That(state.LoginFailureCount, Is.EqualTo(expectedCount));
        Assert.That(state.LockoutUntilUtc, Is.EqualTo(expectedLockout));
        var (count, lockout) = await ReadStateAsync(outcome.AccountID);
        Assert.That(count, Is.EqualTo(expectedCount));
        Assert.That(lockout, Is.EqualTo(expectedLockout));
    }

    [Test]
    public async Task RecordFailureAsync_TenParallelCalls_NoLostUpdate()
    {
        RegistrationOutcome outcome = await RegisterAsync();

        LoginFailureState[] states = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _repository.RecordFailureAsync(outcome.AccountID, T0, CancellationToken.None)));

        var (count, _) = await ReadStateAsync(outcome.AccountID);
        Assert.That(count, Is.EqualTo(10));
        Assert.That(states.Select(s => s.LoginFailureCount).OrderBy(c => c), Is.EqualTo(Enumerable.Range(1, 10)));
    }

    [Test]
    public async Task ResetFailuresAsync_CountAndLockoutSet_ClearsBoth()
    {
        RegistrationOutcome outcome = await RegisterAsync();
        await SetStateAsync(outcome.AccountID, 4, T0.AddSeconds(30));

        await _repository.ResetFailuresAsync(outcome.AccountID, CancellationToken.None);

        var (count, lockout) = await ReadStateAsync(outcome.AccountID);
        Assert.That(count, Is.EqualTo(0));
        Assert.That(lockout, Is.Null);
    }

    [Test]
    public async Task GetCharacterIDAsync_RegisteredAccount_ReturnsRegistrationCharacterID()
    {
        RegistrationOutcome outcome = await RegisterAsync();

        CharacterID id = await _repository.GetCharacterIDAsync(outcome.AccountID, CancellationToken.None);

        Assert.That(id, Is.EqualTo(outcome.CharacterID));
    }

    [Test]
    public async Task GetCharacterIDAsync_AccountWithoutCharacter_ReturnsInvalid()
    {
        AccountID account = await InsertAccountWithoutCharacterAsync();

        CharacterID id = await _repository.GetCharacterIDAsync(account, CancellationToken.None);

        Assert.That(id, Is.EqualTo(CharacterID.Invalid));
    }

    [Test]
    public async Task Schema_AccountsAndCharacterRecords_SeparateCharacterDataFromAccounts()
    {
        var columns = new List<string>();
        await using (var connection = new Npgsql.NpgsqlConnection(_db.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand(
                "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'accounts'",
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.That(
            columns,
            Is.EquivalentTo(new[] { "account_id", "username_lower", "password_hash_phc", "created_at_utc", "is_banned", "login_failure_count", "lockout_until_utc" }));
        Assert.That(
            await _db.AdminCountAsync(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'character_records' AND column_name = 'account_id'"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task GetLastZoneIdAsync_RegisteredAccount_ReturnsInvalidThenStoredZone()
    {
        RegistrationOutcome outcome = await RegisterAsync();

        ZoneID before = await _repository.GetLastZoneIdAsync(outcome.AccountID, CancellationToken.None);
        await _db.AdminExecuteAsync("UPDATE character_records SET last_zone_id = 7");
        ZoneID after = await _repository.GetLastZoneIdAsync(outcome.AccountID, CancellationToken.None);

        Assert.That(before, Is.EqualTo(ZoneID.Invalid));
        Assert.That(after, Is.EqualTo(new ZoneID(7u)));
    }

    [Test]
    public async Task GetLastZoneIdAsync_AccountWithoutCharacter_ReturnsInvalid()
    {
        AccountID account = await InsertAccountWithoutCharacterAsync();

        ZoneID zone = await _repository.GetLastZoneIdAsync(account, CancellationToken.None);

        Assert.That(zone, Is.EqualTo(ZoneID.Invalid));
    }

    [Test]
    public void RecordFailureAsync_UnknownAccount_ThrowsInvalidOperation()
    {
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.RecordFailureAsync(new AccountID(999u), T0, CancellationToken.None));
    }

    [Test]
    public void ResetFailuresAsync_UnknownAccount_ThrowsInvalidOperation()
    {
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _repository.ResetFailuresAsync(new AccountID(999u), CancellationToken.None));
    }

    [TestCase(DateTimeKind.Local)]
    [TestCase(DateTimeKind.Unspecified)]
    public async Task RecordFailureAsync_NonUtcTime_ThrowsArgumentException(DateTimeKind kind)
    {
        RegistrationOutcome outcome = await RegisterAsync();
        DateTime now = DateTime.SpecifyKind(T0, kind);

        Assert.ThrowsAsync<ArgumentException>(
            () => _repository.RecordFailureAsync(outcome.AccountID, now, CancellationToken.None));
    }

    private async Task<AccountID> InsertAccountWithoutCharacterAsync()
    {
        await _db.AdminExecuteAsync(
            "INSERT INTO accounts (account_id, username_lower, password_hash_phc) VALUES (nextval('account_id_seq'), 'nochar', 'x')");
        long id = await _db.AdminCountAsync("SELECT account_id FROM accounts WHERE username_lower = 'nochar'");
        return new AccountID((uint)id);
    }
}

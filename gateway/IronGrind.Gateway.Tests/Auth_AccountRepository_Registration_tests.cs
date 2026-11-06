using IronGrind.Authentication;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Auth;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 006 (ADR-016 Decision 5): proves <see cref="NpgsqlAccountRepository.RegisterAsync"/>
/// against a real PostgreSQL. Needs <c>IRONGRIND_GATEWAY_DB</c> (role gateway, used by the repository) and
/// <c>IRONGRIND_GATEWAY_TEST_ADMIN_DB</c> (owner role, fixture reset and row checks only), both pointing at a
/// dedicated test database such as <c>irongrind_auth_test</c>. The fixture TRUNCATEs, so it refuses to run
/// against a database named <c>irongrind</c>. Without both variables every test is ignored.
/// </summary>
/// <example>
/// <c>dotnet test gateway/IronGrind.Gateway.sln --filter "FullyQualifiedName~Auth_AccountRepository_Registration_tests"</c>
/// </example>
[TestFixture]
[Category("Database")]
internal sealed class Auth_AccountRepository_Registration_tests
{
    private const string FAKE_PHC = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHQ$aGFzaGhhc2g";
    private const string CHARACTER_NAME = "Grim_Iron7";
    private const byte CLASS_ONE = 1;

    private AuthTestDatabase _db = null!;
    private ControllableFaultHook _hook = null!;
    private RecordingLogger<NpgsqlAccountRepository> _logger = null!;
    private NpgsqlAccountRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = await AuthTestDatabase.CreateAsync();
        _hook = new ControllableFaultHook();
        _logger = new RecordingLogger<NpgsqlAccountRepository>();
        _repository = CreateRepository();
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

    private NpgsqlAccountRepository CreateRepository() =>
        new(_db.DataSource, new LockoutPolicy(new AuthOptions()), _hook, _logger);

    private Task<object?> AdminScalarAsync(string sql, params (string Name, object Value)[] parameters) =>
        _db.AdminScalarAsync(sql, parameters);

    private Task<long> AdminCountAsync(string sql, params (string Name, object Value)[] parameters) =>
        _db.AdminCountAsync(sql, parameters);

    private Task<RegistrationOutcome> RegisterAsync(string username, string characterName, byte classType = CLASS_ONE) =>
        _repository.RegisterAsync(username, FAKE_PHC, characterName, classType, CancellationToken.None);

    [Test]
    public async Task RegisterAsync_FreshDatabase_WritesAccountAndCharacterRows()
    {
        RegistrationOutcome outcome = await RegisterAsync("alice", CHARACTER_NAME);

        Assert.That(outcome.Success, Is.True);
        Assert.That(outcome.AccountID.RawValue, Is.EqualTo(1u));
        Assert.That(outcome.CharacterID.RawValue, Is.EqualTo(1u));

        Assert.That(await AdminScalarAsync("SELECT username_lower FROM accounts WHERE account_id = 1"), Is.EqualTo("alice"));
        Assert.That(await AdminScalarAsync("SELECT password_hash_phc FROM accounts WHERE account_id = 1"), Is.EqualTo(FAKE_PHC));
        Assert.That(await AdminScalarAsync("SELECT is_banned FROM accounts WHERE account_id = 1"), Is.EqualTo(false));
        Assert.That(await AdminCountAsync("SELECT login_failure_count FROM accounts WHERE account_id = 1"), Is.EqualTo(0));
        Assert.That(await AdminScalarAsync("SELECT lockout_until_utc FROM accounts WHERE account_id = 1"), Is.Null);
        Assert.That(
            await AdminScalarAsync("SELECT created_at_utc <= now() AND created_at_utc > now() - interval '1 minute' FROM accounts WHERE account_id = 1"),
            Is.EqualTo(true));

        Assert.That(await AdminCountAsync("SELECT account_id FROM character_records WHERE character_id = 1"), Is.EqualTo(1));
        Assert.That(await AdminScalarAsync("SELECT character_name FROM character_records WHERE character_id = 1"), Is.EqualTo(CHARACTER_NAME));
        Assert.That(await AdminCountAsync("SELECT class_type FROM character_records WHERE character_id = 1"), Is.EqualTo(CLASS_ONE));
    }

    [Test]
    public async Task RegisterAsync_FreshDatabase_CharacterOtherColumnsEqualDdlDefaults()
    {
        await RegisterAsync("alice", CHARACTER_NAME);

        var columns = new List<(string Name, string? Default, string Type)>();
        await using (var connection = new NpgsqlConnection(_db.AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT column_name, column_default, udt_name FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = 'character_records' " +
                "AND column_name NOT IN ('character_id', 'account_id', 'character_name', 'class_type') ORDER BY ordinal_position",
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
            }
        }

        Assert.That(columns, Is.Not.Empty);
        foreach (var (name, defaultExpression, udtName) in columns)
        {
            string? stored = (string?)await AdminScalarAsync($"SELECT \"{name}\"::text FROM character_records WHERE character_id = 1");
            if (defaultExpression == null)
            {
                Assert.That(stored, Is.Null, $"column {name} has no default and must be NULL");
            }
            else if (defaultExpression.Contains("now()", StringComparison.OrdinalIgnoreCase)
                     || defaultExpression.Contains("current_timestamp", StringComparison.OrdinalIgnoreCase))
            {
                // A clock default differs per statement: check it was set by the database just now.
                Assert.That(
                    await AdminScalarAsync($"SELECT \"{name}\" <= now() AND \"{name}\" > now() - interval '1 minute' FROM character_records WHERE character_id = 1"),
                    Is.EqualTo(true),
                    $"column {name} must hold a clock default set just now");
            }
            else
            {
                string? expected = (string?)await AdminScalarAsync($"SELECT (({defaultExpression})::\"{udtName}\")::text");
                Assert.That(stored, Is.EqualTo(expected), $"column {name} must hold its DDL default");
            }
        }
    }

    [Test]
    public async Task RegisterAsync_ThreeRegistrationsThenNewRepository_IdsAreMonotonic()
    {
        var ids = new List<uint>();
        var characterIds = new List<uint>();
        foreach (string name in new[] { "user1", "user2", "user3" })
        {
            RegistrationOutcome outcome = await RegisterAsync(name, "Char_" + name);
            Assert.That(outcome.Success, Is.True);
            ids.Add(outcome.AccountID.RawValue);
            characterIds.Add(outcome.CharacterID.RawValue);
        }

        Assert.That(ids, Is.EqualTo(new uint[] { 1, 2, 3 }));
        Assert.That(characterIds, Is.EqualTo(new uint[] { 1, 2, 3 }));

        await _db.RecreateDataSourceAsync();
        _repository = CreateRepository();

        RegistrationOutcome fourth = await RegisterAsync("user4", "Char_user4");

        Assert.That(fourth.Success, Is.True);
        Assert.That(fourth.AccountID.RawValue, Is.EqualTo(4u));
        Assert.That(fourth.CharacterID.RawValue, Is.EqualTo(4u));
    }

    [Test]
    public async Task RegisterAsync_DuplicateUsername_ReturnsUsernameTaken()
    {
        Assert.That((await RegisterAsync("alice", "Alice_One")).Success, Is.True);

        RegistrationOutcome second = await RegisterAsync("alice", "Alice_Two");

        Assert.That(second.Success, Is.False);
        Assert.That(second.Error, Is.EqualTo(RegistrationError.UsernameTaken));
        Assert.That(await AdminCountAsync("SELECT count(*) FROM accounts WHERE username_lower = 'alice'"), Is.EqualTo(1));
    }

    [Test]
    public async Task RegisterAsync_ConcurrentSameUsername_OneSuccessOneUsernameTaken()
    {
        for (int round = 0; round < 5; round++)
        {
            string username = "bob" + round;
            Task<RegistrationOutcome> first = RegisterAsync(username, "BobA_" + round);
            Task<RegistrationOutcome> second = RegisterAsync(username, "BobB_" + round);
            RegistrationOutcome[] outcomes = await Task.WhenAll(first, second);

            Assert.That(outcomes.Count(o => o.Success), Is.EqualTo(1), $"round {round}");
            Assert.That(outcomes.Count(o => !o.Success && o.Error == RegistrationError.UsernameTaken), Is.EqualTo(1), $"round {round}");
            Assert.That(await AdminCountAsync("SELECT count(*) FROM accounts WHERE username_lower = @u", ("u", username)), Is.EqualTo(1), $"round {round}");
        }
    }

    [Test]
    public async Task RegisterAsync_CharacterNameDifferingByCase_ReturnsCharacterNameTakenAndLeavesNoAccount()
    {
        Assert.That((await RegisterAsync("alice", "Grim_Iron7")).Success, Is.True);

        RegistrationOutcome second = await RegisterAsync("carol", "grim_iron7");

        Assert.That(second.Success, Is.False);
        Assert.That(second.Error, Is.EqualTo(RegistrationError.CharacterNameTaken));
        Assert.That(await AdminCountAsync("SELECT count(*) FROM character_records WHERE lower(character_name) = 'grim_iron7'"), Is.EqualTo(1));
        Assert.That(await AdminCountAsync("SELECT count(*) FROM accounts WHERE username_lower = 'carol'"), Is.EqualTo(0));
    }

    [Test]
    public async Task RegisterAsync_FaultBetweenInserts_ReturnsServerErrorRollsBackAndConsumesIds()
    {
        RegistrationOutcome first = await RegisterAsync("alice", "Alice_One");
        Assert.That(first.AccountID.RawValue, Is.EqualTo(1u));
        Assert.That(first.CharacterID.RawValue, Is.EqualTo(1u));

        _hook.ThrowOnNextCall(new InvalidOperationException("injected fault"));
        RegistrationOutcome failed = await RegisterAsync("dave", "Dave_One");

        Assert.That(failed.Success, Is.False);
        Assert.That(failed.Error, Is.EqualTo(RegistrationError.ServerError));
        Assert.That(await AdminCountAsync("SELECT count(*) FROM accounts WHERE username_lower = 'dave'"), Is.EqualTo(0));
        Assert.That(await AdminCountAsync("SELECT count(*) FROM character_records WHERE lower(character_name) = 'dave_one'"), Is.EqualTo(0));

        RegistrationOutcome next = await RegisterAsync("erin", "Erin_One");

        Assert.That(next.Success, Is.True);
        Assert.That(next.AccountID.RawValue, Is.GreaterThan(2u));
        Assert.That(next.CharacterID.RawValue, Is.GreaterThan(2u));
    }

    [TestCase((byte)0)]
    [TestCase((byte)1)]
    public async Task RegisterAsync_PlayableClass_StoresClassType(byte classType)
    {
        RegistrationOutcome outcome = await RegisterAsync("alice", CHARACTER_NAME, classType);

        Assert.That(outcome.Success, Is.True);
        Assert.That(await AdminCountAsync("SELECT class_type FROM character_records WHERE character_id = 1"), Is.EqualTo(classType));
    }

    [Test]
    public async Task RegisterAsync_DatabaseException_LogsOnceWithoutPasswordHash()
    {
        _hook.ThrowOnNextCall(new NpgsqlException("boom"));

        RegistrationOutcome outcome = await RegisterAsync("alice", CHARACTER_NAME);

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Error, Is.EqualTo(RegistrationError.ServerError));
        var errors = _logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0].Message, Does.Not.Contain(FAKE_PHC));
        Assert.That(errors[0].Exception?.ToString() ?? string.Empty, Does.Not.Contain(FAKE_PHC));
    }

    private sealed class ControllableFaultHook : IRegistrationFaultHook
    {
        private Exception? _next;

        public void ThrowOnNextCall(Exception exception) => _next = exception;

        public void BetweenInserts()
        {
            Exception? toThrow = _next;
            _next = null;
            if (toThrow != null)
            {
                throw toThrow;
            }
        }
    }
}

using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 003 (ADR-016 Decision 5): proves migration 004 and the grants of the
/// <c>gateway</c> role against a real PostgreSQL. Needs <c>IRONGRIND_GATEWAY_DB</c> (Npgsql connection
/// string for role gateway); without it every test is ignored. Every test runs in a transaction that is
/// rolled back, so no rows are left behind (sequence values are consumed, which is harmless).
/// </summary>
/// <example>
/// <c>dotnet test gateway/IronGrind.Gateway.sln --filter "TestCategory=Database"</c>
/// </example>
[TestFixture]
[Category("Database")]
internal sealed class Db_Migration004_tests
{
    private const string ENV_VAR = "IRONGRIND_GATEWAY_DB";
    private const string SQLSTATE_INSUFFICIENT_PRIVILEGE = "42501";
    private const string SQLSTATE_FOREIGN_KEY_VIOLATION = "23503";
    private const string SQLSTATE_UNIQUE_VIOLATION = "23505";
    private const string NAME_UNIQUE_CONSTRAINT = "ux_character_records_name_lower";

    private const string INSERT_ACCOUNT_SQL =
        "INSERT INTO accounts (account_id, username_lower, password_hash_phc) VALUES (@id, @user, 'x')";

    private const string INSERT_CHARACTER_SQL =
        "INSERT INTO character_records (character_id, account_id, character_name, class_type) " +
        "VALUES (nextval('character_id_seq'), @account, @name, 0)";

    private NpgsqlConnection _connection = null!;
    private NpgsqlTransaction _transaction = null!;

    [SetUp]
    public async Task SetUp()
    {
        string? connectionString = Environment.GetEnvironmentVariable(ENV_VAR);
        if (string.IsNullOrEmpty(connectionString))
        {
            Assert.Ignore("IRONGRIND_GATEWAY_DB not set — Database tests need the dev PostgreSQL as role gateway (tools/db/README.md)");
        }

        _connection = new NpgsqlConnection(connectionString);
        await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        // Ignored tests never reach SetUp's connection code.
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null!;
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null!;
        }
    }

    private NpgsqlCommand Command(string sql) => new(sql, _connection, _transaction);

    private async Task<long> ScalarLongAsync(string sql)
    {
        await using var command = Command(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> InsertAccountAsync()
    {
        long id = await ScalarLongAsync("SELECT nextval('account_id_seq')");
        await ExecuteAsync(INSERT_ACCOUNT_SQL, ("id", id), ("user", "db004_" + id));
        return id;
    }

    private Task InsertCharacterAsync(long accountId, string name) =>
        ExecuteAsync(INSERT_CHARACTER_SQL, ("account", accountId), ("name", name));

    [TestCase("account_id_seq")]
    [TestCase("character_id_seq")]
    [TestCase("zone_id_seq")]
    public async Task test_nextval_granted_sequence_succeeds(string sequence)
    {
        long value = await ScalarLongAsync($"SELECT nextval('{sequence}')");

        Assert.That(value, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void test_setval_zone_id_seq_fails_insufficient_privilege()
    {
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ScalarLongAsync("SELECT setval('zone_id_seq', 1)"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_INSUFFICIENT_PRIVILEGE));
    }

    [TestCase("SELECT password_hash_phc FROM accounts")]
    [TestCase("SELECT * FROM accounts")]
    [TestCase("SELECT character_id, account_id, last_zone_id FROM character_records")]
    public void test_select_granted_columns_succeeds(string sql)
    {
        // The permission check happens when the query executes, so ExecuteReader is inside the assert.
        Assert.DoesNotThrowAsync(async () =>
        {
            await using var command = Command(sql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
            }
        });
    }

    [Test]
    public async Task test_lock_and_update_account_row_succeeds()
    {
        // Story 007's failure counting: SELECT ... FOR UPDATE then UPDATE, both under "all rights on accounts".
        long accountId = await InsertAccountAsync();

        Assert.DoesNotThrowAsync(async () =>
        {
            await ExecuteAsync("SELECT login_failure_count FROM accounts WHERE account_id = @id FOR UPDATE", ("id", accountId));
            await ExecuteAsync("UPDATE accounts SET login_failure_count = 1 WHERE account_id = @id", ("id", accountId));
        });
        Assert.That(await ScalarLongAsync($"SELECT login_failure_count FROM accounts WHERE account_id = {accountId}"), Is.EqualTo(1));
    }

    [Test]
    public void test_select_pending_purchases_fails_insufficient_privilege()
    {
        // "Nothing else": the role has no rights on tables outside the grant list.
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("SELECT 1 FROM pending_purchases"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_INSUFFICIENT_PRIVILEGE));
    }

    [Test]
    public void test_select_character_name_fails_insufficient_privilege()
    {
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("SELECT character_name FROM character_records"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_INSUFFICIENT_PRIVILEGE));
    }

    [Test]
    public void test_update_character_records_fails_insufficient_privilege()
    {
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("UPDATE character_records SET level = level"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_INSUFFICIENT_PRIVILEGE));
    }

    [Test]
    public void test_delete_character_records_fails_insufficient_privilege()
    {
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await ExecuteAsync("DELETE FROM character_records"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_INSUFFICIENT_PRIVILEGE));
    }

    [Test]
    public void test_insert_character_without_account_fails_foreign_key()
    {
        // 4294967295 (uint.MaxValue) is the sequences' MAXVALUE, so it is never issued as an account_id in dev.
        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertCharacterAsync(4294967295L, "Db004NoAccount"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_FOREIGN_KEY_VIOLATION));
    }

    [Test]
    public async Task test_insert_character_names_differing_by_case_fails_unique_name_index()
    {
        // If dev data ever holds "db004grim" in any case, the second insert may report the wrong constraint.
        long accountId = await InsertAccountAsync();
        await InsertCharacterAsync(accountId, "Db004Grim");

        var ex = Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertCharacterAsync(accountId, "db004grim"));

        Assert.That(ex!.SqlState, Is.EqualTo(SQLSTATE_UNIQUE_VIOLATION));
        Assert.That(ex.ConstraintName, Is.EqualTo(NAME_UNIQUE_CONSTRAINT));
    }
}

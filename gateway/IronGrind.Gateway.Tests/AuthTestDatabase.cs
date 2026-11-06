using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Shared fixture plumbing for the account repository Database tests (Authentication Stories 006, 007).
/// Needs <c>IRONGRIND_GATEWAY_DB</c> (role gateway) and <c>IRONGRIND_GATEWAY_TEST_ADMIN_DB</c> (owner role, reset
/// and row checks only), both on a dedicated test database. Refuses to run against a database named
/// <c>irongrind</c> because it TRUNCATEs.
/// </summary>
internal sealed class AuthTestDatabase : IAsyncDisposable
{
    private const string GATEWAY_ENV_VAR = "IRONGRIND_GATEWAY_DB";
    private const string ADMIN_ENV_VAR = "IRONGRIND_GATEWAY_TEST_ADMIN_DB";
    private const string FORBIDDEN_DATABASE = "irongrind";

    private const string RESET_SQL =
        "TRUNCATE pending_purchases, character_records, accounts; " +
        "ALTER SEQUENCE account_id_seq RESTART; ALTER SEQUENCE character_id_seq RESTART;";

    private readonly string _gatewayConnectionString;

    private AuthTestDatabase(string gatewayConnectionString, string adminConnectionString)
    {
        _gatewayConnectionString = gatewayConnectionString;
        AdminConnectionString = adminConnectionString;
        DataSource = NpgsqlDataSource.Create(gatewayConnectionString);
    }

    /// <summary>Connection string of the owner role (fixture use only).</summary>
    public string AdminConnectionString { get; }

    /// <summary>Pooled data source for role gateway, as the repository uses it.</summary>
    public NpgsqlDataSource DataSource { get; private set; }

    /// <summary>Ignores the test when the variables are unset, refuses a forbidden database, resets the tables.</summary>
    public static async Task<AuthTestDatabase> CreateAsync()
    {
        string? gateway = Environment.GetEnvironmentVariable(GATEWAY_ENV_VAR);
        string? admin = Environment.GetEnvironmentVariable(ADMIN_ENV_VAR);
        if (string.IsNullOrEmpty(gateway) || string.IsNullOrEmpty(admin))
        {
            Assert.Ignore("IRONGRIND_GATEWAY_DB and IRONGRIND_GATEWAY_TEST_ADMIN_DB must both be set — see tools/db/README.md (Gateway Database tests)");
        }

        foreach (string connectionString in new[] { gateway!, admin! })
        {
            string? database = new NpgsqlConnectionStringBuilder(connectionString).Database;
            if (string.Equals(database, FORBIDDEN_DATABASE, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail("Refusing to run: these tests TRUNCATE accounts/character_records and must never point at the 'irongrind' database");
            }
        }

        var db = new AuthTestDatabase(gateway!, admin!);
        await db.AdminExecuteAsync(RESET_SQL);
        return db;
    }

    /// <summary>Disposes the data source and creates a fresh one (new pool, new connections).</summary>
    public async Task RecreateDataSourceAsync()
    {
        await DataSource.DisposeAsync();
        DataSource = NpgsqlDataSource.Create(_gatewayConnectionString);
    }

    /// <summary>Runs a statement as the owner role.</summary>
    public async Task AdminExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a query as the owner role and returns the first column of the first row (null for NULL/no row).</summary>
    public async Task<object?> AdminScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    /// <summary>Runs a query as the owner role and returns the first column as a long.</summary>
    public async Task<long> AdminCountAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(await AdminScalarAsync(sql, parameters));

    /// <summary>Disposes the data source and resets the tables.</summary>
    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await AdminExecuteAsync(RESET_SQL);
    }
}

/// <summary>Logger that records every entry for assertions.</summary>
/// <typeparam name="T">The logging category type.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly object _lock = new();
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

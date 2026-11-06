using Dapper;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountRepository"/> (ADR-016 Decision 5). Registration is one
/// transaction; a unique violation is told apart by constraint name.
/// </summary>
/// <remarks>
/// Database errors are logged with their exception. Npgsql redacts <c>PostgresException.Detail</c> (which can
/// list the failing row, password hash included) unless the connection string sets
/// <c>Include Error Detail=true</c>; the gateway's connection string must never set it (the host rejects it, Story 012).
/// </remarks>
public sealed class NpgsqlAccountRepository : IAccountRepository
{
    private const string ACCOUNTS_USERNAME_UNIQUE = "accounts_username_lower_key";
    private const string CHARACTER_NAME_UNIQUE_INDEX = "ux_character_records_name_lower";

    private const string NEXT_IDS_SQL =
        "SELECT nextval('account_id_seq') AS AccountValue, nextval('character_id_seq') AS CharacterValue";

    private const string INSERT_ACCOUNT_SQL =
        "INSERT INTO accounts (account_id, username_lower, password_hash_phc) VALUES (@accountId, @usernameLower, @passwordHash)";

    private const string INSERT_CHARACTER_SQL =
        "INSERT INTO character_records (character_id, account_id, character_name, class_type) " +
        "VALUES (@characterId, @accountId, @characterName, @classType)";

    private const string FIND_BY_USERNAME_SQL =
        "SELECT account_id AS AccountValue, password_hash_phc AS PasswordHash, is_banned AS IsBanned, " +
        "login_failure_count AS FailureCount, lockout_until_utc AS LockoutUntil " +
        "FROM accounts WHERE username_lower = @usernameLower";

    private const string LOCK_FAILURE_COUNT_SQL =
        "SELECT login_failure_count FROM accounts WHERE account_id = @id FOR UPDATE";

    private const string WRITE_FAILURE_STATE_SQL =
        "UPDATE accounts SET login_failure_count = @count, lockout_until_utc = @lockout WHERE account_id = @id";

    private const string RESET_FAILURES_SQL =
        "UPDATE accounts SET login_failure_count = 0, lockout_until_utc = NULL WHERE account_id = @id";

    private const string SELECT_CHARACTER_ID_SQL =
        "SELECT character_id FROM character_records WHERE account_id = @id";

    private const string SELECT_LAST_ZONE_SQL =
        "SELECT last_zone_id FROM character_records WHERE account_id = @id";

    private readonly NpgsqlDataSource _dataSource;
    private readonly LockoutPolicy _lockoutPolicy;
    private readonly IRegistrationFaultHook _faultHook;
    private readonly ILogger<NpgsqlAccountRepository> _logger;

    /// <summary>Creates the repository.</summary>
    /// <param name="dataSource">Pooled data source for role gateway.</param>
    /// <param name="lockoutPolicy">F-AUTH-1 lockout lookup used by <see cref="RecordFailureAsync"/>.</param>
    /// <param name="faultHook">Seam between the two inserts.</param>
    /// <param name="logger">Logger for database errors.</param>
    public NpgsqlAccountRepository(NpgsqlDataSource dataSource, LockoutPolicy lockoutPolicy, IRegistrationFaultHook faultHook, ILogger<NpgsqlAccountRepository> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _lockoutPolicy = lockoutPolicy ?? throw new ArgumentNullException(nameof(lockoutPolicy));
        _faultHook = faultHook ?? throw new ArgumentNullException(nameof(faultHook));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<RegistrationOutcome> RegisterAsync(string usernameLower, string passwordHashPhc, string characterName, byte classType, CancellationToken ct)
    {
        try
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            IdRow ids = await connection.QuerySingleAsync<IdRow>(
                new CommandDefinition(NEXT_IDS_SQL, transaction: transaction, cancellationToken: ct));
            uint accountId = checked((uint)ids.AccountValue);
            uint characterId = checked((uint)ids.CharacterValue);

            await connection.ExecuteAsync(new CommandDefinition(
                INSERT_ACCOUNT_SQL,
                new { accountId = (long)accountId, usernameLower, passwordHash = passwordHashPhc },
                transaction,
                cancellationToken: ct));

            _faultHook.BetweenInserts();

            await connection.ExecuteAsync(new CommandDefinition(
                INSERT_CHARACTER_SQL,
                new { characterId = (long)characterId, accountId = (long)accountId, characterName, classType = (short)classType },
                transaction,
                cancellationToken: ct));

            await transaction.CommitAsync(ct);
            return RegistrationOutcome.Succeeded(new AccountID(accountId), new CharacterID(characterId));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == ACCOUNTS_USERNAME_UNIQUE)
        {
            return RegistrationOutcome.Failed(RegistrationError.UsernameTaken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == CHARACTER_NAME_UNIQUE_INDEX)
        {
            return RegistrationOutcome.Failed(RegistrationError.CharacterNameTaken);
        }
        catch (Exception ex)
        {
            // Fixed message only: the password hash is never passed to the logger.
            _logger.LogError(ex, "Account registration failed with a database or server error");
            return RegistrationOutcome.Failed(RegistrationError.ServerError);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountRecord?> FindByUsernameAsync(string usernameLower, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        AccountRow? row = await connection.QuerySingleOrDefaultAsync<AccountRow>(
            new CommandDefinition(FIND_BY_USERNAME_SQL, new { usernameLower }, cancellationToken: ct));
        if (row == null)
        {
            return null;
        }

        return new AccountRecord(new AccountID(checked((uint)row.AccountValue)), row.PasswordHash, row.IsBanned, row.FailureCount, row.LockoutUntil);
    }

    /// <inheritdoc/>
    public async Task<LoginFailureState> RecordFailureAsync(AccountID account, DateTime utcNow, CancellationToken ct)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("utcNow must have DateTimeKind.Utc", nameof(utcNow));
        }

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

        int? current = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(LOCK_FAILURE_COUNT_SQL, new { id = (long)account.RawValue }, transaction, cancellationToken: ct));
        if (current == null)
        {
            throw new InvalidOperationException($"No account row for {account}");
        }

        int newCount = current.Value + 1;
        DateTime? lockout = _lockoutPolicy.LockoutUntil(newCount, utcNow);

        await connection.ExecuteAsync(new CommandDefinition(
            WRITE_FAILURE_STATE_SQL,
            new { id = (long)account.RawValue, count = newCount, lockout },
            transaction,
            cancellationToken: ct));

        await transaction.CommitAsync(ct);
        return new LoginFailureState(newCount, lockout);
    }

    /// <inheritdoc/>
    public async Task ResetFailuresAsync(AccountID account, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        int affected = await connection.ExecuteAsync(
            new CommandDefinition(RESET_FAILURES_SQL, new { id = (long)account.RawValue }, cancellationToken: ct));
        if (affected == 0)
        {
            throw new InvalidOperationException($"No account row for {account}");
        }
    }

    /// <inheritdoc/>
    public async Task<CharacterID> GetCharacterIDAsync(AccountID account, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        long? value = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(SELECT_CHARACTER_ID_SQL, new { id = (long)account.RawValue }, cancellationToken: ct));
        return value == null ? CharacterID.Invalid : new CharacterID(checked((uint)value.Value));
    }

    /// <inheritdoc/>
    public async Task<ZoneID> GetLastZoneIdAsync(AccountID account, CancellationToken ct)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        long? value = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(SELECT_LAST_ZONE_SQL, new { id = (long)account.RawValue }, cancellationToken: ct));
        return value == null || value.Value == 0 ? ZoneID.Invalid : new ZoneID(checked((uint)value.Value));
    }

    internal sealed class AccountRow
    {
        public long AccountValue { get; set; }

        public string PasswordHash { get; set; } = string.Empty;

        public bool IsBanned { get; set; }

        public int FailureCount { get; set; }

        public DateTime? LockoutUntil { get; set; }
    }

    internal sealed class IdRow
    {
        public long AccountValue { get; set; }

        public long CharacterValue { get; set; }
    }
}

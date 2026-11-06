using IronGrind.CharacterPersistence;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Login step 1 (CR-AUTH-9 steps 3-9, CR-AUTH-10): hash check, lockout, ban and failure counting against the accounts
/// table. An unknown username still runs Argon2id against the startup dummy hash through the same gate and thread-pool
/// hop as a real verify, so the timing is the same. Locked and banned accounts return without hashing.
/// </summary>
public sealed class CredentialCheck
{
    private static readonly CredentialOutcome INVALID = new(CredentialResult.InvalidCredentials, default);
    private static readonly CredentialOutcome SERVER_ERROR = new(CredentialResult.ServerError, default);

    private readonly IAccountRepository _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly IAuthHashGate _gate;
    private readonly DummyHash _dummy;
    private readonly TimeProvider _time;
    private readonly ILogger _infraLog;
    private readonly ILogger _failLog;

    /// <summary>Creates the credential check.</summary>
    /// <param name="accounts">Account persistence.</param>
    /// <param name="hasher">Password hasher.</param>
    /// <param name="gate">The shared MAX_AUTH_PENDING gate.</param>
    /// <param name="dummy">Startup dummy hash used for unknown usernames.</param>
    /// <param name="time">Clock; its UTC time is passed to the repository for lockout computation.</param>
    /// <param name="loggerFactory">Source of the infrastructure and failure loggers.</param>
    public CredentialCheck(
        IAccountRepository accounts,
        IPasswordHasher hasher,
        IAuthHashGate gate,
        DummyHash dummy,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _dummy = dummy ?? throw new ArgumentNullException(nameof(dummy));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _infraLog = loggerFactory.CreateLogger(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY);
        _failLog = loggerFactory.CreateLogger<CredentialCheck>();
    }

    private enum HashStep
    {
        Matched,
        Mismatched,
        GateFull,
        Failed,
    }

    /// <summary>Runs the credential path for one login attempt.</summary>
    /// <param name="username">Raw username as typed; normalized here.</param>
    /// <param name="password">Raw password; never logged.</param>
    /// <param name="ct">Request token; read by the lookup, checked before the gate and after the hash. Writes ignore it.</param>
    /// <returns>The outcome; <c>AccountID</c> is set only on <see cref="CredentialResult.Passed"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the gate or while hashing.</exception>
    public async Task<CredentialOutcome> RunAsync(string username, string password, CancellationToken ct)
    {
        (bool found, AccountRecord? account) = await FindAsync(UsernameRules.Normalize(username), ct).ConfigureAwait(false);
        if (!found)
        {
            return SERVER_ERROR;
        }

        if (account is null)
        {
            return await RunUnknownAsync(password, ct).ConfigureAwait(false);
        }

        // One clock read decides "locked" and is the time recorded for it.
        DateTime now = _time.GetUtcNow().UtcDateTime;
        if (account.LockoutUntilUtc is not null && now < account.LockoutUntilUtc.Value)
        {
            return await FailAndRecordAsync(account.AccountID, now, "locked").ConfigureAwait(false);
        }

        if (account.IsBanned)
        {
            _failLog.LogInformation("Credential failure (banned) for {AccountID}.", account.AccountID);
            return INVALID;
        }

        ct.ThrowIfCancellationRequested();
        HashStep step = await VerifyInsideGateAsync(password, account.PasswordHashPhc, ct).ConfigureAwait(false);
        if (step is HashStep.GateFull or HashStep.Failed)
        {
            return SERVER_ERROR;
        }

        // Abandoned-auth seam (CR-AUTH-9): a request aborted while the hash ran must not write a failure or a reset.
        ct.ThrowIfCancellationRequested();

        if (step == HashStep.Mismatched)
        {
            // Fresh clock read: the hash took real time, so the failure is recorded at the moment it was decided.
            return await FailAndRecordAsync(account.AccountID, _time.GetUtcNow().UtcDateTime, "wrong password").ConfigureAwait(false);
        }

        return await PassAsync(account.AccountID).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks the account up. <c>Ok</c> is false after an infrastructure failure (one Critical entry written);
    /// <c>Account</c> is null for an unknown username. A cancelled request propagates.
    /// </summary>
    private async Task<(bool Ok, AccountRecord? Account)> FindAsync(string usernameLower, CancellationToken ct)
    {
        try
        {
            return (true, await _accounts.FindByUsernameAsync(usernameLower, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _infraLog.LogCritical(ex, "Credential check account lookup failed.");
            return (false, null);
        }
    }

    /// <summary>Resets the failure state with <see cref="CancellationToken.None"/> and returns <see cref="CredentialResult.Passed"/>.</summary>
    private async Task<CredentialOutcome> PassAsync(AccountID account)
    {
        try
        {
            await _accounts.ResetFailuresAsync(account, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _infraLog.LogCritical(ex, "Credential check failure reset failed.");
            return SERVER_ERROR;
        }

        return new CredentialOutcome(CredentialResult.Passed, account);
    }

    private async Task<CredentialOutcome> RunUnknownAsync(string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HashStep step = await VerifyInsideGateAsync(password, _dummy.Phc, ct).ConfigureAwait(false);
        if (step is HashStep.GateFull or HashStep.Failed)
        {
            return SERVER_ERROR;
        }

        ct.ThrowIfCancellationRequested();
        _failLog.LogInformation("Credential failure (unknown username).");
        return INVALID;
    }

    /// <summary>Records one failure with <see cref="CancellationToken.None"/> and returns the identical invalid outcome.</summary>
    /// <param name="account">The account that failed.</param>
    /// <param name="utcNow">The time the failure was decided; the repository computes the lockout from it.</param>
    /// <param name="reason">Log reason (no secrets).</param>
    private async Task<CredentialOutcome> FailAndRecordAsync(AccountID account, DateTime utcNow, string reason)
    {
        try
        {
            await _accounts.RecordFailureAsync(account, utcNow, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _infraLog.LogCritical(ex, "Credential check failure recording failed.");
            return SERVER_ERROR;
        }

        _failLog.LogInformation("Credential failure ({Reason}) for {AccountID}.", reason, account);
        return INVALID;
    }

    /// <summary>
    /// Takes a gate permit, verifies on the thread pool and returns the permit exactly once.
    /// A hasher fault writes one Critical entry; a full gate writes nothing.
    /// </summary>
    private async Task<HashStep> VerifyInsideGateAsync(string password, string phc, CancellationToken ct)
    {
        if (!_gate.TryEnter())
        {
            return HashStep.GateFull;
        }

        try
        {
            bool ok = await Task.Run(() => _hasher.Verify(password, phc), ct).ConfigureAwait(false);
            return ok ? HashStep.Matched : HashStep.Mismatched;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _infraLog.LogCritical(ex, "Credential check password verification failed.");
            return HashStep.Failed;
        }
        finally
        {
            _gate.Exit();
        }
    }
}

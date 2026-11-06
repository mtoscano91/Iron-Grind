using IronGrind.Authentication;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// <see cref="IAuthenticationService"/> over the account repository, hasher and hash gate (ADR-016 Decision 1).
/// Registration (Story 008) and fresh login (Story 010).
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    /// <summary>Logger category for infrastructure failures (kept apart from auth-failure logging); reused by login (Story 009).</summary>
    public const string INFRASTRUCTURE_LOG_CATEGORY = "IronGrind.Gateway.Infrastructure";

    private readonly IAccountRepository _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly IAuthHashGate _gate;
    private readonly RegistrationValidator _validator;
    private readonly CredentialCheck _credentials;
    private readonly SessionEstablishment _sessions;
    private readonly TokenPath _tokenPath;
    private readonly ILogger _log;

    /// <summary>Creates the service.</summary>
    /// <param name="accounts">Account persistence.</param>
    /// <param name="hasher">Password hasher.</param>
    /// <param name="gate">The shared MAX_AUTH_PENDING gate.</param>
    /// <param name="validator">Registration input validator.</param>
    /// <param name="credentials">Login credential check.</param>
    /// <param name="sessions">Login session establishment.</param>
    /// <param name="tokenPath">Login with a non-zero token.</param>
    /// <param name="loggerFactory">Source of the infrastructure logger.</param>
    public AuthenticationService(
        IAccountRepository accounts,
        IPasswordHasher hasher,
        IAuthHashGate gate,
        RegistrationValidator validator,
        CredentialCheck credentials,
        SessionEstablishment sessions,
        TokenPath tokenPath,
        ILoggerFactory loggerFactory)
    {
        _tokenPath = tokenPath ?? throw new ArgumentNullException(nameof(tokenPath));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _log = loggerFactory.CreateLogger(INFRASTRUCTURE_LOG_CATEGORY);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A full gate is load, not an infrastructure fault, so it returns <c>ServerError</c> without any log entry.
    /// Hasher and repository failures write exactly one Critical entry; messages never carry the password or PHC.
    /// </remarks>
    public async Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken ct)
    {
        RegistrationError? invalid = _validator.Validate(request);
        if (invalid is not null)
        {
            return Failed(invalid.Value);
        }

        ct.ThrowIfCancellationRequested();

        if (!_gate.TryEnter())
        {
            return Failed(RegistrationError.ServerError);
        }

        string? phc = await HashInsideGateAsync(request.Password, ct).ConfigureAwait(false);
        if (phc is null)
        {
            return Failed(RegistrationError.ServerError);
        }

        ct.ThrowIfCancellationRequested();

        return await StoreAsync(request, phc).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the account and character stub with <see cref="CancellationToken.None"/> (the request is past its abort
    /// check, and a cancelled commit would leave the outcome unknown — Story 006) and maps the outcome.
    /// </summary>
    private async Task<RegistrationResult> StoreAsync(RegistrationRequest request, string phc)
    {
        RegistrationOutcome outcome;
        try
        {
            outcome = await _accounts.RegisterAsync(
                UsernameRules.Normalize(request.Username), phc, request.CharacterName, request.ClassType, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Registration database call failed.");
            return Failed(RegistrationError.ServerError);
        }

        if (outcome.Success)
        {
            return new RegistrationResult(true, default);
        }

        if (outcome.Error == RegistrationError.ServerError)
        {
            _log.LogCritical("Registration database call returned ServerError.");
        }

        return Failed(outcome.Error);
    }

    /// <summary>
    /// Hashes on the thread pool while holding a gate permit taken by the caller; the permit is returned exactly once.
    /// </summary>
    /// <returns>The PHC string, or null when the hasher failed (one Critical entry written).</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the hash started.</exception>
    private async Task<string?> HashInsideGateAsync(string password, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => _hasher.Hash(password), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Registration password hashing failed.");
            return null;
        }
        finally
        {
            _gate.Exit();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Credential failure never reaches session establishment. A non-zero token field takes the token path, which
    /// neither hashes nor takes the gate.
    /// </remarks>
    public async Task<LoginResponse> AuthenticateAsync(ConnectionRequest request, CancellationToken ct)
    {
        if (request.TokenField is not null && Array.Exists(request.TokenField, b => b != 0))
        {
            return await _tokenPath.RunAsync(request.Username, request.TokenField, ct).ConfigureAwait(false);
        }

        CredentialOutcome outcome = await _credentials.RunAsync(request.Username, request.Password, ct).ConfigureAwait(false);
        return outcome.Result switch
        {
            CredentialResult.Passed => await _sessions.RunAsync(outcome.AccountID).ConfigureAwait(false),
            CredentialResult.InvalidCredentials => LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials),
            _ => LoginResponse.ForFailure(AuthFailureReason.ServerError),
        };
    }

    private static RegistrationResult Failed(RegistrationError error) => new(false, error);
}

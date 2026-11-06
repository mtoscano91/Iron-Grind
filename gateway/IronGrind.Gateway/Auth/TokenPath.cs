using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Login with a non-zero token (CR-AUTH-9 step 2, ADR-016 Decision 4 "Token path"). No hashing, no gate, no lockout
/// fields: the gateway only checks that the account exists, is not banned and holds a session, then asks that zone
/// to <c>Reconnect</c>. The zone validates and rotates the token; the gateway relays the new one. The account to zone
/// map is never changed here.
/// </summary>
public sealed class TokenPath
{
    private static readonly LoginResponse SERVER_ERROR = LoginResponse.ForFailure(AuthFailureReason.ServerError);
    private static readonly LoginResponse INVALID = LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials);

    private readonly IAccountRepository _accounts;
    private readonly IAccountZoneMap _map;
    private readonly IZoneRouter _router;
    private readonly IZoneLink _link;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _infraLog;
    private readonly ILogger _pathLog;

    /// <summary>Creates the token path.</summary>
    /// <param name="accounts">Account persistence (lookup and character).</param>
    /// <param name="map">Account to zone map (read only).</param>
    /// <param name="router">Zone router (port lookup).</param>
    /// <param name="link">Zone link.</param>
    /// <param name="options">Source of the Reconnect timeout.</param>
    /// <param name="time">Clock for the EC-AUTH-5 infrastructure log entry (AC-AUTH-23 timestamp).</param>
    /// <param name="loggerFactory">Source of the infrastructure and token path loggers.</param>
    public TokenPath(
        IAccountRepository accounts,
        IAccountZoneMap map,
        IZoneRouter router,
        IZoneLink link,
        GatewayOptions options,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _infraLog = loggerFactory.CreateLogger(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY);
        _pathLog = loggerFactory.CreateLogger<TokenPath>();
    }

    /// <summary>Runs the token path for one login attempt.</summary>
    /// <param name="username">Raw username as typed; normalized here, never logged.</param>
    /// <param name="token">The non-zero 16-byte token presented by the client; never logged.</param>
    /// <param name="ct">
    /// Request token; read by the lookup and checked once before <c>Reconnect</c> is sent. After that it is not
    /// observed, because the zone rotates the token and the reply must be delivered.
    /// </param>
    /// <returns>Success with the rotated token, or <c>InvalidCredentials</c> or <c>ServerError</c>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before <c>Reconnect</c> was sent.</exception>
    public async Task<LoginResponse> RunAsync(string username, byte[] token, CancellationToken ct)
    {
        try
        {
            return await RunCoreAsync(username, token, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _infraLog.LogCritical(ex, "Token path failed.");
            return SERVER_ERROR;
        }
    }

    private async Task<LoginResponse> RunCoreAsync(string username, byte[] token, CancellationToken ct)
    {
        AccountRecord? account = await _accounts.FindByUsernameAsync(UsernameRules.Normalize(username), ct).ConfigureAwait(false);
        if (account is null)
        {
            _pathLog.LogInformation("Token login refused (unknown username).");
            return INVALID;
        }

        if (account.IsBanned)
        {
            _pathLog.LogInformation("Token login refused (banned) for {AccountID}.", account.AccountID);
            return INVALID;
        }

        if (!_map.TryGetZone(account.AccountID, out ZoneID zone))
        {
            _pathLog.LogInformation("Token login refused (no session) for {AccountID}.", account.AccountID);
            return INVALID;
        }

        if (!_map.IsLinkUp(zone))
        {
            _infraLog.LogWarning("Token login for {AccountID} refused: its session zone {ZoneID} has no link.", account.AccountID, zone);
            return SERVER_ERROR;
        }

        CharacterID character = await _accounts.GetCharacterIDAsync(account.AccountID, CancellationToken.None).ConfigureAwait(false);
        if (character == CharacterID.Invalid)
        {
            _infraLog.LogCritical(
                "No character mapped to {AccountID} at {UtcTime:O} on the token path (EC-AUTH-5).",
                account.AccountID,
                _time.GetUtcNow().UtcDateTime);
            return SERVER_ERROR;
        }

        if (!_router.TryGetPort(zone, out ushort port))
        {
            _infraLog.LogWarning("Token login for {AccountID} refused: no port known for its session zone {ZoneID}.", account.AccountID, zone);
            return SERVER_ERROR;
        }

        ct.ThrowIfCancellationRequested();
        ReconnectOutcome outcome = await ReconnectAsync(zone, account.AccountID, token).ConfigureAwait(false);
        return Finish(outcome, account.AccountID, character, zone, port);
    }

    private LoginResponse Finish(ReconnectOutcome outcome, AccountID account, CharacterID character, ZoneID zone, ushort port)
    {
        if (!outcome.Accepted)
        {
            _pathLog.LogInformation("Token login refused (token rejected) for {AccountID}.", account);
            return INVALID;
        }

        byte[]? fresh = outcome.NewToken;
        if (!SessionTokenBytes.IsValid(fresh))
        {
            _infraLog.LogCritical("Zone {ZoneID} accepted a Reconnect for {AccountID} with an invalid rotated token.", zone, account);
            return SERVER_ERROR;
        }

        return LoginResponse.ForSuccess(fresh, character, zone, port);
    }

    /// <summary>
    /// Sends <c>Reconnect</c> with only its own timeout token. A cancellation the link raises on its own is wrapped,
    /// so it is treated as a link fault and not as the request being cancelled.
    /// </summary>
    private async Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token)
    {
        try
        {
            return await ZoneLinkTimeout.WithTimeoutAsync(
                linkToken => _link.ReconnectAsync(zone, account, token, linkToken),
                _options.ZoneRequestTimeoutMs,
                $"Reconnect to zone {zone}").ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException($"Reconnect to zone {zone} was cancelled by the link.", ex);
        }
    }
}

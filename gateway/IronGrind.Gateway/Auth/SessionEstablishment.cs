using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Config;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Fresh-login steps 2-6 (ADR-016 Decision 4): resolve the character, evict a live session, route, admit, record.
/// The gateway keeps no token after the response is built. The request token is deliberately not an input: once the
/// credential check has passed the login runs to completion (CR-AUTH-9 "Abandoned auth").
/// </summary>
public sealed class SessionEstablishment
{
    private static readonly LoginResponse SERVER_ERROR = LoginResponse.ForFailure(AuthFailureReason.ServerError);

    private readonly IAccountRepository _accounts;
    private readonly IAccountZoneMap _map;
    private readonly IZoneRouter _router;
    private readonly IZoneLink _link;
    private readonly GatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    /// <summary>Creates the service.</summary>
    /// <param name="accounts">Account persistence (character and last zone).</param>
    /// <param name="map">Account to zone map.</param>
    /// <param name="router">Zone router.</param>
    /// <param name="link">Zone link.</param>
    /// <param name="options">Source of the Admit and Evict timeouts.</param>
    /// <param name="time">Clock for the corrupt-mapping log entry.</param>
    /// <param name="loggerFactory">Source of the infrastructure logger.</param>
    public SessionEstablishment(
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
        _log = loggerFactory.CreateLogger(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY);
    }

    /// <summary>Runs login steps 2-6 for an account whose credentials passed.</summary>
    /// <param name="account">The authenticated account.</param>
    /// <returns>Success with token, character, zone and port; otherwise <c>ServerStarting</c> or <c>ServerError</c>.</returns>
    public async Task<LoginResponse> RunAsync(AccountID account)
    {
        try
        {
            return await RunCoreAsync(account).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Session establishment failed for {AccountID}.", account);
            return SERVER_ERROR;
        }
    }

    private async Task<LoginResponse> RunCoreAsync(AccountID account)
    {
        CharacterID character = await _accounts.GetCharacterIDAsync(account, CancellationToken.None).ConfigureAwait(false);
        if (character == CharacterID.Invalid)
        {
            _log.LogCritical(
                "No character mapped to {AccountID} at {UtcTime:O} (EC-AUTH-5).", account, _time.GetUtcNow().UtcDateTime);
            return SERVER_ERROR;
        }

        ZoneID lastZone = await _accounts.GetLastZoneIdAsync(account, CancellationToken.None).ConfigureAwait(false);

        if (_map.TryGetZone(account, out ZoneID held))
        {
            if (!_map.IsLinkUp(held))
            {
                // Link-loss grace (ADR-016 Decision 3): no Evict to a zone with no link; the player retries.
                _log.LogWarning("Login for {AccountID} refused: its session zone {ZoneID} has no link.", account, held);
                return SERVER_ERROR;
            }

            await EvictAsync(held, account).ConfigureAwait(false);
        }

        if (!_router.TryPick(lastZone, ZoneID.Invalid, out ZoneID zone, out ushort port))
        {
            return LoginResponse.ForFailure(AuthFailureReason.ServerStarting);
        }

        byte[]? token = await AdmitAsync(zone, account, character).ConfigureAwait(false);
        if (token is not null)
        {
            return Accept(account, character, token, zone, port);
        }

        // Only an explicit rejection re-routes, once, excluding the rejecting zone.
        if (!_router.TryPick(lastZone, zone, out ZoneID second, out ushort secondPort))
        {
            return SERVER_ERROR;
        }

        token = await AdmitAsync(second, account, character).ConfigureAwait(false);
        return token is not null ? Accept(account, character, token, second, secondPort) : SERVER_ERROR;
    }

    private LoginResponse Accept(AccountID account, CharacterID character, byte[] token, ZoneID zone, ushort port)
    {
        _map.SetSession(account, zone);
        return LoginResponse.ForSuccess(token, character, zone, port);
    }

    /// <summary>
    /// Sends one <c>Admit</c> with a fresh token, so no token is ever offered to two zones.
    /// Returns the token when the zone accepted, null on an explicit rejection.
    /// </summary>
    private async Task<byte[]?> AdmitAsync(ZoneID zone, AccountID account, CharacterID character)
    {
        byte[] token = SessionTokenBytes.NewRandom();
        bool accepted = await ZoneLinkTimeout.WithTimeoutAsync(
            ct => _link.AdmitAsync(zone, account, character, token, ct),
            _options.ZoneRequestTimeoutMs,
            $"Admit to zone {zone}").ConfigureAwait(false);
        return accepted ? token : null;
    }

    private Task EvictAsync(ZoneID zone, AccountID account)
    {
        return ZoneLinkTimeout.WithTimeoutAsync(
            async ct =>
            {
                await _link.EvictAsync(zone, account, ct).ConfigureAwait(false);
                return true;
            },
            _options.EvictTimeoutMs,
            $"Evict from zone {zone}");
    }
}

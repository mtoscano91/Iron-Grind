using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// The gateway's request/reply seam to zone processes (ADR-016 Key Interfaces). The real TCP link comes from the
/// gateway epic. Story 011 adds <c>ReconnectAsync</c> and <c>ReconnectOutcome</c>.
/// </summary>
public interface IZoneLink
{
    /// <summary>Asks a zone to reserve a session for the account (ADR-016 Decision 4 step 5).</summary>
    /// <param name="zone">The target zone.</param>
    /// <param name="account">The account being admitted.</param>
    /// <param name="character">The character the zone should expect.</param>
    /// <param name="token">The 16-byte session token; never logged or stored by the caller.</param>
    /// <param name="ct">Fires at the configured request timeout.</param>
    /// <returns>True when the zone accepted; false on explicit rejection.</returns>
    Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct);

    /// <summary>Asks a zone to end the account's session and waits for its reply (EC-AUTH-6).</summary>
    /// <param name="zone">The zone holding the session.</param>
    /// <param name="account">The account to evict.</param>
    /// <param name="ct">Fires at the configured evict timeout.</param>
    /// <returns>A task that completes when the zone has replied.</returns>
    Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct);

    /// <summary>
    /// Asks a zone to validate and rotate the account's session token (token path, ADR-016 Decision 4).
    /// </summary>
    /// <param name="zone">The zone holding the session.</param>
    /// <param name="account">The account reconnecting.</param>
    /// <param name="token">The 16-byte token the client presented; never logged.</param>
    /// <param name="ct">Fires at the configured request timeout.</param>
    /// <returns>Accepted with the rotated token, or rejected.</returns>
    Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct);
}

using IronGrind.CharacterPersistence;
using IronGrind.Currency;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Account to zone map with no entries and no live link. Stand-in until the gateway epic supplies the real map
/// (Authentication Story 012); replaced there.
/// </summary>
public sealed class EmptyAccountZoneMap : IAccountZoneMap
{
    /// <inheritdoc/>
    public bool TryGetZone(AccountID account, out ZoneID zone)
    {
        zone = ZoneID.Invalid;
        return false;
    }

    /// <inheritdoc/>
    public bool IsLinkUp(ZoneID zone) => false;

    /// <inheritdoc/>
    public void SetSession(AccountID account, ZoneID zone)
    {
        // No zone ever admits a session before the gateway epic, so there is nothing to record.
    }

    /// <inheritdoc/>
    public bool Remove(AccountID account, ZoneID fromZone) => false;
}

/// <summary>
/// Router that never finds a zone, so a correct login answers <c>ServerStarting</c>. Stand-in until the gateway epic
/// supplies the fill-first router (Authentication Story 012); replaced there.
/// </summary>
public sealed class NoZoneRouter : IZoneRouter
{
    /// <inheritdoc/>
    public bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port)
    {
        zone = ZoneID.Invalid;
        port = 0;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetPort(ZoneID zone, out ushort port)
    {
        port = 0;
        return false;
    }
}

/// <summary>
/// Zone link that must never be reached: with <see cref="NoZoneRouter"/> no zone is chosen, so no request is sent.
/// Stand-in until the gateway epic supplies the TCP link (Authentication Story 012); replaced there.
/// </summary>
public sealed class UnavailableZoneLink : IZoneLink
{
    private const string MESSAGE = "No zone link exists until the gateway epic; the no-zone router should never route a login here.";

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<bool> AdmitAsync(ZoneID zone, AccountID account, CharacterID character, byte[] token, CancellationToken ct) =>
        throw new InvalidOperationException(MESSAGE);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task EvictAsync(ZoneID zone, AccountID account, CancellationToken ct) =>
        throw new InvalidOperationException(MESSAGE);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<ReconnectOutcome> ReconnectAsync(ZoneID zone, AccountID account, byte[] token, CancellationToken ct) =>
        throw new InvalidOperationException(MESSAGE);
}

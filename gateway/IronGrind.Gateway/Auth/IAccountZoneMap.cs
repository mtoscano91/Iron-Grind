using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// The gateway's account to zone map (ADR-016 Decision 3): one entry per account with a session in some zone.
/// Rules the gateway epic's implementation must follow:
/// <list type="bullet">
/// <item>Zone-matched removal: a <c>SessionReleased</c> or <c>EvictResult</c> from zone Z removes the account's entry only if the entry points to Z.</item>
/// <item>Link loss grace: when a zone's link is lost its entries stay for <c>GATEWAY_LINK_RETRY_SECONDS x 2</c>; a fresh login for one of those accounts gets <c>ServerError</c> at once (<see cref="IsLinkUp"/> is false). After the grace period with no re-registration every entry pointing to that zone is removed.</item>
/// <item>Registration: a <c>RegisterZone</c> replaces all entries pointing to that zone with its <c>liveAccounts</c>; a listed account the map assigns to another zone is not added and the registering zone gets <c>Evict</c> for it.</item>
/// <item>Late admission: an <c>AdmitResult(accepted)</c> whose request is unknown or timed out is answered with <c>Evict</c> and no entry is added.</item>
/// <item>Gateway boot: the map starts empty; until the boot wait ends the router answers "no routable zone" (<c>ServerStarting</c>).</item>
/// </list>
/// The real link (<c>LinkZoneLink</c>) calls <see cref="SetSession"/> when <c>AdmitResult(accepted)</c> arrives for a live
/// <c>Admit</c>, before the caller's task completes (ADR-016 Decision 3 "Added"); it is the only caller of <see cref="SetSession"/>.
/// </summary>
public interface IAccountZoneMap
{
    /// <summary>Looks up the zone holding the account's session.</summary>
    /// <param name="account">The account.</param>
    /// <param name="zone">The zone, or <see cref="ZoneID.Invalid"/> when there is no entry.</param>
    /// <returns>True when the account has an entry.</returns>
    bool TryGetZone(AccountID account, out ZoneID zone);

    /// <summary>Reports whether the zone currently has a live link.</summary>
    /// <param name="zone">The zone.</param>
    /// <returns>False when the link is down (including the grace period).</returns>
    bool IsLinkUp(ZoneID zone);

    /// <summary>Sets the account's entry, replacing any existing one (upsert).</summary>
    /// <param name="account">The account.</param>
    /// <param name="zone">The zone that accepted the admission.</param>
    void SetSession(AccountID account, ZoneID zone);

    /// <summary>Zone-matched removal: removes the account's entry only if it points to <paramref name="fromZone"/>.</summary>
    /// <param name="account">The account.</param>
    /// <param name="fromZone">The zone that reported the release or eviction.</param>
    /// <returns>True when an entry was removed; false when there was none or it points to another zone.</returns>
    bool Remove(AccountID account, ZoneID fromZone);
}

using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Narrow seam the account to zone map uses to send an <c>Evict</c> to a registering zone (Gateway Story 005).
/// </summary>
public interface IZoneEvictSender
{
    /// <summary>Asks the zone to evict the account's session. Fire-and-forget: must not block and must not wait for the reply.</summary>
    /// <param name="zone">The zone to send the Evict to.</param>
    /// <param name="account">The account to evict.</param>
    void SendEvict(ZoneID zone, AccountID account);
}

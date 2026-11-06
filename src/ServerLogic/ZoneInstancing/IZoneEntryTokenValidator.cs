using IronGrind.CharacterPersistence;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Decides whether the session token of a zone entry request is acceptable and resolves the account it belongs to
    /// (Zone Instancing Story 009; CR-TOK-8: the client never sends an AccountID, the zone resolves it from its own record).
    /// Until the gateway admission is wired, <see cref="RejectingZoneEntryTokenValidator"/> stands in.
    /// </summary>
    public interface IZoneEntryTokenValidator
    {
        /// <summary>Validates the token carried by <paramref name="request"/> and resolves its account. Tick thread; must not block. Implementations must not retain or mutate <see cref="ZoneEntryRequest.SessionToken"/>; the decoder hands over an array it will not reuse.</summary>
        /// <param name="request">The decoded request.</param>
        /// <param name="account">The account resolved from the zone's own record when this returns true; <see cref="AccountID.Invalid"/> otherwise.</param>
        /// <returns>True when the token is accepted.</returns>
        bool TryValidate(in ZoneEntryRequest request, out AccountID account);
    }
}

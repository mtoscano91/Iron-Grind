namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Decides whether the session token of a zone entry request is acceptable (Zone Instancing Story 009).
    /// Cross-process validation is defined by the gateway / Authentication work; until then
    /// <see cref="RejectingZoneEntryTokenValidator"/> stands in.
    /// </summary>
    public interface IZoneEntryTokenValidator
    {
        /// <summary>Validates the token carried by <paramref name="request"/>. Tick thread; must not block. Implementations must not retain or mutate <see cref="ZoneEntryRequest.SessionToken"/>; the decoder hands over an array it will not reuse.</summary>
        /// <param name="request">The decoded request.</param>
        /// <returns>True when the token is accepted.</returns>
        bool IsValid(in ZoneEntryRequest request);
    }
}

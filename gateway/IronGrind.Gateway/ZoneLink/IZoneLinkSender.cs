using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.ZoneLink;

/// <summary>Outbound side of the zone link (Gateway Story 003). Thread-safe.</summary>
public interface IZoneLinkSender
{
    /// <summary>True only between a zone's <c>RegisterZone</c> and the loss of its link.</summary>
    /// <param name="zone">The zone.</param>
    /// <returns>Whether the zone has a live link.</returns>
    bool IsLinkUp(ZoneID zone);

    /// <summary>
    /// Writes one whole encoded frame (from <c>GatewayLinkCodec.Encode</c>) to the zone's link. Concurrent calls never
    /// interleave frames. Returns false, without throwing, when the zone has no link or the write failed (a failed
    /// write closes the link).
    /// </summary>
    /// <param name="zone">The destination zone.</param>
    /// <param name="frame">The complete frame bytes.</param>
    /// <param name="ct">Cancels waiting for the link's write turn or the write itself (a cancelled write closes the link).</param>
    /// <returns>True if the frame was written.</returns>
    Task<bool> SendAsync(ZoneID zone, byte[] frame, CancellationToken ct);
}

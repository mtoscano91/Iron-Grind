using IronGrind.CharacterPersistence;
using IronGrind.ServerLogic.Gateway.Link;

namespace IronGrind.Gateway.ZoneLink;

/// <summary>
/// Handler for zone-link lifecycle and inbound messages (Gateway Story 003; consumed by Stories 004, 005, 006).
/// Threading: every method is called only from the read loop of the connection concerned (a thread-pool thread), never
/// from a sender or the stop path. For one link the calls are therefore ordered and never concurrent:
/// <see cref="OnZoneRegistered"/> first, then messages, then at most one <see cref="OnLinkLost"/> last. Different
/// links' calls run in parallel (an older link's final close can overlap a newer link's registration, but it raises no
/// event). Handlers must not block; an exception thrown by a handler is logged and the link stays up.
/// </summary>
public interface IZoneLinkEvents
{
    /// <summary>
    /// A connection sent a valid <c>RegisterZone</c> and is now the link for its zone (any older link of the same
    /// zone was already closed, without an <see cref="OnLinkLost"/>).
    /// </summary>
    /// <param name="zone">The registered zone.</param>
    /// <param name="message">The RegisterZone message.</param>
    void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message);

    /// <summary>A valid message arrived on a registered link (never RegisterZone).</summary>
    /// <param name="zone">The zone the link belongs to.</param>
    /// <param name="message">The decoded message.</param>
    void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message);

    /// <summary>
    /// A registered link ended for any reason other than being replaced by a newer link. Raised exactly once per
    /// registered link.
    /// </summary>
    /// <param name="zone">The zone that lost its link.</param>
    void OnLinkLost(ZoneID zone);
}

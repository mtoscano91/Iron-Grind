using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// Fill-first zone routing (CR-ZI-6, ADR-016 Decision 4 step 4). The real router comes from the gateway epic.
/// </summary>
public interface IZoneRouter
{
    /// <summary>Picks a routable zone for a login.</summary>
    /// <param name="lastZone">The character's last zone, or <see cref="ZoneID.Invalid"/> for none.</param>
    /// <param name="excluded">A zone that must not be picked, or <see cref="ZoneID.Invalid"/> for none.</param>
    /// <param name="zone">The chosen zone.</param>
    /// <param name="port">The chosen zone's UDP port (from <c>RegisterZone.udpPort</c>).</param>
    /// <returns>False when no zone is routable (boot wait, none active, all full).</returns>
    bool TryPick(ZoneID lastZone, ZoneID excluded, out ZoneID zone, out ushort port);

    /// <summary>Looks up a registered zone's UDP port (token path).</summary>
    /// <param name="zone">The zone.</param>
    /// <param name="port">The zone's UDP port (from <c>RegisterZone.udpPort</c>).</param>
    /// <returns>False when the router knows no port for the zone.</returns>
    bool TryGetPort(ZoneID zone, out ushort port);
}

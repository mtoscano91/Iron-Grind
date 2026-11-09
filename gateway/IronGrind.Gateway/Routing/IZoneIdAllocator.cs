using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Routing;

/// <summary>
/// Hands out a fresh ZoneID for each zone process the gateway starts (ADR-016 Decisions 3 and 5,
/// <c>zone-instancing.md</c> CR-ZI-1). IDs are never reused, not even across gateway restarts.
/// </summary>
public interface IZoneIdAllocator
{
    /// <summary>Allocates a new ZoneID.</summary>
    /// <param name="ct">Cancels the database round trip.</param>
    /// <returns>A ZoneID never handed out before; never <see cref="ZoneID.Invalid"/>.</returns>
    /// <exception cref="Exception">
    /// Any failure (database unreachable, missing rights, out-of-range value). The caller treats it as a
    /// failed zone start; nothing is recorded.
    /// </exception>
    Task<ZoneID> AllocateAsync(CancellationToken ct);
}

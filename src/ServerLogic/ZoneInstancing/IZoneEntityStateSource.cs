using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Read seam for the entity fields the zone process does not own (position, health, level, appearance, respawn
    /// tick), used by <see cref="ZoneSnapshotBuilder"/> (Zone Instancing Story 007, CR-ZI-8 step 6). The real sources
    /// (Character Stats, combat, Equipment, Death and Respawn) are wired in Story 014. Main thread only; an
    /// implementation should not allocate, so the snapshot path stays allocation-free.
    /// </summary>
    public interface IZoneEntityStateSource
    {
        /// <summary>Reads the player's wire-unit state.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="view">The state, or default when unknown.</param>
        /// <returns>False when the source has no data for <paramref name="id"/>.</returns>
        bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view);

        /// <summary>Reads the mob's wire-unit state.</summary>
        /// <param name="id">The mob's EntityID.</param>
        /// <param name="view">The state, or default when unknown.</param>
        /// <returns>False when the source has no data for <paramref name="id"/>.</returns>
        bool TryGetMobState(EntityID id, out ZoneMobStateView view);
    }
}

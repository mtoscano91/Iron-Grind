using IronGrind.CharacterStats;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Supplies the HP a player had when its connection dropped (the pre-disconnect snapshot, GD-CGS-2). A ghost's live
    /// HP is 0 when it dies, so the ghost-death session end writes this value instead (Death and Respawn Story 010,
    /// Decision 1). When no snapshot exists the caller falls back to the effective MaxHP.
    /// </summary>
    public interface IPreDisconnectHpSource
    {
        /// <summary>Looks up the pre-disconnect HP of <paramref name="entity"/>.</summary>
        /// <param name="entity">The ghost's EntityID.</param>
        /// <param name="hp">The snapshot HP, or 0 when there is none.</param>
        /// <returns><see langword="true"/> if a snapshot exists.</returns>
        bool TryGetHp(EntityID entity, out float hp);
    }
}

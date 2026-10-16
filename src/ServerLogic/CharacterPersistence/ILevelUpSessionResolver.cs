using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Maps a player's <see cref="IronGrind.CharacterStats.EntityID"/> to the connection and character the
    /// irreversible-outcome coordinator needs (Leveling Story 016, decision 5).
    /// </summary>
    public interface ILevelUpSessionResolver
    {
        /// <summary>
        /// Resolves the session of <paramref name="entityId"/>.
        /// </summary>
        /// <param name="entityId">The entity that levelled up.</param>
        /// <param name="clientId">The connection of that entity, when resolved.</param>
        /// <param name="charId">The character of that entity, when resolved.</param>
        /// <returns>True when both were resolved; false when the entity has no live session.</returns>
        bool TryResolve(IronGrind.CharacterStats.EntityID entityId, out uint clientId, out CharacterID charId);
    }
}

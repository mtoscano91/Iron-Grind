using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Narrow seam through which the session-end release removes a character's entry from the persistence session
    /// table (CR-CP-10 step 4, Story 010). Kept off <see cref="ICharacterPersistence"/>, whose method set is the
    /// CR-CP-1 contract.
    /// </summary>
    public interface ICharacterSessionRelease
    {
        /// <summary>
        /// Removes the character's entry from the persistence session table. Synchronous and tick-thread only; called by
        /// the session-end release after the save was handled.
        /// </summary>
        /// <param name="id">The character to release.</param>
        /// <param name="entityId">The entity the entry belonged to; <see cref="EntityID.Invalid"/> when there was no entry.</param>
        /// <returns><see langword="true"/> if an entry was removed; <see langword="false"/> (a no-op) for an unknown character.</returns>
        /// <example>
        /// <code>
        /// if (sessions.ReleaseSession(charId, out EntityID entityId)) { leveling.UnregisterPlayerEntity(entityId); }
        /// </code>
        /// </example>
        bool ReleaseSession(CharacterID id, out EntityID entityId);
    }
}

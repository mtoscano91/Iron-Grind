using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Read-only lookup of a loaded character's display name (Enhancement Story 016; design/gdd/enhancement-system.md
    /// CR-ENH-14, UI-ENH-3, EC-ENH-8; ADR-015). Implemented by <see cref="CharacterPersistenceService"/> from its
    /// session entry. Tick thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// if (names.TryGetCharacterName(charId, out string name)) { /* use name */ }
    /// </code>
    /// </example>
    public interface ICharacterNameLookup
    {
        /// <summary>
        /// Gets the name of a character whose session is loaded.
        /// </summary>
        /// <param name="id">The character to look up.</param>
        /// <param name="name">The character's name; <see cref="string.Empty"/> when the result is false.</param>
        /// <returns>True while the character has a session entry with a non-empty name; false otherwise (unknown, not loaded, released, or an empty name).</returns>
        bool TryGetCharacterName(CharacterID id, out string name);
    }
}

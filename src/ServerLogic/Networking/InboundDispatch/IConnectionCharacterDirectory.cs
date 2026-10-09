using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Resolves the character a connection plays (ADR-014 Decision 4, Pass B step 2). Implemented
    /// by the session layer.
    /// </summary>
    /// <example>
    /// <code>
    /// if (directory.TryGetCharacterId(clientId, out CharacterID charId)) { /* ... */ }
    /// </code>
    /// </example>
    public interface IConnectionCharacterDirectory
    {
        /// <summary>Returns true and the character when the connection has one.</summary>
        /// <param name="clientId">The connection.</param>
        /// <param name="charId">The character, or <see cref="CharacterID.Invalid"/>.</param>
        bool TryGetCharacterId(uint clientId, out CharacterID charId);
    }
}

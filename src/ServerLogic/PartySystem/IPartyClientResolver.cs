using IronGrind.Currency;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Resolves a party member to the connection that receives its messages (Party System Story 010, D7). A seam
    /// over the registry, slot pool and outbox so <see cref="PartyHealthStreamStep"/> is testable without them.
    /// </summary>
    public interface IPartyClientResolver
    {
        /// <summary>Finds the client connection that owns a character.</summary>
        /// <param name="character">The character.</param>
        /// <param name="clientId">The connection id, or 0 when none.</param>
        /// <returns>True when the character has a pool slot and an outbox connection. Must not allocate.</returns>
        bool TryGetClient(CharacterID character, out uint clientId);
    }
}

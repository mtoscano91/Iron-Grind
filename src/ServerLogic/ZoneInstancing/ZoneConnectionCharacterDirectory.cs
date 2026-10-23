using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The zone process's <see cref="IConnectionCharacterDirectory"/> (ADR-014 Decision 4, Zone Instancing Story 010).
    /// <see cref="ZoneEntryCompletion"/> adds a connection when its player is admitted; the exit and teardown paths
    /// (Stories 011-013) remove it. Until a connection is added the dispatcher drops its requests as
    /// <c>InboundRequestWithoutCharacter</c>. A reverse map (<see cref="TryGetClientId"/>, Story 011) is kept in step
    /// with the forward map. Tick thread only.
    /// </summary>
    public sealed class ZoneConnectionCharacterDirectory : IConnectionCharacterDirectory
    {
        private readonly Dictionary<uint, CharacterID> _characters = new Dictionary<uint, CharacterID>();
        private readonly Dictionary<CharacterID, uint> _clients = new Dictionary<CharacterID, uint>();

        /// <summary>Number of connections with a character.</summary>
        public int Count => _characters.Count;

        /// <summary>Maps a connection to its character; an existing mapping is replaced.</summary>
        /// <param name="clientId">The connection.</param>
        /// <param name="character">The character it plays.</param>
        public void Add(uint clientId, CharacterID character)
        {
            if (_characters.TryGetValue(clientId, out CharacterID previous)
                && _clients.TryGetValue(previous, out uint previousClient) && previousClient == clientId)
            {
                _clients.Remove(previous);
            }

            _characters[clientId] = character;
            _clients[character] = clientId;
        }

        /// <summary>Removes a connection's mapping. An unknown connection changes nothing.</summary>
        /// <param name="clientId">The connection.</param>
        /// <returns>True when a mapping was removed.</returns>
        public bool Remove(uint clientId)
        {
            if (!_characters.TryGetValue(clientId, out CharacterID character))
            {
                return false;
            }

            _characters.Remove(clientId);
            if (_clients.TryGetValue(character, out uint mapped) && mapped == clientId)
            {
                _clients.Remove(character);
            }

            return true;
        }

        /// <inheritdoc/>
        public bool TryGetCharacterId(uint clientId, out CharacterID charId)
        {
            if (_characters.TryGetValue(clientId, out charId))
            {
                return true;
            }

            charId = CharacterID.Invalid;
            return false;
        }

        /// <summary>Reverse lookup: the connection that currently plays <paramref name="character"/>.</summary>
        /// <param name="character">The character.</param>
        /// <param name="clientId">The connection, or 0 when none.</param>
        /// <returns>True when a connection plays the character.</returns>
        public bool TryGetClientId(CharacterID character, out uint clientId)
        {
            return _clients.TryGetValue(character, out clientId);
        }
    }
}

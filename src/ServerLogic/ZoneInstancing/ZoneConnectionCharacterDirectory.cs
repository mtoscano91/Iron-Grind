using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The zone process's <see cref="IConnectionCharacterDirectory"/> (ADR-014 Decision 4, Zone Instancing Story 010).
    /// <see cref="ZoneEntryCompletion"/> adds a connection when its player is admitted; the exit and teardown paths
    /// (Stories 012, 013) remove it. Until a connection is added the dispatcher drops its requests as
    /// <c>InboundRequestWithoutCharacter</c>. Tick thread only.
    /// </summary>
    public sealed class ZoneConnectionCharacterDirectory : IConnectionCharacterDirectory
    {
        private readonly Dictionary<uint, CharacterID> _characters = new Dictionary<uint, CharacterID>();

        /// <summary>Number of connections with a character.</summary>
        public int Count => _characters.Count;

        /// <summary>Maps a connection to its character; an existing mapping is replaced.</summary>
        /// <param name="clientId">The connection.</param>
        /// <param name="character">The character it plays.</param>
        public void Add(uint clientId, CharacterID character)
        {
            _characters[clientId] = character;
        }

        /// <summary>Removes a connection's mapping. An unknown connection changes nothing.</summary>
        /// <param name="clientId">The connection.</param>
        /// <returns>True when a mapping was removed.</returns>
        public bool Remove(uint clientId)
        {
            return _characters.Remove(clientId);
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
    }
}

using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ServerLogic.DeathAndRespawn;

namespace IronGrind.Tests.EditMode.DeathAndRespawn
{
    /// <summary>
    /// Test fake for <see cref="IPlayerDeathStateReader"/> (Death and Respawn Story 007): a player is dead while it
    /// has an entry in <see cref="RespawnTicks"/>. Shared by the Zone Instancing, Enemy AI and Death and Respawn
    /// tests.
    /// </summary>
    internal sealed class FakePlayerDeathStateReader : IPlayerDeathStateReader
    {
        /// <summary>Dead players and their respawn tick.</summary>
        public readonly Dictionary<EntityID, long> RespawnTicks = new Dictionary<EntityID, long>();

        /// <summary>Marks <paramref name="id"/> dead until <see cref="RespawnTicks"/> loses the entry.</summary>
        public void SetDead(EntityID id, long respawnTick = 0L)
        {
            RespawnTicks[id] = respawnTick;
        }

        /// <inheritdoc/>
        public bool IsDead(EntityID id)
        {
            return RespawnTicks.ContainsKey(id);
        }

        /// <inheritdoc/>
        public bool TryGetRespawnTick(EntityID id, out long respawnTick)
        {
            return RespawnTicks.TryGetValue(id, out respawnTick);
        }
    }
}

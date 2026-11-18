using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Read seam for whether a player is dead (Death and Respawn Story 007, EC-DR-1, EC-DR-3). The zone slot state
    /// says only whether a player is connected; <see cref="DeathState"/> is the only source of dead-ness, and the
    /// snapshot builder, zone close and aggro candidate source read it through this interface. Implemented by
    /// <see cref="DeathAndRespawnService"/>. Tick thread only; allocation-free.
    /// </summary>
    /// <example>
    /// <code>
    /// if (reader.TryGetRespawnTick(id, out long respawnTick)) { /* dead; respawns at respawnTick */ }
    /// </code>
    /// </example>
    public interface IPlayerDeathStateReader
    {
        /// <summary>True when the player is Dead or Respawning. False for an unknown id or a mob.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <returns>Whether the player is dead.</returns>
        bool IsDead(EntityID id);

        /// <summary>Reads the tick at which a dead player respawns.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="respawnTick"><see cref="DeathState.RespawnTick"/> while dead, otherwise 0.</param>
        /// <returns>True only while the player is Dead or Respawning.</returns>
        bool TryGetRespawnTick(EntityID id, out long respawnTick);
    }
}

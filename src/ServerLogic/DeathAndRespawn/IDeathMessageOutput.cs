using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Output seam of <see cref="DeathAndRespawnService"/> for the death and respawn wire messages
    /// (death-and-respawn.md CR-DR-6, CR-DR-7, CR-DR-13; Death and Respawn Story 005). Called on the tick thread.
    /// The production implementation is <see cref="DeathMessageEmitter"/>.
    /// </summary>
    public interface IDeathMessageOutput
    {
        /// <summary>Sends <c>DeathStateEntered</c> to the dying player and <c>EntityDied</c> to the zone.</summary>
        /// <param name="entity">The player that died.</param>
        /// <param name="respawnTimerSeconds">Whole seconds until the respawn.</param>
        void SendDeath(EntityID entity, byte respawnTimerSeconds);

        /// <summary>Sends <c>EntityRespawned</c> to the zone and <c>RespawnConfirmed</c> to the respawned player.</summary>
        /// <param name="entity">The player that respawned.</param>
        /// <param name="position">The position the player was teleported to.</param>
        void SendRespawn(EntityID entity, Vector3 position);
    }
}

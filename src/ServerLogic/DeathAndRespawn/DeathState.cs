using IronGrind.CharacterPersistence;
using UnityEngine;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// Per-player death record (death-and-respawn.md <c>DeathState</c> struct; CR-DR-2, AC-DR-15). Ticks are
    /// <c>long</c> so a delay added to a large tick never overflows. <c>default</c> is Alive with zero ticks.
    /// </summary>
    public struct DeathState
    {
        /// <summary>Current state; <see cref="DeathStateKind.Alive"/> by default.</summary>
        public DeathStateKind CurrentState;

        /// <summary>Server tick of the death (T-1). 0 while Alive.</summary>
        public long DeathTick;

        /// <summary>Tick at which the respawn fires: <c>DeathTick + RESPAWN_DELAY_TICKS</c> (F-DR-1). 0 while Alive.</summary>
        public long RespawnTick;

        /// <summary>
        /// Respawn zone, filled at T-2. Keeps its value after the respawn; meaningful only while Dead or Respawning
        /// (Story 005 reads it before the reset).
        /// </summary>
        public ZoneID RespawnZoneID;

        /// <summary>Respawn position, filled at T-2. Same lifetime as <see cref="RespawnZoneID"/>.</summary>
        public Vector3 RespawnPosition;
    }
}

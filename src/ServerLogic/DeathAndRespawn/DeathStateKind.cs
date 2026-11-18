namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>
    /// The per-player death state (death-and-respawn.md States and Transitions). T-1 (Alive to Dead) is Story 002;
    /// T-2 / T-3 (Respawning, back to Alive) are Story 004.
    /// </summary>
    public enum DeathStateKind : byte
    {
        /// <summary>Not dead. The default value.</summary>
        Alive = 0,

        /// <summary>Dead and waiting for the respawn tick (CR-DR-2).</summary>
        Dead = 1,

        /// <summary>The respawn sequence is running (Story 004).</summary>
        Respawning = 2
    }
}

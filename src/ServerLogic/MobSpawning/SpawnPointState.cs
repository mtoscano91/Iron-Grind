namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Lifecycle state of one runtime spawn point (mob-spawning.md CR-MS-3, CR-MS-4).
    /// </summary>
    public enum SpawnPointState : byte
    {
        /// <summary>T-1 initialisation done; the T-2 population sweep has not reached this point.</summary>
        Uninitialized = 0,

        /// <summary>A mob is alive; <see cref="SpawnPoint.CurrentEntityID"/> is valid.</summary>
        Live = 1,

        /// <summary>The mob died; the corpse linger countdown runs in <see cref="SpawnPoint.TimerTick"/>.</summary>
        Dying = 2,

        /// <summary>The corpse was removed; the respawn delay countdown runs in <see cref="SpawnPoint.TimerTick"/>.</summary>
        Respawning = 3,

        /// <summary>No slot was available; the point waits on the deferred list.</summary>
        Deferred = 4,

        /// <summary>The zone is shutting down; the point does nothing further.</summary>
        Cancelled = 5,
    }
}

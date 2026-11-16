using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// Runtime state of one spawn point (mob-spawning.md CR-MS-3). A struct held in a pre-allocated array so the
    /// per-tick scan creates no garbage. Mutated in place by <see cref="MobSpawningService"/> on the tick thread;
    /// tests read copies through the service's internal accessors.
    /// </summary>
    internal struct SpawnPoint
    {
        /// <summary>Current lifecycle state.</summary>
        public SpawnPointState State;

        /// <summary>The mob's id in Live and Dying; <see cref="EntityID.Invalid"/> in every other state.</summary>
        public EntityID CurrentEntityID;

        /// <summary>Countdown in ticks for Dying and Respawning; 0 in every other state.</summary>
        public int TimerTick;

        /// <summary>Index of this point's authored entry (declaration order); gives access to id, type and position.</summary>
        public int EntryIndex;
    }
}

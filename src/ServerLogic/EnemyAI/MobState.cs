namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>Server-side behaviour state of one mob (design/gdd/enemy-ai.md, States and Transitions).</summary>
    public enum MobState
    {
        /// <summary>Idle at the spawn point, scanning for a target.</summary>
        Dormant = 0,

        /// <summary>Chasing a target.</summary>
        Pursuing = 1,

        /// <summary>Attack telegraph before the hit.</summary>
        WindingUp = 2,

        /// <summary>The attack tick(s) where the hit is resolved.</summary>
        AttackExecution = 3,

        /// <summary>Post-attack recovery.</summary>
        Recovering = 4,

        /// <summary>Walking back to the spawn point.</summary>
        Returning = 5,

        /// <summary>Dead; terminal until Mob Spawning respawns the mob.</summary>
        Dead = 6,
    }
}

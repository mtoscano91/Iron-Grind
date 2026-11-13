namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Mob tier. Governs the respawn delay tier (Mob Spawning CR-MS-9); stored on <see cref="MobDefinition"/>.
    /// The numeric values are explicit because serialized assets depend on them: never reorder or renumber.
    /// </summary>
    public enum MobTier
    {
        /// <summary>Common mob.</summary>
        Common = 0,
        /// <summary>Uncommon mob.</summary>
        Uncommon = 1,
        /// <summary>Rare mob.</summary>
        Rare = 2,
        /// <summary>Elite mob.</summary>
        Elite = 3
    }
}

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// The effect triggered when a consumable item is used. Stored in
    /// <see cref="ConsumableData.EffectType"/> and interpreted by the consumable
    /// use system at runtime.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for IL2CPP efficiency.
    /// </remarks>
    public enum EffectType : byte
    {
        /// <summary>Restores a flat amount of hit points determined by <see cref="ConsumableData.EffectMagnitude"/>.</summary>
        RestoreHP = 0,

        /// <summary>Restores a flat amount of mana points determined by <see cref="ConsumableData.EffectMagnitude"/>.</summary>
        RestoreMP = 1,
    }
}

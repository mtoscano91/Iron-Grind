namespace IronGrind.StatusEffects
{
    /// <summary>Outcome of applying a buff to an entity (status-effects.md).</summary>
    public enum ApplyResult
    {
        /// <summary>The effect was applied.</summary>
        Success,

        /// <summary>The entity's modifier capacity would be exceeded.</summary>
        CapacityExceeded,

        /// <summary>The entity has no free effect slot.</summary>
        EffectSlotsFull,

        /// <summary>The duration is zero or above the authoring ceiling.</summary>
        InvalidDuration,

        /// <summary>A modifier targets a stat that buffs may not change.</summary>
        InvalidStat,

        /// <summary>The target entity is invalid.</summary>
        InvalidTarget,

        /// <summary>A modifier amount is not a finite number or is out of range.</summary>
        InvalidAmount,

        /// <summary>The definition changes nothing (no modifiers and no regen).</summary>
        NoOpDefinition,
    }
}

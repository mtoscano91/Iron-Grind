namespace IronGrind.SkillSystem
{
    /// <summary>
    /// One skill slot of an entity (skill-system.md States and Transitions; Skill System Story 002).
    /// Server-only; not a wire type.
    /// </summary>
    public struct SkillInstance
    {
        /// <summary>The skill in this slot; <see cref="SkillID.Invalid"/> for an unused slot.</summary>
        public SkillID SkillID;

        /// <summary>True once the entity's level reached the skill's UnlockLevel.</summary>
        public bool IsUnlocked;

        /// <summary>Server tick at which the cooldown ends; 0 = ready.</summary>
        public uint CooldownExpiryTick;
    }
}

namespace IronGrind.SkillSystem
{
    /// <summary>Which targets a skill accepts (skill-system.md SkillDefinition Schema).</summary>
    public enum TargetConstraint : byte
    {
        /// <summary>One hostile target.</summary>
        SingleHostile = 0,

        /// <summary>One friendly target.</summary>
        SingleFriendly = 1,

        /// <summary>The caster only.</summary>
        Self = 2,
    }
}

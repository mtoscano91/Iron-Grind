namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Character class (skill-system.md SkillDefinition Schema; registered in entities.yaml). The Leveling and
    /// Class registry APIs take a <see cref="byte"/>; callers cast.
    /// </summary>
    public enum ClassType : byte
    {
        /// <summary>Warrior.</summary>
        Warrior = 0,

        /// <summary>Healer.</summary>
        Healer = 1,
    }
}

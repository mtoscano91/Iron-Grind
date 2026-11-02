namespace IronGrind.SkillSystem
{
    /// <summary>The stat a skill's damage scales from (skill-system.md SkillDefinition Schema; entities.yaml).</summary>
    public enum DamageStat : byte
    {
        /// <summary>Attack power (default for physical damage).</summary>
        AttackPower = 0,

        /// <summary>Intelligence (default for magical damage).</summary>
        Intelligence = 1,

        /// <summary>Strength (STR-scaling Healer skills).</summary>
        Strength = 2,
    }
}

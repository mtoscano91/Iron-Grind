namespace IronGrind.SkillSystem
{
    /// <summary>
    /// What one accepted cast did, filled by the execution steps and handed to <see cref="ISkillCastOutput.SendAccepted"/>
    /// (skill-system.md CR-SK-7, F-SK-3; Skill System Story 008). Zeroed per cast; a stack local in the executor, so it allocates nothing.
    /// </summary>
    /// <remarks>
    /// Single-target at MVP (CR-SK-17): the fields describe the one target. AoE would need per-target results.
    /// Story 009 serializes these into the cast result message.
    /// </remarks>
    public struct SkillExecutionOutcome
    {
        /// <summary>Final damage dealt to the target; 0 for a miss, a non-damage skill, or a result with no damage.</summary>
        public int DamageDealt;

        /// <summary>True when the damage hit was a critical hit (only set when damage was dealt).</summary>
        public bool IsCrit;

        /// <summary>True when this cast's damage killed the target (only set when damage was dealt).</summary>
        public bool IsKill;

        /// <summary>
        /// HP of the instant heal delivered: floor(INT x HealMultiplier) when Status Effects accepted the heal; 0 for a skill
        /// without InstantHeal, a rejected heal (dead or unknown target, amount 0) or a target this cast killed.
        /// </summary>
        public int HealAmount;
    }
}

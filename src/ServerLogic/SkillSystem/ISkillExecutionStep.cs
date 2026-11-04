namespace IronGrind.SkillSystem
{
    /// <summary>
    /// The execution seam between V-9 and the cooldown write: Story 007 (NotifySkillUsed, damage) then Story 008
    /// (effects) plug in behind it (skill-system.md CR-SK-2 to CR-SK-8; Story 006).
    /// </summary>
    public interface ISkillExecutionStep
    {
        /// <summary>Executes an accepted cast whose mana is already deducted. Must not allocate.</summary>
        /// <param name="cast">The validated cast.</param>
        void Execute(in ValidatedSkillCast cast);
    }
}

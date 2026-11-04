namespace IronGrind.SkillSystem
{
    /// <summary>
    /// The stage after V-0 to V-8: Story 006 adds V-9 (mana) and the cooldown write behind this seam, Story 007 the
    /// execution.
    /// </summary>
    public interface ISkillCastValidatedSink
    {
        /// <summary>Receives a cast that passed V-0 to V-8.</summary>
        /// <param name="cast">The validated cast.</param>
        void Accept(in ValidatedSkillCast cast);
    }
}

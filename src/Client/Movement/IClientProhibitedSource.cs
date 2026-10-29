namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Seam through which the client learns it is Prohibited (stunned or rooted, movement-system.md CR-MOV-8,
    /// AC-MOV-19; Movement Story 011). No wire message exists yet, so the real source is a follow-up; tests use a fake.
    /// </summary>
    public interface IClientProhibitedSource
    {
        /// <summary>True while the character may not move (stun or root).</summary>
        bool IsProhibited { get; }
    }
}

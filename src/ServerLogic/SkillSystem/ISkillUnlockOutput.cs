using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Narrow seam for the <c>SkillUnlockNotification</c> send (skill-system.md CR-SK-21; Skill System Story 003).
    /// </summary>
    public interface ISkillUnlockOutput
    {
        /// <summary>Tells the owner's client that <paramref name="skillId"/> unlocked. Must not throw.</summary>
        /// <param name="owner">The entity that owns the skill.</param>
        /// <param name="skillId">The skill that unlocked.</param>
        void SendUnlock(EntityID owner, SkillID skillId);
    }
}

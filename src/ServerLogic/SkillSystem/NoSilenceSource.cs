using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Production stand-in for <see cref="ISkillSilenceSource"/> (Decision A, 2026-11-02): status-effects.md defines no
    /// silence effect, so nobody is ever silenced. Replace when a silence query is designed.
    /// </summary>
    public sealed class NoSilenceSource : ISkillSilenceSource
    {
        /// <inheritdoc />
        public bool IsSilenced(EntityID entity)
        {
            return false;
        }
    }
}

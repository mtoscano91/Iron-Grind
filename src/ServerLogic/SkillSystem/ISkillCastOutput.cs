using IronGrind.CharacterStats;
using IronGrind.Networking;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Where the cast step reports a rejected cast (skill-system.md V-0 to V-9). Declared by Story 005; Story 009
    /// implements it as the <c>SkillCastResult</c> wire message.
    /// </summary>
    public interface ISkillCastOutput
    {
        /// <summary>Reports that a cast was rejected.</summary>
        /// <param name="caster">The caster.</param>
        /// <param name="request">The request as received.</param>
        /// <param name="code">The first failed check's code.</param>
        void SendRejection(EntityID caster, in SkillCastRequest request, SkillCastRejectionCode code);
    }
}

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

        /// <summary>
        /// Reports that a cast was accepted and its cooldown registered (Story 006 calls it after the cooldown write;
        /// Story 009 implements the wire side, <c>SkillCooldownUpdate</c>).
        /// </summary>
        /// <param name="caster">The caster.</param>
        /// <param name="request">The request as received.</param>
        /// <param name="newCooldownTicks">The skill's <c>CooldownTicks</c>, the length of the cooldown just started.</param>
        void SendAccepted(EntityID caster, in SkillCastRequest request, int newCooldownTicks);
    }
}

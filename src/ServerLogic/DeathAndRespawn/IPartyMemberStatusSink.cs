using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>Seam to the Party system for member death status (CR-DR-5, AC-DR-13, TR-dr-009).</summary>
    public interface IPartyMemberStatusSink
    {
        /// <summary>Reports a member's death status.</summary>
        /// <param name="entity">The player.</param>
        /// <param name="status">The new status.</param>
        void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status);
    }
}

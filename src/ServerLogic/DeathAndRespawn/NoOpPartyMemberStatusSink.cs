using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.DeathAndRespawn
{
    /// <summary>Production placeholder until the Party system exists: does nothing (CR-DR-5).</summary>
    public sealed class NoOpPartyMemberStatusSink : IPartyMemberStatusSink
    {
        /// <inheritdoc />
        public void SetMemberStatus(EntityID entity, PartyMemberDeathStatus status)
        {
        }
    }
}

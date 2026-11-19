using IronGrind.CharacterStats;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>Reads a member's level for XP eligibility (CR-PS-5; Party System Story 004).</summary>
    public interface IPartyMemberLevelSource
    {
        /// <summary>Reads an entity's level. <c>EntityID.Invalid</c> must return false.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="level">The level, or 0 when unreadable.</param>
        /// <returns>True when the level is readable.</returns>
        bool TryGetLevel(EntityID entity, out int level);
    }
}

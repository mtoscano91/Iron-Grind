using IronGrind.CharacterStats;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Answers "are these two entities in the same formal party" for the cast-result recipient rule
    /// (skill-system.md CR-SK-9; Skill System Story 009, readiness decision 2026-11-04). The real Party System replaces
    /// the stand-in behind this seam later; <see cref="ZoneSkillCastPartySource"/> is the production adapter over
    /// <c>IPartyZonePresence</c>.
    /// </summary>
    /// <remarks>Tick thread only. Implementations must not allocate.</remarks>
    public interface ISkillCastPartySource
    {
        /// <summary>
        /// True when both entities are in the same formal party. A solo pseudo-party counts as "no party", and an
        /// entity that is not a player (or is unknown) is in no party.
        /// </summary>
        /// <param name="a">The first entity.</param>
        /// <param name="b">The second entity.</param>
        /// <returns>True when both are in one formal party.</returns>
        bool AreInSameParty(EntityID a, EntityID b);
    }
}

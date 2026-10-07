using IronGrind.Currency;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Consumer-side query for "does this player have an active NPC session" (CR-ENH-17). Declared
    /// here so the Enhancement System depends on no concrete session class; Enhancement Story 006
    /// provides the implementation, which is shared with the NPC Shop. Same pattern as the Loot
    /// Table module's <c>IPartyService</c>. Enhancement Story 003.
    /// </summary>
    public interface INpcInteractionSessions
    {
        /// <summary>True iff <paramref name="charId"/> currently has an active NPC interaction session (CR-ENH-17).</summary>
        /// <param name="charId">The character to query.</param>
        bool IsActive(CharacterID charId);
    }
}

using IronGrind.Currency;

namespace IronGrind.NpcInteraction
{
    /// <summary>
    /// Consumer-side query for "does this player have an active NPC session" (enhancement-system.md
    /// CR-ENH-17; npc-shop.md CR-SHOP-3). Lives in the neutral <c>IronGrind.NpcInteraction</c>
    /// namespace, shared by the Enhancement System and the NPC Shop, so neither references the other.
    /// Implemented by <see cref="NpcInteractionSessionTracker"/>. Enhancement Story 003 declared it;
    /// Story 006 moved it here. Same pattern as the Loot Table module's <c>IPartyService</c>.
    /// </summary>
    public interface INpcInteractionSessions
    {
        /// <summary>True iff <paramref name="charId"/> currently has an active NPC interaction session (CR-ENH-17).</summary>
        /// <param name="charId">The character to query.</param>
        bool IsActive(CharacterID charId);
    }
}

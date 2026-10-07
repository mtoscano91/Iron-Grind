namespace IronGrind.NpcInteraction
{
    /// <summary>
    /// Result of <see cref="NpcInteractionSessionTracker.Open"/>; mirrors the wire responses
    /// <c>NPCInteractionOpened</c> and <c>RejectedNotInTownHub</c> (enhancement-system.md CR-ENH-16,
    /// npc-shop.md CR-SHOP-3). Enhancement Story 006.
    /// </summary>
    public enum NpcInteractionOpenResult
    {
        /// <summary>The session was opened (any previous session was replaced silently).</summary>
        NPCInteractionOpened = 0,

        /// <summary>The character is not in the town hub; no state changed (CR-ENH-16).</summary>
        RejectedNotInTownHub = 1,
    }
}

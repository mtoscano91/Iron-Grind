using IronGrind.Currency;

namespace IronGrind.NpcInteraction
{
    /// <summary>
    /// Consumer-side query for "is this character in the town hub" (enhancement-system.md CR-ENH-16;
    /// npc-shop.md CR-SHOP-3 step 2). Zone Instancing has no code yet, so this module declares the
    /// query it needs and tests stub it; the Zone Instancing epic provides the real implementation.
    /// Enhancement Story 006.
    /// </summary>
    public interface ITownHubQuery
    {
        /// <summary>True iff <paramref name="charId"/> is currently in the town hub zone (CR-ENH-16).</summary>
        /// <param name="charId">The character to query.</param>
        bool IsInTownHub(CharacterID charId);
    }
}

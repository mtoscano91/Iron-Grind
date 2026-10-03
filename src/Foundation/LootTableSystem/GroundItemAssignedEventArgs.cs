using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="IGroundItemService.OnGroundItemAssigned"/> (ADR-010 Tier 2; mirrors the
    /// GroundItemAssigned wire message). Raised when an item's assignee is set after it spawned: the
    /// round-robin fallback of a zero-bid auction (design/gdd/loot-table-system.md CR-LT-10).
    /// </summary>
    public readonly struct GroundItemAssignedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public GroundItemAssignedEventArgs(GroundItemID groundItemId, CharacterID assignedTo)
        {
            GroundItemId = groundItemId;
            AssignedTo = assignedTo;
        }

        /// <summary>The ground item that was assigned.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The character the item is now assigned to.</summary>
        public CharacterID AssignedTo { get; }
    }
}

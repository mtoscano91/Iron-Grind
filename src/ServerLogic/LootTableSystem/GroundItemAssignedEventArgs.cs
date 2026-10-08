using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="IGroundItemService.OnGroundItemAssigned"/> (ADR-010 Tier 2; mirrors the
    /// GroundItemAssigned wire message). Raised when an item's assignee is set after it spawned: the
    /// round-robin fallback of an auction with no payable bid (design/gdd/loot-table-system.md CR-LT-10).
    /// </summary>
    public readonly struct GroundItemAssignedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public GroundItemAssignedEventArgs(GroundItemID groundItemId, CharacterID assignedTo, uint expiryTick)
        {
            GroundItemId = groundItemId;
            AssignedTo = assignedTo;
            ExpiryTick = expiryTick;
        }

        /// <summary>The item's current expiry tick; the network layer fills <c>GroundItemAssigned.expiryTick</c> from it.</summary>
        public uint ExpiryTick { get; }

        /// <summary>The ground item that was assigned.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The character the item is now assigned to.</summary>
        public CharacterID AssignedTo { get; }
    }
}

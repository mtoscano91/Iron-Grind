namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Lifecycle state of a ground item (design/gdd/loot-table-system.md, States and Transitions).
    /// <see cref="Inventory"/> and <see cref="Despawned"/> are terminal: the record is destroyed.
    /// </summary>
    public enum GroundItemState : byte
    {
        /// <summary>Just spawned; lasts exactly one server tick, during which the spawn event is raised.</summary>
        Spawning = 0,

        /// <summary>Visible and waiting for pickup by its assigned character (or expiry).</summary>
        Assigned = 1,

        /// <summary>A rare drop under auction for its party (CR-LT-8). Entered from <see cref="Spawning"/>; it has no assignee.</summary>
        Auctioning = 2,

        /// <summary>A pickup call is in flight (CR-LT-7). The call is synchronous, so this state is only visible to code that runs inside it.</summary>
        Claiming = 3,

        /// <summary>Terminal: the item reached the assignee's inventory.</summary>
        Inventory = 4,

        /// <summary>Terminal: the item expired (CR-LT-12), or its pickup call threw, and it was removed.</summary>
        Despawned = 5,
    }
}

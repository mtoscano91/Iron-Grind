using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Read-only snapshot of a ground item record (design/gdd/loot-table-system.md, CR-LT-12 and the
    /// GroundItem state table). A snapshot: it does not change when the service advances the item.
    /// </summary>
    public readonly struct GroundItem
    {
        /// <summary>Creates a snapshot.</summary>
        public GroundItem(
            GroundItemID id,
            ItemID itemId,
            UnityEngine.Vector3 position,
            CharacterID assignedTo,
            uint spawnTick,
            uint expiryTick,
            GroundItemState state,
            int pauseBudgetRemaining,
            PartyID partyId,
            uint windowCloseTick)
        {
            PartyId = partyId;
            WindowCloseTick = windowCloseTick;
            Id = id;
            ItemId = itemId;
            Position = position;
            AssignedTo = assignedTo;
            SpawnTick = spawnTick;
            ExpiryTick = expiryTick;
            State = state;
            PauseBudgetRemaining = pauseBudgetRemaining;
        }

        /// <summary>The ground item's identifier.</summary>
        public GroundItemID Id { get; }

        /// <summary>The dropped item.</summary>
        public ItemID ItemId { get; }

        /// <summary>World position of the item.</summary>
        public UnityEngine.Vector3 Position { get; }

        /// <summary>The character the item is assigned to; <see cref="CharacterID.Invalid"/> for an item under auction.</summary>
        public CharacterID AssignedTo { get; }

        /// <summary>The server tick at which the item spawned.</summary>
        public uint SpawnTick { get; }

        /// <summary>The server tick at which the item expires (CR-LT-12).</summary>
        public uint ExpiryTick { get; }

        /// <summary>Current lifecycle state.</summary>
        public GroundItemState State { get; }

        /// <summary>Remaining TTL pause budget in ticks; starts at <see cref="LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS"/> and is spent by the TTL pause on app background (CR-LT-13.1).</summary>
        public int PauseBudgetRemaining { get; }

        /// <summary>The auction's party for a Rare auction drop; <see cref="PartyID.Uninitialized"/> for a common drop.</summary>
        public PartyID PartyId { get; }

        /// <summary>Server tick at which the auction window closes (CR-LT-8). Meaningful only while <see cref="GroundItemState.Auctioning"/>; 0 otherwise.</summary>
        public uint WindowCloseTick { get; }
    }
}

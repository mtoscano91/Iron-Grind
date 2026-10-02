using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="GroundItemService.OnGroundItemSpawned"/> (ADR-010 Tier 2; mirrors the
    /// GroundItemSpawned wire message in design/gdd/networking-wire-protocol.md).
    /// </summary>
    public readonly struct GroundItemSpawnedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public GroundItemSpawnedEventArgs(
            GroundItemID groundItemId,
            ItemID itemId,
            GearTier gearTier,
            bool isAuction,
            UnityEngine.Vector3 position,
            uint expiryTick,
            CharacterID assignedTo,
            PartyID partyId)
        {
            PartyId = partyId;
            GroundItemId = groundItemId;
            ItemId = itemId;
            GearTier = gearTier;
            IsAuction = isAuction;
            Position = position;
            ExpiryTick = expiryTick;
            AssignedTo = assignedTo;
        }

        /// <summary>The new ground item.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The dropped item.</summary>
        public ItemID ItemId { get; }

        /// <summary>Gear tier from the equipment cache; <see cref="ItemDatabase.GearTier.None"/> on a cache miss.</summary>
        public GearTier GearTier { get; }

        /// <summary>True for a Rare auction drop (CR-LT-8).</summary>
        public bool IsAuction { get; }

        /// <summary>World position of the item.</summary>
        public UnityEngine.Vector3 Position { get; }

        /// <summary>The server tick at which the item expires.</summary>
        public uint ExpiryTick { get; }

        /// <summary>The character the item is assigned to (a common drop's message goes to this character only).</summary>
        public CharacterID AssignedTo { get; }

        /// <summary>
        /// The auction's party when <see cref="IsAuction"/> is true: the network layer sends the message
        /// to its current members and <see cref="AssignedTo"/> is <see cref="CharacterID.Invalid"/>.
        /// <see cref="PartyID.Uninitialized"/> for a common drop.
        /// </summary>
        public PartyID PartyId { get; }
    }
}

using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="IGroundItemService.OnBagFullPickupBlocked"/> (ADR-010 Tier 2; mirrors the
    /// BagFullPickupBlocked wire message in design/gdd/networking-wire-protocol.md). Truncating
    /// <see cref="DisplayName"/> to the wire's 24 UTF-8 bytes is the codec's job, not this payload's.
    /// </summary>
    public readonly struct BagFullPickupBlockedEventArgs
    {
        /// <summary>Creates the payload. A null <paramref name="displayName"/> becomes an empty string.</summary>
        public BagFullPickupBlockedEventArgs(
            GroundItemID groundItemId,
            ItemID itemId,
            string displayName,
            uint remainingTicks,
            CharacterID recipient)
        {
            GroundItemId = groundItemId;
            ItemId = itemId;
            DisplayName = displayName ?? string.Empty;
            RemainingTicks = remainingTicks;
            Recipient = recipient;
        }

        /// <summary>The ground item whose pickup was blocked.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The blocked item.</summary>
        public ItemID ItemId { get; }

        /// <summary>The Item Database display name; empty when the item is unknown. Never null.</summary>
        public string DisplayName { get; }

        /// <summary>Ticks left before the item expires: <c>expiryTick - currentTick</c> (wraparound-safe).</summary>
        public uint RemainingTicks { get; }

        /// <summary>The assigned character, the only one who is told.</summary>
        public CharacterID Recipient { get; }
    }
}

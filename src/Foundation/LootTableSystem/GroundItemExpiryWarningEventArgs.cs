using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="IGroundItemService.OnGroundItemExpiryWarning"/> (ADR-010 Tier 2; mirrors
    /// the GroundItemExpiryWarning wire message in design/gdd/networking-wire-protocol.md).
    /// Truncating <see cref="DisplayName"/> to the wire's 24 UTF-8 bytes is the codec's job.
    /// </summary>
    public readonly struct GroundItemExpiryWarningEventArgs
    {
        /// <summary>Creates the payload. A null <paramref name="displayName"/> becomes an empty string.</summary>
        public GroundItemExpiryWarningEventArgs(
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

        /// <summary>The ground item about to expire.</summary>
        public GroundItemID GroundItemId { get; }

        /// <summary>The item on the ground.</summary>
        public ItemID ItemId { get; }

        /// <summary>The Item Database display name; empty when the item is unknown. Never null.</summary>
        public string DisplayName { get; }

        /// <summary>Ticks left before the item expires: <c>expiryTick - currentTick</c>, equal to <c>EXPIRY_WARNING_TICKS</c> when raised.</summary>
        public uint RemainingTicks { get; }

        /// <summary>The assigned character, the only one who is told.</summary>
        public CharacterID Recipient { get; }
    }
}

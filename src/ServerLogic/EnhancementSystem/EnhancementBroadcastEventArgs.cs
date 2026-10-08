using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Payload of <see cref="EnhancementService.OnEnhancementBroadcastLevelReached"/>
    /// (design/gdd/enhancement-system.md CR-ENH-14, AC-ENH-18, EC-ENH-8; ADR-010). Server-side trigger for
    /// the ServerBroadcast_Enhancement9 message: carries ids, not names (Story 010 resolves the player and
    /// item display names). Declared in this system's namespace like the other systems' event arguments.
    /// Enhancement Story 007.
    /// </summary>
    public readonly struct EnhancementBroadcastEventArgs
    {
        /// <summary>Creates the payload.</summary>
        /// <param name="characterId">The character who reached the broadcast level.</param>
        /// <param name="itemId">The enhanced item.</param>
        /// <param name="level">The level reached (<see cref="EnhancementConstants.SERVER_BROADCAST_LEVEL"/>).</param>
        public EnhancementBroadcastEventArgs(CharacterID characterId, ItemID itemId, byte level)
        {
            CharacterId = characterId;
            ItemId = itemId;
            Level = level;
        }

        /// <summary>The character who reached the broadcast level.</summary>
        public CharacterID CharacterId { get; }

        /// <summary>The enhanced item.</summary>
        public ItemID ItemId { get; }

        /// <summary>The level reached.</summary>
        public byte Level { get; }
    }
}

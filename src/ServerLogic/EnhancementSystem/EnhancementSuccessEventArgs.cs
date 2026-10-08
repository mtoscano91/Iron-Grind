using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Payload of <see cref="EnhancementService.OnEnhancementSuccess"/> (design/gdd/enhancement-system.md
    /// CR-ENH-15 steps 7-9, CR-ENH-11; ADR-010). Server-side: raised from
    /// <see cref="EnhancementService.CompleteAttempt"/> after a successful commit. Carries ids, not names.
    /// Declared in this system's namespace like the other systems' event arguments. Enhancement Story 007.
    /// </summary>
    public readonly struct EnhancementSuccessEventArgs
    {
        /// <summary>Creates the payload.</summary>
        /// <param name="characterId">The character who enhanced the item.</param>
        /// <param name="itemId">The enhanced item.</param>
        /// <param name="newLevel">The item's level after the success.</param>
        public EnhancementSuccessEventArgs(CharacterID characterId, ItemID itemId, byte newLevel)
        {
            CharacterId = characterId;
            ItemId = itemId;
            NewLevel = newLevel;
        }

        /// <summary>The character who enhanced the item.</summary>
        public CharacterID CharacterId { get; }

        /// <summary>The enhanced item.</summary>
        public ItemID ItemId { get; }

        /// <summary>The item's level after the success.</summary>
        public byte NewLevel { get; }
    }
}

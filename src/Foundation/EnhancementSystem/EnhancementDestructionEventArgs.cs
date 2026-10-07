using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Payload of <see cref="EnhancementService.OnEnhancementDestruction"/> (design/gdd/enhancement-system.md
    /// CR-ENH-15 steps 7-9, CR-ENH-11; ADR-010). Server-side: raised from
    /// <see cref="EnhancementService.CompleteAttempt"/> after a successful commit. Carries ids, not names.
    /// Declared in this system's namespace like the other systems' event arguments. Enhancement Story 007.
    /// </summary>
    public readonly struct EnhancementDestructionEventArgs
    {
        /// <summary>Creates the payload.</summary>
        /// <param name="characterId">The character whose item was destroyed.</param>
        /// <param name="itemId">The destroyed item.</param>
        public EnhancementDestructionEventArgs(CharacterID characterId, ItemID itemId)
        {
            CharacterId = characterId;
            ItemId = itemId;
        }

        /// <summary>The character whose item was destroyed.</summary>
        public CharacterID CharacterId { get; }

        /// <summary>The destroyed item.</summary>
        public ItemID ItemId { get; }
    }
}

using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Payload of <see cref="IEquipmentService.OnAppearanceChanged"/> (design/gdd/equipment-system.md
    /// CR-EQS-11, EC-EQS-8; ADR-010). Server-side: raised at the end of an <c>Equip</c> or
    /// <c>Unequip</c> call whose <c>equipmentAppearanceFlags</c> byte changed. Equipment Story 005;
    /// Story 009 delivers it to the zone.
    /// </summary>
    public readonly struct EquipmentAppearanceChangedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        /// <param name="characterId">The character whose gear changed.</param>
        /// <param name="entityId">The character's entity.</param>
        /// <param name="flags">The new <c>equipmentAppearanceFlags</c> byte.</param>
        public EquipmentAppearanceChangedEventArgs(CharacterID characterId, EntityID entityId, byte flags)
        {
            CharacterId = characterId;
            EntityId = entityId;
            Flags = flags;
        }

        /// <summary>The character whose gear changed.</summary>
        public CharacterID CharacterId { get; }

        /// <summary>The character's entity.</summary>
        public EntityID EntityId { get; }

        /// <summary>The new <c>equipmentAppearanceFlags</c> byte (CR-EQS-11).</summary>
        public byte Flags { get; }
    }
}

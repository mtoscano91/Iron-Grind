using IronGrind.CharacterStats;

namespace IronGrind.StatusEffects
{
    /// <summary>
    /// Maps an <see cref="EntityID"/> to its dense slot index in the effect table (CR-SE-15).
    /// The production implementation is <see cref="ZoneSlotPoolEntityIndex"/> (Story 009).
    /// </summary>
    public interface IEntitySlotIndex
    {
        /// <summary>Looks up the slot index of an entity.</summary>
        /// <param name="id">The entity.</param>
        /// <param name="index">Slot index in [0, <see cref="StatusEffectsConstants.MAX_ENTITY_SLOTS"/>) when found.</param>
        /// <returns>True when the entity has a slot; false for an unknown entity.</returns>
        bool TryGetSlotIndex(EntityID id, out int index);
    }
}

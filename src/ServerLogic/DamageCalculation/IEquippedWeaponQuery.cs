using IronGrind.CharacterStats;

namespace IronGrind.DamageCalculation
{
    /// <summary>
    /// Read seam through which Damage Calculation asks what weapon an entity wields
    /// (design/gdd/damage-calculation.md Resolution Sequence Step 4). The Equipment System is not built
    /// yet; it implements this interface later. Declared here so the resolver does not wait for the
    /// Equipment epic. Damage Calculation Story 002.
    /// </summary>
    /// <remarks>
    /// Implementations must be side-effect free: the resolver is read-only and calls these methods at
    /// most once each per hit.
    /// </remarks>
    public interface IEquippedWeaponQuery
    {
        /// <summary>Returns the <see cref="ItemID"/> of the weapon the entity has equipped.</summary>
        /// <param name="entityId">The entity to query.</param>
        /// <returns>The weapon's item id, or <see cref="ItemID.Invalid"/> when no weapon is equipped.</returns>
        ItemID GetEquippedWeaponID(EntityID entityId);

        /// <summary>Returns the enhancement level of the entity's equipped weapon.</summary>
        /// <param name="entityId">The entity to query.</param>
        /// <returns>The enhancement level; meaningful only when a weapon is equipped.</returns>
        byte GetEquippedWeaponEnhancementLevel(EntityID entityId);
    }
}

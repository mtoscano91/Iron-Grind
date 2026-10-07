using System;
using IronGrind.ItemDatabase;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Read contract for the stat bonuses an enhancement level grants
    /// (design/gdd/enhancement-system.md F-ENH-1, F-ENH-2, AC-ENH-19 to AC-ENH-21). Owned by the
    /// Enhancement System; consumed by the Equipment System (enhanced modifier registration) and
    /// by Damage Calculation (elemental damage input for F-DC-2). Inject this interface; never
    /// depend on <see cref="EnhancementBonusProvider"/> directly (ADR-010). Enhancement Story 002.
    /// </summary>
    /// <remarks>
    /// Both methods are pure arithmetic over an injected <see cref="EnhancementConfig"/>. Callers
    /// pass a level validated when the item was stored, so an out-of-range level is a caller bug
    /// and throws.
    /// </remarks>
    public interface IEnhancementBonusProvider
    {
        /// <summary>
        /// Enhanced flat stat bonus (F-ENH-1): <c>baseFlatBonus + level * BonusPerLevel[gearTier]</c>.
        /// </summary>
        /// <param name="level">Enhancement level, 0 to <see cref="EnhancementConfig.MaxEnhancementLevel"/>.</param>
        /// <param name="baseFlatBonus">
        /// Item's unenhanced flat bonus. Not validated. The GDD declares this as <c>int</c> while the
        /// Item Database's <c>StatModifierEntry.FlatBonus</c> is a <c>float</c>; the caller converts.
        /// </param>
        /// <param name="gearTier">Tier of the item; selects the per-level bonus.</param>
        /// <returns>The enhanced flat bonus.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="level"/> is negative or above the cap, or <paramref name="gearTier"/> is
        /// <see cref="GearTier.None"/> or an undefined value.
        /// </exception>
        int GetFlatBonus(int level, int baseFlatBonus, GearTier gearTier);

        /// <summary>
        /// Enhanced elemental damage, weapons only (F-ENH-2):
        /// <c>min(baseElementalDamage + level * ElementalBonusPerLevel[gearTier], ElementalDamageCeiling)</c>.
        /// </summary>
        /// <param name="level">Enhancement level, 0 to <see cref="EnhancementConfig.MaxEnhancementLevel"/>.</param>
        /// <param name="baseElementalDamage">Item's unenhanced elemental damage. Not validated.</param>
        /// <param name="gearTier">Tier of the item; selects the per-level bonus.</param>
        /// <param name="isWeapon">False for non-weapons, which always yield 0 (AC-ENH-21).</param>
        /// <returns>The enhanced elemental damage, capped at the ceiling; 0 when not a weapon.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Only when <paramref name="isWeapon"/> is true: <paramref name="level"/> is negative or above
        /// the cap, or <paramref name="gearTier"/> is <see cref="GearTier.None"/> or undefined. A
        /// non-weapon call returns 0 before any other check and never throws.
        /// </exception>
        int GetElementalBonus(int level, int baseElementalDamage, GearTier gearTier, bool isWeapon);
    }
}

#nullable enable

using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Injectable seam for the accessory merge's defense-in-depth equipped check (CR-EQS-14
    /// precondition 1, AC-EQS-27). Internal: the production implementation lives in
    /// <see cref="EquipmentService"/>; tests replace it to force the <see cref="MergeResult.RejectedEquipped"/> path.
    /// </summary>
    internal interface IMergeEquippedCheck
    {
        /// <summary>
        /// Returns whether the bag item at <paramref name="bagSlotIndex"/> is also present in an
        /// equipment slot (an inconsistent server state).
        /// </summary>
        /// <param name="charId">A registered character.</param>
        /// <param name="bagSlotIndex">The inventory slot holding the merge source.</param>
        /// <param name="itemId">The item in that slot.</param>
        /// <returns><see langword="true"/> when the source is also equipped; the merge is then rejected.</returns>
        bool IsSourceEquipped(CharacterID charId, int bagSlotIndex, ItemID itemId);
    }
}

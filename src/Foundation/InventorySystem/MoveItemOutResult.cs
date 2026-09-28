using IronGrind.CharacterStats;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.MoveItemOut"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="DiscardResult"/>).
    /// </summary>
    /// <remarks>
    /// A move-out is atomic (GDD Rule 8): on failure no slot was written and no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired.
    /// </remarks>
    public readonly struct MoveItemOutResult
    {
        /// <summary>The item removed from the slot, or <see cref="ItemID.Invalid"/> on any failure.</summary>
        public readonly ItemID ItemId;

        /// <summary><see cref="MoveItemOutCode.Success"/> iff the move-out applied; otherwise the reason it did not.</summary>
        public readonly MoveItemOutCode Code;

        /// <summary>Creates a new result.</summary>
        /// <param name="itemId">The removed item, or <see cref="ItemID.Invalid"/> on failure.</param>
        /// <param name="code"><see cref="MoveItemOutCode.Success"/> iff <paramref name="itemId"/> is a real item.</param>
        public MoveItemOutResult(ItemID itemId, MoveItemOutCode code)
        {
            ItemId = itemId;
            Code = code;
        }

        /// <summary>Creates a success result carrying the removed <paramref name="itemId"/>.</summary>
        /// <param name="itemId">The item that was removed from the slot.</param>
        public static MoveItemOutResult Succeeded(ItemID itemId)
        {
            return new MoveItemOutResult(itemId, MoveItemOutCode.Success);
        }

        /// <summary>Creates a failure result with the given <paramref name="code"/>.</summary>
        /// <param name="code">The failure code. Must not be <see cref="MoveItemOutCode.Success"/>.</param>
        public static MoveItemOutResult Fail(MoveItemOutCode code)
        {
            UnityEngine.Debug.Assert(code != MoveItemOutCode.Success, "MoveItemOutResult.Fail requires a non-Success code.");
            return new MoveItemOutResult(ItemID.Invalid, code);
        }
    }
}

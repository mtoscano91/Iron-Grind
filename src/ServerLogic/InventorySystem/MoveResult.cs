namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.Move"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="DiscardResult"/>).
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DiscardResult"/>, a move always carries both slots' post-operation
    /// states — on success <em>and</em> on failure — so the network handler can serialize the
    /// wire <c>MoveResultMessage</c> without re-reading either slot. On failure the slots are
    /// each side's unchanged current state, except an out-of-range index or an unregistered
    /// <c>charId</c>, for which both slots are <see cref="InventorySlot.Empty"/> (there is no
    /// real slot to report).
    /// </remarks>
    public readonly struct MoveResult
    {
        /// <summary><see langword="true"/> when the move (merge, swap, or relocate) applied.</summary>
        public readonly bool Success;

        /// <summary><see cref="MoveFailReason.None"/> iff <see cref="Success"/>; otherwise the reason the move did not apply.</summary>
        public readonly MoveFailReason Reason;

        /// <summary>The source slot's state after the operation (unchanged on failure).</summary>
        public readonly InventorySlot FromSlot;

        /// <summary>The destination slot's state after the operation (unchanged on failure).</summary>
        public readonly InventorySlot ToSlot;

        /// <summary>Creates a new result.</summary>
        /// <param name="success">Whether the move applied.</param>
        /// <param name="reason"><see cref="MoveFailReason.None"/> iff <paramref name="success"/>.</param>
        /// <param name="fromSlot">The source slot's post-operation state.</param>
        /// <param name="toSlot">The destination slot's post-operation state.</param>
        public MoveResult(bool success, MoveFailReason reason, InventorySlot fromSlot, InventorySlot toSlot)
        {
            Success = success;
            Reason = reason;
            FromSlot = fromSlot;
            ToSlot = toSlot;
        }

        /// <summary>Creates a success result carrying both slots' post-operation states.</summary>
        /// <param name="fromSlot">The source slot's state after the move.</param>
        /// <param name="toSlot">The destination slot's state after the move.</param>
        public static MoveResult Succeeded(InventorySlot fromSlot, InventorySlot toSlot)
        {
            return new MoveResult(true, MoveFailReason.None, fromSlot, toSlot);
        }

        /// <summary>Creates a failure result with the given <paramref name="reason"/>.</summary>
        /// <param name="reason">The failure reason. Must not be <see cref="MoveFailReason.None"/>.</param>
        /// <param name="fromSlot">The source slot's unchanged state (or <see cref="InventorySlot.Empty"/> for an out-of-range/unregistered failure).</param>
        /// <param name="toSlot">The destination slot's unchanged state (or <see cref="InventorySlot.Empty"/> for an out-of-range/unregistered failure).</param>
        public static MoveResult Fail(MoveFailReason reason, InventorySlot fromSlot, InventorySlot toSlot)
        {
            UnityEngine.Debug.Assert(reason != MoveFailReason.None, "MoveResult.Fail requires a non-None reason.");
            return new MoveResult(false, reason, fromSlot, toSlot);
        }
    }
}

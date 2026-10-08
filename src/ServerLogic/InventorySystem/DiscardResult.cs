namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.Discard"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="PickupResult"/>).
    /// </summary>
    /// <remarks>
    /// A discard is atomic (GDD Rule 6): on failure no slot was written and no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired.
    /// </remarks>
    public readonly struct DiscardResult
    {
        /// <summary>The canonical success result.</summary>
        public static readonly DiscardResult Succeeded = new DiscardResult(true, DiscardFailReason.None);

        /// <summary><see langword="true"/> when the requested quantity was discarded.</summary>
        public readonly bool Success;

        /// <summary><see cref="DiscardFailReason.None"/> iff <see cref="Success"/>; otherwise the reason the discard did not apply.</summary>
        public readonly DiscardFailReason Reason;

        /// <summary>Creates a new result.</summary>
        /// <param name="success">Whether the discard applied.</param>
        /// <param name="reason"><see cref="DiscardFailReason.None"/> iff <paramref name="success"/>.</param>
        public DiscardResult(bool success, DiscardFailReason reason)
        {
            Success = success;
            Reason = reason;
        }

        /// <summary>Creates a failure result with the given <paramref name="reason"/>.</summary>
        /// <param name="reason">The failure reason. Must not be <see cref="DiscardFailReason.None"/>.</param>
        public static DiscardResult Fail(DiscardFailReason reason)
        {
            UnityEngine.Debug.Assert(reason != DiscardFailReason.None, "DiscardResult.Fail requires a non-None reason.");
            return new DiscardResult(false, reason);
        }
    }
}

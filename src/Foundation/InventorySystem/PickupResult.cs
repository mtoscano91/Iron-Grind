namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.Pickup"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <c>GoldMutationResult</c>).
    /// </summary>
    /// <remarks>
    /// A pickup is atomic (GDD Rule 3): on failure no slot was written and no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired.
    /// </remarks>
    public readonly struct PickupResult
    {
        /// <summary>The canonical success result.</summary>
        public static readonly PickupResult Succeeded = new PickupResult(true, PickupFailReason.None);

        /// <summary><see langword="true"/> when every requested unit was placed.</summary>
        public readonly bool Success;

        /// <summary><see cref="PickupFailReason.None"/> iff <see cref="Success"/>; otherwise the reason the pickup did not apply.</summary>
        public readonly PickupFailReason Reason;

        /// <summary>Creates a new result.</summary>
        /// <param name="success">Whether the pickup applied.</param>
        /// <param name="reason"><see cref="PickupFailReason.None"/> iff <paramref name="success"/>.</param>
        public PickupResult(bool success, PickupFailReason reason)
        {
            Success = success;
            Reason = reason;
        }

        /// <summary>Creates a failure result with the given <paramref name="reason"/>.</summary>
        /// <param name="reason">The failure reason. Must not be <see cref="PickupFailReason.None"/>.</param>
        public static PickupResult Fail(PickupFailReason reason)
        {
            UnityEngine.Debug.Assert(reason != PickupFailReason.None, "PickupResult.Fail requires a non-None reason.");
            return new PickupResult(false, reason);
        }
    }
}

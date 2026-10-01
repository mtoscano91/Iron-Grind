namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.ConsumeItem"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="DiscardResult"/>).
    /// </summary>
    /// <remarks>
    /// A consume is atomic (plan-then-commit): on failure no slot was written and no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired.
    /// </remarks>
    public readonly struct ConsumeItemResult
    {
        /// <summary>The canonical success result.</summary>
        public static readonly ConsumeItemResult Succeeded = new ConsumeItemResult(true, ConsumeItemFailReason.None);

        /// <summary><see langword="true"/> when the requested quantity was consumed.</summary>
        public readonly bool Success;

        /// <summary><see cref="ConsumeItemFailReason.None"/> iff <see cref="Success"/>; otherwise the reason the consume did not apply.</summary>
        public readonly ConsumeItemFailReason Reason;

        private ConsumeItemResult(bool success, ConsumeItemFailReason reason)
        {
            Success = success;
            Reason = reason;
        }

        /// <summary>Creates a failure result with the given <paramref name="reason"/>.</summary>
        /// <param name="reason">The failure reason. Must not be <see cref="ConsumeItemFailReason.None"/>.</param>
        public static ConsumeItemResult Fail(ConsumeItemFailReason reason)
        {
            UnityEngine.Debug.Assert(reason != ConsumeItemFailReason.None, "ConsumeItemResult.Fail requires a non-None reason.");
            return new ConsumeItemResult(false, reason);
        }
    }
}

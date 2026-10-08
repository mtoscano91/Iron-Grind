namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Return type of <see cref="IInventoryService.SellItem"/>. <see langword="readonly struct"/> —
    /// zero GC allocation per call (mirrors <see cref="DiscardResult"/>).
    /// </summary>
    /// <remarks>
    /// A sell is atomic: on failure no slot was written and no
    /// <see cref="IInventoryService.OnInventoryChanged"/> event fired.
    /// </remarks>
    public readonly struct SellItemResult
    {
        /// <summary><see langword="true"/> when the requested quantity was sold.</summary>
        public readonly bool Success;

        /// <summary><see cref="SellItemFailReason.None"/> iff <see cref="Success"/>; otherwise the reason the sell did not apply.</summary>
        public readonly SellItemFailReason Reason;

        /// <summary>The quantity actually sold. 0 on failure.</summary>
        public readonly int QuantitySold;

        private SellItemResult(bool success, SellItemFailReason reason, int quantitySold)
        {
            Success = success;
            Reason = reason;
            QuantitySold = quantitySold;
        }

        /// <summary>Creates a success result carrying the sold quantity.</summary>
        /// <param name="quantitySold">The quantity sold. Must be &gt; 0.</param>
        public static SellItemResult Succeeded(int quantitySold)
        {
            return new SellItemResult(true, SellItemFailReason.None, quantitySold);
        }

        /// <summary>Creates a failure result with the given <paramref name="reason"/>. <see cref="QuantitySold"/> is always 0.</summary>
        /// <param name="reason">The failure reason. Must not be <see cref="SellItemFailReason.None"/>.</param>
        public static SellItemResult Fail(SellItemFailReason reason)
        {
            UnityEngine.Debug.Assert(reason != SellItemFailReason.None, "SellItemResult.Fail requires a non-None reason.");
            return new SellItemResult(false, reason, 0);
        }
    }
}

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Result of <see cref="EnhancementService.PreviewAttempt"/> (UI-ENH-1, CR-ENH-15 step 2): either
    /// the validation rejection code, or the item's current level and the F-ENH-4 table probabilities.
    /// Enhancement Story 010.
    /// </summary>
    /// <example>
    /// <code>
    /// EnhancementPreview preview = service.PreviewAttempt(charId, itemSlot, scrollSlot);
    /// if (preview.IsValid) { double p = preview.SuccessProbability; }
    /// </code>
    /// </example>
    public readonly struct EnhancementPreview
    {
        private EnhancementPreview(
            EnhancementResultCode code, bool isValid, byte currentLevel, double successProbability, double destructionProbability)
        {
            Code = code;
            IsValid = isValid;
            CurrentLevel = currentLevel;
            SuccessProbability = successProbability;
            DestructionProbability = destructionProbability;
        }

        /// <summary>The validation code: <see cref="EnhancementResultCode.Success"/> when valid, otherwise the first failed check.</summary>
        public EnhancementResultCode Code { get; }

        /// <summary>True iff the selection passes every CR-ENH-15 step 2 check.</summary>
        public bool IsValid { get; }

        /// <summary>The item's enhancement level. 0 when rejected.</summary>
        public byte CurrentLevel { get; }

        /// <summary>F-ENH-4 success probability for <see cref="CurrentLevel"/>. 0 when rejected.</summary>
        public double SuccessProbability { get; }

        /// <summary>F-ENH-4 destruction probability for <see cref="CurrentLevel"/>. 0 when rejected.</summary>
        public double DestructionProbability { get; }

        /// <summary>Creates a valid preview.</summary>
        /// <param name="currentLevel">The item's level.</param>
        /// <param name="successProbability">Success probability at that level.</param>
        /// <param name="destructionProbability">Destruction probability at that level.</param>
        internal static EnhancementPreview Valid(byte currentLevel, double successProbability, double destructionProbability)
        {
            return new EnhancementPreview(EnhancementResultCode.Success, true, currentLevel, successProbability, destructionProbability);
        }

        /// <summary>Creates a rejected preview.</summary>
        /// <param name="code">The rejection reason.</param>
        internal static EnhancementPreview Rejected(EnhancementResultCode code)
        {
            return new EnhancementPreview(code, false, 0, 0.0, 0.0);
        }
    }
}

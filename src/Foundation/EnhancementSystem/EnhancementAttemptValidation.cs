using IronGrind.CharacterStats;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Outcome of the validation step of an enhancement attempt (CR-ENH-15 step 2). Either a
    /// rejection with its <see cref="EnhancementResultCode"/>, or a valid request carrying the
    /// values Story 005's rollback needs before the bag changes. Internal: only
    /// <see cref="EnhancementService.ValidateAttempt"/> returns it. Enhancement Story 003.
    /// </summary>
    internal readonly struct EnhancementAttemptValidation
    {
        private EnhancementAttemptValidation(
            bool isValid, EnhancementResultCode rejectionCode, ItemID itemId, byte currentLevel, ItemID scrollItemId)
        {
            IsValid = isValid;
            RejectionCode = rejectionCode;
            ItemId = itemId;
            CurrentLevel = currentLevel;
            ScrollItemId = scrollItemId;
        }

        /// <summary>True iff the request passed every check.</summary>
        public bool IsValid { get; }

        /// <summary>The rejection reason. Meaningful only when <see cref="IsValid"/> is false.</summary>
        public EnhancementResultCode RejectionCode { get; }

        /// <summary>The item to enhance. <see cref="ItemID.Invalid"/> when rejected.</summary>
        public ItemID ItemId { get; }

        /// <summary>The item's enhancement level before the attempt. 0 when rejected.</summary>
        public byte CurrentLevel { get; }

        /// <summary>The scroll validated at the scroll slot. <see cref="ItemID.Invalid"/> when rejected.</summary>
        public ItemID ScrollItemId { get; }

        /// <summary>Creates a valid result.</summary>
        /// <param name="itemId">The item to enhance.</param>
        /// <param name="currentLevel">Its level before the attempt.</param>
        /// <param name="scrollItemId">The validated scroll.</param>
        public static EnhancementAttemptValidation Valid(ItemID itemId, byte currentLevel, ItemID scrollItemId)
        {
            return new EnhancementAttemptValidation(true, EnhancementResultCode.Success, itemId, currentLevel, scrollItemId);
        }

        /// <summary>Creates a rejected result; the item and scroll ids are <see cref="ItemID.Invalid"/> and the level is 0.</summary>
        /// <param name="code">The rejection reason.</param>
        public static EnhancementAttemptValidation Rejected(EnhancementResultCode code)
        {
            return new EnhancementAttemptValidation(false, code, ItemID.Invalid, 0, ItemID.Invalid);
        }
    }
}

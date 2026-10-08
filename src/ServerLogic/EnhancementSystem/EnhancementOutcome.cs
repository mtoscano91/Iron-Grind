namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// The two possible results of an enhancement attempt (CR-ENH-9, CR-ENH-10). There is no
    /// level-loss outcome: a failed attempt destroys the item at every level.
    /// </summary>
    public enum EnhancementOutcome : byte
    {
        /// <summary>The item gains one enhancement level.</summary>
        Success = 0,

        /// <summary>The item is permanently destroyed.</summary>
        Destruction = 1,
    }
}

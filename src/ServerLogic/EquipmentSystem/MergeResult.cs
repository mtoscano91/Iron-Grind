namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Outcome of <c>RequestMerge</c> (GDD CR-EQS-14, UI Requirements). Member names match the GDD.
    /// </summary>
    public enum MergeResult : byte
    {
        /// <summary>Three source items removed, one merged result item added to the inventory.</summary>
        Success = 0,

        /// <summary>Defense in depth: a source item was also found in a gear slot (unreachable in normal play); nothing changed.</summary>
        RejectedEquipped = 1,

        /// <summary>The item is at <c>MAX_ACCESSORY_LEVEL</c> (<c>MergeResultItemID</c> is null); nothing changed.</summary>
        RejectedMaxLevel = 2,

        /// <summary>No free inventory slot for the merge result; nothing changed.</summary>
        RejectedInventoryFull = 3,

        /// <summary>Inserting the result failed and all three source items were restored; no item lost.</summary>
        RejectedMergeError = 4,

        /// <summary>Inserting the result failed and at least one source item could not be restored; a critical error was logged.</summary>
        CriticalRollbackFailed = 5,

        /// <summary>A source selection failed CR-EQS-14 preconditions 0–2 (index out of range or repeated, empty slot, differing ItemIDs, locked slot, or not a Ring / Necklace); nothing changed.</summary>
        RejectedInvalidSources = 6,
    }
}

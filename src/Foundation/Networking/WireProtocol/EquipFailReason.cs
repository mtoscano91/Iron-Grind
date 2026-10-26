namespace IronGrind.Networking
{
    /// <summary>
    /// Wire reason of a failed <see cref="EquipResultMessage"/> (networking-wire-protocol.md <c>EquipFailReason</c>;
    /// equipment-system.md CR-EQS-13 and the <c>EquipResult</c> → wire mapping table, Equipment Story 008).
    /// <see cref="None"/> accompanies <c>success = true</c>; every other value accompanies <c>success = false</c>.
    /// </summary>
    /// <remarks>
    /// The values are the wire's, not those of the server's <c>IronGrind.EquipmentSystem.EquipResult</c> (which has
    /// <c>Success</c> and <c>NoChange</c> ahead of the failures). The server maps one to the other 1:1 by name
    /// (OQ-EQS-9 option a). Shared with the client, so it lives in <c>IronGrind.Foundation</c> (ADR-012).
    /// <b>Adding a value</b> also means updating the valid-set check in <see cref="EquipResultMessageCodec.TryReadBody"/>
    /// (the set is not contiguous) and the server's <c>EquipmentClientRequestHandlers.ToWire</c>.
    /// </remarks>
    public enum EquipFailReason : byte
    {
        /// <summary>No failure: the request succeeded or changed nothing.</summary>
        None = 0,

        /// <summary>The character does not meet the item's stat requirement.</summary>
        StatRequirementNotMet = 1,

        /// <summary>The bag has no free slot for the item being taken off or swapped out.</summary>
        InventoryFull = 2,

        /// <summary>The item does not fit the requested gear slot.</summary>
        SlotMismatch = 3,

        /// <summary>The source item is locked by another operation.</summary>
        ItemLocked = 4,

        /// <summary>The bag slot no longer holds the requested item (stale client view).</summary>
        ItemNotInInventory = 5,

        /// <summary>The Inventory System refused a step of the operation.</summary>
        InventoryError = 6,

        /// <summary>Reserved for wire-enum stability; no GDD rule produces it.</summary>
        CriticalFailure = 255,
    }
}

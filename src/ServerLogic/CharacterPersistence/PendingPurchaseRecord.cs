using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// One row of the <c>pending_purchases</c> table (ADR-001 Decisions 2-4). Data only;
    /// the behaviour belongs to the purchase methods of <see cref="ICharacterPersistence"/>.
    /// </summary>
    public readonly struct PendingPurchaseRecord
    {
        /// <summary>Database-generated key; 0 before the row is inserted.</summary>
        public readonly long Id;

        /// <summary>The purchasing character.</summary>
        public readonly CharacterID CharId;

        /// <summary>The id of the purchase request.</summary>
        public readonly uint RequestId;

        /// <summary>Raw id of the purchased item.</summary>
        public readonly uint ItemId;

        /// <summary>Quantity purchased.</summary>
        public readonly int Quantity;

        /// <summary>Total gold cost of the purchase.</summary>
        public readonly uint TotalCost;

        /// <summary>Current state of the purchase.</summary>
        public readonly PendingPurchaseState State;

        /// <summary>Constructs a purchase record.</summary>
        /// <param name="id">Database key; 0 before insert.</param>
        /// <param name="charId">The purchasing character.</param>
        /// <param name="requestId">The id of the purchase request.</param>
        /// <param name="itemId">Raw id of the purchased item.</param>
        /// <param name="quantity">Quantity purchased.</param>
        /// <param name="totalCost">Total gold cost.</param>
        /// <param name="state">Current state of the purchase.</param>
        public PendingPurchaseRecord(long id, CharacterID charId, uint requestId, uint itemId, int quantity, uint totalCost, PendingPurchaseState state)
        {
            Id = id;
            CharId = charId;
            RequestId = requestId;
            ItemId = itemId;
            Quantity = quantity;
            TotalCost = totalCost;
            State = state;
        }
    }
}

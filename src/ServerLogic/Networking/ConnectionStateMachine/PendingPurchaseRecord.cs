using IronGrind.Currency;

namespace IronGrind.Networking
{
    /// <summary>
    /// Minimal caller-facing view of one in-flight <c>PendingPurchase</c> record (ADR-001 Decision 2)
    /// in the <c>GoldDebited</c> state, as needed by <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/>'s
    /// reconciliation step (ADR-001 Decision 4 / CR-NET-6.4 step 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Delegate seam, not real infrastructure:</b> no <c>PendingPurchase</c> persistence-backed
    /// store exists anywhere in this codebase yet (ADR-001 Decision 2 defines the record's shape in
    /// prose only). This struct is the minimal data <see cref="ConnectionStateMachine"/> needs to
    /// drive the reconciliation loop and log the event — it is supplied by a caller-owned
    /// <c>Func&lt;uint, System.Collections.Generic.IReadOnlyList{PendingPurchaseRecord}&gt;</c> query
    /// delegate, mirroring Story 011's <c>disconnectClient</c>/<c>preserveSessionForTtl</c>
    /// forward-dependency precedent.
    /// </para>
    /// <para>
    /// <b>Deliberately excludes <c>state</c> and <c>createdAt</c>:</b> ADR-001's full
    /// <c>PendingPurchase</c> record includes a <c>state</c> field (<c>GoldDebited | Refunded |
    /// Completed</c>) and a <c>createdAt</c> timestamp. This story's query delegate is named
    /// <c>queryGoldDebitedPurchases</c> — the caller is contractually responsible for returning only
    /// <c>GoldDebited</c> records, so <c>state</c> would be redundant here. <c>createdAt</c> has no
    /// consumer in this story's reconciliation logic. Both are omitted to keep this seam proving only
    /// what CR-NET-6.4 step 2 requires — the ordering guarantee, not a full record mirror.
    /// </para>
    /// </remarks>
    public readonly struct PendingPurchaseRecord
    {
        /// <summary>The character the purchase was debited against.</summary>
        public readonly CharacterID CharId;

        /// <summary>The original <c>BuyRequest.requestId</c> that initiated this purchase.</summary>
        public readonly uint RequestId;

        /// <summary>The item that was being purchased.</summary>
        public readonly uint ItemId;

        /// <summary>The quantity that was being purchased.</summary>
        public readonly int Quantity;

        /// <summary>The gold amount already debited — the exact amount refunded on reconciliation.</summary>
        public readonly uint TotalCost;

        /// <summary>Initializes a new <see cref="PendingPurchaseRecord"/> with the specified field values.</summary>
        public PendingPurchaseRecord(CharacterID charId, uint requestId, uint itemId, int quantity, uint totalCost)
        {
            CharId = charId;
            RequestId = requestId;
            ItemId = itemId;
            Quantity = quantity;
            TotalCost = totalCost;
        }
    }
}

namespace IronGrind.Currency
{
    /// <summary>
    /// Audit reason attached to every gold balance mutation. Wire format authority is
    /// <c>networking-core.md</c> CR-NET-7; values are pre-registered in
    /// <c>design/registry/entities.yaml</c> — do not renumber.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="byte"/> for wire compactness and IL2CPP efficiency.
    /// Receivers must treat unknown byte values as <see cref="Other"/> and still apply the
    /// balance update — never drop the event on an unrecognized reason.
    /// </remarks>
    public enum GoldTransactionReason : byte
    {
        /// <summary>Monster kill drop — the primary gold faucet (Loot Table System).</summary>
        MonsterDrop = 0,

        /// <summary>NPC Shop scroll or consumable purchase.</summary>
        ScrollPurchase = 1,

        /// <summary>Stat respec cost (Class/Leveling System).</summary>
        RespecStat = 2,

        /// <summary>Skill respec cost (Class/Leveling System).</summary>
        RespecSkill = 3,

        /// <summary>Manual administrative correction, or a compensating refund predating the dedicated <see cref="CompensatingRefund"/> value.</summary>
        AdminAdjust = 4,

        /// <summary>Enhancement System scroll/attempt economy validation (F-ENH-5).</summary>
        Enhancement = 5,

        /// <summary>Generic NPC Shop item purchase (non-scroll).</summary>
        ItemPurchase = 6,

        /// <summary>Inventory System item sell-back (OQ-CS-1 — restored to MVP scope 2026-05-15).</summary>
        ItemSell = 7,

        /// <summary>Compensating refund after a downstream failure following a successful debit (ADR-001 Decision 3). Distinguishable from <see cref="AdminAdjust"/> in audit logs.</summary>
        CompensatingRefund = 8,

        /// <summary>Fallback for any reason not covered by a named value above.</summary>
        Other = 255,
    }
}

#nullable enable

using System;
using System.Collections.Generic;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Save/load value for one character's inventory contents (Story 009 — GDD Rule 1.2,
    /// Interactions table Character Persistence row, Persistence and Load Edge Cases, AC-INV-3).
    /// Produced by <see cref="IInventoryService.ExportSnapshot"/> and consumed by
    /// <see cref="IInventoryService.ImportSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// A class, not a struct: it owns a list and is created once per save/load, not per frame
    /// (Implementation Notes — performance). The Inventory System owns only this type and its
    /// export/import; ADR-006 (Persistence Layer) maps it to/from the
    /// <c>character_records.inventory_slots</c> JSONB column — that mapping, and
    /// <c>SaveSession</c> orchestration, belong to Character Persistence, not this class
    /// (Out of Scope).
    /// </remarks>
    public sealed class InventorySnapshot
    {
        /// <summary>The canonical empty snapshot: zero entries.</summary>
        public static readonly InventorySnapshot Empty = new InventorySnapshot(Array.Empty<InventorySnapshotEntry>());

        /// <summary>
        /// The slots captured by this snapshot. When produced by
        /// <see cref="IInventoryService.ExportSnapshot"/> these are the non-empty slots only, in
        /// ascending <see cref="InventorySnapshotEntry.SlotIndex"/> order. A hand-built snapshot
        /// (e.g. in tests, or deserialized storage) may use any order and may contain
        /// duplicate or out-of-range entries — <see cref="IInventoryService.ImportSnapshot"/>
        /// validates every entry independently.
        /// </summary>
        public IReadOnlyList<InventorySnapshotEntry> Slots { get; }

        /// <summary>Constructs a snapshot wrapping the given list of entries.</summary>
        /// <param name="slots">The entries to wrap. Not copied.</param>
        /// <exception cref="ArgumentNullException"><paramref name="slots"/> is <see langword="null"/>.</exception>
        public InventorySnapshot(IReadOnlyList<InventorySnapshotEntry> slots)
        {
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
        }
    }
}

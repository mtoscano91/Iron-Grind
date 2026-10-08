using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Immutable loot table for one mob type: item entries plus a gold range
    /// (design/gdd/loot-table-system.md, CR-LT-2). Validity is checked by <see cref="LootTableValidator"/>,
    /// not by this constructor.
    /// </summary>
    public sealed class LootTableDefinition
    {
        // A read-only wrapper, not the array itself: a caller cannot cast Entries back to a
        // LootTableEntry[] and overwrite an entry.
        private readonly ReadOnlyCollection<LootTableEntry> _entries;

        /// <summary>Creates a definition. The entries are copied; later changes to the caller's collection have no effect.</summary>
        /// <param name="entries">Drop entries. A null collection is treated as empty.</param>
        /// <param name="goldMin">Inclusive lower bound of the gold draw.</param>
        /// <param name="goldMax">Inclusive upper bound of the gold draw.</param>
        public LootTableDefinition(IEnumerable<LootTableEntry> entries, int goldMin, int goldMax)
        {
            LootTableEntry[] copy = entries == null
                ? Array.Empty<LootTableEntry>()
                : new List<LootTableEntry>(entries).ToArray();
            _entries = Array.AsReadOnly(copy);
            GoldMin = goldMin;
            GoldMax = goldMax;
        }

        /// <summary>
        /// The drop entries: a private copy behind a read-only wrapper. Any attempt to write
        /// through it throws <see cref="NotSupportedException"/>.
        /// </summary>
        public IReadOnlyList<LootTableEntry> Entries => _entries;

        /// <summary>Inclusive lower bound of the gold draw.</summary>
        public int GoldMin { get; }

        /// <summary>Inclusive upper bound of the gold draw.</summary>
        public int GoldMax { get; }
    }
}

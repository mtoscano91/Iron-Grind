using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Randomness;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Stateless drop roll (design/gdd/loot-table-system.md, CR-LT-1). The random provider is injected by the caller (ADR-013).
    /// </summary>
    public static class LootDropRoller
    {
        /// <summary>
        /// Rolls every entry of <paramref name="table"/> independently, in entry order (CR-LT-1).
        /// Exactly one <see cref="IRandomProvider.NextDouble"/> draw is made per entry, and every entry is
        /// evaluated regardless of earlier results. An entry drops when the draw is below its DropChance.
        /// </summary>
        /// <param name="table">The mob's loot table.</param>
        /// <param name="random">Injected random provider (ADR-013); see <see cref="RandomProviderFactory"/>.</param>
        /// <returns>A new, non-null list of dropped item IDs (length 0..N, in entry order).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="random"/> is null.</exception>
        public static List<ItemID> Roll(LootTableDefinition table, IRandomProvider random)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var drops = new List<ItemID>();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                LootTableEntry entry = table.Entries[i];
                // Compare in double: a float cast of the draw could round up to 1.0f and
                // make a DropChance = 1.0 entry miss. The float chance widens to double.
                if (random.NextDouble() < entry.DropChance)
                {
                    drops.Add(entry.ItemId);
                }
            }
            return drops;
        }
    }
}

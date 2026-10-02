using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Stateless drop roll (design/gdd/loot-table-system.md, CR-LT-1). The PRNG is injected by the caller.
    /// </summary>
    public static class LootDropRoller
    {
        /// <summary>
        /// Rolls every entry of <paramref name="table"/> independently, in entry order (CR-LT-1).
        /// Exactly one <see cref="Random.NextDouble"/> draw is made per entry, and every entry is
        /// evaluated regardless of earlier results. An entry drops when the draw is below its DropChance.
        /// </summary>
        /// <param name="table">The mob's loot table.</param>
        /// <param name="rng">Injected PRNG; see <see cref="LootRandomFactory"/>.</param>
        /// <returns>A new, non-null list of dropped item IDs (length 0..N, in entry order).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="table"/> or <paramref name="rng"/> is null.</exception>
        public static List<ItemID> Roll(LootTableDefinition table, Random rng)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var drops = new List<ItemID>();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                LootTableEntry entry = table.Entries[i];
                // Compare in double: a float cast of the draw could round up to 1.0f and
                // make a DropChance = 1.0 entry miss. The float chance widens to double.
                if (rng.NextDouble() < entry.DropChance)
                {
                    drops.Add(entry.ItemId);
                }
            }
            return drops;
        }
    }
}

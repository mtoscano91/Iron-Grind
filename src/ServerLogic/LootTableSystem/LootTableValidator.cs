using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.Networking;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Pure startup validation of loot tables (design/gdd/loot-table-system.md: CR-LT-2, F-LT-1 Notes,
    /// F-LT-4 variable range, Tuning Knobs, CR-LT-16 scroll exclusion). Performs no logging; the caller decides what to do with the issues.
    /// </summary>
    public static class LootTableValidator
    {
        /// <summary>
        /// Validates the full set of tables.
        /// </summary>
        /// <param name="tables">The (mob type, table) pairs. A null set is treated as empty.</param>
        /// <param name="itemDatabase">Used to recognise Enhancement Scroll entries (CR-LT-16). Must not be null.</param>
        /// <param name="allowEnhancementScrollDrops">
        /// When false, an entry naming an Enhancement Scroll (a definition with non-null ScrollData) is an issue.
        /// An ItemID unknown to the database is not reported by this rule.
        /// </param>
        /// <returns>All issues found, one per violation; an empty list means valid.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="itemDatabase"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="allowEnhancementScrollDrops"/> is false and <paramref name="itemDatabase"/> is not
        /// ready: every lookup would fail, so every scroll entry would pass unseen.
        /// </exception>
        public static IReadOnlyList<LootTableValidationIssue> Validate(
            IReadOnlyList<KeyValuePair<MobTypeID, LootTableDefinition>> tables,
            IItemDatabase itemDatabase,
            bool allowEnhancementScrollDrops)
        {
            if (itemDatabase == null)
            {
                throw new ArgumentNullException(nameof(itemDatabase));
            }

            if (!allowEnhancementScrollDrops && !itemDatabase.IsReady)
            {
                throw new InvalidOperationException(
                    "Loot tables cannot be validated before the item database is ready: the Enhancement Scroll rule (CR-LT-16) needs item lookups.");
            }

            var issues = new List<LootTableValidationIssue>();
            if (tables == null)
            {
                return issues;
            }

            var seen = new HashSet<MobTypeID>();
            for (int i = 0; i < tables.Count; i++)
            {
                MobTypeID id = tables[i].Key;
                LootTableDefinition table = tables[i].Value;

                if (id == MobTypeID.Invalid)
                {
                    issues.Add(new LootTableValidationIssue(id,
                        $"MobTypeID: {id} is the Invalid sentinel and cannot be used as a table key."));
                }

                if (!seen.Add(id))
                {
                    issues.Add(new LootTableValidationIssue(id,
                        $"MobTypeID: {id} appears more than once; only one loot table is allowed per mob type."));
                }

                if (table == null)
                {
                    issues.Add(new LootTableValidationIssue(id, "Definition: table is null."));
                    continue;
                }

                ValidateGoldRange(id, table, issues);
                ValidateEntries(id, table.Entries, itemDatabase, allowEnhancementScrollDrops, issues);
            }

            return issues;
        }

        private static void ValidateGoldRange(
            MobTypeID id, LootTableDefinition table, List<LootTableValidationIssue> issues)
        {
            // MAX_PARTY_SIZE belongs to the Party System; move this reference when a Party System module exists.
            if (table.GoldMin < RelevanceFilter.MAX_PARTY_SIZE)
            {
                issues.Add(new LootTableValidationIssue(id,
                    $"GoldMin: {table.GoldMin} is below the minimum of {RelevanceFilter.MAX_PARTY_SIZE} (MAX_PARTY_SIZE)."));
            }

            if ((long)table.GoldMax > CurrencySystem.GOLD_CAP)
            {
                issues.Add(new LootTableValidationIssue(id,
                    $"GoldMax: {table.GoldMax} exceeds GOLD_CAP ({CurrencySystem.GOLD_CAP})."));
            }

            if (table.GoldMax < table.GoldMin)
            {
                issues.Add(new LootTableValidationIssue(id,
                    $"GoldMax: {table.GoldMax} is less than GoldMin ({table.GoldMin}); the gold range is empty."));
            }
        }

        private static void ValidateEntries(
            MobTypeID id,
            IReadOnlyList<LootTableEntry> entries,
            IItemDatabase itemDatabase,
            bool allowEnhancementScrollDrops,
            List<LootTableValidationIssue> issues)
        {
            for (int e = 0; e < entries.Count; e++)
            {
                float chance = entries[e].DropChance;
                // Negated form also rejects NaN.
                if (!(chance >= 0f && chance <= 1f))
                {
                    issues.Add(new LootTableValidationIssue(id,
                        $"Entries[{e}] ({entries[e].ItemId}) DropChance: {chance} is outside [0, 1]."));
                }

                // CR-LT-16: a scroll is identified by data (ScrollData != null, item-database.md Rule 36).
                if (!allowEnhancementScrollDrops
                    && itemDatabase.TryGetItem(entries[e].ItemId, out var item)
                    && item.ScrollData != null)
                {
                    issues.Add(new LootTableValidationIssue(id,
                        $"Entries[{e}] ({entries[e].ItemId}) is an Enhancement Scroll; scrolls cannot be dropped while ALLOW_ENHANCEMENT_SCROLL_DROPS is false (CR-LT-16)."));
                }
            }
        }
    }
}

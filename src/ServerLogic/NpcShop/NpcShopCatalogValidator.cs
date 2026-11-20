using System.Collections.Generic;
using System.Globalization;
using IronGrind.Currency;
using IronGrind.ItemDatabase;

namespace IronGrind.ServerLogic.NpcShop
{
    /// <summary>
    /// Outcome of <see cref="NpcShopCatalogValidator.Validate"/>: every failure, or the validated catalog.
    /// </summary>
    public sealed class NpcShopCatalogValidationResult
    {
        internal NpcShopCatalogValidationResult(IReadOnlyList<string> failures, ValidatedNpcShopCatalog catalog)
        {
            Failures = failures;
            Catalog = catalog;
        }

        /// <summary>Every failure found (no early stop); empty on success.</summary>
        public IReadOnlyList<string> Failures { get; }

        /// <summary>The validated catalog; null when there is any failure.</summary>
        public ValidatedNpcShopCatalog Catalog { get; }

        /// <summary>True when there is no failure.</summary>
        public bool IsValid => Failures.Count == 0;
    }

    /// <summary>
    /// Pure startup validation of an <see cref="NpcShopCatalog"/> against the item database (npc-shop.md CR-SHOP-2,
    /// CR-SHOP-10, F-NS-1 overflow guard, F-NS-3; NPC Shop Story 001, AC-3 and AC-5). Collects every failure; never logs.
    /// </summary>
    public static class NpcShopCatalogValidator
    {
        /// <summary>Validates the catalog.</summary>
        /// <param name="catalog">The authored catalog; null is a failure.</param>
        /// <param name="items">The item database used for existence, sell price and scroll checks.</param>
        /// <returns>All failures, plus the validated catalog when there are none.</returns>
        public static NpcShopCatalogValidationResult Validate(NpcShopCatalog catalog, IItemDatabase items)
        {
            var failures = new List<string>();
            if (catalog == null)
            {
                failures.Add("catalog missing");
                return new NpcShopCatalogValidationResult(failures, null);
            }

            if (items == null)
            {
                failures.Add("item database missing");
                return new NpcShopCatalogValidationResult(failures, null);
            }

            IReadOnlyList<NpcShopCatalogEntry> entries = catalog.Entries;
            int count = entries == null ? 0 : entries.Count;
            if (count == 0)
            {
                failures.Add("catalog has no entries");
            }

            ValidateKnobs(catalog, failures, out bool capValid, out bool ratioValid);
            var lines = new List<ShopCatalogEntry>(count);
            var seen = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                ValidateEntry(entries[i], catalog, items, capValid, ratioValid, seen, failures, lines);
            }

            if (failures.Count > 0)
            {
                return new NpcShopCatalogValidationResult(failures, null);
            }

            return new NpcShopCatalogValidationResult(failures,
                new ValidatedNpcShopCatalog(lines, catalog.QuantityCap, catalog.RatioNumerator, catalog.RatioDenominator));
        }

        /// <summary>Checks the quantity cap range and the ratio parts; reports which are usable by the entry checks.</summary>
        private static void ValidateKnobs(NpcShopCatalog catalog, List<string> failures, out bool capValid, out bool ratioValid)
        {
            int cap = catalog.QuantityCap;
            capValid = cap >= NpcShopCatalog.QUANTITY_CAP_MIN && cap <= NpcShopCatalog.QUANTITY_CAP_MAX;
            if (!capValid)
            {
                failures.Add("QuantityCap " + Num(cap) + " outside [" + Num(NpcShopCatalog.QUANTITY_CAP_MIN) + ", "
                             + Num(NpcShopCatalog.QUANTITY_CAP_MAX) + "]");
            }

            ratioValid = catalog.RatioNumerator != 0 && catalog.RatioDenominator != 0;
            if (!ratioValid)
            {
                failures.Add("ratio " + Num(catalog.RatioNumerator) + "/" + Num(catalog.RatioDenominator)
                             + " has a zero part");
            }
        }

        /// <summary>
        /// Checks one line: duplicate (skips the rest), zero price, missing item (skips the rest), price x cap overflow
        /// (only with a valid cap), F-NS-3 (only with a valid ratio and SellPriceGold &gt; 0). Adds the validated line.
        /// </summary>
        private static void ValidateEntry(NpcShopCatalogEntry entry, NpcShopCatalog catalog, IItemDatabase items,
            bool capValid, bool ratioValid, HashSet<uint> seen, List<string> failures, List<ShopCatalogEntry> lines)
        {
            uint id = entry.ItemId.RawValue;
            uint price = entry.BuyPrice;
            bool found = items.TryGetItem(entry.ItemId, out ItemDefinition def) && def != null;
            string head = "ItemID " + Num(id) + " (BuyPrice " + Num(price) + ", SellPriceGold "
                          + (found ? Num(def.SellPriceGold) : "n/a") + "): ";

            if (!seen.Add(id))
            {
                failures.Add(head + "duplicate ItemID");
                return;
            }

            if (price == 0)
            {
                failures.Add(head + "BuyPrice is 0");
            }

            if (!found)
            {
                failures.Add(head + "item missing from the item database");
                return;
            }

            if (capValid && (ulong)price * (ulong)catalog.QuantityCap > uint.MaxValue)
            {
                failures.Add(head + "BuyPrice x QuantityCap " + Num(catalog.QuantityCap) + " overflows uint");
            }

            if (ratioValid && def.SellPriceGold > 0
                && (ulong)price * catalog.RatioDenominator < (ulong)def.SellPriceGold * catalog.RatioNumerator)
            {
                failures.Add(head + "F-NS-3 violated: BuyPrice x " + Num(catalog.RatioDenominator) + " < SellPriceGold x "
                             + Num(catalog.RatioNumerator));
            }

            GoldTransactionReason reason = def.ScrollData != null
                ? GoldTransactionReason.ScrollPurchase
                : GoldTransactionReason.ItemPurchase;
            lines.Add(new ShopCatalogEntry(entry.ItemId, price, reason));
        }

        private static string Num(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

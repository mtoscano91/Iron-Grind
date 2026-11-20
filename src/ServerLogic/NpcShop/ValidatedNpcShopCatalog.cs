using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.NpcShop
{
    /// <summary>
    /// A validated shop line: item, unit price and the derived gold transaction reason (NPC Shop Story 001, G2).
    /// </summary>
    public readonly struct ShopCatalogEntry
    {
        /// <summary>Creates a validated line.</summary>
        /// <param name="itemId">The item sold.</param>
        /// <param name="buyPrice">Gold per unit.</param>
        /// <param name="reason">ScrollPurchase for scrolls, ItemPurchase otherwise.</param>
        public ShopCatalogEntry(ItemID itemId, uint buyPrice, GoldTransactionReason reason)
        {
            ItemId = itemId;
            BuyPrice = buyPrice;
            Reason = reason;
        }

        /// <summary>The item sold.</summary>
        public ItemID ItemId { get; }

        /// <summary>Gold per unit.</summary>
        public uint BuyPrice { get; }

        /// <summary>The gold transaction reason of a purchase of this item.</summary>
        public GoldTransactionReason Reason { get; }
    }

    /// <summary>
    /// Read-only result of a successful <see cref="NpcShopCatalogValidator.Validate"/> (NPC Shop Story 001). Lookup is
    /// allocation-free; the quantity cap and ratio are the authored values, never literals.
    /// </summary>
    public sealed class ValidatedNpcShopCatalog
    {
        private readonly Dictionary<ItemID, ShopCatalogEntry> _byId;

        /// <summary>Creates the catalog from already validated lines.</summary>
        /// <param name="entries">Lines in authored order.</param>
        /// <param name="quantityCap">The validated quantity cap.</param>
        /// <param name="ratioNumerator">The validated ratio numerator.</param>
        /// <param name="ratioDenominator">The validated ratio denominator.</param>
        internal ValidatedNpcShopCatalog(IReadOnlyList<ShopCatalogEntry> entries, int quantityCap, uint ratioNumerator,
            uint ratioDenominator)
        {
            Entries = new List<ShopCatalogEntry>(entries).AsReadOnly(); // own copy: Entries and the lookup cannot drift
            QuantityCap = quantityCap;
            RatioNumerator = ratioNumerator;
            RatioDenominator = ratioDenominator;
            _byId = new Dictionary<ItemID, ShopCatalogEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                _byId[entries[i].ItemId] = entries[i];
            }
        }

        /// <summary>The lines in authored order.</summary>
        public IReadOnlyList<ShopCatalogEntry> Entries { get; }

        /// <summary>Largest quantity of one item per purchase.</summary>
        public int QuantityCap { get; }

        /// <summary>Anti-arbitrage ratio numerator.</summary>
        public uint RatioNumerator { get; }

        /// <summary>Anti-arbitrage ratio denominator.</summary>
        public uint RatioDenominator { get; }

        /// <summary>Finds the line for an item.</summary>
        /// <param name="itemId">The item.</param>
        /// <param name="entry">The line when found.</param>
        /// <returns>True when the shop sells the item.</returns>
        public bool TryGetEntry(ItemID itemId, out ShopCatalogEntry entry)
        {
            return _byId.TryGetValue(itemId, out entry);
        }
    }
}

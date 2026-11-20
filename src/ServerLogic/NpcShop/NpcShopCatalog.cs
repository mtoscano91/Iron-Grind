using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.NpcShop
{
    /// <summary>
    /// One authored shop line: an item and the gold the NPC charges per unit (npc-shop.md CR-SHOP-2; NPC Shop Story 001).
    /// </summary>
    /// <remarks><c>[SerializeField]</c> on private fields only; the id is stored as its raw <c>uint</c> because
    /// <see cref="ItemID"/> is a readonly struct the serializer skips.</remarks>
    [Serializable]
    public struct NpcShopCatalogEntry
    {
        [SerializeField] private uint _itemId;
        [SerializeField] private uint _buyPrice;

        /// <summary>Creates an entry (code-built catalogs and tests).</summary>
        /// <param name="itemId">The item sold.</param>
        /// <param name="buyPrice">Gold per unit.</param>
        public NpcShopCatalogEntry(ItemID itemId, uint buyPrice)
        {
            _itemId = itemId.RawValue;
            _buyPrice = buyPrice;
        }

        /// <summary>The item sold.</summary>
        public ItemID ItemId => new ItemID(_itemId);

        /// <summary>Gold per unit, as authored.</summary>
        public uint BuyPrice => _buyPrice;
    }

    /// <summary>
    /// Designer-authored NPC shop catalog (npc-shop.md CR-SHOP-2, CR-SHOP-10, Tuning Knobs; NPC Shop Story 001, TR-shop-001):
    /// lines in authored order, the per-purchase quantity cap and the anti-arbitrage ratio as an integer pair. Validated at
    /// startup S2 by <see cref="NpcShopCatalogValidator"/>; read at runtime only through <see cref="ValidatedNpcShopCatalog"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "NpcShopCatalog", menuName = "IronGrind/NPC Shop Catalog")]
    public sealed class NpcShopCatalog : ScriptableObject
    {
        /// <summary>Default per-purchase quantity cap (npc-shop.md QUANTITY_SELECTOR_CAP; GDD tuning range [10, 99]).</summary>
        public const int QUANTITY_CAP_DEFAULT = 99;

        /// <summary>Lowest accepted quantity cap: 1, below the GDD tuning floor of 10, so small test catalogs validate (Story 001 readiness G3).</summary>
        public const int QUANTITY_CAP_MIN = 1;

        /// <summary>Highest accepted quantity cap (the wire quantity is a byte; the GDD caps it at 99).</summary>
        public const int QUANTITY_CAP_MAX = 99;

        /// <summary>Default anti-arbitrage ratio numerator (buy price at least 1.5 x sell price).</summary>
        public const uint RATIO_NUMERATOR_DEFAULT = 15;

        /// <summary>Default anti-arbitrage ratio denominator.</summary>
        public const uint RATIO_DENOMINATOR_DEFAULT = 10;

        [SerializeField] private List<NpcShopCatalogEntry> _entries = new List<NpcShopCatalogEntry>();
        [SerializeField] private int _quantityCap = QUANTITY_CAP_DEFAULT;
        [SerializeField] private uint _ratioNumerator = RATIO_NUMERATOR_DEFAULT;
        [SerializeField] private uint _ratioDenominator = RATIO_DENOMINATOR_DEFAULT;

        /// <summary>The shop lines in authored order.</summary>
        public IReadOnlyList<NpcShopCatalogEntry> Entries => _entries;

        /// <summary>Largest quantity of one item per purchase, as authored.</summary>
        public int QuantityCap => _quantityCap;

        /// <summary>Anti-arbitrage ratio numerator, as authored.</summary>
        public uint RatioNumerator => _ratioNumerator;

        /// <summary>Anti-arbitrage ratio denominator, as authored.</summary>
        public uint RatioDenominator => _ratioDenominator;

        /// <summary>
        /// Builds a catalog in code (tests; no validation). The caller owns the instance and destroys it.
        /// </summary>
        /// <param name="entries">The lines in order; null for none.</param>
        /// <param name="quantityCap">The quantity cap.</param>
        /// <param name="ratioNumerator">The ratio numerator.</param>
        /// <param name="ratioDenominator">The ratio denominator.</param>
        /// <returns>A new catalog instance.</returns>
        internal static NpcShopCatalog CreateForTests(IEnumerable<NpcShopCatalogEntry> entries, int quantityCap,
            uint ratioNumerator, uint ratioDenominator)
        {
            NpcShopCatalog catalog = CreateInstance<NpcShopCatalog>();
            catalog._entries = entries == null ? new List<NpcShopCatalogEntry>() : new List<NpcShopCatalogEntry>(entries);
            catalog._quantityCap = quantityCap;
            catalog._ratioNumerator = ratioNumerator;
            catalog._ratioDenominator = ratioDenominator;
            return catalog;
        }
    }
}

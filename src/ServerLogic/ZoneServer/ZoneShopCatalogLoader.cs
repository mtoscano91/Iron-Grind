using IronGrind.ItemDatabase;
using IronGrind.ServerLogic.NpcShop;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The validated NPC shop catalog loaded at startup S2, read by the composition (NPC Shop Story 009). A loader that
    /// wraps another loader must forward it.
    /// </summary>
    public interface IZoneShopCatalogData
    {
        /// <summary>The validated catalog; null until <c>LoadAndValidate</c> succeeded.</summary>
        ValidatedNpcShopCatalog LoadedShopCatalog { get; }
    }

    /// <summary>
    /// Startup S2 loader for the NPC shop catalog (NPC Shop Story 001, AC-6; npc-shop.md CR-SHOP-2). Wraps the loader that
    /// builds the item database, runs it, validates the catalog against that database, logs one
    /// <c>[ZoneServer] S2: shop catalog: ...</c> error per failure and returns false (no partial catalog).
    /// </summary>
    /// <remarks>
    /// Deviation from the story wording: this loader runs AFTER <c>rest</c> (the item loader), because the item database
    /// is only exposed once that loader succeeded. The host chain (Story 009) becomes Mob, ClassSkill, ShopCatalog, Item,
    /// stand-in; outer loaders must forward <see cref="IZoneShopCatalogData"/>. It forwards <see cref="IZoneItemData"/>.
    /// </remarks>
    public sealed class ZoneShopCatalogLoader : IZoneStaticDataLoader, IZoneShopCatalogData, IZoneItemData
    {
        private const string LOG_PREFIX = "[ZoneServer] S2: ";

        private readonly NpcShopCatalog _catalog;
        private readonly IZoneStaticDataLoader _rest;

        /// <summary>Creates the loader.</summary>
        /// <param name="catalog">The authored catalog asset; null fails S2.</param>
        /// <param name="rest">The item loader chain (must implement <see cref="IZoneItemData"/>).</param>
        public ZoneShopCatalogLoader(NpcShopCatalog catalog, IZoneStaticDataLoader rest)
        {
            _catalog = catalog;
            _rest = rest;
        }

        /// <inheritdoc/>
        public ValidatedNpcShopCatalog LoadedShopCatalog { get; private set; }

        /// <inheritdoc/>
        public IItemDatabase LoadedItemDatabase { get; private set; }

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            LoadedShopCatalog = null;
            LoadedItemDatabase = null;
            if (_catalog == null)
            {
                Debug.LogError(LOG_PREFIX + "NpcShopCatalog missing");
                return false;
            }

            if (_rest == null)
            {
                Debug.LogError(LOG_PREFIX + "item database missing for the shop catalog");
                return false;
            }

            if (!_rest.LoadAndValidate())
            {
                return false;
            }

            IItemDatabase items = (_rest as IZoneItemData)?.LoadedItemDatabase;
            if (items == null)
            {
                Debug.LogError(LOG_PREFIX + "item database missing for the shop catalog");
                return false;
            }

            NpcShopCatalogValidationResult result = NpcShopCatalogValidator.Validate(_catalog, items);
            foreach (string failure in result.Failures)
            {
                Debug.LogError(LOG_PREFIX + "shop catalog: " + failure);
            }

            if (!result.IsValid)
            {
                return false;
            }

            LoadedShopCatalog = result.Catalog;
            LoadedItemDatabase = items;
            return true;
        }
    }
}

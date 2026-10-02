using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Equipment index built once from the Item Database (design/gdd/loot-table-system.md,
    /// CR-LT-2) and used to classify drops (CR-LT-5) without further database calls.
    /// </summary>
    public sealed class LootEquipmentCache
    {
        private readonly Dictionary<ItemID, ItemDefinition> _equipment = new Dictionary<ItemID, ItemDefinition>();

        /// <summary>
        /// Calls <see cref="IItemDatabase.GetItemsByCategory"/> for <see cref="ItemCategory.Equipment"/>
        /// exactly once and indexes the result by <see cref="ItemID"/>. The database is never queried again.
        /// Null elements and elements without equipment data are skipped.
        /// </summary>
        /// <param name="itemDatabase">The injected item database.</param>
        /// <exception cref="ArgumentNullException"><paramref name="itemDatabase"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="itemDatabase"/> is not ready. An unready database returns an empty list, which
        /// would leave this cache empty for good and classify every Steel / DarkSteel drop as Common.
        /// </exception>
        public LootEquipmentCache(IItemDatabase itemDatabase)
        {
            if (itemDatabase == null)
            {
                throw new ArgumentNullException(nameof(itemDatabase));
            }
            if (!itemDatabase.IsReady)
            {
                throw new InvalidOperationException(
                    "LootEquipmentCache was constructed before the Item Database finished initializing.");
            }

            IReadOnlyList<ItemDefinition> items = itemDatabase.GetItemsByCategory(ItemCategory.Equipment);
            if (items == null)
            {
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                // ItemDefinition is a ScriptableObject: explicit null check, never ?. or ??.
                if (item == null || item.EquipmentData == null)
                {
                    continue;
                }
                _equipment[item.ItemId] = item;
            }
        }

        /// <summary>
        /// Classifies an item (CR-LT-5): Steel / DarkSteel equipment is <see cref="DropTier.Rare"/>;
        /// Bronze / Iron equipment, consumables and unknown IDs (cache misses) are <see cref="DropTier.Common"/>.
        /// Never calls the database.
        /// </summary>
        /// <param name="itemId">The dropped item.</param>
        /// <returns>The drop tier.</returns>
        public DropTier Classify(ItemID itemId)
        {
            if (!_equipment.TryGetValue(itemId, out ItemDefinition definition))
            {
                return DropTier.Common;
            }
            GearTier tier = definition.EquipmentData.GearTier;
            return tier == GearTier.Steel || tier == GearTier.DarkSteel ? DropTier.Rare : DropTier.Common;
        }

        /// <summary>
        /// Looks an equipment item up in the cache (gear tier, sell price, display name for later stories).
        /// </summary>
        /// <param name="itemId">The item to find.</param>
        /// <param name="definition">The cached definition, or null when the item is not cached equipment.</param>
        /// <returns>True when the item is in the cache.</returns>
        public bool TryGetEquipment(ItemID itemId, out ItemDefinition definition)
        {
            return _equipment.TryGetValue(itemId, out definition);
        }
    }
}

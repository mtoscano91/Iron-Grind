using System.Collections.Generic;
using IronGrind.ItemDatabase;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Immutable lookup of loot tables by mob type (design/gdd/loot-table-system.md, CR-LT-2).
    /// Created only through <see cref="TryCreate"/>, which validates the input; no table can be
    /// added, removed or replaced afterwards.
    /// </summary>
    public sealed class LootTableRegistry
    {
        private readonly Dictionary<MobTypeID, LootTableDefinition> _tables;

        private LootTableRegistry(Dictionary<MobTypeID, LootTableDefinition> tables)
        {
            _tables = tables;
        }

        /// <summary>
        /// Validates <paramref name="tables"/> and builds a registry when there are no issues.
        /// </summary>
        /// <param name="tables">The (mob type, table) pairs.</param>
        /// <param name="itemDatabase">Used to recognise Enhancement Scroll entries (CR-LT-16). Must not be null.</param>
        /// <param name="allowEnhancementScrollDrops">
        /// When false, an entry naming an Enhancement Scroll is a validation issue. Production wiring passes
        /// <see cref="LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS"/>.
        /// </param>
        /// <param name="registry">The registry, or null when validation failed.</param>
        /// <param name="issues">Every validation issue found (empty on success).</param>
        /// <returns>True if the registry was created.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="itemDatabase"/> is null.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="allowEnhancementScrollDrops"/> is false and <paramref name="itemDatabase"/> is not ready.
        /// </exception>
        public static bool TryCreate(
            IReadOnlyList<KeyValuePair<MobTypeID, LootTableDefinition>> tables,
            IItemDatabase itemDatabase,
            bool allowEnhancementScrollDrops,
            out LootTableRegistry registry,
            out IReadOnlyList<LootTableValidationIssue> issues)
        {
            IReadOnlyList<LootTableValidationIssue> found =
                LootTableValidator.Validate(tables, itemDatabase, allowEnhancementScrollDrops);
            issues = found;
            if (found.Count > 0)
            {
                registry = null;
                return false;
            }

            var map = new Dictionary<MobTypeID, LootTableDefinition>();
            if (tables != null)
            {
                for (int i = 0; i < tables.Count; i++)
                {
                    map.Add(tables[i].Key, tables[i].Value);
                }
            }

            registry = new LootTableRegistry(map);
            return true;
        }

        /// <summary>Looks up the table for a mob type. Returns false (and null) if none is registered.</summary>
        public bool TryGetTable(MobTypeID id, out LootTableDefinition definition)
        {
            return _tables.TryGetValue(id, out definition);
        }
    }
}

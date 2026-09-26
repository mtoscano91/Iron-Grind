#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.ItemDatabase;

namespace IronGrind.Tests.EditMode.InventorySystem
{
    /// <summary>
    /// Minimal in-memory <see cref="IItemDatabase"/> for Inventory System tests, backed by a
    /// dictionary. <see cref="IsReady"/> defaults to <see langword="true"/> and is settable so
    /// tests can exercise the "database not ready" pickup path. Does not own the registered
    /// <see cref="ItemDefinition"/> instances — the test that created them destroys them.
    /// </summary>
    internal sealed class StubItemDatabase : IItemDatabase
    {
        private readonly Dictionary<ItemID, ItemDefinition> _items = new Dictionary<ItemID, ItemDefinition>();

        /// <inheritdoc/>
        public bool IsReady { get; set; } = true;

        /// <inheritdoc/>
        /// <remarks>Unused by inventory tests; subscriptions are ignored.</remarks>
        public event Action OnDatabaseReady
        {
            add { }
            remove { }
        }

        /// <summary>Registers <paramref name="definition"/> under its own <see cref="ItemDefinition.ItemId"/>.</summary>
        public void Add(ItemDefinition definition) => _items[definition.ItemId] = definition;

        /// <inheritdoc/>
        public ItemDefinition? GetItem(ItemID id) => TryGetItem(id, out var item) ? item : null;

        /// <inheritdoc/>
        public bool TryGetItem(ItemID id, out ItemDefinition? item)
        {
            item = null;
            return IsReady && id != ItemID.Invalid && _items.TryGetValue(id, out item);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
        {
            var result = new List<ItemDefinition>();
            if (!IsReady)
                return result;
            foreach (var item in _items.Values)
            {
                if (item.ItemCategory == category)
                    result.Add(item);
            }
            return result;
        }
    }
}

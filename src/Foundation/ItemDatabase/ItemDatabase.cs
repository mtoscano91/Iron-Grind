#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Runtime implementation of <see cref="IItemDatabase"/>. Stores item definitions
    /// in a hash-keyed dictionary for O(1) lookup, pre-indexed by category at
    /// <see cref="Initialize"/> time.
    /// </summary>
    /// <remarks>
    /// <para><b>Thread safety:</b> Not thread-safe. All calls must occur on the Unity
    /// main thread. <see cref="Initialize"/> must complete before any query method is used.</para>
    ///
    /// <para><b>Memory:</b> Holds strong references to all <see cref="ItemDefinition"/>
    /// ScriptableObject assets for the lifetime of the database. Assets are loaded and
    /// owned externally (typically via Addressables) and must not be unloaded while the
    /// database is active.</para>
    ///
    /// <para><b>Lifecycle:</b></para>
    /// <list type="number">
    ///   <item>Construct the instance.</item>
    ///   <item>Subscribe to <see cref="OnDatabaseReady"/> if needed.</item>
    ///   <item>Call <see cref="Initialize"/> with the full set of item records.</item>
    ///   <item>Query via <see cref="GetItem"/>, <see cref="TryGetItem"/>, or
    ///   <see cref="GetItemsByCategory"/>.</item>
    /// </list>
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// var db = new ItemDatabase();
    /// db.OnDatabaseReady += () => Debug.Log("Items loaded!");
    /// db.Initialize(loadedDefinitions);
    /// </code>
    /// </remarks>
    public sealed class ItemDatabase : IItemDatabase
    {
        private readonly Dictionary<ItemID, ItemDefinition> _items = new Dictionary<ItemID, ItemDefinition>();
        private readonly Dictionary<ItemCategory, List<ItemDefinition>> _byCategory = new Dictionary<ItemCategory, List<ItemDefinition>>();
        private bool _isReady;
        private Action? _onDatabaseReadyInternal;

        /// <inheritdoc/>
        public bool IsReady => _isReady;

        /// <inheritdoc/>
        /// <remarks>
        /// Custom add accessor implements the late-subscriber pattern (AC-29c): if
        /// <see cref="IsReady"/> is already <c>true</c> when the handler is added, it is
        /// invoked synchronously before the <c>+=</c> expression returns, rather than being
        /// stored for a future event that will never fire.
        /// </remarks>
        public event Action OnDatabaseReady
        {
            add
            {
                if (_isReady)
                {
                    // Late-subscriber: database is already ready — invoke immediately on
                    // calling thread. Do NOT store the handler; the event fires only once.
                    value?.Invoke();
                }
                else
                {
                    _onDatabaseReadyInternal += value;
                }
            }
            remove => _onDatabaseReadyInternal -= value;
        }

        /// <summary>
        /// Populates the database from the supplied <paramref name="records"/> and fires
        /// <see cref="OnDatabaseReady"/>. Subsequent calls are no-ops — the event does not
        /// fire a second time and existing data is not replaced.
        /// </summary>
        /// <param name="records">
        /// The full set of <see cref="ItemDefinition"/> assets to register. Null entries
        /// within the enumerable are skipped silently.
        /// </param>
        /// <remarks>
        /// Runs in O(n) time and O(n) space where n is the number of records. Duplicate
        /// <see cref="ItemDefinition.ItemId"/> values are overwritten by the last entry
        /// encountered (last-write-wins; no error is logged).
        /// </remarks>
        public void Initialize(IEnumerable<ItemDefinition> records)
        {
            // Second Initialize() call is a deliberate no-op (AC-29b: event must not re-fire).
            if (_isReady) return;

            _items.Clear();
            _byCategory.Clear();

            foreach (var record in records)
            {
                if (record == null) continue;

                _items[record.ItemId] = record;

                if (!_byCategory.TryGetValue(record.ItemCategory, out var list))
                {
                    list = new List<ItemDefinition>();
                    _byCategory[record.ItemCategory] = list;
                }
                list.Add(record);
            }

            _isReady = true;
            _onDatabaseReadyInternal?.Invoke();
        }

        /// <inheritdoc/>
        public ItemDefinition? GetItem(ItemID id)
        {
            // ItemID.Invalid (uint 0) is a valid sentinel meaning "no item / empty slot".
            // Return null silently — no error logged (AC-1).
            if (id == ItemID.Invalid) return null;

            if (!_isReady)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError("[ItemDatabase] GetItem called before Initialize(). IsReady == false.");
#endif
                return null;
            }

            return _items.TryGetValue(id, out var def) ? def : null;
        }

        /// <inheritdoc/>
        public bool TryGetItem(ItemID id, out ItemDefinition? item)
        {
            // ItemID.Invalid is a valid sentinel — return false/null silently, no error (AC-37).
            if (id == ItemID.Invalid)
            {
                item = null;
                return false;
            }

            if (!_isReady)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError("[ItemDatabase] TryGetItem called before Initialize(). IsReady == false.");
#endif
                item = null;
                return false;
            }

            return _items.TryGetValue(id, out item);
        }

        /// <inheritdoc/>
        public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
        {
            if (!_isReady)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError("[ItemDatabase] GetItemsByCategory called before Initialize(). IsReady == false.");
#endif
                return Array.Empty<ItemDefinition>();
            }

            if (!Enum.IsDefined(typeof(ItemCategory), category))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[ItemDatabase] GetItemsByCategory called with unknown ItemCategory value ({(int)category}).");
#endif
                return Array.Empty<ItemDefinition>();
            }

            return _byCategory.TryGetValue(category, out var list)
                ? list
                : Array.Empty<ItemDefinition>();
        }
    }
}

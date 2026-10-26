using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;

namespace IronGrind.Tests.EditMode.EquipmentSystem
{
    /// <summary>In-memory <see cref="IItemDatabase"/> the tests add definitions to. Shared by the Equipment System fixtures.</summary>
    internal sealed class FakeItemDatabase : IItemDatabase
    {
        private readonly Dictionary<ItemID, ItemDefinition> _items = new Dictionary<ItemID, ItemDefinition>();

        public bool IsReady => true;

        public event Action OnDatabaseReady
        {
            add { }
            remove { }
        }

        public void Add(ItemDefinition definition) => _items[definition.ItemId] = definition;

        public void Remove(ItemID id) => _items.Remove(id);

        public ItemDefinition GetItem(ItemID id) => TryGetItem(id, out var item) ? item : null;

        public bool TryGetItem(ItemID id, out ItemDefinition item)
        {
            item = null;
            return id != ItemID.Invalid && _items.TryGetValue(id, out item);
        }

        public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
        {
            var result = new List<ItemDefinition>();
            foreach (ItemDefinition item in _items.Values)
            {
                if (item.ItemCategory == category)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Forwards to a real inventory, counts <c>MoveItemOut</c> / <c>MoveItemIn</c> calls and can force
    /// either to fail. Shared by the Equipment System fixtures.
    /// </summary>
    internal sealed class InventoryDecorator : IInventoryService
    {
        private readonly IInventoryService _inner;

        /// <summary>Count of calls forwarded to the real inventory, across all methods (the "no Inventory call" spy).</summary>
        public int TotalForwardedCalls;

        private IInventoryService Inner
        {
            get
            {
                TotalForwardedCalls++;
                return _inner;
            }
        }

        public MoveItemOutCode? MoveItemOutOverride;
        public bool FailMoveItemIn;
        public int MoveItemInCalls;
        public int MoveItemOutCalls;

        /// <summary>When set, <c>MoveItemOut</c> fails with <see cref="FailMoveItemOutCode"/> for this bag slot only.</summary>
        public int? FailMoveItemOutSlot;
        public MoveItemOutCode FailMoveItemOutCode = MoveItemOutCode.SlotLocked;
        public int ForceInsertCalls;

        /// <summary>1-based <c>ForceInsert</c> call numbers (counted in <see cref="ForceInsertCalls"/>) that return false without forwarding.</summary>
        public readonly HashSet<int> FailForceInsertCallNumbers = new HashSet<int>();

        /// <summary>Invoked at the start of <c>MoveItemIn</c> / <c>MoveItemOut</c>, for mid-swap observation.</summary>
        public Action BeforeMoveItemIn;
        public Action BeforeMoveItemOut;

        public InventoryDecorator(IInventoryService inner)
        {
            _inner = inner;
        }

        public event Action<InventoryChangedEventArgs> OnInventoryChanged
        {
            add { _inner.OnInventoryChanged += value; }
            remove { _inner.OnInventoryChanged -= value; }
        }

        public event Action<InventoryFullEventArgs> OnInventoryFull
        {
            add { _inner.OnInventoryFull += value; }
            remove { _inner.OnInventoryFull -= value; }
        }

        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex)
        {
            MoveItemOutCalls++;
            BeforeMoveItemOut?.Invoke();
            if (FailMoveItemOutSlot.HasValue && FailMoveItemOutSlot.Value == slotIndex)
            {
                return MoveItemOutResult.Fail(FailMoveItemOutCode);
            }

            return MoveItemOutOverride.HasValue
                ? MoveItemOutResult.Fail(MoveItemOutOverride.Value)
                : Inner.MoveItemOut(charId, slotIndex);
        }

        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            MoveItemInCalls++;
            BeforeMoveItemIn?.Invoke();
            return FailMoveItemIn ? MoveItemInResult.Failed : Inner.MoveItemIn(charId, itemId, enhancementLevel);
        }

        public void RegisterCharacter(CharacterID charId) => Inner.RegisterCharacter(charId);

        public InventorySlot GetSlot(CharacterID charId, int slotIndex) => Inner.GetSlot(charId, slotIndex);

        public bool IsFull(CharacterID charId) => Inner.IsFull(charId);

        public int FilledSlots(CharacterID charId) => Inner.FilledSlots(charId);

        public bool HasFreeSlot(CharacterID charId) => Inner.HasFreeSlot(charId);

        public bool IsSlotLocked(CharacterID charId, int slotIndex) => Inner.IsSlotLocked(charId, slotIndex);

        public bool HasItem(CharacterID charId, ItemID itemId) => Inner.HasItem(charId, itemId);

        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity) => Inner.Pickup(characterId, itemId, quantity);

        public void LockSlot(CharacterID charId, int slotIndex) => Inner.LockSlot(charId, slotIndex);

        public void UnlockSlot(CharacterID charId, int slotIndex) => Inner.UnlockSlot(charId, slotIndex);

        public void RemoveItem(CharacterID charId, int slotIndex) => Inner.RemoveItem(charId, slotIndex);

        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity) => Inner.Discard(charId, slotIndex, quantity);

        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot) => Inner.Move(charId, fromSlot, toSlot);

        public bool ForceInsert(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            ForceInsertCalls++;
            if (FailForceInsertCallNumbers.Contains(ForceInsertCalls))
            {
                return false;
            }

            return Inner.ForceInsert(charId, itemId, enhancementLevel);
        }

        public bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level) => Inner.SetEnhancementLevel(charId, slotIndex, level);

        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity) => Inner.SellItem(charId, slotIndex, itemId, quantity);

        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity) => Inner.ConsumeItem(charId, itemId, quantity);

        public InventorySnapshot ExportSnapshot(CharacterID charId) => Inner.ExportSnapshot(charId);

        public bool ImportSnapshot(CharacterID charId, InventorySnapshot snapshot) => Inner.ImportSnapshot(charId, snapshot);

        public void UnregisterCharacter(CharacterID charId) => Inner.UnregisterCharacter(charId);
    }
}

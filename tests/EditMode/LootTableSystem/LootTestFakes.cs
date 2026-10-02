using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.Tests.EditMode.LootTableSystem
{
    /// <summary>
    /// Recording <see cref="IInventoryService"/> fake shared by the Loot Table tests.
    /// <see cref="Pickup"/> records the call and returns a result chosen by the test;
    /// <see cref="HasFreeSlot"/> answers only when the test has set <see cref="FreeSlot"/>. Every
    /// other method throws <see cref="NotSupportedException"/>, so a loot change that starts
    /// calling one fails loudly instead of reading a made-up answer.
    /// </summary>
    /// <remarks>
    /// <see cref="OnInventoryChanged"/> fires only when the test calls
    /// <see cref="RaiseInventoryChanged"/> — <see cref="Pickup"/> does not raise it. A test that
    /// needs "a subscriber of OnInventoryChanged calls back into the loot service during a pickup"
    /// uses <see cref="OnPickup"/>, which runs inside <see cref="Pickup"/> the way a synchronous
    /// subscriber would. <see cref="OnInventoryFull"/> accepts subscriptions and never fires.
    /// </remarks>
    internal sealed class RecordingInventoryService : IInventoryService
    {
        /// <summary>One recorded <see cref="Pickup"/> call.</summary>
        public readonly struct PickupCall
        {
            public readonly CharacterID Character;
            public readonly ItemID Item;
            public readonly int Quantity;

            public PickupCall(CharacterID character, ItemID item, int quantity)
            {
                Character = character;
                Item = item;
                Quantity = quantity;
            }
        }

        /// <summary>Every <see cref="Pickup"/> call, in order.</summary>
        public readonly List<PickupCall> Calls = new List<PickupCall>();

        /// <summary>Result returned by <see cref="Pickup"/>.</summary>
        public PickupResult DefaultResult = PickupResult.Succeeded;

        /// <summary>When set, <see cref="Pickup"/> throws it (after recording the call and running <see cref="OnPickup"/>).</summary>
        public Exception ThrowOnPickup;

        /// <summary>Invoked inside <see cref="Pickup"/> after the call is recorded, for re-entrancy tests.</summary>
        public Action<CharacterID, ItemID> OnPickup;

        /// <summary>
        /// Answer of <see cref="HasFreeSlot"/>. While null, <see cref="HasFreeSlot"/> throws like
        /// the other unfaked members.
        /// </summary>
        public bool? FreeSlot;

        /// <summary>Every character <see cref="HasFreeSlot"/> was asked about, in order.</summary>
        public readonly List<CharacterID> HasFreeSlotCalls = new List<CharacterID>();

        private static readonly SlotChange[] NoChanges = new SlotChange[0];

        public event Action<InventoryChangedEventArgs> OnInventoryChanged;

        /// <summary>Raises <see cref="OnInventoryChanged"/> for the character, with no slot entries.</summary>
        public void RaiseInventoryChanged(CharacterID character)
        {
            OnInventoryChanged?.Invoke(new InventoryChangedEventArgs(character, NoChanges, 0));
        }

        public event Action<InventoryFullEventArgs> OnInventoryFull
        {
            add { }
            remove { }
        }

        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity)
        {
            Calls.Add(new PickupCall(characterId, itemId, quantity));
            OnPickup?.Invoke(characterId, itemId);
            if (ThrowOnPickup != null)
            {
                throw ThrowOnPickup;
            }
            return DefaultResult;
        }

        private static NotSupportedException Unsupported([CallerMemberName] string member = "")
        {
            return new NotSupportedException($"RecordingInventoryService.{member} is not faked; the loot tests only expect Pickup to be called.");
        }

        public void RegisterCharacter(CharacterID charId) => throw Unsupported();

        public InventorySlot GetSlot(CharacterID charId, int slotIndex) => throw Unsupported();

        public bool IsFull(CharacterID charId) => throw Unsupported();

        public int FilledSlots(CharacterID charId) => throw Unsupported();

        public bool HasFreeSlot(CharacterID charId)
        {
            if (!FreeSlot.HasValue)
            {
                throw Unsupported();
            }
            HasFreeSlotCalls.Add(charId);
            return FreeSlot.Value;
        }

        public bool IsSlotLocked(CharacterID charId, int slotIndex) => throw Unsupported();

        public bool HasItem(CharacterID charId, ItemID itemId) => throw Unsupported();

        public void LockSlot(CharacterID charId, int slotIndex) => throw Unsupported();

        public void UnlockSlot(CharacterID charId, int slotIndex) => throw Unsupported();

        public void RemoveItem(CharacterID charId, int slotIndex) => throw Unsupported();

        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity) => throw Unsupported();

        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot) => throw Unsupported();

        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex) => throw Unsupported();

        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId) => throw Unsupported();

        public bool ForceInsert(CharacterID charId, ItemID itemId) => throw Unsupported();

        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity) => throw Unsupported();

        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity) => throw Unsupported();

        public InventorySnapshot ExportSnapshot(CharacterID charId) => throw Unsupported();

        public bool ImportSnapshot(CharacterID charId, InventorySnapshot snapshot) => throw Unsupported();

        public void UnregisterCharacter(CharacterID charId) => throw Unsupported();
    }

    /// <summary>
    /// <see cref="IItemDatabase"/> fake that knows no item, for fixtures that do not care about
    /// display names.
    /// </summary>
    internal sealed class EmptyItemDatabase : IItemDatabase
    {
        public bool IsReady => true;

        public event Action OnDatabaseReady
        {
            add { }
            remove { }
        }

        public ItemDefinition GetItem(ItemID id) => null;

        public bool TryGetItem(ItemID id, out ItemDefinition item)
        {
            item = null;
            return false;
        }

        public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
        {
            return new List<ItemDefinition>();
        }
    }

    /// <summary>
    /// <see cref="ICharacterPositionProvider"/> fake: a settable position per character. A character
    /// with no entry has no position (disconnected, not in the zone).
    /// </summary>
    internal sealed class SettablePositionProvider : ICharacterPositionProvider
    {
        private readonly Dictionary<CharacterID, Vector3> _positions = new Dictionary<CharacterID, Vector3>();

        /// <summary>Gives <paramref name="character"/> a position.</summary>
        public void Set(CharacterID character, Vector3 position)
        {
            _positions[character] = position;
        }

        /// <summary>Removes the character's position, as a disconnect would.</summary>
        public void Remove(CharacterID character)
        {
            _positions.Remove(character);
        }

        public bool TryGetPosition(CharacterID characterId, out Vector3 position)
        {
            return _positions.TryGetValue(characterId, out position);
        }
    }
}

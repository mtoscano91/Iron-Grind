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

        /// <summary>
        /// Per-character answer of <see cref="HasFreeSlot"/>. A character with an entry gets that
        /// answer; every other character falls back to <see cref="FreeSlot"/>.
        /// </summary>
        public readonly Dictionary<CharacterID, bool> FreeSlotByCharacter = new Dictionary<CharacterID, bool>();

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
            bool hasOverride = FreeSlotByCharacter.TryGetValue(charId, out bool overrideAnswer);
            if (!hasOverride && !FreeSlot.HasValue)
            {
                throw Unsupported();
            }
            HasFreeSlotCalls.Add(charId);
            return hasOverride ? overrideAnswer : FreeSlot.Value;
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
    /// <see cref="IItemDatabase"/> fake whose equipment category returns a list the test supplies;
    /// every other category is empty and no item is known by id. Feeds a <see cref="LootEquipmentCache"/>.
    /// </summary>
    internal sealed class FakeEquipmentItemDatabase : IItemDatabase
    {
        private readonly List<ItemDefinition> _equipment;

        public FakeEquipmentItemDatabase(List<ItemDefinition> equipment)
        {
            _equipment = equipment;
        }

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
            return category == ItemCategory.Equipment ? _equipment : new List<ItemDefinition>();
        }
    }

    /// <summary>One gold call recorded by <see cref="RecordingCurrencyService"/>.</summary>
    internal readonly struct GoldCall
    {
        public readonly CharacterID Character;
        public readonly uint Amount;
        public readonly GoldTransactionReason Reason;

        public GoldCall(CharacterID character, uint amount, GoldTransactionReason reason)
        {
            Character = character;
            Amount = amount;
            Reason = reason;
        }
    }

    /// <summary>
    /// <see cref="ICurrencyService"/> wrapper that records every <see cref="TrySpendGold"/> and
    /// <see cref="AddGold"/> call and delegates everything to the wrapped service.
    /// </summary>
    /// <remarks>
    /// When <see cref="ThrowOnAdd"/> is set, <see cref="AddGold"/> records the call and then throws
    /// <see cref="InvalidOperationException"/> with <see cref="ADD_FAILURE_MESSAGE"/> instead of delegating.
    /// </remarks>
    internal sealed class RecordingCurrencyService : ICurrencyService
    {
        /// <summary>Message of the exception thrown by <see cref="AddGold"/> while <see cref="ThrowOnAdd"/> is set.</summary>
        public const string ADD_FAILURE_MESSAGE = "pool failure";

        private readonly ICurrencyService _inner;

        /// <summary>Every <see cref="TrySpendGold"/> call, in order.</summary>
        public readonly List<GoldCall> Spends = new List<GoldCall>();

        /// <summary>Every <see cref="AddGold"/> call, in order.</summary>
        public readonly List<GoldCall> Adds = new List<GoldCall>();

        /// <summary>When set, <see cref="AddGold"/> throws after recording the call.</summary>
        public bool ThrowOnAdd;

        public RecordingCurrencyService(ICurrencyService inner)
        {
            _inner = inner;
        }

        public event Action<GoldSyncEventArgs> OnGoldSync
        {
            add { _inner.OnGoldSync += value; }
            remove { _inner.OnGoldSync -= value; }
        }

        public void RegisterCharacter(CharacterID charId, uint initialBalance) => _inner.RegisterCharacter(charId, initialBalance);

        public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
        {
            Adds.Add(new GoldCall(charId, amount, reason));
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException(ADD_FAILURE_MESSAGE);
            }
            return _inner.AddGold(charId, amount, reason);
        }

        public uint GetBalance(CharacterID charId) => _inner.GetBalance(charId);

        public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
        {
            Spends.Add(new GoldCall(charId, cost, reason));
            return _inner.TrySpendGold(charId, cost, reason);
        }

        public GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount) => _inner.TransferGold(fromId, toId, amount);
    }

    /// <summary>
    /// <see cref="IPartyService"/> fake for one party whose member list, connection state and
    /// round-robin cursor the test can change. A character is in the party while it is in
    /// <see cref="Members"/>; <see cref="IsMemberConnected"/> is true unless the character is in
    /// <see cref="NotConnected"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ThrowOnMembersRead"/> and <see cref="ThrowOnCursorRead"/> make
    /// <see cref="GetPartyMembers"/> and <see cref="GetRrNextIndex"/> throw
    /// <see cref="InvalidOperationException"/> with <see cref="MEMBERS_FAILURE_MESSAGE"/> and
    /// <see cref="ROUND_ROBIN_FAILURE_MESSAGE"/>, so a test can expect the logged exception.
    /// <see cref="AdvanceCalls"/> counts <see cref="AdvanceRrNextIndex"/> calls.
    /// </remarks>
    internal sealed class MutablePartyService : IPartyService
    {
        /// <summary>Message thrown by <see cref="GetPartyMembers"/> while <see cref="ThrowOnMembersRead"/> is set.</summary>
        public const string MEMBERS_FAILURE_MESSAGE = "members failure";

        /// <summary>Message thrown by <see cref="GetRrNextIndex"/> while <see cref="ThrowOnCursorRead"/> is set.</summary>
        public const string ROUND_ROBIN_FAILURE_MESSAGE = "round-robin failure";

        private readonly PartyID _party;

        /// <summary>The party's members, in round-robin order.</summary>
        public readonly List<CharacterID> Members = new List<CharacterID>();

        /// <summary>Members for whom <see cref="IsMemberConnected"/> answers false.</summary>
        public readonly HashSet<CharacterID> NotConnected = new HashSet<CharacterID>();

        /// <summary>Round-robin cursor returned by <see cref="GetRrNextIndex"/>.</summary>
        public int Cursor;

        /// <summary>Number of <see cref="AdvanceRrNextIndex"/> calls.</summary>
        public int AdvanceCalls;

        /// <summary>When set, <see cref="GetRrNextIndex"/> throws.</summary>
        public bool ThrowOnCursorRead;

        /// <summary>When set, <see cref="GetPartyMembers"/> throws.</summary>
        public bool ThrowOnMembersRead;

        public MutablePartyService(PartyID party)
        {
            _party = party;
        }

        public PartyID GetPartyID(CharacterID characterId)
        {
            return Members.Contains(characterId) ? _party : PartyID.Uninitialized;
        }

        public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
        {
            if (ThrowOnMembersRead)
            {
                throw new InvalidOperationException(MEMBERS_FAILURE_MESSAGE);
            }
            return new List<CharacterID>(Members);
        }

        public CharacterID GetMemberAtIndex(PartyID partyId, int index)
        {
            return index >= 0 && index < Members.Count ? Members[index] : CharacterID.Invalid;
        }

        public int GetRrNextIndex(PartyID partyId)
        {
            if (ThrowOnCursorRead)
            {
                throw new InvalidOperationException(ROUND_ROBIN_FAILURE_MESSAGE);
            }
            return Cursor;
        }

        public void AdvanceRrNextIndex(PartyID partyId)
        {
            AdvanceCalls++;
            Cursor = (Cursor + 1) % Members.Count;
        }

        public bool IsMemberConnected(CharacterID characterId)
        {
            return !NotConnected.Contains(characterId);
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

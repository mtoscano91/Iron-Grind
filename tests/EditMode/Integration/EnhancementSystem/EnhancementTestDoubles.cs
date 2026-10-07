using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// Random source for tests: <see cref="NextDouble"/> returns queued values in order and counts
    /// draws. Asking for a value when none is queued throws, so an unexpected draw fails the test.
    /// Shared by the Enhancement System integration tests (Story 004 onwards).
    /// </summary>
    internal sealed class ScriptedRandom : System.Random
    {
        private readonly Queue<double> _values = new Queue<double>();

        /// <summary>Number of values handed out so far.</summary>
        public int DrawCount { get; private set; }

        /// <summary>Appends a value to the end of the script.</summary>
        public void Enqueue(double value)
        {
            _values.Enqueue(value);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">No value is queued.</exception>
        public override double NextDouble()
        {
            if (_values.Count == 0)
                throw new InvalidOperationException("ScriptedRandom: a draw was requested but no value is queued.");
            DrawCount++;
            return _values.Dequeue();
        }
    }

    /// <summary>Settable stand-in for the NPC session query; active by default.</summary>
    internal sealed class StubNpcSessions : INpcInteractionSessions
    {
        /// <summary>Whether every character is reported as having an active NPC session.</summary>
        public bool Active { get; set; } = true;

        /// <inheritdoc />
        public bool IsActive(CharacterID charId) => Active;
    }

    /// <summary>
    /// Forwards every <see cref="IInventoryService"/> member to an inner service and records the
    /// names of the mutating calls in order in <see cref="Calls"/>. Reads are forwarded unrecorded.
    /// When <see cref="FailSetEnhancementLevel"/> is true, <c>SetEnhancementLevel</c> is recorded
    /// and returns false without reaching the inner service (simulates a broken invariant).
    /// </summary>
    internal sealed class RecordingInventoryDecorator : IInventoryService
    {
        private readonly IInventoryService _inner;

        /// <summary>Creates a decorator over <paramref name="inner"/>.</summary>
        public RecordingInventoryDecorator(IInventoryService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Names of the mutating calls received, in call order.</summary>
        public List<string> Calls { get; } = new List<string>();

        /// <summary>When true, <see cref="SetEnhancementLevel"/> records the call and returns false without forwarding.</summary>
        public bool FailSetEnhancementLevel { get; set; }

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

        public void RegisterCharacter(CharacterID charId) => _inner.RegisterCharacter(charId);

        public InventorySlot GetSlot(CharacterID charId, int slotIndex) => _inner.GetSlot(charId, slotIndex);

        public bool IsFull(CharacterID charId) => _inner.IsFull(charId);

        public int FilledSlots(CharacterID charId) => _inner.FilledSlots(charId);

        public bool HasFreeSlot(CharacterID charId) => _inner.HasFreeSlot(charId);

        public bool IsSlotLocked(CharacterID charId, int slotIndex) => _inner.IsSlotLocked(charId, slotIndex);

        public bool HasItem(CharacterID charId, ItemID itemId) => _inner.HasItem(charId, itemId);

        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity)
        {
            Calls.Add("Pickup");
            return _inner.Pickup(characterId, itemId, quantity);
        }

        public void LockSlot(CharacterID charId, int slotIndex)
        {
            Calls.Add("LockSlot");
            _inner.LockSlot(charId, slotIndex);
        }

        public void UnlockSlot(CharacterID charId, int slotIndex)
        {
            Calls.Add("UnlockSlot");
            _inner.UnlockSlot(charId, slotIndex);
        }

        public void RemoveItem(CharacterID charId, int slotIndex)
        {
            Calls.Add("RemoveItem");
            _inner.RemoveItem(charId, slotIndex);
        }

        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity)
        {
            Calls.Add("Discard");
            return _inner.Discard(charId, slotIndex, quantity);
        }

        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot)
        {
            Calls.Add("Move");
            return _inner.Move(charId, fromSlot, toSlot);
        }

        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex)
        {
            Calls.Add("MoveItemOut");
            return _inner.MoveItemOut(charId, slotIndex);
        }

        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            Calls.Add("MoveItemIn");
            return _inner.MoveItemIn(charId, itemId, enhancementLevel);
        }

        public bool ForceInsert(CharacterID charId, ItemID itemId, byte enhancementLevel)
        {
            Calls.Add("ForceInsert");
            return _inner.ForceInsert(charId, itemId, enhancementLevel);
        }

        public bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level)
        {
            Calls.Add("SetEnhancementLevel");
            if (FailSetEnhancementLevel)
                return false;
            return _inner.SetEnhancementLevel(charId, slotIndex, level);
        }

        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            Calls.Add("SellItem");
            return _inner.SellItem(charId, slotIndex, itemId, quantity);
        }

        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity)
        {
            Calls.Add("ConsumeItem");
            return _inner.ConsumeItem(charId, itemId, quantity);
        }

        public InventorySnapshot ExportSnapshot(CharacterID charId) => _inner.ExportSnapshot(charId);

#nullable enable
        public bool ImportSnapshot(CharacterID charId, InventorySnapshot? snapshot)
        {
            Calls.Add("ImportSnapshot");
            return _inner.ImportSnapshot(charId, snapshot);
        }
#nullable restore

        public void UnregisterCharacter(CharacterID charId)
        {
            Calls.Add("UnregisterCharacter");
            _inner.UnregisterCharacter(charId);
        }
    }
}

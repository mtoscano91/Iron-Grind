using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using IronGrind.NpcInteraction;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>Settable stand-in for the NPC session query; active by default.</summary>
    internal sealed class StubNpcSessions : INpcInteractionSessions
    {
        /// <summary>Whether every character is reported as having an active NPC session.</summary>
        public bool Active { get; set; } = true;

        /// <inheritdoc />
        public bool IsActive(CharacterID charId) => Active;
    }

    /// <summary>
    /// Settable stand-in for the town hub query: every character is in the town hub by default;
    /// <see cref="OutsideTownHub"/> holds per-character overrides for characters that are not.
    /// </summary>
    internal sealed class StubTownHubQuery : ITownHubQuery
    {
        /// <summary>Whether characters not listed in <see cref="OutsideTownHub"/> are in the town hub.</summary>
        public bool InTownHub { get; set; } = true;

        /// <summary>Characters reported as outside the town hub regardless of <see cref="InTownHub"/>.</summary>
        public HashSet<CharacterID> OutsideTownHub { get; } = new HashSet<CharacterID>();

        /// <inheritdoc />
        public bool IsInTownHub(CharacterID charId) => InTownHub && !OutsideTownHub.Contains(charId);
    }

    /// <summary>Manually driven wall clock in seconds; tests pass <c>() =&gt; clock.Now</c> to the code under test.</summary>
    internal sealed class ManualClock
    {
        /// <summary>The current time in seconds.</summary>
        public double Now { get; set; }

        /// <summary>Moves the clock forward by <paramref name="seconds"/>.</summary>
        public void Advance(double seconds)
        {
            Now += seconds;
        }
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

        /// <summary>When true, <see cref="ForceInsert"/> records the call and returns false without forwarding.</summary>
        public bool FailForceInsert { get; set; }

        /// <summary>When true, <see cref="Pickup"/> records the call and returns a failed (inventory full) result without forwarding.</summary>
        public bool FailPickup { get; set; }

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
            if (FailPickup)
                return PickupResult.Fail(PickupFailReason.InventoryFull);
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
            if (FailForceInsert)
                return false;
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

    /// <summary>One message recorded by <see cref="RecordingClientMessageOutbox"/>; <see cref="Body"/> is a copy.</summary>
    internal sealed class RecordedClientMessage
    {
        /// <summary>The receiving connection.</summary>
        public uint ClientId { get; }

        /// <summary>The wire message type id.</summary>
        public ushort MessageTypeId { get; }

        /// <summary>A copy of the body.</summary>
        public byte[] Body { get; }

        /// <summary>The cap-exemption flag.</summary>
        public bool IsCapExempt { get; }

        /// <summary>Creates a record.</summary>
        public RecordedClientMessage(uint clientId, ushort messageTypeId, byte[] body, bool isCapExempt)
        {
            ClientId = clientId;
            MessageTypeId = messageTypeId;
            Body = body;
            IsCapExempt = isCapExempt;
        }
    }

    /// <summary>Records every <c>Enqueue</c> call, copying the body inside the call.</summary>
    internal sealed class RecordingClientMessageOutbox : IClientMessageOutbox
    {
        /// <summary>All recorded messages, in call order.</summary>
        public List<RecordedClientMessage> Messages { get; } = new List<RecordedClientMessage>();

        /// <inheritdoc />
        public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
        {
            Messages.Add(new RecordedClientMessage(clientId, messageTypeId, body.ToArray(), isCapExempt));
        }

        /// <summary>The messages recorded for <paramref name="clientId"/>.</summary>
        public List<RecordedClientMessage> ForClient(uint clientId)
        {
            return Messages.FindAll(message => message.ClientId == clientId);
        }

        /// <summary>The messages of <paramref name="messageTypeId"/> recorded for <paramref name="clientId"/>.</summary>
        public List<RecordedClientMessage> ForClient(uint clientId, ushort messageTypeId)
        {
            return Messages.FindAll(message => message.ClientId == clientId && message.MessageTypeId == messageTypeId);
        }
    }

    /// <summary>One call recorded by <see cref="RecordingAttemptStarter"/>.</summary>
    internal readonly struct RecordedAttemptStart
    {
        /// <summary>The requesting character.</summary>
        public readonly CharacterID CharacterId;

        /// <summary>The request id.</summary>
        public readonly uint RequestId;

        /// <summary>The item slot.</summary>
        public readonly int ItemSlotIndex;

        /// <summary>The scroll slot.</summary>
        public readonly int ScrollSlotIndex;

        /// <summary>Creates a record.</summary>
        public RecordedAttemptStart(CharacterID characterId, uint requestId, int itemSlotIndex, int scrollSlotIndex)
        {
            CharacterId = characterId;
            RequestId = requestId;
            ItemSlotIndex = itemSlotIndex;
            ScrollSlotIndex = scrollSlotIndex;
        }
    }

    /// <summary>Records every <c>StartAttempt</c> call.</summary>
    internal sealed class RecordingAttemptStarter : IEnhancementAttemptStarter
    {
        /// <summary>All recorded calls, in call order.</summary>
        public List<RecordedAttemptStart> Calls { get; } = new List<RecordedAttemptStart>();

        /// <inheritdoc />
        public void StartAttempt(CharacterID characterId, uint requestId, int itemSlotIndex, int scrollSlotIndex)
        {
            Calls.Add(new RecordedAttemptStart(characterId, requestId, itemSlotIndex, scrollSlotIndex));
        }
    }

    /// <summary>Dictionary-backed <see cref="IEnhancementRequestDedupLookup"/>; a character not set has no deduplicator.</summary>
    internal sealed class DictionaryDedupLookup : IEnhancementRequestDedupLookup
    {
        private readonly Dictionary<CharacterID, EnhancementRequestDeduplicator> _byCharacter =
            new Dictionary<CharacterID, EnhancementRequestDeduplicator>();

        /// <summary>Sets the deduplicator of a character.</summary>
        public void Set(CharacterID characterId, EnhancementRequestDeduplicator deduplicator)
        {
            _byCharacter[characterId] = deduplicator;
        }

        /// <inheritdoc />
        public bool TryGetDeduplicator(CharacterID characterId, out EnhancementRequestDeduplicator deduplicator)
        {
            return _byCharacter.TryGetValue(characterId, out deduplicator);
        }
    }
}

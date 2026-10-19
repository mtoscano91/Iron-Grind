using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>An <see cref="ICharacterZoneLocator"/> that returns a settable zone and counts calls.</summary>
    internal sealed class FakeZoneLocator : ICharacterZoneLocator
    {
        public ZoneID Zone { get; set; } = ZoneID.Invalid;

        public int CallCount { get; private set; }

        public ZoneID GetCurrentZone(CharacterID charId)
        {
            CallCount++;
            return Zone;
        }
    }

    /// <summary>An <see cref="ICharacterRecordStore"/> that forwards to an inner store and counts each kind of call.</summary>
    internal sealed class CountingStore : ICharacterRecordStore
    {
        private readonly ICharacterRecordStore _inner;

        public CountingStore(ICharacterRecordStore inner)
        {
            _inner = inner;
        }

        public int FetchCount { get; private set; }

        public int InsertCount { get; private set; }

        public int UpdateCount { get; private set; }

        public System.Threading.Tasks.Task<CharacterRecord> FetchAsync(CharacterID id, System.Threading.CancellationToken ct)
        {
            FetchCount++;
            return _inner.FetchAsync(id, ct);
        }

        public System.Threading.Tasks.Task<bool> InsertAsync(CharacterRecord record, System.Threading.CancellationToken ct)
        {
            InsertCount++;
            return _inner.InsertAsync(record, ct);
        }

        /// <summary>
        /// When true the update ignores its token, as a commit that wins against the service's write-timeout cancel
        /// (Story 014: the service cancels the store's token at the timeout).
        /// </summary>
        public bool IgnoreUpdateToken { get; set; }

        public System.Threading.Tasks.Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, System.Threading.CancellationToken ct)
        {
            UpdateCount++;
            return _inner.UpdateAsync(record, expectedSaveVersion, IgnoreUpdateToken ? System.Threading.CancellationToken.None : ct);
        }
    }

    /// <summary>
    /// An <see cref="IInventoryService"/> for the Character Persistence load tests: records each <c>ImportSnapshot</c>
    /// call into an optional shared call log, samples the current HP / MP at that moment through an optional probe,
    /// and returns a configurable result. Every other member throws, so no test passes on a silent default.
    /// </summary>
    internal sealed class RecordingInventory : IInventoryService
    {
        /// <summary>The call-log entry written by <see cref="ImportSnapshot"/>.</summary>
        public const string CALL_IMPORT = "import";

        private readonly List<string> _log;
        private readonly Func<(float Hp, float Mp)> _resources;

        public RecordingInventory(List<string> log = null, Func<(float Hp, float Mp)> resources = null)
        {
            _log = log ?? new List<string>();
            _resources = resources;
        }

        public bool ImportResult { get; set; } = true;

        public List<(CharacterID CharId, InventorySnapshot Snapshot)> ImportCalls { get; } = new List<(CharacterID, InventorySnapshot)>();

        /// <summary>Current HP seen at the last import; -1 when there was no import or no probe.</summary>
        public float HpAtImport { get; private set; } = -1f;

        /// <summary>Current MP seen at the last import; -1 when there was no import or no probe.</summary>
        public float MpAtImport { get; private set; } = -1f;

        public event Action<InventoryChangedEventArgs> OnInventoryChanged { add { } remove { } }

        public event Action<InventoryFullEventArgs> OnInventoryFull { add { } remove { } }

#nullable enable
        public bool ImportSnapshot(CharacterID charId, InventorySnapshot? snapshot)
        {
            _log.Add(CALL_IMPORT);
            if (_resources != null)
            {
                (HpAtImport, MpAtImport) = _resources();
            }

            ImportCalls.Add((charId, snapshot!));
            return ImportResult;
        }
#nullable restore

        public void RegisterCharacter(CharacterID charId) => throw new NotSupportedException();

        public InventorySlot GetSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public bool IsFull(CharacterID charId) => throw new NotSupportedException();

        public int FilledSlots(CharacterID charId) => throw new NotSupportedException();

        public bool HasFreeSlot(CharacterID charId) => throw new NotSupportedException();

        public bool IsSlotLocked(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public bool HasItem(CharacterID charId, ItemID itemId) => throw new NotSupportedException();

        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity) => throw new NotSupportedException();

        public void LockSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public void UnlockSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public void RemoveItem(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity) => throw new NotSupportedException();

        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot) => throw new NotSupportedException();

        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId, byte enhancementLevel) => throw new NotSupportedException();

        public bool ForceInsert(CharacterID charId, ItemID itemId, byte enhancementLevel) => throw new NotSupportedException();

        public bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level) => throw new NotSupportedException();

        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity) => throw new NotSupportedException();

        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity) => throw new NotSupportedException();

        /// <summary>What <see cref="ExportSnapshot"/> returns; null until a test sets it, in which case the call throws.</summary>
        public InventorySnapshot ExportResult { get; set; }

        /// <summary>Number of <see cref="ExportSnapshot"/> calls.</summary>
        public int ExportCallCount { get; private set; }

        public InventorySnapshot ExportSnapshot(CharacterID charId)
        {
            ExportCallCount++;
            return ExportResult ?? throw new NotSupportedException("RecordingInventory.ExportResult was not set.");
        }

        /// <summary>Every <see cref="UnregisterCharacter"/> call, in call order (Story 010).</summary>
        public List<CharacterID> UnregisterCalls { get; } = new List<CharacterID>();

        public void UnregisterCharacter(CharacterID charId) => UnregisterCalls.Add(charId);
    }
}

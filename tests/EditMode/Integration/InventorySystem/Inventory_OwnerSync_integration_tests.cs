using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.Integration.EnhancementSystem;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.InventorySystem
{
    /// <summary>
    /// EditMode integration tests for Inventory Story 012: <see cref="OwnerInventorySyncSender"/> over a real
    /// <see cref="InventoryService"/> and a recording outbox. Captured bodies are decoded with the
    /// <c>InventorySlotUpdate</c> / <c>InventoryFullSync</c> codecs (networking-wire-protocol.md TD-046, AC-NC-44).
    /// Warning counts: each expected warning is consumed with <c>LogAssert.Expect</c> and its exact count is asserted
    /// through <c>Application.logMessageReceived</c> (<see cref="WarningCounter"/>).
    /// </summary>
    [TestFixture]
    internal sealed class Inventory_OwnerSync_Integration_Tests
    {
        private const uint CLIENT_A = 101u;
        private const uint CLIENT_B = 102u;
        private const uint CHARACTER_A_RAW = 501u;
        private const uint CHARACTER_B_RAW = 502u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint IRON_SWORD_ID = 4002u;
        private const uint POTION_ID = 4010u;
        private const int POTION_STACK_LIMIT = 99;
        private const int POTION_QUANTITY = 5;
        private const int POTION_EXTRA_QUANTITY = 3;
        private const byte ENHANCEMENT_LEVEL = 3;

        private const int SWORD_SLOT = 0;
        private const int OTHER_SWORD_SLOT = 1;
        private const int POTION_SLOT = 2;

        // A pickup of two full stacks plus POTION_QUANTITY units fills slots 0, 1 and 2 in one event.
        private const int BIG_POTION_PICKUP_QUANTITY = 2 * POTION_STACK_LIMIT + POTION_QUANTITY;
        private const int BIG_POTION_PICKUP_SLOT_COUNT = 3;
        private const int SWORD_SLOT_AFTER_BIG_PICKUP = 3;

        // Hard-coded on purpose: MAX_HELD_UPDATES_PER_CHARACTER (8) + 3 swords fill slots 0 to 10.
        private const int OVERFLOW_EXTRA_EVENTS = 3;
        private const int OVERFLOW_LAST_SWORD_SLOT = 10;
        private const int OVERFLOW_FIRST_EMPTY_SLOT = 11;
        private const int SECOND_HOLD_EVENT_COUNT = OwnerInventorySyncSender.MAX_HELD_UPDATES_PER_CHARACTER + 1;

        // Messages EveryMessage_IsNotCapExempt produces: 2 full syncs and 4 updates (2 live, 2 released held).
        private const int CAP_EXEMPT_TEST_MESSAGE_COUNT = 6;

        private const string OPEN_HOLD_WARNING = "OpenHold: a hold is already open";
        private const string RELEASE_WARNING = "ReleaseHold: no hold is open";
        private const string DISCARD_WARNING = "DiscardHold: no hold is open";
        private const string OVERFLOW_WARNING = "hold overflow";

        private static readonly CharacterID CharacterA = new CharacterID(CHARACTER_A_RAW);
        private static readonly CharacterID CharacterB = new CharacterID(CHARACTER_B_RAW);
        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID IronSwordId = new ItemID(IRON_SWORD_ID);
        private static readonly ItemID PotionId = new ItemID(POTION_ID);

        private List<ItemDefinition> _definitions;
        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private RecordingClientMessageOutbox _outbox;
        private OwnerInventorySyncSender _sender;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SWORD_ID, "Iron Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron)),
                ItemDefinitionBuilder.Build(POTION_ID, "Health Potion", ItemCategory.Consumable,
                    stackLimit: POTION_STACK_LIMIT),
            };
            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(CharacterA);
            _inventory.RegisterCharacter(CharacterB);

            _outbox = new RecordingClientMessageOutbox();
            _sender = new OwnerInventorySyncSender(_inventory, _outbox);
        }

        [TearDown]
        public void TearDown()
        {
            _sender.Dispose();
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        // Counts the warnings whose text contains a fragment, from construction until disposal. Unity fails a test on an
        // unexpected Error / Exception / Assert log but not on a warning, so the exact count is checked here.
        private sealed class WarningCounter : IDisposable
        {
            private readonly string _fragment;

            public WarningCounter(string fragment)
            {
                _fragment = fragment;
                Application.logMessageReceived += OnLog;
            }

            public int Count { get; private set; }

            public void Dispose()
            {
                Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains(_fragment))
                    Count++;
            }
        }

        private void Connect(uint clientId, CharacterID characterId)
        {
            _sender.OnSessionReadySent(clientId, characterId);
        }

        // Connects character A and forgets the zone-entry full sync, so tests see only what follows.
        private void ConnectAAndClear()
        {
            Connect(CLIENT_A, CharacterA);
            _outbox.Messages.Clear();
        }

        private void PickupSword(CharacterID character, ItemID id)
        {
            Assert.IsTrue(_inventory.Pickup(character, id, 1).Success, "The pickup must succeed.");
        }

        // SetEnhancementLevel requires a locked slot (the Enhancement System locks it first). Lock and unlock raise no
        // InventoryChangedEvent, so the level change is the only bag change this helper produces.
        private void EnhanceSword(CharacterID character, int slotIndex, byte level)
        {
            _inventory.LockSlot(character, slotIndex);
            Assert.IsTrue(_inventory.SetEnhancementLevel(character, slotIndex, level), "The level change must succeed.");
            _inventory.UnlockSlot(character, slotIndex);
        }

        private void PickupSwords(int count)
        {
            for (int i = 0; i < count; i++)
                PickupSword(CharacterA, BronzeSwordId);
        }

        private static InventorySlotEntry[] DecodeUpdate(RecordedClientMessage message)
        {
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, message.MessageTypeId);
            var entries = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            Assert.IsTrue(InventorySlotUpdateCodec.TryReadBody(message.Body, entries, out int count), "The update body must decode.");
            var result = new InventorySlotEntry[count];
            Array.Copy(entries, result, count);
            return result;
        }

        private static InventorySlotEntry[] DecodeFullSync(RecordedClientMessage message)
        {
            Assert.AreEqual(InventoryFullSync.MessageTypeId, message.MessageTypeId);
            var entries = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            Assert.IsTrue(InventoryFullSyncCodec.TryReadBody(message.Body, entries), "The full sync body must decode.");
            return entries;
        }

        private void AssertFullSyncEqualsBag(RecordedClientMessage message, CharacterID character)
        {
            Assert.AreEqual(InventoryFullSync.BodySize, message.Body.Length);
            InventorySlotEntry[] entries = DecodeFullSync(message);
            for (int i = 0; i < entries.Length; i++)
            {
                InventorySlot slot = _inventory.GetSlot(character, i);
                Assert.AreEqual(i, entries[i].SlotIndex, "Entries must be in ascending slot order.");
                Assert.AreEqual(slot.ItemId, entries[i].ItemId, $"Slot {i} item.");
                Assert.AreEqual(slot.Quantity, entries[i].Quantity, $"Slot {i} quantity.");
                Assert.AreEqual(slot.EnhancementLevel, entries[i].EnhancementLevel, $"Slot {i} level.");
            }
        }

        private void AssertEntriesEqualBag(InventorySlotEntry[] entries, CharacterID character)
        {
            foreach (InventorySlotEntry entry in entries)
            {
                InventorySlot slot = _inventory.GetSlot(character, entry.SlotIndex);
                Assert.AreEqual(slot.ItemId, entry.ItemId, $"Slot {entry.SlotIndex} item.");
                Assert.AreEqual(slot.Quantity, entry.Quantity, $"Slot {entry.SlotIndex} quantity.");
                Assert.AreEqual(slot.EnhancementLevel, entry.EnhancementLevel, $"Slot {entry.SlotIndex} level.");
            }
        }

        private static void AssertSlotIndicesDistinct(InventorySlotEntry[] entries)
        {
            var seen = new HashSet<byte>();
            foreach (InventorySlotEntry entry in entries)
                Assert.IsTrue(seen.Add(entry.SlotIndex), $"Slot {entry.SlotIndex} appears twice in one message.");
        }

        private static int CountOfType(List<RecordedClientMessage> messages, ushort messageTypeId)
        {
            return messages.FindAll(message => message.MessageTypeId == messageTypeId).Count;
        }

        // ------------------------------------------------------------------
        // One update per bag change
        // ------------------------------------------------------------------

        [Test]
        public void Pickup_IntoEmptySlot_SendsOneUpdateWithOneEntry()
        {
            ConnectAAndClear();

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(1, _outbox.Messages.Count);
            InventorySlotEntry[] entries = DecodeUpdate(_outbox.Messages[0]);
            Assert.AreEqual(1, entries.Length);
            Assert.AreEqual(SWORD_SLOT, entries[0].SlotIndex);
            Assert.AreEqual(BronzeSwordId, entries[0].ItemId);
            Assert.AreEqual(1, entries[0].Quantity);
            Assert.AreEqual(0, entries[0].EnhancementLevel);
            Assert.AreEqual(CLIENT_A, _outbox.Messages[0].ClientId);
        }

        [Test]
        public void Move_Swap_SendsOneUpdateWithTwoDistinctSlotEntries()
        {
            PickupSword(CharacterA, BronzeSwordId);
            PickupSword(CharacterA, IronSwordId);
            ConnectAAndClear();

            Assert.IsTrue(_inventory.Move(CharacterA, SWORD_SLOT, OTHER_SWORD_SLOT).Success);

            Assert.AreEqual(1, _outbox.Messages.Count);
            InventorySlotEntry[] entries = DecodeUpdate(_outbox.Messages[0]);
            Assert.AreEqual(2, entries.Length);
            AssertSlotIndicesDistinct(entries);
            Assert.AreEqual(SWORD_SLOT, Math.Min(entries[0].SlotIndex, entries[1].SlotIndex), "The lower slot is the first swapped slot.");
            Assert.AreEqual(OTHER_SWORD_SLOT, Math.Max(entries[0].SlotIndex, entries[1].SlotIndex), "The higher slot is the second swapped slot.");
            AssertEntriesEqualBag(entries, CharacterA);
        }

        [Test]
        public void SetEnhancementLevel_OnSlotZero_SendsEntryCarryingTheLevel()
        {
            PickupSword(CharacterA, BronzeSwordId);
            ConnectAAndClear();

            EnhanceSword(CharacterA, SWORD_SLOT, ENHANCEMENT_LEVEL);

            Assert.AreEqual(1, _outbox.Messages.Count);
            InventorySlotEntry[] entries = DecodeUpdate(_outbox.Messages[0]);
            Assert.AreEqual(1, entries.Length);
            Assert.AreEqual(SWORD_SLOT, entries[0].SlotIndex);
            Assert.AreEqual(BronzeSwordId, entries[0].ItemId);
            Assert.AreEqual(ENHANCEMENT_LEVEL, entries[0].EnhancementLevel);
        }

        [Test]
        public void Pickup_OfStackableItem_SendsAbsoluteQuantityNotDelta()
        {
            ConnectAAndClear();

            Assert.IsTrue(_inventory.Pickup(CharacterA, PotionId, POTION_QUANTITY).Success);
            Assert.IsTrue(_inventory.Pickup(CharacterA, PotionId, POTION_EXTRA_QUANTITY).Success);

            Assert.AreEqual(2, _outbox.Messages.Count);
            Assert.AreEqual(POTION_QUANTITY, DecodeUpdate(_outbox.Messages[0])[0].Quantity);
            Assert.AreEqual(POTION_QUANTITY + POTION_EXTRA_QUANTITY, DecodeUpdate(_outbox.Messages[1])[0].Quantity);
        }

        [Test]
        public void Pickup_SpreadingOverThreeSlots_SendsOneUpdateWithThreeDistinctEntriesEqualToTheBag()
        {
            ConnectAAndClear();

            Assert.IsTrue(_inventory.Pickup(CharacterA, PotionId, BIG_POTION_PICKUP_QUANTITY).Success);

            Assert.AreEqual(1, _outbox.Messages.Count);
            InventorySlotEntry[] entries = DecodeUpdate(_outbox.Messages[0]);
            Assert.AreEqual(BIG_POTION_PICKUP_SLOT_COUNT, entries.Length);
            AssertSlotIndicesDistinct(entries);
            AssertEntriesEqualBag(entries, CharacterA);
            Assert.AreEqual(POTION_STACK_LIMIT, _inventory.GetSlot(CharacterA, SWORD_SLOT).Quantity);
            Assert.AreEqual(POTION_QUANTITY, _inventory.GetSlot(CharacterA, POTION_SLOT).Quantity);
        }

        [Test]
        public void Hold_MultiEntryPickupThenSingleSlotChange_ReleaseSendsTwoUpdatesInOrderWithRightEntryCounts()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);

            Assert.IsTrue(_inventory.Pickup(CharacterA, PotionId, BIG_POTION_PICKUP_QUANTITY).Success);
            PickupSword(CharacterA, BronzeSwordId);
            Assert.AreEqual(0, _outbox.Messages.Count, "Nothing is sent while the hold is open.");

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(2, _outbox.Messages.Count);
            InventorySlotEntry[] first = DecodeUpdate(_outbox.Messages[0]);
            InventorySlotEntry[] second = DecodeUpdate(_outbox.Messages[1]);
            Assert.AreEqual(BIG_POTION_PICKUP_SLOT_COUNT, first.Length);
            AssertSlotIndicesDistinct(first);
            AssertEntriesEqualBag(first, CharacterA);
            Assert.AreEqual(1, second.Length);
            Assert.AreEqual(SWORD_SLOT_AFTER_BIG_PICKUP, second[0].SlotIndex);
            Assert.AreEqual(BronzeSwordId, second[0].ItemId);
        }

        [Test]
        public void BagChange_OfOneCharacter_DoesNotReachTheOtherClient()
        {
            Connect(CLIENT_A, CharacterA);
            Connect(CLIENT_B, CharacterB);
            _outbox.Messages.Clear();

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(1, _outbox.ForClient(CLIENT_A).Count);
            Assert.AreEqual(0, _outbox.ForClient(CLIENT_B).Count);
        }

        // ------------------------------------------------------------------
        // Full sync on zone entry
        // ------------------------------------------------------------------

        [Test]
        public void ZoneEntry_ThenPickup_SendsFullSyncBeforeTheUpdate()
        {
            Connect(CLIENT_A, CharacterA);
            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(2, _outbox.Messages.Count);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, _outbox.Messages[1].MessageTypeId);
            Assert.AreEqual(InventoryFullSync.BodySize, _outbox.Messages[0].Body.Length);
        }

        [Test]
        public void ZoneEntry_WithItemsStackAndEmptySlots_FullSyncEqualsTheBag()
        {
            PickupSword(CharacterA, BronzeSwordId);
            PickupSword(CharacterA, IronSwordId);
            Assert.IsTrue(_inventory.Pickup(CharacterA, PotionId, POTION_QUANTITY).Success);
            EnhanceSword(CharacterA, SWORD_SLOT, ENHANCEMENT_LEVEL);

            Connect(CLIENT_A, CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
            InventorySlotEntry[] entries = DecodeFullSync(_outbox.Messages[0]);
            Assert.AreEqual(POTION_QUANTITY, entries[POTION_SLOT].Quantity);
            Assert.AreEqual(ItemID.Invalid, entries[POTION_SLOT + 1].ItemId);
            Assert.AreEqual(0, entries[POTION_SLOT + 1].Quantity);
        }

        [Test]
        public void ZoneEntry_WithFullBag_FullSyncEqualsTheBagAndNoEntryIsEmpty()
        {
            PickupSwords(InventoryConstants.INVENTORY_SLOT_COUNT);

            Connect(CLIENT_A, CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
            foreach (InventorySlotEntry entry in DecodeFullSync(_outbox.Messages[0]))
                Assert.AreNotEqual(ItemID.Invalid, entry.ItemId, $"Slot {entry.SlotIndex} must not be empty.");
        }

        // ------------------------------------------------------------------
        // Hold, release, overflow, discard
        // ------------------------------------------------------------------

        [Test]
        public void Hold_WithTwoChanges_SendsNothingUntilReleaseThenTwoUpdatesInOrder()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);

            PickupSword(CharacterA, BronzeSwordId);
            PickupSword(CharacterA, IronSwordId);
            Assert.AreEqual(0, _outbox.Messages.Count, "Nothing is sent while the hold is open.");

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(2, _outbox.Messages.Count);
            Assert.AreEqual(BronzeSwordId, DecodeUpdate(_outbox.Messages[0])[0].ItemId);
            Assert.AreEqual(IronSwordId, DecodeUpdate(_outbox.Messages[1])[0].ItemId);
            Assert.AreEqual(0, CountOfType(_outbox.Messages, InventoryFullSync.MessageTypeId));
        }

        [Test]
        public void Hold_OnOneCharacter_DoesNotAffectAnother()
        {
            Connect(CLIENT_A, CharacterA);
            Connect(CLIENT_B, CharacterB);
            _outbox.Messages.Clear();
            _sender.OpenHold(CharacterA);

            PickupSword(CharacterA, BronzeSwordId);
            PickupSword(CharacterB, BronzeSwordId);

            Assert.AreEqual(0, _outbox.ForClient(CLIENT_A).Count);
            Assert.AreEqual(1, _outbox.ForClient(CLIENT_B).Count);
        }

        [Test]
        public void Hold_ChangeThenZoneEntryThenRelease_SendsOneFullSyncAndNoUpdate()
        {
            _sender.OpenHold(CharacterA);
            Connect(CLIENT_A, CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            EnhanceSword(CharacterA, SWORD_SLOT, ENHANCEMENT_LEVEL);
            Assert.AreEqual(0, _outbox.Messages.Count, "Nothing is sent while the hold is open.");

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
            Assert.AreEqual(ENHANCEMENT_LEVEL, DecodeFullSync(_outbox.Messages[0])[SWORD_SLOT].EnhancementLevel);
        }

        [Test]
        public void Hold_ZoneEntryWithNoBagChangeThenRelease_SendsOneFullSyncAndNoUpdate()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            _sender.OnSessionReadySent(CLIENT_A, CharacterA);
            Assert.AreEqual(0, _outbox.Messages.Count, "Nothing is sent while the hold is open.");

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
            Assert.AreEqual(0, CountOfType(_outbox.Messages, InventorySlotUpdate.MessageTypeId));
        }

        [Test]
        public void Hold_ChangeBeforeZoneEntry_ZoneEntryDropsHeldUpdatesAndDefersFullSync()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);

            _sender.OnSessionReadySent(CLIENT_A, CharacterA);
            Assert.AreEqual(0, _outbox.Messages.Count);

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
        }

        [Test]
        public void Hold_ExceedingMaxHeldUpdates_ReleaseSendsOneFullSyncAndLogsOneWarning()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            LogAssert.Expect(LogType.Warning, new Regex(OVERFLOW_WARNING));
            using var overflowWarnings = new WarningCounter(OVERFLOW_WARNING);

            PickupSwords(OwnerInventorySyncSender.MAX_HELD_UPDATES_PER_CHARACTER + OVERFLOW_EXTRA_EVENTS);
            Assert.AreEqual(0, _outbox.Messages.Count);

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, overflowWarnings.Count, "The overflow is logged once per hold.");
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
            Assert.AreEqual(0, CountOfType(_outbox.Messages, InventorySlotUpdate.MessageTypeId));
            InventorySlotEntry[] entries = DecodeFullSync(_outbox.Messages[0]);
            Assert.AreEqual(BronzeSwordId, entries[SWORD_SLOT].ItemId);
            Assert.AreEqual(BronzeSwordId, entries[OVERFLOW_LAST_SWORD_SLOT].ItemId);
            Assert.AreEqual(1, entries[OVERFLOW_LAST_SWORD_SLOT].Quantity);
            Assert.AreEqual(ItemID.Invalid, entries[OVERFLOW_FIRST_EMPTY_SLOT].ItemId);
        }

        [Test]
        public void Hold_OverflowingAgainAfterARelease_LogsTheWarningOncePerHold()
        {
            ConnectAAndClear();
            LogAssert.Expect(LogType.Warning, new Regex(OVERFLOW_WARNING));
            LogAssert.Expect(LogType.Warning, new Regex(OVERFLOW_WARNING));
            using var overflowWarnings = new WarningCounter(OVERFLOW_WARNING);

            _sender.OpenHold(CharacterA);
            PickupSwords(SECOND_HOLD_EVENT_COUNT);
            _sender.ReleaseHold(CharacterA);
            Assert.AreEqual(1, overflowWarnings.Count, "One warning for the first hold.");

            _sender.OpenHold(CharacterA);
            PickupSwords(SECOND_HOLD_EVENT_COUNT);
            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(2, overflowWarnings.Count, "One more warning for the second hold.");
            Assert.AreEqual(2, _outbox.Messages.Count);
            Assert.AreEqual(2, CountOfType(_outbox.Messages, InventoryFullSync.MessageTypeId));
            AssertFullSyncEqualsBag(_outbox.Messages[1], CharacterA);
        }

        [Test]
        public void Hold_OverflowThenDisconnectThenRelease_SendsNothing()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            LogAssert.Expect(LogType.Warning, new Regex(OVERFLOW_WARNING));
            using var overflowWarnings = new WarningCounter(OVERFLOW_WARNING);
            PickupSwords(SECOND_HOLD_EVENT_COUNT);

            _sender.OnClientDisconnected(CLIENT_A);
            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, overflowWarnings.Count);
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void Hold_WithExactlyMaxHeldUpdates_ReleaseSendsThatManyUpdatesAndNoFullSync()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);

            PickupSwords(OwnerInventorySyncSender.MAX_HELD_UPDATES_PER_CHARACTER);
            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(OwnerInventorySyncSender.MAX_HELD_UPDATES_PER_CHARACTER, _outbox.Messages.Count);
            Assert.AreEqual(OwnerInventorySyncSender.MAX_HELD_UPDATES_PER_CHARACTER,
                CountOfType(_outbox.Messages, InventorySlotUpdate.MessageTypeId));
            for (int i = 0; i < _outbox.Messages.Count; i++)
                Assert.AreEqual(i, DecodeUpdate(_outbox.Messages[i])[0].SlotIndex, "Held updates keep the order raised.");
        }

        [Test]
        public void Hold_ChangeThenDiscard_SendsNothing()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);

            _sender.DiscardHold(CharacterA);

            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void Discard_ThenBagChange_IsSentNormally()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            _sender.DiscardHold(CharacterA);

            PickupSword(CharacterA, IronSwordId);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(IronSwordId, DecodeUpdate(_outbox.Messages[0])[0].ItemId);
        }

        [Test]
        public void Discard_AfterZoneEntryDuringHold_DropsTheDeferredFullSync()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            _sender.OnSessionReadySent(CLIENT_A, CharacterA);

            _sender.DiscardHold(CharacterA);

            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void OpenHold_WhenAlreadyOpen_LogsOneWarningAndKeepsTheHeldUpdates()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            LogAssert.Expect(LogType.Warning, new Regex(OPEN_HOLD_WARNING));
            using var warnings = new WarningCounter(OPEN_HOLD_WARNING);

            _sender.OpenHold(CharacterA);
            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, _outbox.Messages[0].MessageTypeId);
        }

        [Test]
        public void ReleaseHold_WithNoOpenHold_LogsOneWarningAndSendsNothing()
        {
            ConnectAAndClear();
            LogAssert.Expect(LogType.Warning, new Regex(RELEASE_WARNING));
            using var warnings = new WarningCounter(RELEASE_WARNING);

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void DiscardHold_WithNoOpenHold_LogsOneWarningAndSendsNothing()
        {
            ConnectAAndClear();
            LogAssert.Expect(LogType.Warning, new Regex(DISCARD_WARNING));
            using var warnings = new WarningCounter(DISCARD_WARNING);

            _sender.DiscardHold(CharacterA);

            Assert.AreEqual(1, warnings.Count);
            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        // ------------------------------------------------------------------
        // No client and disconnect
        // ------------------------------------------------------------------

        [Test]
        public void BagChange_WithNoSessionReadyClient_SendsNothingAndThrowsNothing()
        {
            Assert.DoesNotThrow(() => PickupSword(CharacterA, BronzeSwordId));

            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void BagChange_AfterDisconnect_SendsNothing()
        {
            ConnectAAndClear();
            _sender.OnClientDisconnected(CLIENT_A);

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void Hold_DisconnectThenRelease_SendsNothing()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            _sender.OnClientDisconnected(CLIENT_A);

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(0, _outbox.Messages.Count);
        }

        [Test]
        public void Hold_DisconnectReconnectThenRelease_SendsOneFullSync()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            _sender.OnClientDisconnected(CLIENT_A);
            PickupSword(CharacterA, IronSwordId);
            _sender.OnSessionReadySent(CLIENT_B, CharacterA);

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(CLIENT_B, _outbox.Messages[0].ClientId);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
        }

        [Test]
        public void Hold_StaysOpenAcrossDisconnectAndReconnectOnNewClient_ChangeSendsNothingUntilRelease()
        {
            ConnectAAndClear();
            _sender.OpenHold(CharacterA);
            _sender.OnClientDisconnected(CLIENT_A);
            _sender.OnSessionReadySent(CLIENT_B, CharacterA);

            PickupSword(CharacterA, BronzeSwordId);
            Assert.AreEqual(0, _outbox.Messages.Count, "The hold is still open, so nothing is sent.");

            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(CLIENT_B, _outbox.Messages[0].ClientId);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
        }

        [Test]
        public void SessionReady_OnNewClientForSameCharacter_StopsSendingToTheOldClient()
        {
            ConnectAAndClear();
            Connect(CLIENT_B, CharacterA);
            _outbox.Messages.Clear();

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(0, _outbox.ForClient(CLIENT_A).Count);
            Assert.AreEqual(1, _outbox.ForClient(CLIENT_B).Count);
        }

        [Test]
        public void SessionReady_ClientIdReusedForSecondCharacter_FirstCharacterSendsNothingSecondGoesToTheClient()
        {
            ConnectAAndClear();
            Connect(CLIENT_A, CharacterB);
            _outbox.Messages.Clear();

            PickupSword(CharacterA, BronzeSwordId);
            Assert.AreEqual(0, _outbox.Messages.Count, "Character A no longer has a client.");

            PickupSword(CharacterB, IronSwordId);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(CLIENT_A, _outbox.Messages[0].ClientId);
            Assert.AreEqual(IronSwordId, DecodeUpdate(_outbox.Messages[0])[0].ItemId);
        }

        // State cleanup is internal; these tests prove it changes nothing the outbox sees.
        [Test]
        public void Connect_DisconnectConnectAgainOnSameClient_SendsOneFullSyncAtEachConnectAndOneUpdatePerChange()
        {
            Connect(CLIENT_A, CharacterA);
            Assert.AreEqual(1, CountOfType(_outbox.Messages, InventoryFullSync.MessageTypeId));
            _outbox.Messages.Clear();

            _sender.OnClientDisconnected(CLIENT_A);
            Connect(CLIENT_A, CharacterA);
            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            _outbox.Messages.Clear();

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventorySlotUpdate.MessageTypeId, _outbox.Messages[0].MessageTypeId);
        }

        [Test]
        public void Hold_OpenedWithNoClientThenReleased_SendsNothingUntilConnectThenOneFullSync()
        {
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, BronzeSwordId);

            _sender.ReleaseHold(CharacterA);
            Assert.AreEqual(0, _outbox.Messages.Count);

            Connect(CLIENT_A, CharacterA);

            Assert.AreEqual(1, _outbox.Messages.Count);
            Assert.AreEqual(InventoryFullSync.MessageTypeId, _outbox.Messages[0].MessageTypeId);
            AssertFullSyncEqualsBag(_outbox.Messages[0], CharacterA);
        }

        // ------------------------------------------------------------------
        // Cap exemption, construction, dispose
        // ------------------------------------------------------------------

        [Test]
        public void EveryMessage_IsNotCapExempt()
        {
            // Full sync at connect, one live update.
            Connect(CLIENT_A, CharacterA);
            PickupSword(CharacterA, BronzeSwordId);
            // Hold with a zone entry: released as one full sync.
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, IronSwordId);
            _sender.OnSessionReadySent(CLIENT_A, CharacterA);
            _sender.ReleaseHold(CharacterA);
            // One live update, then a hold without zone entry: released as two held updates.
            PickupSword(CharacterA, BronzeSwordId);
            _sender.OpenHold(CharacterA);
            PickupSword(CharacterA, IronSwordId);
            PickupSword(CharacterA, BronzeSwordId);
            _sender.ReleaseHold(CharacterA);

            Assert.AreEqual(CAP_EXEMPT_TEST_MESSAGE_COUNT, _outbox.Messages.Count);
            Assert.AreEqual(2, CountOfType(_outbox.Messages, InventoryFullSync.MessageTypeId));
            Assert.AreEqual(4, CountOfType(_outbox.Messages, InventorySlotUpdate.MessageTypeId));
            foreach (RecordedClientMessage message in _outbox.Messages)
                Assert.IsFalse(message.IsCapExempt);
        }

        [Test]
        public void Constructor_WithNullInventory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OwnerInventorySyncSender(null, _outbox));
        }

        [Test]
        public void Constructor_WithNullOutbox_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new OwnerInventorySyncSender(_inventory, null));
        }

        [Test]
        public void BagChange_AfterDispose_SendsNothing()
        {
            ConnectAAndClear();
            _sender.Dispose();

            PickupSword(CharacterA, BronzeSwordId);

            Assert.AreEqual(0, _outbox.Messages.Count);
        }
    }
}

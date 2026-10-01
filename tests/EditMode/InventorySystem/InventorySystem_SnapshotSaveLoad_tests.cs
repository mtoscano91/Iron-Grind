using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.InventorySystem
{
    /// <summary>
    /// EditMode unit tests for Inventory System Story 009 — InventorySnapshot Save/Load &amp;
    /// Load-Time Validation (GDD Rule 1.2; Interactions table, Character Persistence row;
    /// Persistence and Load Edge Cases; Lock State Edge Case; AC-INV-3; TD-042).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_SnapshotSaveLoad_Tests
    {
        private static readonly CharacterID Player = new CharacterID(2001u);
        private static readonly CharacterID SecondPlayer = new CharacterID(2002u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(2999u);

        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u); // Equipment, StackLimit 1
        private static readonly ItemID HPPotionItemId = new ItemID(3001u);    // Consumable, StackLimit 99
        private static readonly ItemID MPPotionItemId = new ItemID(3003u);    // Consumable, StackLimit 99
        private const uint UnknownItemIdRaw = 999999u;

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private List<ItemDefinition> _definitions;
        private List<SlotChange[]> _events;
        private List<CharacterID> _eventCharacterIds;
        private int _fullEvents;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
                ItemDefinitionBuilder.Build(MPPotionItemId.RawValue, "MP Potion", ItemCategory.Consumable, stackLimit: 99),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u);
            _inventory.RegisterCharacter(Player);

            _events = new List<SlotChange[]>();
            _eventCharacterIds = new List<CharacterID>();
            _inventory.OnInventoryChanged += RecordEvent;

            _fullEvents = 0;
            _inventory.OnInventoryFull += _ => _fullEvents++;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        // Copies the entries out during dispatch — the args' buffer is service-owned and reused.
        private void RecordEvent(InventoryChangedEventArgs args)
        {
            var entries = new SlotChange[args.Count];
            for (int i = 0; i < args.Count; i++)
                entries[i] = args[i];
            _events.Add(entries);
            _eventCharacterIds.Add(args.CharacterID);
        }

        private static InventorySnapshot Snapshot(params InventorySnapshotEntry[] entries)
        {
            return new InventorySnapshot(entries);
        }

        private static void AssertSnapshotEntry(InventorySnapshotEntry entry, byte slotIndex, uint itemIdRaw, int quantity)
        {
            Assert.AreEqual(slotIndex, entry.SlotIndex, "Snapshot entry slot index.");
            Assert.AreEqual(itemIdRaw, entry.ItemId, $"Snapshot entry for slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, entry.Quantity, $"Snapshot entry for slot {slotIndex} Quantity.");
        }

        private static void AssertSlot(InventoryService service, CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            var slot = service.GetSlot(charId, slotIndex);
            Assert.AreEqual(itemId, slot.ItemId, $"Slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"Slot {slotIndex} Quantity.");
        }

        private void AssertSlot(CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            AssertSlot(_inventory, charId, slotIndex, itemId, quantity);
        }

        private void AssertSlot(int slotIndex, ItemID itemId, int quantity)
        {
            AssertSlot(_inventory, Player, slotIndex, itemId, quantity);
        }

        // =====================================================================
        // ExportSnapshot
        // =====================================================================

        // -----------------------------------------------------------------------
        // AC-INV-3 — export then import into a brand-new InventoryService.
        // -----------------------------------------------------------------------

        [Test]
        public void ExportImport_RoundTrip_IntoNewInventoryService_RestoresSlotsExactly_AC_INV_3()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            _inventory.SeedSlotForTesting(Player, 19, HPPotionItemId, 99);

            var importingService = new InventoryService(_itemDatabase, () => 0u);
            var importEvents = new List<SlotChange[]>();
            int importFullEvents = 0;
            importingService.OnInventoryChanged += args =>
            {
                var entries = new SlotChange[args.Count];
                for (int i = 0; i < args.Count; i++)
                    entries[i] = args[i];
                importEvents.Add(entries);
            };
            importingService.OnInventoryFull += _ => importFullEvents++;

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);
            bool result = importingService.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(importingService, Player, 0, BronzeSwordItemId, 1);
            AssertSlot(importingService, Player, 7, HPPotionItemId, 42);
            AssertSlot(importingService, Player, 19, HPPotionItemId, 99);
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i == 0 || i == 7 || i == 19)
                    continue;
                AssertSlot(importingService, Player, i, ItemID.Invalid, 0);
            }
            Assert.AreEqual(0, importEvents.Count);
            Assert.AreEqual(0, importFullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Non-empty slots only, in ascending order, carrying raw values.
        // -----------------------------------------------------------------------

        [Test]
        public void ExportSnapshot_NonEmptySlotsOnly_AscendingOrder_RawValuesAndQuantities()
        {
            // Arrange — seeded out of order; the export must still come back ascending.
            _inventory.SeedSlotForTesting(Player, 19, HPPotionItemId, 99);
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);

            // Assert
            Assert.AreEqual(3, snapshot.Slots.Count);
            AssertSnapshotEntry(snapshot.Slots[0], 0, BronzeSwordItemId.RawValue, 1);
            AssertSnapshotEntry(snapshot.Slots[1], 7, HPPotionItemId.RawValue, 42);
            AssertSnapshotEntry(snapshot.Slots[2], 19, HPPotionItemId.RawValue, 99);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ExportSnapshot_EmptyInventory_ReturnsZeroEntries()
        {
            // Act
            var snapshot = _inventory.ExportSnapshot(Player);

            // Assert
            Assert.AreEqual(0, snapshot.Slots.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Unregistered character — server error, empty snapshot, no event.
        // -----------------------------------------------------------------------

        [Test]
        public void ExportSnapshot_UnregisteredCharacter_LogsError_ReturnsEmptySnapshot_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ExportSnapshot: .*is not a registered character"));

            // Act
            var snapshot = _inventory.ExportSnapshot(UnregisteredPlayer);

            // Assert
            Assert.AreEqual(0, snapshot.Slots.Count);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
        }

        // -----------------------------------------------------------------------
        // Export is a read — no mutation, no event.
        // -----------------------------------------------------------------------

        [Test]
        public void ExportSnapshot_DoesNotMutateSlotsOrLocks_FiresNoEvents()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            _inventory.LockSlot(Player, 7);

            // Act
            _inventory.ExportSnapshot(Player);

            // Assert
            AssertSlot(7, HPPotionItemId, 42);
            Assert.IsTrue(_inventory.IsSlotLocked(Player, 7));
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Over-limit quantity exported as-is; an exported snapshot is not a live view.
        // -----------------------------------------------------------------------

        [Test]
        public void ExportSnapshot_OverLimitQuantity_ExportedAsIs()
        {
            // Arrange — the test seam bypasses StackLimit (99 for HP Potion).
            _inventory.SeedSlotForTesting(Player, 5, HPPotionItemId, 150);

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);

            // Assert
            Assert.AreEqual(1, snapshot.Slots.Count);
            AssertSnapshotEntry(snapshot.Slots[0], 5, HPPotionItemId.RawValue, 150);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ExportSnapshot_LaterInventoryMutations_DoNotChangeTheExportedSnapshot()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            var snapshot = _inventory.ExportSnapshot(Player);

            // Act — reduce the exported stack and add a new slot after the export.
            _inventory.Discard(Player, 7, 10);
            _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.AreEqual(1, snapshot.Slots.Count);
            AssertSnapshotEntry(snapshot.Slots[0], 7, HPPotionItemId.RawValue, 42);
            AssertSlot(7, HPPotionItemId, 32);
        }

        // =====================================================================
        // ImportSnapshot
        // =====================================================================

        // -----------------------------------------------------------------------
        // Locks not persisted.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_LocksNotPersisted_SlotUnlockedAfterImport_ItemIntact()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            _inventory.LockSlot(Player, 7);
            var snapshot = _inventory.ExportSnapshot(Player);

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 7));
            AssertSlot(7, HPPotionItemId, 42);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Import replaces — same reset as RegisterCharacter, then applies entries.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ReplacesExistingContents_ClearsSlotsNotInSnapshot_UnlocksPreviouslyLockedSlot()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, MPPotionItemId, 5);
            _inventory.LockSlot(Player, 3);
            var snapshot = Snapshot(new InventorySnapshotEntry(7, HPPotionItemId.RawValue, 42));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(3, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 3));
            AssertSlot(7, HPPotionItemId, 42);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Import registers a never-registered character.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_NeverRegisteredCharacter_ReturnsTrue_RegistersCharacter_SubsequentGetSlotLogsNoError()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(0, HPPotionItemId.RawValue, 1));

            // Act — no LogAssert.Expect: GetSlot must not log once the character is registered.
            bool result = _inventory.ImportSnapshot(UnregisteredPlayer, snapshot);
            var slot = _inventory.GetSlot(UnregisteredPlayer, 0);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(HPPotionItemId, slot.ItemId);
            Assert.AreEqual(1, slot.Quantity);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Import resets the bag-full dedup window.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ResetsBagFullDedupWindow_SecondBlockedPickupNotifiesAgain()
        {
            // Arrange — fill the bag and trigger one blocked-pickup notification.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);
            _inventory.Pickup(Player, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _fullEvents, "Precondition: the first blocked pickup notifies.");

            var fullEntries = new InventorySnapshotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < fullEntries.Length; i++)
                fullEntries[i] = new InventorySnapshotEntry((byte)i, HPPotionItemId.RawValue, 99);

            // Act — import the same full snapshot, then trigger another blocked pickup.
            _inventory.ImportSnapshot(Player, new InventorySnapshot(fullEntries));
            _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.AreEqual(2, _fullEvents, "ImportSnapshot must reset the bag-full dedup window.");
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Unknown ItemId.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_UnknownItemId_SlotLeftEmpty_WarningNamesCharacterAndItemId_OtherEntriesLoad()
        {
            // Arrange
            var snapshot = Snapshot(
                new InventorySnapshotEntry(3, UnknownItemIdRaw, 5),
                new InventorySnapshotEntry(4, HPPotionItemId.RawValue, 2));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 3 for .* references unknown item ItemID\(999999\); slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(3, ItemID.Invalid, 0);
            AssertSlot(4, HPPotionItemId, 2);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Duplicate SlotIndex.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_DuplicateSlotIndex_FirstEntryWins_SecondRejectedWithWarning()
        {
            // Arrange
            var snapshot = Snapshot(
                new InventorySnapshotEntry(5, HPPotionItemId.RawValue, 10),
                new InventorySnapshotEntry(5, BronzeSwordItemId.RawValue, 1));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: duplicate entry for slot 5 for .*; entry rejected\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, HPPotionItemId, 10);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_DuplicateSlotIndex_FirstEntryClearedBySubsequentRule_SlotStaysEmpty_TwoWarnings()
        {
            // Arrange — the first entry claims slot 5, but is itself cleared by the Quantity <= 0 rule.
            var snapshot = Snapshot(
                new InventorySnapshotEntry(5, HPPotionItemId.RawValue, 0),
                new InventorySnapshotEntry(5, BronzeSwordItemId.RawValue, 1));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 5 for .* has non-positive Quantity 0; slot left empty\.$"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: duplicate entry for slot 5 for .*; entry rejected\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Quantity 0 / negative.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ZeroQuantity_SlotLeftEmpty_Warning()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(5, HPPotionItemId.RawValue, 0));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 5 for .* has non-positive Quantity 0; slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_NegativeQuantity_SlotLeftEmpty_Warning()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(5, HPPotionItemId.RawValue, -3));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 5 for .* has non-positive Quantity -3; slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Out-of-range SlotIndex.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_OutOfRangeSlotIndexes_RejectedWithWarning_NoException_AllSlotsEmpty_ReturnsTrue()
        {
            // Arrange
            var snapshot = Snapshot(
                new InventorySnapshotEntry(20, HPPotionItemId.RawValue, 1),
                new InventorySnapshotEntry(255, HPPotionItemId.RawValue, 1));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry slotIndex 20 for .* is out of range \[0, 20\); entry rejected\.$"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry slotIndex 255 for .* is out of range \[0, 20\); entry rejected\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                AssertSlot(i, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // ItemId 0.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ItemIdZero_SlotLeftEmpty_Warning()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(5, 0u, 3));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 5 for .* has ItemId 0; slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Quantity > StackLimit — loads as-is, never clamped or cleared.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_OverLimitQuantity_ConsumableLoadsAsIs_Warning()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(5, HPPotionItemId.RawValue, 150));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 5 for .* has Quantity 150 above StackLimit 99 for ItemID\(3001\); loading as-is\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(5, HPPotionItemId, 150);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_OverLimitQuantity_EquipmentLoadsAsIs_Warning()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(0, BronzeSwordItemId.RawValue, 2));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 0 for .* has Quantity 2 above StackLimit 1 for ItemID\(3002\); loading as-is\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(0, BronzeSwordItemId, 2);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Per-entry validation order — ItemId 0 before Quantity <= 0; Quantity <= 0 before the
        // unknown-item lookup. Exactly one warning each.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ItemIdZeroWithZeroQuantity_LogsItemIdZeroWarningOnly()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(6, 0u, 0));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 6 for .* has ItemId 0; slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(6, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_UnknownItemWithZeroQuantity_LogsQuantityWarningOnly()
        {
            // Arrange
            var snapshot = Snapshot(new InventorySnapshotEntry(3, UnknownItemIdRaw, 0));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 3 for .* has non-positive Quantity 0; slot left empty\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(3, ItemID.Invalid, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Empty snapshot — applied (true) and clears an existing bag.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_EmptySnapshot_ReturnsTrue_ClearsExistingContentsAndLocks()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 5);
            _inventory.LockSlot(Player, 2);

            // Act
            bool result = _inventory.ImportSnapshot(Player, InventorySnapshot.Empty);

            // Assert
            Assert.IsTrue(result);
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                AssertSlot(i, ItemID.Invalid, 0);
                Assert.IsFalse(_inventory.IsSlotLocked(Player, i), $"Slot {i} lock.");
            }
            Assert.AreEqual(0, _events.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Import affects only the imported character.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_SecondRegisteredCharacter_SlotsAndLocksUnaffected()
        {
            // Arrange
            _inventory.RegisterCharacter(SecondPlayer);
            _inventory.SeedSlotForTesting(SecondPlayer, 3, MPPotionItemId, 7);
            _inventory.LockSlot(SecondPlayer, 3);
            var snapshot = Snapshot(new InventorySnapshotEntry(3, HPPotionItemId.RawValue, 42));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(Player, 3, HPPotionItemId, 42);
            AssertSlot(SecondPlayer, 3, MPPotionItemId, 7);
            Assert.IsTrue(_inventory.IsSlotLocked(SecondPlayer, 3));
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Item Database not ready.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_ItemDatabaseNotReady_RegisteredCharacter_ReturnsFalse_ContentsIntact_ErrorLogged()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 5);
            _itemDatabase.IsReady = false;
            var snapshot = Snapshot(new InventorySnapshotEntry(7, HPPotionItemId.RawValue, 1));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ImportSnapshot: Item Database is not ready; aborting import for .* with nothing changed\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(2, HPPotionItemId, 5);
            AssertSlot(7, ItemID.Invalid, 0);
        }

        [Test]
        public void ImportSnapshot_ItemDatabaseNotReady_LocksAndBagFullDedupWindowUntouched()
        {
            // Arrange — full bag with a locked slot; a blocked pickup opens the dedup window.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);
            _inventory.LockSlot(Player, 4);
            _inventory.Pickup(Player, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _fullEvents, "Precondition: the first blocked pickup notifies.");

            _itemDatabase.IsReady = false;
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ImportSnapshot: Item Database is not ready; aborting import for .* with nothing changed\.$"));

            // Act — refused import, then (database ready again) a second blocked pickup.
            bool result = _inventory.ImportSnapshot(Player, InventorySnapshot.Empty);
            _itemDatabase.IsReady = true;
            _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.IsFalse(result);
            Assert.IsTrue(_inventory.IsSlotLocked(Player, 4), "A refused import must not clear locks.");
            AssertSlot(4, HPPotionItemId, 99);
            Assert.AreEqual(1, _fullEvents, "A refused import must not reset the bag-full dedup window.");
        }

        [Test]
        public void ImportSnapshot_ItemDatabaseNotReady_NeverRegisteredCharacter_ReturnsFalse_StaysUnregistered()
        {
            // Arrange
            _itemDatabase.IsReady = false;
            var snapshot = Snapshot(new InventorySnapshotEntry(0, HPPotionItemId.RawValue, 1));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ImportSnapshot: Item Database is not ready; aborting import for .* with nothing changed\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(UnregisteredPlayer, snapshot);

            // Assert
            Assert.IsFalse(result);

            // Act 2 — a following GetSlot must still log the unregistered error.
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] GetSlot: .*is not a registered character"));
            var slot = _inventory.GetSlot(UnregisteredPlayer, 0);

            // Assert 2
            Assert.AreEqual(ItemID.Invalid, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
        }

        // -----------------------------------------------------------------------
        // Null snapshot.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_NullSnapshot_ReturnsFalse_ErrorLogged_ExistingContentsUnchanged()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 5);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ImportSnapshot: snapshot is null for .*; nothing changed\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, null);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(2, HPPotionItemId, 5);
        }

        [Test]
        public void ImportSnapshot_NullSnapshotWithItemDatabaseNotReady_LogsNullErrorOnly_NullCheckedFirst()
        {
            // Arrange — only the null error is expected; a not-ready error would be an
            // unexpected log and fail the test.
            _itemDatabase.IsReady = false;
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ImportSnapshot: snapshot is null for .*; nothing changed\.$"));

            // Act
            bool result = _inventory.ImportSnapshot(Player, null);

            // Assert
            Assert.IsFalse(result);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // No events on load.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_FiresNoInventoryChangedOrInventoryFullEvents()
        {
            // Arrange
            var snapshot = Snapshot(
                new InventorySnapshotEntry(0, BronzeSwordItemId.RawValue, 1),
                new InventorySnapshotEntry(7, HPPotionItemId.RawValue, 42));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_FillingAllTwentySlots_FiresNoEvents()
        {
            // Arrange
            var entries = new InventorySnapshotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new InventorySnapshotEntry((byte)i, HPPotionItemId.RawValue, 99);
            var snapshot = new InventorySnapshot(entries);

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                AssertSlot(i, HPPotionItemId, 99);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Import re-entrancy.
        // -----------------------------------------------------------------------

        [Test]
        public void ImportSnapshot_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_StateUnchangedApartFromTriggeringPickup()
        {
            // Arrange — slot 7 holds HP Potions; a pickup of MP Potion into the empty slot 0
            // triggers dispatch; the handler tries to import a snapshot.
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            var snapshot = Snapshot(new InventorySnapshotEntry(3, BronzeSwordItemId.RawValue, 1));
            _inventory.OnInventoryChanged += _ => _inventory.ImportSnapshot(Player, snapshot);

            // Act / Assert — the message pins the re-entrancy guard, not just the exception type.
            var ex = Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, MPPotionItemId, 1));
            StringAssert.Contains("mutated synchronously", ex.Message);
            AssertSlot(0, MPPotionItemId, 1);
            AssertSlot(7, HPPotionItemId, 42);
            AssertSlot(3, ItemID.Invalid, 0);
        }

        // =====================================================================
        // UnregisterCharacter
        // =====================================================================

        [Test]
        public void UnregisterCharacter_RemovesSlotsLocksAndDedupWindow_SubsequentGetSlotLogsUnregisteredError_NoEvents_OtherCharacterUnaffected()
        {
            // Arrange
            _inventory.RegisterCharacter(SecondPlayer);
            _inventory.SeedSlotForTesting(Player, 3, HPPotionItemId, 5);
            _inventory.LockSlot(Player, 3);
            _inventory.SeedSlotForTesting(SecondPlayer, 3, MPPotionItemId, 7);

            // Act
            _inventory.UnregisterCharacter(Player);

            // Assert
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] GetSlot: .*is not a registered character"));
            var slot = _inventory.GetSlot(Player, 3);
            Assert.AreEqual(ItemID.Invalid, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            AssertSlot(SecondPlayer, 3, MPPotionItemId, 7);
        }

        [Test]
        public void UnregisterCharacter_OtherApisTreatCharacterAsUnregistered()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, HPPotionItemId, 5);
            _inventory.LockSlot(Player, 3);
            _inventory.UnregisterCharacter(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] IsSlotLocked: .*is not a registered character"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] HasItem: .*is not a registered character"));
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*is not a registered character"));

            // Act
            bool locked = _inventory.IsSlotLocked(Player, 3);
            bool hasItem = _inventory.HasItem(Player, HPPotionItemId);
            var pickup = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(locked);
            Assert.IsFalse(hasItem);
            Assert.AreEqual(PickupFailReason.CharacterNotRegistered, pickup.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void UnregisterCharacter_ThenRegisterCharacter_GivesCleanEmptyUnlockedBag()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, HPPotionItemId, 5);
            _inventory.LockSlot(Player, 3);

            // Act
            _inventory.UnregisterCharacter(Player);
            _inventory.RegisterCharacter(Player);

            // Assert
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                AssertSlot(i, ItemID.Invalid, 0);
                Assert.IsFalse(_inventory.IsSlotLocked(Player, i), $"Slot {i} lock.");
            }
            Assert.AreEqual(0, _events.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnregisterCharacter_CalledTwice_SecondCallIsSilentNoOp()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 1, HPPotionItemId, 3);
            _inventory.UnregisterCharacter(Player);

            // Act — no LogAssert.Expect: any unexpected log call fails this test.
            _inventory.UnregisterCharacter(Player);

            // Assert
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnregisterCharacter_NeverRegisteredCharacter_IsSilentNoOp()
        {
            // Act — no LogAssert.Expect: any unexpected log call fails this test.
            _inventory.UnregisterCharacter(UnregisteredPlayer);

            // Assert
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnregisterCharacter_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_CharacterStillRegistered()
        {
            // Arrange — slot 7 holds HP Potions; a pickup of Bronze Sword into the empty slot 0
            // triggers dispatch; the handler tries to unregister the character.
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            _inventory.OnInventoryChanged += _ => _inventory.UnregisterCharacter(Player);

            // Act / Assert — the message pins the re-entrancy guard, not just the exception type.
            var ex = Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, BronzeSwordItemId, 1));
            StringAssert.Contains("mutated synchronously", ex.Message);
            AssertSlot(7, HPPotionItemId, 42);
            AssertSlot(0, BronzeSwordItemId, 1);
        }

        // -----------------------------------------------------------------------
        // Round trip — export, unregister, import (the save/logout/login path).
        // -----------------------------------------------------------------------

        [Test]
        public void ExportUnregisterImport_RoundTrip_RestoresSameSlots_NoLocks()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 42);
            _inventory.SeedSlotForTesting(Player, 19, HPPotionItemId, 99);
            _inventory.LockSlot(Player, 7);
            var snapshot = _inventory.ExportSnapshot(Player);

            // Act
            _inventory.UnregisterCharacter(Player);
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertSlot(7, HPPotionItemId, 42);
            AssertSlot(19, HPPotionItemId, 99);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 7));
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i == 0 || i == 7 || i == 19)
                    continue;
                AssertSlot(i, ItemID.Invalid, 0);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}

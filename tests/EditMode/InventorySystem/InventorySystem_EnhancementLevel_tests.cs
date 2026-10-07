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
    /// EditMode unit tests for Inventory System Story 010 — Per-Slot Enhancement Level
    /// (GDD Rule 1.1/1.4, Rule 5.14a <c>SetEnhancementLevel</c>, Rule 7.20, Rule 8.24a,
    /// Persistence and Load Edge Cases; AC-INV-17 – AC-INV-22). The Inventory System stores and
    /// transports the level; it never computes with it.
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_EnhancementLevel_Tests
    {
        private const byte MaxEnhancementLevel = 10;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID HPPotionItemId = new ItemID(3001u);   // Consumable, StackLimit 99
        private static readonly ItemID IronSwordItemId = new ItemID(3002u);  // Equipment, StackLimit 1

        // Slot conventions mirrored from the story's QA Test Cases.
        private const int SwordSlot = 3;
        private const int SlotA = 2;
        private const int SlotB = 8;
        private const int EmptySlot = 10;
        private const int OutOfRangeSlotHigh = 20;   // INVENTORY_SLOT_COUNT is 20, so 20 is one past the end.
        private const int OutOfRangeSlotLow = -1;

        private const byte SwordLevel = 5;
        private const byte AboveMaxLevel = 11;       // MaxEnhancementLevel + 1: the boundary the story names.
        private const byte FarAboveMaxLevel = 200;   // The story's snapshot "above maximum" value.

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private List<ItemDefinition> _definitions;
        private List<SlotChange[]> _events;
        private int _fullEvents;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
                ItemDefinitionBuilder.Build(IronSwordItemId.RawValue, "Iron Sword", ItemCategory.Equipment, stackLimit: 1),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);

            _events = new List<SlotChange[]>();
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
        }

        private void SeedSword(int slotIndex, byte level)
        {
            _inventory.SeedSlotForTesting(Player, slotIndex, IronSwordItemId, 1, level);
        }

        private static void ExpectError(string method)
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] " + method + @": "));
        }

        private static void ExpectLevelWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"enhancement level"));
        }

        // Pins that the clamp warning names the slot, the character and both levels (story QA case).
        private static void ExpectClampWarning(int slotIndex, byte level)
        {
            LogAssert.Expect(LogType.Warning, new Regex(
                @"^\[InventoryService\] ImportSnapshot: entry for slot " + slotIndex + " for " + Regex.Escape(Player.ToString())
                + " has enhancement level " + level + " above the maximum " + MaxEnhancementLevel + ";"));
        }

        private static void AssertEntry(SlotChange entry, int slotIndex, ItemID itemId, int quantity, byte level)
        {
            Assert.AreEqual(slotIndex, entry.SlotIndex, "Entry slot index.");
            Assert.AreEqual(itemId, entry.ItemId, $"Entry for slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, entry.Quantity, $"Entry for slot {slotIndex} Quantity.");
            Assert.AreEqual(level, entry.EnhancementLevel, $"Entry for slot {slotIndex} EnhancementLevel.");
        }

        private static void AssertSlotValue(InventorySlot slot, ItemID itemId, int quantity, byte level, string label)
        {
            Assert.AreEqual(itemId, slot.ItemId, $"{label} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"{label} Quantity.");
            Assert.AreEqual(level, slot.EnhancementLevel, $"{label} EnhancementLevel.");
        }

        private void AssertSlot(int slotIndex, ItemID itemId, int quantity, byte level)
        {
            AssertSlotValue(_inventory.GetSlot(Player, slotIndex), itemId, quantity, level, $"Slot {slotIndex}");
        }

        private void AssertAllSlotsEmpty()
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                AssertSlot(i, ItemID.Invalid, 0, 0);
        }

        private void AssertNoEvents()
        {
            Assert.AreEqual(0, _events.Count, "OnInventoryChanged count.");
            Assert.AreEqual(0, _fullEvents, "OnInventoryFull count.");
        }

        private static InventorySnapshot Snapshot(params InventorySnapshotEntry[] entries)
        {
            return new InventorySnapshot(entries);
        }

        // =====================================================================
        // Slot, event and read surface
        // =====================================================================

        [Test]
        public void InventorySlot_Empty_HasLevelZero()
        {
            // Act / Assert
            Assert.AreEqual(0, InventorySlot.Empty.EnhancementLevel);
        }

        [Test]
        public void InventorySlot_TwoArgumentConstructor_DefaultsLevelToZero_ThreeArgumentCarriesLevel()
        {
            // Act
            var plain = new InventorySlot(IronSwordItemId, 1);
            var levelled = new InventorySlot(IronSwordItemId, 1, SwordLevel);

            // Assert
            Assert.AreEqual(0, plain.EnhancementLevel);
            Assert.AreEqual(SwordLevel, levelled.EnhancementLevel);
        }

        [Test]
        public void SlotChange_ThreeArgumentConstructor_DefaultsLevelToZero_FourArgumentCarriesLevel()
        {
            // Act
            var plain = new SlotChange(SwordSlot, IronSwordItemId, 1);
            var levelled = new SlotChange(SwordSlot, IronSwordItemId, 1, SwordLevel);

            // Assert
            Assert.AreEqual(0, plain.EnhancementLevel);
            Assert.AreEqual(SwordLevel, levelled.EnhancementLevel);
        }

        [Test]
        public void GetSlot_SeededLevelledSword_ReturnsItsLevel()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act / Assert
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
        }

        // =====================================================================
        // SetEnhancementLevel (AC-INV-17, AC-INV-22)
        // =====================================================================

        [Test]
        public void SetEnhancementLevel_LockedLevelFiveSword_ToSix_ReturnsTrue_OneEvent_StaysLocked_AC_INV_17()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 6);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, 6);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], SwordSlot, IronSwordItemId, 1, 6);
            Assert.IsTrue(_inventory.IsSlotLocked(Player, SwordSlot));
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SetEnhancementLevel_UnlockedSlot_ReturnsFalse_LevelUnchanged_NoEvent_LogsError_AC_INV_17()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 6);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_EmptySlot_ReturnsFalse_NoChange_NoEvent_LogsError_AC_INV_22()
        {
            // Arrange — an empty slot cannot be locked; the empty check comes before the lock check.
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, EmptySlot, 1);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(EmptySlot, ItemID.Invalid, 0, 0);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_StackableItemQuantityFive_ReturnsFalse_NoChange_NoEvent_LogsError_AC_INV_22()
        {
            // Arrange
            const int quantity = 5;
            _inventory.SeedSlotForTesting(Player, SwordSlot, HPPotionItemId, quantity);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 1);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, HPPotionItemId, quantity, 0);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_StackableItemQuantityOne_ReturnsFalse_NoChange_NoEvent_LogsError_AC_INV_22()
        {
            // Arrange — a stackable item is rejected at any quantity, including 1.
            _inventory.SeedSlotForTesting(Player, SwordSlot, HPPotionItemId, 1);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 1);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, HPPotionItemId, 1, 0);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_OverLimitEquipmentQuantityTwo_ReturnsFalse_NoChange_NoEvent_LogsError_AC_INV_22()
        {
            // Arrange — StackLimit 1 item seeded over its limit (the test seam bypasses it).
            const int overLimitQuantity = 2;
            _inventory.SeedSlotForTesting(Player, SwordSlot, IronSwordItemId, overLimitQuantity);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 1);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, overLimitQuantity, 0);
            AssertNoEvents();
        }

        [TestCase(AboveMaxLevel)]
        [TestCase(byte.MaxValue)]
        public void SetEnhancementLevel_LevelAboveMaximum_ReturnsFalse_LevelUnchanged_NoEvent_LogsError_AC_INV_22(byte level)
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, level);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
        }

        [TestCase(OutOfRangeSlotHigh)]
        [TestCase(OutOfRangeSlotLow)]
        public void SetEnhancementLevel_OutOfRangeSlotIndex_ReturnsFalse_NoChange_NoEvent_LogsError_AC_INV_22(int slotIndex)
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, slotIndex, 6);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_ItemNotResolvableInItemDatabase_ReturnsFalse_LevelUnchanged_NoEvent_LogsError()
        {
            // Arrange — the slot is locked while the database still resolves the sword; then the lookup fails.
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);
            _itemDatabase.IsReady = false;
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 6);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_UnregisteredCharacter_ReturnsFalse_NoEvent_LogsError_AC_INV_22()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);
            ExpectError("SetEnhancementLevel");

            // Act
            bool result = _inventory.SetEnhancementLevel(UnregisteredPlayer, SwordSlot, 6);

            // Assert
            Assert.IsFalse(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
        }

        [Test]
        public void SetEnhancementLevel_SameLevel_ReturnsTrue_NoEvent_NoLog()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, SwordLevel);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            AssertNoEvents();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SetEnhancementLevel_LevelZeroOnLevelFiveSword_ReturnsTrue_OneEventWithLevelZero()
        {
            // Arrange — needed for Enhancement rollback.
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, 0);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SwordSlot, IronSwordItemId, 1, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SetEnhancementLevel_ExactlyMaximum_ReturnsTrue_SlotAtMaximum()
        {
            // Arrange
            SeedSword(SwordSlot, 0);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            bool result = _inventory.SetEnhancementLevel(Player, SwordSlot, MaxEnhancementLevel);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(SwordSlot, IronSwordItemId, 1, MaxEnhancementLevel);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SwordSlot, IronSwordItemId, 1, MaxEnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void SetEnhancementLevel_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_LevelUnchanged()
        {
            // Arrange — a pickup into empty slot 0 triggers dispatch; the handler tries to set the level.
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);
            _inventory.OnInventoryChanged += _ => _inventory.SetEnhancementLevel(Player, SwordSlot, 6);

            // Act / Assert — the message pins the re-entrancy guard, not just the exception type.
            var ex = Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            StringAssert.Contains("mutated synchronously", ex.Message);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
        }

        // =====================================================================
        // Equipment interface (AC-INV-18, AC-INV-22)
        // =====================================================================

        [Test]
        public void MoveItemOut_LevelFiveSword_ReturnsSuccessWithLevel_SlotEmptyAtLevelZero_EventEntryEmpty_AC_INV_18()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act
            var result = _inventory.MoveItemOut(Player, SwordSlot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.Success, result.Code);
            Assert.AreEqual(IronSwordItemId, result.ItemId);
            Assert.AreEqual(SwordLevel, result.EnhancementLevel);
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], SwordSlot, ItemID.Invalid, 0, 0);
        }

        [Test]
        public void MoveItemOut_LockedSlot_FailureCarriesLevelZero_SlotKeepsItsLevel()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            var result = _inventory.MoveItemOut(Player, SwordSlot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotLocked, result.Code);
            Assert.AreEqual(0, result.EnhancementLevel);
            AssertSlot(SwordSlot, IronSwordItemId, 1, SwordLevel);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_EmptySlot_FailureCarriesLevelZero()
        {
            // Act
            var result = _inventory.MoveItemOut(Player, EmptySlot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(0, result.EnhancementLevel);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOutResult_FailFactory_CarriesLevelZero_SucceededFactoryCarriesLevel()
        {
            // Act
            var failed = MoveItemOutResult.Fail(MoveItemOutCode.SlotLocked);
            var succeeded = MoveItemOutResult.Succeeded(IronSwordItemId, SwordLevel);

            // Assert
            Assert.AreEqual(0, failed.EnhancementLevel);
            Assert.AreEqual(MoveItemOutCode.Success, succeeded.Code);
            Assert.AreEqual(IronSwordItemId, succeeded.ItemId);
            Assert.AreEqual(SwordLevel, succeeded.EnhancementLevel);
        }

        [Test]
        public void MoveItemIn_LevelFiveSword_PlacesInLowestEmptySlotAtLevelFive_EventCarriesLevel_AC_INV_18()
        {
            // Act
            var result = _inventory.MoveItemIn(Player, IronSwordItemId, SwordLevel);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.SlotIndex);
            AssertSlot(0, IronSwordItemId, 1, SwordLevel);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 0, IronSwordItemId, 1, SwordLevel);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ForceInsert_LevelFiveSword_PlacesInLowestEmptySlotAtLevelFive_EventCarriesLevel_AC_INV_18()
        {
            // Act
            bool result = _inventory.ForceInsert(Player, IronSwordItemId, SwordLevel);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(0, IronSwordItemId, 1, SwordLevel);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], 0, IronSwordItemId, 1, SwordLevel);
            Assert.AreEqual(0, _fullEvents);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MoveItemIn_ExactlyMaximumLevel_Succeeds()
        {
            // Act
            var result = _inventory.MoveItemIn(Player, IronSwordItemId, MaxEnhancementLevel);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(0, IronSwordItemId, 1, MaxEnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MoveItemIn_StackableItemAtLevelZero_StillSucceeds()
        {
            // Act
            var result = _inventory.MoveItemIn(Player, HPPotionItemId, 0);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(0, HPPotionItemId, 1, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MoveItemIn_LevelAboveMaximum_Fails_NoMutation_NoEvents_NoOnInventoryFull_LogsError_AC_INV_22()
        {
            // Arrange
            ExpectError("MoveItemIn");

            // Act
            var result = _inventory.MoveItemIn(Player, IronSwordItemId, AboveMaxLevel);

            // Assert
            Assert.IsFalse(result.Success);
            AssertAllSlotsEmpty();
            AssertNoEvents();
        }

        [Test]
        public void ForceInsert_LevelAboveMaximum_Fails_NoMutation_NoEvents_NoOnInventoryFull_LogsError_AC_INV_22()
        {
            // Arrange
            ExpectError("ForceInsert");

            // Act
            bool result = _inventory.ForceInsert(Player, IronSwordItemId, AboveMaxLevel);

            // Assert
            Assert.IsFalse(result);
            AssertAllSlotsEmpty();
            AssertNoEvents();
        }

        [Test]
        public void MoveItemIn_NonZeroLevelOnStackableItem_Fails_NoMutation_NoEvents_NoOnInventoryFull_LogsError_AC_INV_22()
        {
            // Arrange
            ExpectError("MoveItemIn");

            // Act
            var result = _inventory.MoveItemIn(Player, HPPotionItemId, 3);

            // Assert
            Assert.IsFalse(result.Success);
            AssertAllSlotsEmpty();
            AssertNoEvents();
        }

        [Test]
        public void ForceInsert_NonZeroLevelOnStackableItem_Fails_NoMutation_NoEvents_NoOnInventoryFull_LogsError_AC_INV_22()
        {
            // Arrange
            ExpectError("ForceInsert");

            // Act
            bool result = _inventory.ForceInsert(Player, HPPotionItemId, 3);

            // Assert
            Assert.IsFalse(result);
            AssertAllSlotsEmpty();
            AssertNoEvents();
        }

        [Test]
        public void EquipRoundTrip_MoveItemOutThenMoveItemIn_SwordBackInBagAtLevelFive()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act
            var outResult = _inventory.MoveItemOut(Player, SwordSlot);
            var inResult = _inventory.MoveItemIn(Player, outResult.ItemId, outResult.EnhancementLevel);

            // Assert
            Assert.AreEqual(MoveItemOutCode.Success, outResult.Code);
            Assert.IsTrue(inResult.Success);
            AssertSlot(inResult.SlotIndex, IronSwordItemId, 1, SwordLevel);
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Move and swap (AC-INV-19, Rule 7.20)
        // =====================================================================

        [Test]
        public void Move_LevelFiveSwordOntoLevelZeroSword_Swaps_LevelsTravel_OneEventBothEntries_AC_INV_19()
        {
            // Arrange
            SeedSword(SlotA, SwordLevel);
            SeedSword(SlotB, 0);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, IronSwordItemId, 1, 0);
            AssertSlot(SlotB, IronSwordItemId, 1, SwordLevel);
            AssertSlotValue(result.FromSlot, IronSwordItemId, 1, 0, "FromSlot");
            AssertSlotValue(result.ToSlot, IronSwordItemId, 1, SwordLevel, "ToSlot");
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], SlotA, IronSwordItemId, 1, 0);
            AssertEntry(_events[0][1], SlotB, IronSwordItemId, 1, SwordLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Move_LevelZeroSwordOntoLevelFiveSword_Swaps_LevelsTravel_OneEvent_AC_INV_19()
        {
            // Arrange — the reverse direction of the AC-INV-19 swap.
            SeedSword(SlotA, 0);
            SeedSword(SlotB, SwordLevel);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, IronSwordItemId, 1, SwordLevel);
            AssertSlot(SlotB, IronSwordItemId, 1, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
        }

        [TestCase((byte)0)]
        [TestCase(SwordLevel)]
        public void Move_IdenticalItemAndLevel_NoOpSuccess_NeitherSlotChanges_NoEvent_EchoesBothSlots_AC_INV_19(byte level)
        {
            // Arrange
            SeedSword(SlotA, level);
            SeedSword(SlotB, level);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, IronSwordItemId, 1, level);
            AssertSlot(SlotB, IronSwordItemId, 1, level);
            AssertSlotValue(result.FromSlot, IronSwordItemId, 1, level, "FromSlot");
            AssertSlotValue(result.ToSlot, IronSwordItemId, 1, level, "ToSlot");
            AssertNoEvents();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Move_LevelFiveSwordOntoEmptySlot_Relocates_SourceEmptyAtLevelZero_DestinationAtLevelFive()
        {
            // Arrange
            SeedSword(SlotA, SwordLevel);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, ItemID.Invalid, 0, 0);
            AssertSlot(SlotB, IronSwordItemId, 1, SwordLevel);
            AssertSlotValue(result.FromSlot, ItemID.Invalid, 0, 0, "FromSlot");
            AssertSlotValue(result.ToSlot, IronSwordItemId, 1, SwordLevel, "ToSlot");
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SlotA, ItemID.Invalid, 0, 0);
            AssertEntry(_events[0][1], SlotB, IronSwordItemId, 1, SwordLevel);
        }

        [Test]
        public void Move_LevelFiveSwordOntoPotionStack_Swaps_SwordKeepsLevel_StackAtLevelZero()
        {
            // Arrange
            const int potionQuantity = 10;
            SeedSword(SlotA, SwordLevel);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, potionQuantity);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, HPPotionItemId, potionQuantity, 0);
            AssertSlot(SlotB, IronSwordItemId, 1, SwordLevel);
            AssertSlotValue(result.FromSlot, HPPotionItemId, potionQuantity, 0, "FromSlot");
            AssertSlotValue(result.ToSlot, IronSwordItemId, 1, SwordLevel, "ToSlot");
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SlotA, HPPotionItemId, potionQuantity, 0);
            AssertEntry(_events[0][1], SlotB, IronSwordItemId, 1, SwordLevel);
        }

        [Test]
        public void Move_PotionStackMerge_BehavesAsBefore_BothSlotsLevelZero()
        {
            // Arrange
            const int sourceQuantity = 50;
            const int destinationQuantity = 30;
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, sourceQuantity);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, destinationQuantity);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SlotA, ItemID.Invalid, 0, 0);
            AssertSlot(SlotB, HPPotionItemId, sourceQuantity + destinationQuantity, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SlotA, ItemID.Invalid, 0, 0);
            AssertEntry(_events[0][1], SlotB, HPPotionItemId, sourceQuantity + destinationQuantity, 0);
        }

        // =====================================================================
        // Level reset when a slot empties / pickup starts at 0 (AC-INV-21)
        // =====================================================================

        [Test]
        public void Discard_FullQuantityOfLevelFiveSword_SlotEmptyAtLevelZero_EventEntryLevelZero_AC_INV_21()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act
            var result = _inventory.Discard(Player, SwordSlot, 1);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SwordSlot, ItemID.Invalid, 0, 0);
        }

        [Test]
        public void SellItem_FullQuantityOfLevelFiveSword_SlotEmptyAtLevelZero_EventEntryLevelZero_AC_INV_21()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act
            var result = _inventory.SellItem(Player, SwordSlot, IronSwordItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SwordSlot, ItemID.Invalid, 0, 0);
        }

        [Test]
        public void RemoveItem_LockedLevelFiveSword_SlotEmptyAtLevelZero_EventEntryLevelZero_AC_INV_21()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.LockSlot(Player, SwordSlot);

            // Act
            _inventory.RemoveItem(Player, SwordSlot);

            // Assert
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], SwordSlot, ItemID.Invalid, 0, 0);
        }

        [Test]
        public void Pickup_SwordAndPotions_EveryTouchedSlotAtLevelZero_AC_INV_21()
        {
            // Arrange
            const int firstPotionQuantity = 5;
            const int topUpQuantity = 3;

            // Act
            var swordPickup = _inventory.Pickup(Player, IronSwordItemId, 1);
            var potionPickup = _inventory.Pickup(Player, HPPotionItemId, firstPotionQuantity);
            var topUpPickup = _inventory.Pickup(Player, HPPotionItemId, topUpQuantity);

            // Assert
            Assert.IsTrue(swordPickup.Success);
            Assert.IsTrue(potionPickup.Success);
            Assert.IsTrue(topUpPickup.Success);
            AssertSlot(0, IronSwordItemId, 1, 0);
            AssertSlot(1, HPPotionItemId, firstPotionQuantity + topUpQuantity, 0);
            Assert.AreEqual(3, _events.Count);
            foreach (var entries in _events)
            {
                foreach (var entry in entries)
                    Assert.AreEqual(0, entry.EnhancementLevel, $"Pickup entry for slot {entry.SlotIndex} must be level 0.");
            }
        }

        [Test]
        public void Discard_LevelFiveSword_ThenPickupSword_NewSlotStartsAtLevelZero_AC_INV_21()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);
            _inventory.Discard(Player, SwordSlot, 1);
            _events.Clear();

            // Act — the lowest empty slot is 0; fill 0..2 so the pickup reuses the discarded slot 3.
            for (int i = 0; i < SwordSlot; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 1);
            var pickup = _inventory.Pickup(Player, IronSwordItemId, 1);

            // Assert
            Assert.IsTrue(pickup.Success);
            AssertSlot(SwordSlot, IronSwordItemId, 1, 0);
            AssertEntry(_events[0][0], SwordSlot, IronSwordItemId, 1, 0);
        }

        // =====================================================================
        // Snapshot (AC-INV-20, AC-INV-22)
        // =====================================================================

        [Test]
        public void ExportSnapshot_LevelFiveSword_EntryCarriesLevel_AC_INV_20()
        {
            // Arrange
            const int slot = 7;
            SeedSword(slot, SwordLevel);

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);

            // Assert
            Assert.AreEqual(1, snapshot.Slots.Count);
            Assert.AreEqual(slot, snapshot.Slots[0].SlotIndex);
            Assert.AreEqual(IronSwordItemId.RawValue, snapshot.Slots[0].ItemId);
            Assert.AreEqual(1, snapshot.Slots[0].Quantity);
            Assert.AreEqual(SwordLevel, snapshot.Slots[0].EnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ExportImport_RoundTrip_IntoNewInventoryService_RestoresLevelFiveSword_NoWarning_AC_INV_20()
        {
            // Arrange
            const int slot = 7;
            SeedSword(slot, SwordLevel);
            var importingService = new InventoryService(_itemDatabase, () => 0u, MaxEnhancementLevel);

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);
            bool result = importingService.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlotValue(importingService.GetSlot(Player, slot), IronSwordItemId, 1, SwordLevel, "Imported slot 7");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ExportSnapshot_StackAndLevelZeroSword_EntriesCarryLevelZero()
        {
            // Arrange
            SeedSword(0, 0);
            _inventory.SeedSlotForTesting(Player, 1, HPPotionItemId, 42);

            // Act
            var snapshot = _inventory.ExportSnapshot(Player);

            // Assert
            Assert.AreEqual(2, snapshot.Slots.Count);
            Assert.AreEqual(0, snapshot.Slots[0].EnhancementLevel);
            Assert.AreEqual(0, snapshot.Slots[1].EnhancementLevel);
        }

        [Test]
        public void InventorySnapshotEntry_ThreeArgumentConstructor_DefaultsLevelToZero_FourArgumentCarriesLevel()
        {
            // Act
            var plain = new InventorySnapshotEntry(0, IronSwordItemId.RawValue, 1);
            var levelled = new InventorySnapshotEntry(0, IronSwordItemId.RawValue, 1, SwordLevel);

            // Assert
            Assert.AreEqual(0, plain.EnhancementLevel);
            Assert.AreEqual(SwordLevel, levelled.EnhancementLevel);
        }

        [Test]
        public void ImportSnapshot_LevelAboveMaximum_ClampsToMaximum_OneWarning_AC_INV_20()
        {
            // Arrange
            const int slot = 2;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, IronSwordItemId.RawValue, 1, FarAboveMaxLevel));
            ExpectClampWarning(slot, FarAboveMaxLevel);

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(slot, IronSwordItemId, 1, MaxEnhancementLevel);
            Assert.AreEqual(0, _events.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_LevelOnStack_LoadsAtLevelZero_OneWarning_AC_INV_20()
        {
            // Arrange
            const int slot = 4;
            const int quantity = 42;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, HPPotionItemId.RawValue, quantity, 3));
            ExpectLevelWarning();

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(slot, HPPotionItemId, quantity, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_LevelOnStackableItemAtQuantityOne_LoadsAtLevelZero_OneWarning_ThenPickupTopsUpAtLevelZero_AC_INV_22()
        {
            // Arrange
            const int slot = 6;
            const int topUpQuantity = 5;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, HPPotionItemId.RawValue, 1, 3));
            ExpectLevelWarning();

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);
            AssertSlot(slot, HPPotionItemId, 1, 0);
            var pickup = _inventory.Pickup(Player, HPPotionItemId, topUpQuantity);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(pickup.Success);
            AssertSlot(slot, HPPotionItemId, 1 + topUpQuantity, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_OverLimitEquipmentWithLevel_LoadsQuantityTwoAtLevelZero_OverLimitWarningAndLevelWarning_AC_INV_22()
        {
            // Arrange — the existing over-limit warning is emitted first, then the level warning.
            const int slot = 0;
            const int overLimitQuantity = 2;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, IronSwordItemId.RawValue, overLimitQuantity, 4));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] ImportSnapshot: entry for slot 0 for .* has Quantity 2 above StackLimit 1 for ItemID\(3002\); loading as-is\.$"));
            ExpectLevelWarning();

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(slot, IronSwordItemId, overLimitQuantity, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_LevelExactlyMaximum_LoadsAtMaximum_NoWarning()
        {
            // Arrange
            const int slot = 2;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, IronSwordItemId.RawValue, 1, MaxEnhancementLevel));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(slot, IronSwordItemId, 1, MaxEnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_LevelOneAboveMaximum_ClampsToMaximum_OneWarning()
        {
            // Arrange
            const int slot = 2;
            var snapshot = Snapshot(new InventorySnapshotEntry(slot, IronSwordItemId.RawValue, 1, AboveMaxLevel));
            ExpectClampWarning(slot, AboveMaxLevel);

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(slot, IronSwordItemId, 1, MaxEnhancementLevel);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_LevelZeroEntries_LoadWithoutExtraWarning()
        {
            // Arrange — a 3-argument sword entry, an explicit level-0 sword entry and a stack.
            var snapshot = Snapshot(
                new InventorySnapshotEntry(0, IronSwordItemId.RawValue, 1),
                new InventorySnapshotEntry(1, IronSwordItemId.RawValue, 1, 0),
                new InventorySnapshotEntry(2, HPPotionItemId.RawValue, 42, 0));

            // Act
            bool result = _inventory.ImportSnapshot(Player, snapshot);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(0, IronSwordItemId, 1, 0);
            AssertSlot(1, IronSwordItemId, 1, 0);
            AssertSlot(2, HPPotionItemId, 42, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ImportSnapshot_ReplacesLevelledContents_ClearedSlotsReturnToLevelZero()
        {
            // Arrange
            SeedSword(SwordSlot, SwordLevel);

            // Act
            bool result = _inventory.ImportSnapshot(Player, InventorySnapshot.Empty);

            // Assert
            Assert.IsTrue(result);
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Seeding seam
        // =====================================================================

        [Test]
        public void SeedSlotForTesting_FourArgumentOverload_SeedsAtLevelZero()
        {
            // Act
            _inventory.SeedSlotForTesting(Player, SwordSlot, IronSwordItemId, 1);

            // Assert
            AssertSlot(SwordSlot, IronSwordItemId, 1, 0);
        }

        [Test]
        public void SeedSlotForTesting_LevelOnEmptySlot_ThrowsArgumentException_SlotUnchanged()
        {
            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.SeedSlotForTesting(Player, EmptySlot, ItemID.Invalid, 0, SwordLevel));
            AssertSlot(EmptySlot, ItemID.Invalid, 0, 0);
        }

        [Test]
        public void SeedSlotForTesting_LevelOnQuantityAboveOne_ThrowsArgumentException_SlotUnchanged()
        {
            // Arrange
            const int quantity = 5;

            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.SeedSlotForTesting(Player, SwordSlot, HPPotionItemId, quantity, SwordLevel));
            AssertSlot(SwordSlot, ItemID.Invalid, 0, 0);
        }
    }
}

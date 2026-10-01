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
    /// EditMode unit tests for Inventory System Story 008 — NPC Shop Sell &amp; Consumable Use
    /// Interfaces (GDD Interactions table — NPC Shop and Consumable Use System rows; Rule 5.12;
    /// Cross-System Interface Edge Cases; AC-INV-16).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_SellAndConsume_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u); // Equipment, StackLimit 1
        private static readonly ItemID HPPotionItemId = new ItemID(3001u);    // Consumable, StackLimit 99
        private static readonly ItemID MPPotionItemId = new ItemID(3003u);    // Consumable, StackLimit 99

        // Slot conventions from the story's QA Test Cases: slot 4 hosts the Sell scenarios; slots
        // 2 and 8 host the Consume lowest-index/across-stacks scenarios; slot 0 hosts the
        // equipment-sell scenario; slot 10 is an always-empty in-range slot; slot 1 hosts the
        // "other items untouched" MP Potion stack, and doubles as the lowest HP Potion stack in
        // the three-stack Consume scenarios.
        private const int SellSlot = 4;
        private const int ConsumeSlotLowest = 1;
        private const int ConsumeSlotLow = 2;
        private const int ConsumeSlotHigh = 8;
        private const int EquipmentSlot = 0;
        private const int EmptySlot = 10;
        private const int MpPotionSlot = 1;

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

        private static void AssertEntry(SlotChange entry, int slotIndex, ItemID itemId, int quantity)
        {
            Assert.AreEqual(slotIndex, entry.SlotIndex, "Entry slot index.");
            Assert.AreEqual(itemId, entry.ItemId, $"Entry for slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, entry.Quantity, $"Entry for slot {slotIndex} Quantity.");
        }

        private void AssertSlot(CharacterID charId, int slotIndex, ItemID itemId, int quantity)
        {
            var slot = _inventory.GetSlot(charId, slotIndex);
            Assert.AreEqual(itemId, slot.ItemId, $"Slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"Slot {slotIndex} Quantity.");
        }

        private void AssertSlot(int slotIndex, ItemID itemId, int quantity)
        {
            AssertSlot(Player, slotIndex, itemId, quantity);
        }

        private void AssertAllSlotsEmptyExceptEquipmentSlot()
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i == EquipmentSlot)
                    continue;
                AssertSlot(i, ItemID.Invalid, 0);
            }
        }

        // =====================================================================
        // SellItem
        // =====================================================================

        // -----------------------------------------------------------------------
        // AC-INV-16 — full-stack sell.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_FullStack_ReturnsSuccess_SlotBecomesEmpty_OneEventEntry_AC_INV_16()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(SellItemFailReason.None, result.Reason);
            Assert.AreEqual(5, result.QuantitySold);
            AssertSlot(SellSlot, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], SellSlot, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId));
        }

        // -----------------------------------------------------------------------
        // AC-INV-16 — partial-stack sell.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_PartialStack_ReturnsSuccess_SlotReducedToRemainder_OneEventEntry_AC_INV_16()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 2);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.QuantitySold);
            AssertSlot(SellSlot, HPPotionItemId, 3);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], SellSlot, HPPotionItemId, 3);
        }

        // -----------------------------------------------------------------------
        // ItemID mismatch — a different item, an empty slot, and an invalid requested itemId.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_DifferentItemInSlot_ReturnsItemMismatch_NoMutation_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, BronzeSwordItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.ItemMismatch, result.Reason);
            Assert.AreEqual(0, result.QuantitySold);
            AssertSlot(SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_EmptySlot_ReturnsItemMismatch_ForRealItemAndForInvalidItemId()
        {
            // Act — a real item requested against an empty slot, and ItemID.Invalid itself.
            var resultRealItem = _inventory.SellItem(Player, EmptySlot, HPPotionItemId, 1);
            var resultInvalidItem = _inventory.SellItem(Player, EmptySlot, ItemID.Invalid, 1);

            // Assert
            Assert.IsFalse(resultRealItem.Success);
            Assert.AreEqual(SellItemFailReason.ItemMismatch, resultRealItem.Reason);
            Assert.IsFalse(resultInvalidItem.Success);
            Assert.AreEqual(SellItemFailReason.ItemMismatch, resultInvalidItem.Reason);
            AssertSlot(EmptySlot, ItemID.Invalid, 0);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Locked slot — rejected even for a matching item and valid quantity.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_LockedSlot_ReturnsSlotLocked_NoMutation_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.LockSlot(Player, SellSlot);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 5);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.SlotLocked, result.Reason);
            AssertSlot(SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_AfterUnlock_SameCallSucceeds()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.LockSlot(Player, SellSlot);
            _inventory.UnlockSlot(Player, SellSlot);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, result.QuantitySold);
            AssertSlot(SellSlot, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Invalid quantity — above the stack, zero, and negative.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_InvalidQuantityEdgeValues_ReturnsInvalidQuantity_NoMutation_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var resultOneOver = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 6);
            var resultZero = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 0);
            var resultNegative = _inventory.SellItem(Player, SellSlot, HPPotionItemId, -1);

            // Assert
            Assert.AreEqual(SellItemFailReason.InvalidQuantity, resultOneOver.Reason);
            Assert.AreEqual(SellItemFailReason.InvalidQuantity, resultZero.Reason);
            Assert.AreEqual(SellItemFailReason.InvalidQuantity, resultNegative.Reason);
            AssertSlot(SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Out-of-range slotIndex / unregistered character — server error, no mutation.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_NegativeSlotIndex_LogsError_AllSlotsUnchanged_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, EquipmentSlot, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] SellItem: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.SellItem(Player, -1, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.InvalidSlot, result.Reason);
            Assert.AreEqual(0, result.QuantitySold);
            AssertSlot(EquipmentSlot, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptEquipmentSlot();
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_SlotIndexTwenty_LogsError_AllSlotsUnchanged_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, EquipmentSlot, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] SellItem: slotIndex 20 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.SellItem(Player, 20, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.InvalidSlot, result.Reason);
            AssertSlot(EquipmentSlot, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptEquipmentSlot();
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_UnregisteredCharacter_LogsError_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] SellItem: .*is not a registered character"));

            // Act
            var result = _inventory.SellItem(UnregisteredPlayer, SellSlot, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.InvalidSlot, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Validation order — range before registration; item match before lock; lock before
        // quantity bounds.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_OutOfRangeSlotWithUnregisteredCharacter_LogsRangeError_RangeCheckedFirst()
        {
            // Arrange — only the range error is expected; a registration error would be an
            // unexpected log and fail the test.
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] SellItem: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.SellItem(UnregisteredPlayer, -1, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SellItemFailReason.InvalidSlot, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_LockedSlotWithWrongItem_ReturnsItemMismatch_NotSlotLocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.LockSlot(Player, SellSlot);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, BronzeSwordItemId, 1);

            // Assert
            Assert.AreEqual(SellItemFailReason.ItemMismatch, result.Reason);
            AssertSlot(SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void SellItem_LockedSlotWithInvalidQuantity_ReturnsSlotLocked_NotInvalidQuantity()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.LockSlot(Player, SellSlot);

            // Act — quantity is out of bounds, but the lock must win.
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 99);

            // Assert
            Assert.AreEqual(SellItemFailReason.SlotLocked, result.Reason);
            AssertSlot(SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Equipment — category-agnostic sell.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_Equipment_QuantityOne_SlotBecomesEmpty_OneEventEntry()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, EquipmentSlot, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.SellItem(Player, EquipmentSlot, BronzeSwordItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.QuantitySold);
            AssertSlot(EquipmentSlot, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            AssertEntry(_events[0][0], EquipmentSlot, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Sequential sells — a partial sell followed by a full sell of the remainder.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_PartialSellThenFullSellOfRemainder_SecondCallEmptiesSlot()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var first = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 2);
            var second = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 3);

            // Assert
            Assert.IsTrue(first.Success);
            Assert.AreEqual(2, first.QuantitySold);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(3, second.QuantitySold);
            AssertSlot(SellSlot, ItemID.Invalid, 0);
            Assert.AreEqual(2, _events.Count);
            AssertEntry(_events[0][0], SellSlot, HPPotionItemId, 3);
            AssertEntry(_events[1][0], SellSlot, ItemID.Invalid, 0);
        }

        // =====================================================================
        // ConsumeItem
        // =====================================================================

        // -----------------------------------------------------------------------
        // Lowest index first.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_LowestIndexFirst_DecrementsLowerSlot_HigherSlotUnchanged()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.None, result.Reason);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 2);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 10);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLow, HPPotionItemId, 2);
        }

        // -----------------------------------------------------------------------
        // Emptying a stack rolls over into the next lowest-index stack on a later call.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_EmptiesLowerStack_NextCallTakesFromNextStack()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 1);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var first = _inventory.ConsumeItem(Player, HPPotionItemId, 1);
            var second = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(first.Success);
            AssertSlot(ConsumeSlotLow, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLow, ItemID.Invalid, 0);

            Assert.IsTrue(second.Success);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 9);
            Assert.AreEqual(2, _events.Count);
            AssertEntry(_events[1][0], ConsumeSlotHigh, HPPotionItemId, 9);
        }

        // -----------------------------------------------------------------------
        // A single call spanning multiple stacks fires one event with ordered entries.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_AcrossStacks_OneEventTwoEntriesInAscendingOrder()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(ConsumeSlotLow, ItemID.Invalid, 0);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 8);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLow, ItemID.Invalid, 0);
            AssertEntry(_events[0][1], ConsumeSlotHigh, HPPotionItemId, 8);
        }

        [Test]
        public void ConsumeItem_ExactTotalAcrossTwoStacks_BothDrainToEmpty_OneEventTwoEntries()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 2);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(ConsumeSlotLow, ItemID.Invalid, 0);
            AssertSlot(ConsumeSlotHigh, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLow, ItemID.Invalid, 0);
            AssertEntry(_events[0][1], ConsumeSlotHigh, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId));
        }

        [Test]
        public void ConsumeItem_AcrossThreeStacks_OneEventThreeEntriesInAscendingOrder()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLowest, HPPotionItemId, 2);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 7);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(ConsumeSlotLowest, ItemID.Invalid, 0);
            AssertSlot(ConsumeSlotLow, ItemID.Invalid, 0);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 8);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(3, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLowest, ItemID.Invalid, 0);
            AssertEntry(_events[0][1], ConsumeSlotLow, ItemID.Invalid, 0);
            AssertEntry(_events[0][2], ConsumeSlotHigh, HPPotionItemId, 8);
        }

        // -----------------------------------------------------------------------
        // Insufficient total — nothing written, no event.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_InsufficientTotal_ReturnsInsufficientQuantity_NoMutation_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 5);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InsufficientQuantity, result.Reason);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 3);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Missing item, and ItemID.Invalid.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_ItemNotInBag_ReturnsInsufficientQuantity_HasItemFalse()
        {
            // Act — no HP Potion was ever seeded.
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InsufficientQuantity, result.Reason);
            Assert.AreEqual(0, _events.Count);
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId));
        }

        [Test]
        public void ConsumeItem_InvalidItemId_ReturnsInsufficientQuantity_NoLog()
        {
            // Act — no LogAssert.Expect: any unexpected log call fails this test.
            var result = _inventory.ConsumeItem(Player, ItemID.Invalid, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InsufficientQuantity, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Locked slots are skipped — neither decremented nor counted toward the total.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_LockedAndUnlockedStacks_TakesOnlyFromUnlocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.LockSlot(Player, ConsumeSlotLow);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 3);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 9);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotHigh, HPPotionItemId, 9);
        }

        [Test]
        public void ConsumeItem_OnlyLockedStackExists_ReturnsInsufficientQuantity_WhileHasItemTrue()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.LockSlot(Player, ConsumeSlotLow);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InsufficientQuantity, result.Reason);
            Assert.IsTrue(_inventory.HasItem(Player, HPPotionItemId), "HasItem must stay true for a locked-only stack (Story 001 contract).");
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 3);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void ConsumeItem_LockedStackPlusInsufficientUnlockedStack_ReturnsInsufficientQuantity_BothUnchanged()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);
            _inventory.LockSlot(Player, ConsumeSlotLow);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 2);

            // Act — only 2 unlocked units are available; 3 is requested.
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 3);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InsufficientQuantity, result.Reason);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 3);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 2);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void ConsumeItem_LockedStackBetweenTwoUnlockedStacks_SkipsOnlyTheLockedOne()
        {
            // Arrange — unlocked (slot 1), locked (slot 2), unlocked (slot 8).
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLowest, HPPotionItemId, 2);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 5);
            _inventory.LockSlot(Player, ConsumeSlotLow);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotHigh, HPPotionItemId, 10);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 3);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(ConsumeSlotLowest, ItemID.Invalid, 0);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 5);
            AssertSlot(ConsumeSlotHigh, HPPotionItemId, 9);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], ConsumeSlotLowest, ItemID.Invalid, 0);
            AssertEntry(_events[0][1], ConsumeSlotHigh, HPPotionItemId, 9);
        }

        // -----------------------------------------------------------------------
        // Other items untouched and absent from the event.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_OtherItemAtLowerIndex_UnchangedAndAbsentFromEvent()
        {
            // Arrange — MP Potion sits at a lower index than the HP Potion stack being consumed.
            _inventory.SeedSlotForTesting(Player, MpPotionSlot, MPPotionItemId, 5);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(MpPotionSlot, MPPotionItemId, 5);
            Assert.AreEqual(1, _events[0].Length, "The untouched MP Potion slot must not appear in the event.");
            AssertEntry(_events[0][0], ConsumeSlotLow, HPPotionItemId, 2);
        }

        // -----------------------------------------------------------------------
        // Invalid quantity / unregistered character.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_InvalidQuantityEdgeValues_ReturnsInvalidQuantity_NoLog_NoEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 3);

            // Act — no LogAssert.Expect: any unexpected log call fails this test.
            var resultZero = _inventory.ConsumeItem(Player, HPPotionItemId, 0);
            var resultNegative = _inventory.ConsumeItem(Player, HPPotionItemId, -1);

            // Assert
            Assert.AreEqual(ConsumeItemFailReason.InvalidQuantity, resultZero.Reason);
            Assert.AreEqual(ConsumeItemFailReason.InvalidQuantity, resultNegative.Reason);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 3);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void ConsumeItem_UnregisteredCharacter_LogsError_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ConsumeItem: .*is not a registered character"));

            // Act
            var result = _inventory.ConsumeItem(UnregisteredPlayer, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.CharacterNotRegistered, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Guard order — quantity before registration; registration before item validity.
        // -----------------------------------------------------------------------

        [Test]
        public void ConsumeItem_UnregisteredCharacterWithInvalidQuantity_ReturnsInvalidQuantity_NoLog()
        {
            // Act — no LogAssert.Expect: the registration error must not be reached.
            var result = _inventory.ConsumeItem(UnregisteredPlayer, HPPotionItemId, 0);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.InvalidQuantity, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void ConsumeItem_UnregisteredCharacterWithInvalidItemId_ReturnsCharacterNotRegistered()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ConsumeItem: .*is not a registered character"));

            // Act
            var result = _inventory.ConsumeItem(UnregisteredPlayer, ItemID.Invalid, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConsumeItemFailReason.CharacterNotRegistered, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        // =====================================================================
        // Both methods — isolation, re-entrancy, result contract, no bag-full side effects.
        // =====================================================================

        [Test]
        public void SellItem_SecondRegisteredCharacter_DoesNotAffectFirstCharacterSlots()
        {
            // Arrange — both characters hold the same item in the same slot index.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.SeedSlotForTesting(secondPlayer, SellSlot, HPPotionItemId, 5);

            // Act
            var result = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(Player, SellSlot, ItemID.Invalid, 0);
            AssertSlot(secondPlayer, SellSlot, HPPotionItemId, 5);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(Player, _eventCharacterIds[0]);
        }

        [Test]
        public void ConsumeItem_SecondRegisteredCharacter_DoesNotAffectFirstCharacterSlots()
        {
            // Arrange — both characters hold the same item in the same slot index.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 5);
            _inventory.SeedSlotForTesting(secondPlayer, ConsumeSlotLow, HPPotionItemId, 5);

            // Act
            var result = _inventory.ConsumeItem(Player, HPPotionItemId, 2);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(Player, ConsumeSlotLow, HPPotionItemId, 3);
            AssertSlot(secondPlayer, ConsumeSlotLow, HPPotionItemId, 5);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(Player, _eventCharacterIds[0]);
        }

        [Test]
        public void SellItem_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_TargetSlotUnchanged()
        {
            // Arrange — slot 4 holds HP Potions; a pickup into an empty slot (Bronze Sword, a
            // different item) triggers dispatch; the handler tries to sell from slot 4.
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);
            _inventory.OnInventoryChanged += _ => _inventory.SellItem(Player, SellSlot, HPPotionItemId, 5);

            // Act / Assert — the message pins the re-entrancy guard, not just the exception type.
            var ex = Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, BronzeSwordItemId, 1));
            StringAssert.Contains("mutated synchronously", ex.Message);
            AssertSlot(SellSlot, HPPotionItemId, 5);
        }

        [Test]
        public void ConsumeItem_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_TargetSlotUnchanged()
        {
            // Arrange — slot 2 holds HP Potions; a pickup into an empty slot (Bronze Sword, a
            // different item) triggers dispatch; the handler tries to consume from slot 2.
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 5);
            _inventory.OnInventoryChanged += _ => _inventory.ConsumeItem(Player, HPPotionItemId, 1);

            // Act / Assert — the message pins the re-entrancy guard, not just the exception type.
            var ex = Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, BronzeSwordItemId, 1));
            StringAssert.Contains("mutated synchronously", ex.Message);
            AssertSlot(ConsumeSlotLow, HPPotionItemId, 5);
        }

        [Test]
        public void SellItem_ResultContract_SuccessReasonNone_FailureReasonSetAndQuantitySoldZero_NoBagFull()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SellSlot, HPPotionItemId, 5);

            // Act
            var success = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 2);
            var failure = _inventory.SellItem(Player, SellSlot, BronzeSwordItemId, 1);

            // Assert
            Assert.IsTrue(success.Success);
            Assert.AreEqual(SellItemFailReason.None, success.Reason);
            Assert.IsFalse(failure.Success);
            Assert.AreNotEqual(SellItemFailReason.None, failure.Reason);
            Assert.AreEqual(0, failure.QuantitySold);
            Assert.AreEqual(0, _fullEvents, "SellItem must never fire OnInventoryFull.");
        }

        [Test]
        public void ConsumeItem_ResultContract_SuccessReasonNone_FailureReasonSet_NoBagFull()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, ConsumeSlotLow, HPPotionItemId, 5);

            // Act
            var success = _inventory.ConsumeItem(Player, HPPotionItemId, 2);
            var failure = _inventory.ConsumeItem(Player, HPPotionItemId, 100);

            // Assert
            Assert.IsTrue(success.Success);
            Assert.AreEqual(ConsumeItemFailReason.None, success.Reason);
            Assert.IsFalse(failure.Success);
            Assert.AreNotEqual(ConsumeItemFailReason.None, failure.Reason);
            Assert.AreEqual(0, _fullEvents, "ConsumeItem must never fire OnInventoryFull.");
        }

        [Test]
        public void SellItemAndConsumeItem_OnFullBag_NeverFireOnInventoryFull()
        {
            // Arrange — fill every slot so the bag is full before either call.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);

            // Act
            var sellResult = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 50);
            var consumeResult = _inventory.ConsumeItem(Player, HPPotionItemId, 10);

            // Assert
            Assert.IsTrue(sellResult.Success);
            Assert.IsTrue(consumeResult.Success);
            Assert.AreEqual(0, _fullEvents, "Neither SellItem nor ConsumeItem must ever fire OnInventoryFull.");
        }

        // -----------------------------------------------------------------------
        // Bag-full dedup window (GDD Rule 4.10) — only a successful Pickup resets it. A partial
        // sell/consume keeps the bag full, so the second blocked pickup would notify again if
        // the window had been reset.
        // -----------------------------------------------------------------------

        [Test]
        public void SellItem_Success_DoesNotResetBagFullDedupWindow()
        {
            // Arrange — full bag; a blocked pickup opens the dedup window.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);
            _inventory.Pickup(Player, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _fullEvents, "Precondition: the first blocked pickup notifies.");

            // Act — partial sell leaves every slot occupied; then a second blocked pickup.
            var sellResult = _inventory.SellItem(Player, SellSlot, HPPotionItemId, 1);
            var pickupResult = _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.IsTrue(sellResult.Success);
            Assert.AreEqual(PickupFailReason.InventoryFull, pickupResult.Reason);
            Assert.AreEqual(1, _fullEvents, "A successful SellItem must not reset the bag-full dedup window.");
        }

        [Test]
        public void ConsumeItem_Success_DoesNotResetBagFullDedupWindow()
        {
            // Arrange — full bag; a blocked pickup opens the dedup window.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);
            _inventory.Pickup(Player, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _fullEvents, "Precondition: the first blocked pickup notifies.");

            // Act — consuming one unit leaves every slot occupied; then a second blocked pickup.
            var consumeResult = _inventory.ConsumeItem(Player, HPPotionItemId, 1);
            var pickupResult = _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            Assert.IsTrue(consumeResult.Success);
            Assert.AreEqual(PickupFailReason.InventoryFull, pickupResult.Reason);
            Assert.AreEqual(1, _fullEvents, "A successful ConsumeItem must not reset the bag-full dedup window.");
        }
    }
}

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
    /// EditMode unit tests for Inventory System Story 002 — atomic pickup resolution and stack
    /// limits (GDD Rule 2, Rule 3, Rule 10, F-INV-2, Pickup Edge Cases).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_AtomicPickup_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID HPPotionItemId = new ItemID(3001u);        // Consumable, StackLimit 99
        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u);     // Equipment, StackLimit 1
        private static readonly ItemID BundleItemId = new ItemID(3003u);          // Consumable, StackLimit 10
        private static readonly ItemID SingleConsumableItemId = new ItemID(3004u); // Consumable, StackLimit 1
        private static readonly ItemID ZeroStackLimitItemId = new ItemID(3005u);  // Data error: StackLimit 0
        private static readonly ItemID UnregisteredItemId = new ItemID(3999u);

        // Occupies slots in seeded "full bag" states. Seeding bypasses the Item Database, so this
        // ID needs no registered definition; it only has to differ from every item picked up.
        private static readonly ItemID FillerItemId = new ItemID(3500u);

        private const int HP_POTION_STACK_LIMIT = 99;
        private const int BUNDLE_STACK_LIMIT = 10;

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private List<ItemDefinition> _definitions;
        private List<SlotChange[]> _events;
        private List<CharacterID> _eventCharacterIds;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: HP_POTION_STACK_LIMIT),
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
                ItemDefinitionBuilder.Build(BundleItemId.RawValue, "Test Bundle", ItemCategory.Consumable, stackLimit: BUNDLE_STACK_LIMIT),
                ItemDefinitionBuilder.Build(SingleConsumableItemId.RawValue, "Test Single Consumable", ItemCategory.Consumable, stackLimit: 1),
                ItemDefinitionBuilder.Build(ZeroStackLimitItemId.RawValue, "Test Zero Stack Limit", ItemCategory.Consumable, stackLimit: 0),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase);
            _inventory.RegisterCharacter(Player);

            _events = new List<SlotChange[]>();
            _eventCharacterIds = new List<CharacterID>();
            _inventory.OnInventoryChanged += RecordEvent;
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

        private void ReentrantPickupHandler(InventoryChangedEventArgs args)
        {
            _inventory.Pickup(args.CharacterID, BronzeSwordItemId, 1);
        }

        private void FillAllSlotsExcept(params int[] emptySlots)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (Array.IndexOf(emptySlots, i) < 0)
                    _inventory.SeedSlotForTesting(Player, i, FillerItemId, 1);
            }
        }

        private InventorySlot[] Snapshot()
        {
            var slots = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = _inventory.GetSlot(Player, i);
            return slots;
        }

        private void AssertSlotsUnchanged(InventorySlot[] before)
        {
            for (int i = 0; i < before.Length; i++)
            {
                var after = _inventory.GetSlot(Player, i);
                Assert.AreEqual(before[i].ItemId, after.ItemId, $"Slot {i} ItemId must be unchanged.");
                Assert.AreEqual(before[i].Quantity, after.Quantity, $"Slot {i} Quantity must be unchanged.");
            }
        }

        private static void AssertEntry(SlotChange entry, int slotIndex, ItemID itemId, int quantity)
        {
            Assert.AreEqual(slotIndex, entry.SlotIndex, "Entry slot index.");
            Assert.AreEqual(itemId, entry.ItemId, $"Entry for slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, entry.Quantity, $"Entry for slot {slotIndex} Quantity.");
        }

        private void AssertSlot(int slotIndex, ItemID itemId, int quantity)
        {
            var slot = _inventory.GetSlot(Player, slotIndex);
            Assert.AreEqual(itemId, slot.ItemId, $"Slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"Slot {slotIndex} Quantity.");
        }

        private static void AssertSucceeded(PickupResult result)
        {
            Assert.IsTrue(result.Success, "Pickup must succeed.");
            Assert.AreEqual(PickupFailReason.None, result.Reason, "A successful pickup must report Reason None.");
        }

        private static void AssertFailed(PickupResult result, PickupFailReason expected)
        {
            Assert.IsFalse(result.Success, "Pickup must fail.");
            Assert.AreEqual(expected, result.Reason, "Pickup fail reason.");
        }

        // -----------------------------------------------------------------------
        // AC-INV-2 — partial stack topped up first, overflow into the next empty
        // slot; one event with both entries (F-INV-2: max(0, 80+30-99) = 11).
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_PartialStackOverflow_FillsStackThenEmptySlot_OneEventTwoEntries_AC_INV_2()
        {
            // Arrange
            FillAllSlotsExcept(2, 5);
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 80);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 30);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count, "Exactly one InventoryChangedEvent must fire.");
            Assert.AreEqual(2, _events[0].Length, "The event must contain exactly two entries.");
            AssertEntry(_events[0][0], 2, HPPotionItemId, 99);
            AssertEntry(_events[0][1], 5, HPPotionItemId, 11);
            AssertSlot(2, HPPotionItemId, 99);
            AssertSlot(5, HPPotionItemId, 11);
        }

        [Test]
        public void Pickup_ExactlyFillsPartialStack_OnlyThatSlotChanges_AC_INV_2_EdgeCase()
        {
            // Arrange
            FillAllSlotsExcept(2, 5);
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 80);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 19);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length, "Only slot 2 changes.");
            AssertEntry(_events[0][0], 2, HPPotionItemId, 99);
            Assert.IsTrue(_inventory.GetSlot(Player, 5).IsEmpty, "Slot 5 must stay empty.");
        }

        [Test]
        public void Pickup_PartialStackAfterLowerEmptySlot_StackFilledFirst_EntriesAscending_Rule3_Step1()
        {
            // Arrange — Step 1 (partial stacks) takes precedence over a lower-index empty slot.
            _inventory.SeedSlotForTesting(Player, 5, HPPotionItemId, 90);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 20);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], 0, HPPotionItemId, 11);
            AssertEntry(_events[0][1], 5, HPPotionItemId, 99);
        }

        [Test]
        public void Pickup_TwoPartialStacksAndEmptySlot_AllPlannedInOneEvent_EntriesAscending_Rule3_Step1()
        {
            // Arrange — 14 units: 9 top up slot 2, 4 top up slot 7, 1 spills into empty slot 0.
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 90);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 95);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 14);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(3, _events[0].Length);
            AssertEntry(_events[0][0], 0, HPPotionItemId, 1);
            AssertEntry(_events[0][1], 2, HPPotionItemId, 99);
            AssertEntry(_events[0][2], 7, HPPotionItemId, 99);
        }

        [Test]
        public void Pickup_SeededStackAboveLimit_SkippedInStep1_PlacedInEmptySlot_EdgeCase()
        {
            // Arrange — corrupt/legacy data: a stack already above StackLimit receives no units.
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 120);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            AssertSucceeded(result);
            AssertSlot(0, HPPotionItemId, 120);
            AssertSlot(1, HPPotionItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 1, HPPotionItemId, 1);
        }

        // -----------------------------------------------------------------------
        // AC-INV-5 — full bag, partial fills insufficient: atomic failure.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_FullBagPartialRoomInsufficient_FailsAtomically_NoEvent_AC_INV_5()
        {
            // Arrange
            FillAllSlotsExcept(4, 11);
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 98);
            _inventory.SeedSlotForTesting(Player, 11, HPPotionItemId, 98);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 3);

            // Assert
            AssertFailed(result, PickupFailReason.InventoryFull);
            Assert.AreEqual(0, _events.Count, "No InventoryChangedEvent may fire on a failed pickup.");
            AssertSlot(4, HPPotionItemId, 98);
            AssertSlot(11, HPPotionItemId, 98);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_FullBagPartialRoomSufficient_Succeeds_AC_INV_5_EdgeCase()
        {
            // Arrange
            FillAllSlotsExcept(4, 11);
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 98);
            _inventory.SeedSlotForTesting(Player, 11, HPPotionItemId, 98);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 2);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], 4, HPPotionItemId, 99);
            AssertEntry(_events[0][1], 11, HPPotionItemId, 99);
        }

        // -----------------------------------------------------------------------
        // AC-INV-6 — StackLimit 1 equipment never merges.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_SecondEquipmentItem_PlacedInNewSlot_OriginalUnchanged_AC_INV_6()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            AssertSucceeded(result);
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertSlot(1, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length, "Only the new slot is reported — slot 0 is never merged into.");
            AssertEntry(_events[0][0], 1, BronzeSwordItemId, 1);
        }

        // -----------------------------------------------------------------------
        // AC-INV-12 — same-tick pickups resolve FIFO in call order (Rule 10).
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_TwoDifferentItemsInOrder_FirstTakesLowestSlot_AC_INV_12()
        {
            // Act
            var first = _inventory.Pickup(Player, HPPotionItemId, 1);
            var second = _inventory.Pickup(Player, BronzeSwordItemId, 1);

            // Assert
            AssertSucceeded(first);
            AssertSucceeded(second);
            Assert.AreEqual(2, _events.Count);
            AssertEntry(_events[0][0], 0, HPPotionItemId, 1);
            AssertEntry(_events[1][0], 1, BronzeSwordItemId, 1);
        }

        [Test]
        public void Pickup_TwoDifferentItemsReversedOrder_SlotAssignmentReverses_AC_INV_12_EdgeCase()
        {
            // Act
            _inventory.Pickup(Player, BronzeSwordItemId, 1);
            _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            Assert.AreEqual(2, _events.Count);
            AssertEntry(_events[0][0], 0, BronzeSwordItemId, 1);
            AssertEntry(_events[1][0], 1, HPPotionItemId, 1);
        }

        // -----------------------------------------------------------------------
        // AC-INV-13 — lowest-index empty slot wins when no partial stack exists.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_ScatteredEmptySlots_LandsInLowestIndex_AC_INV_13()
        {
            // Arrange
            FillAllSlotsExcept(3, 7, 15);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 3, HPPotionItemId, 1);
            Assert.IsTrue(_inventory.GetSlot(Player, 7).IsEmpty, "Slot 7 must stay empty.");
            Assert.IsTrue(_inventory.GetSlot(Player, 15).IsEmpty, "Slot 15 must stay empty.");
        }

        // -----------------------------------------------------------------------
        // Rule 3.7 Step 2 — remainder above StackLimit spans successive empty slots.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_QuantityAboveStackLimit_SpansEmptySlotsAscending_OneEvent_Rule3_Step2()
        {
            // Act
            var result = _inventory.Pickup(Player, BundleItemId, 25);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(3, _events[0].Length);
            AssertEntry(_events[0][0], 0, BundleItemId, 10);
            AssertEntry(_events[0][1], 1, BundleItemId, 10);
            AssertEntry(_events[0][2], 2, BundleItemId, 5);
            Assert.IsTrue(_inventory.GetSlot(Player, 3).IsEmpty);
        }

        [Test]
        public void Pickup_QuantityNeedsMoreEmptySlotsThanExist_FailsAtomically_Rule3_Step2_EdgeCase()
        {
            // Arrange — 25 units at StackLimit 10 need 3 empty slots; only 2 exist.
            FillAllSlotsExcept(6, 9);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, BundleItemId, 25);

            // Assert
            AssertFailed(result, PickupFailReason.InventoryFull);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_Step1AndStep2PlannedButRemainderLeft_FailsAtomically_Rule3_Step3()
        {
            // Arrange — 109 units: 9 fit slot 2, 99 fit empty slot 5, 1 is left over.
            FillAllSlotsExcept(2, 5);
            _inventory.SeedSlotForTesting(Player, 2, HPPotionItemId, 90);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 109);

            // Assert
            AssertFailed(result, PickupFailReason.InventoryFull);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_OversizedQuantity_SpansTwoSlots_OneEvent()
        {
            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 150);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], 0, HPPotionItemId, 99);
            AssertEntry(_events[0][1], 1, HPPotionItemId, 51);
        }

        // -----------------------------------------------------------------------
        // Edge — consumable with StackLimit = 1 behaves like equipment.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_StackLimitOneConsumableTwice_EachUnitOwnSlot_EdgeCase()
        {
            // Act
            _inventory.Pickup(Player, SingleConsumableItemId, 1);
            _inventory.Pickup(Player, SingleConsumableItemId, 1);

            // Assert
            AssertSlot(0, SingleConsumableItemId, 1);
            AssertSlot(1, SingleConsumableItemId, 1);
            Assert.AreEqual(2, _events.Count);
        }

        [Test]
        public void Pickup_StackLimitOneConsumableQuantityTwo_OccupiesTwoSlots_EdgeCase()
        {
            // Act
            var result = _inventory.Pickup(Player, SingleConsumableItemId, 2);

            // Assert
            AssertSucceeded(result);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(_events[0][0], 0, SingleConsumableItemId, 1);
            AssertEntry(_events[0][1], 1, SingleConsumableItemId, 1);
        }

        // -----------------------------------------------------------------------
        // Edge — IncomingQty <= 0 and the other fail reasons: no mutation, no event.
        // -----------------------------------------------------------------------

        [TestCase(0)]
        [TestCase(-1)]
        public void Pickup_NonPositiveQuantity_InvalidQuantity_NoMutationNoEvent_EdgeCase(int quantity)
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);
            var before = Snapshot();

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, quantity);

            // Assert
            AssertFailed(result, PickupFailReason.InvalidQuantity);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
            Assert.AreEqual(1, _inventory.FilledSlots(Player), "No phantom (ItemID, 0) slot may be created.");
        }

        [Test]
        public void Pickup_InvalidItemId_UnknownItem_LogsError_NoMutationNoEvent()
        {
            // Arrange
            var before = Snapshot();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a known item"));

            // Act
            var result = _inventory.Pickup(Player, ItemID.Invalid, 1);

            // Assert
            AssertFailed(result, PickupFailReason.UnknownItem);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_ItemNotInDatabase_UnknownItem_LogsError_NoMutationNoEvent()
        {
            // Arrange
            var before = Snapshot();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a known item"));

            // Act
            var result = _inventory.Pickup(Player, UnregisteredItemId, 1);

            // Assert
            AssertFailed(result, PickupFailReason.UnknownItem);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_DatabaseNotReady_UnknownItem_LogsError_NoMutationNoEvent()
        {
            // Arrange
            _itemDatabase.IsReady = false;
            var before = Snapshot();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a known item"));

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 1);

            // Assert
            AssertFailed(result, PickupFailReason.UnknownItem);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_ItemWithStackLimitBelowOne_UnknownItem_LogsError_NoMutationNoEvent()
        {
            // Arrange
            var before = Snapshot();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*invalid StackLimit 0"));

            // Act
            var result = _inventory.Pickup(Player, ZeroStackLimitItemId, 1);

            // Assert — a data error is never reported as InventoryFull (which would show bag-full UI).
            AssertFailed(result, PickupFailReason.UnknownItem);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_UnregisteredCharacter_CharacterNotRegistered_LogsError_NoEvent()
        {
            // Arrange
            var before = Snapshot();
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Pickup: .*not a registered character"));

            // Act
            var result = _inventory.Pickup(UnregisteredPlayer, HPPotionItemId, 1);

            // Assert
            AssertFailed(result, PickupFailReason.CharacterNotRegistered);
            Assert.AreEqual(0, _events.Count);
            AssertSlotsUnchanged(before);
        }

        [Test]
        public void Pickup_InvalidQuantityAndUnregisteredCharacter_QuantityGuardWinsWithoutLog()
        {
            // Act — guard order: quantity is checked first, and it logs nothing.
            var result = _inventory.Pickup(UnregisteredPlayer, HPPotionItemId, 0);

            // Assert
            AssertFailed(result, PickupFailReason.InvalidQuantity);
            LogAssert.NoUnexpectedReceived();
        }

        // -----------------------------------------------------------------------
        // Multi-character isolation — the change buffer is service-wide.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_TwoCharacters_EachEventScopedToItsCharacter_OtherInventoryUntouched()
        {
            // Arrange
            _inventory.RegisterCharacter(OtherPlayer);
            _inventory.SeedSlotForTesting(OtherPlayer, 0, HPPotionItemId, 50);

            // Act
            var playerResult = _inventory.Pickup(Player, HPPotionItemId, 30);
            var otherResult = _inventory.Pickup(OtherPlayer, BronzeSwordItemId, 1);

            // Assert
            AssertSucceeded(playerResult);
            AssertSucceeded(otherResult);
            Assert.AreEqual(2, _events.Count);

            Assert.AreEqual(Player, _eventCharacterIds[0]);
            Assert.AreEqual(1, _events[0].Length, "Player's event must not carry OtherPlayer's slots.");
            AssertEntry(_events[0][0], 0, HPPotionItemId, 30);

            Assert.AreEqual(OtherPlayer, _eventCharacterIds[1]);
            Assert.AreEqual(1, _events[1].Length, "OtherPlayer's event must not carry Player's slots.");
            AssertEntry(_events[1][0], 1, BronzeSwordItemId, 1);

            var otherSlot0 = _inventory.GetSlot(OtherPlayer, 0);
            Assert.AreEqual(HPPotionItemId, otherSlot0.ItemId);
            Assert.AreEqual(50, otherSlot0.Quantity, "Player's pickup must not top up OtherPlayer's stack.");
            Assert.AreEqual(2, _inventory.FilledSlots(OtherPlayer));
            Assert.AreEqual(1, _inventory.FilledSlots(Player));
        }

        // -----------------------------------------------------------------------
        // Re-entrancy guard and constructor.
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException()
        {
            // Arrange
            _inventory.OnInventoryChanged += ReentrantPickupHandler;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(0, HPPotionItemId, 1);
            Assert.IsTrue(_inventory.GetSlot(Player, 1).IsEmpty, "The re-entrant pickup must not have placed anything.");
        }

        [Test]
        public void Constructor_NullItemDatabase_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new InventoryService(null));
        }
    }
}

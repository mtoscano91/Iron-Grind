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
    /// EditMode unit tests for Inventory System Story 007 — Equipment System Interface
    /// (<c>HasFreeSlot</c>, <c>MoveItemOut</c>, <c>MoveItemIn</c>, <c>ForceInsert</c>; GDD Rule 8,
    /// Rule 4.10 dedup, Rule 8.23). No Equipment System exists yet — these tests call the
    /// interface directly, standing in for it.
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_EquipmentInterface_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID HPPotionItemId = new ItemID(3001u);     // Consumable, StackLimit 99
        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u);  // Equipment, StackLimit 1
        private static readonly ItemID UnregisteredItemId = new ItemID(3999u);

        // Occupies slots in seeded full-bag states; seeding bypasses the Item Database.
        private static readonly ItemID FillerItemId = new ItemID(3500u);

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private List<ItemDefinition> _definitions;
        private List<SlotChange[]> _events;
        private List<CharacterID> _fullEvents;
        private uint _tick;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _tick = 0u;
            _inventory = new InventoryService(_itemDatabase, CurrentTick);
            _inventory.RegisterCharacter(Player);

            _events = new List<SlotChange[]>();
            _fullEvents = new List<CharacterID>();
            _inventory.OnInventoryChanged += RecordChangedEvent;
            _inventory.OnInventoryFull += RecordFullEvent;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        private uint CurrentTick() => _tick;

        // Copies the entries out during dispatch — the args' buffer is service-owned and reused.
        private void RecordChangedEvent(InventoryChangedEventArgs args)
        {
            var entries = new SlotChange[args.Count];
            for (int i = 0; i < args.Count; i++)
                entries[i] = args[i];
            _events.Add(entries);
        }

        private void RecordFullEvent(InventoryFullEventArgs args) => _fullEvents.Add(args.CharacterID);

        private void FillBag(CharacterID characterId)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(characterId, i, FillerItemId, 1);
        }

        private void FillBagExceptSlot(CharacterID characterId, int emptySlot)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i != emptySlot)
                    _inventory.SeedSlotForTesting(characterId, i, FillerItemId, 1);
            }
        }

        private InventorySlot[] Snapshot(CharacterID characterId)
        {
            var slots = new InventorySlot[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = _inventory.GetSlot(characterId, i);
            return slots;
        }

        private void AssertSlotsUnchanged(CharacterID characterId, InventorySlot[] before)
        {
            for (int i = 0; i < before.Length; i++)
            {
                var after = _inventory.GetSlot(characterId, i);
                Assert.AreEqual(before[i].ItemId, after.ItemId, $"Slot {i} ItemId must be unchanged.");
                Assert.AreEqual(before[i].Quantity, after.Quantity, $"Slot {i} Quantity must be unchanged.");
            }
        }

        private void AssertSlot(CharacterID characterId, int slotIndex, ItemID itemId, int quantity)
        {
            var slot = _inventory.GetSlot(characterId, slotIndex);
            Assert.AreEqual(itemId, slot.ItemId, $"Slot {slotIndex} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"Slot {slotIndex} Quantity.");
        }

        // -----------------------------------------------------------------------
        // AC-INV-10 / Rule 8.23 — HasFreeSlot counts only truly empty slots.
        // -----------------------------------------------------------------------

        [Test]
        public void HasFreeSlot_AllTwentySlotsOccupied_ReturnsFalse_NoMutation_NoEvent_AC_INV_10()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot(Player);

            // Act — as the Equipment System would: query first; since it's false, never call MoveItemIn.
            bool hasFreeSlot = _inventory.HasFreeSlot(Player);

            // Assert
            Assert.IsFalse(hasFreeSlot);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void HasFreeSlot_NineteenOccupiedInclPartialStackPlusOneEmpty_ReturnsTrue_Rule8_23()
        {
            // Arrange — 19 occupied slots, one a partial HP Potion stack, and slot 19 empty.
            FillBagExceptSlot(Player, emptySlot: 19);
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);

            // Act
            bool hasFreeSlot = _inventory.HasFreeSlot(Player);

            // Assert
            Assert.IsTrue(hasFreeSlot);
        }

        [Test]
        public void HasFreeSlot_TwentyOccupiedInclPartialStack_ReturnsFalse_Rule8_23()
        {
            // Arrange — all 20 slots occupied; slot 0 is a partial 50/99 HP Potion stack.
            FillBag(Player);
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);

            // Act
            bool hasFreeSlot = _inventory.HasFreeSlot(Player);

            // Assert — a partial stack is not a free slot for equipment purposes.
            Assert.IsFalse(hasFreeSlot);
        }

        // -----------------------------------------------------------------------
        // MoveItemOut.
        // -----------------------------------------------------------------------

        [Test]
        public void MoveItemOut_OccupiedUnlockedSlot_ReturnsSuccess_EmptiesSlot_OneEvent()
        {
            // Arrange
            const int slot = 5;
            _inventory.SeedSlotForTesting(Player, slot, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.MoveItemOut(Player, slot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.Success, result.Code);
            Assert.AreEqual(BronzeSwordItemId, result.ItemId);
            AssertSlot(Player, slot, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            Assert.AreEqual(slot, _events[0][0].SlotIndex);
            Assert.AreEqual(ItemID.Invalid, _events[0][0].ItemId);
            Assert.AreEqual(0, _events[0][0].Quantity);
        }

        [Test]
        public void MoveItemOut_EmptySlot_ReturnsSlotEmpty_NoMutation_NoEvent()
        {
            // Arrange
            const int slot = 6;

            // Act — no LogAssert.Expect: an empty slot must not log anything.
            var result = _inventory.MoveItemOut(Player, slot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(ItemID.Invalid, result.ItemId);
            AssertSlot(Player, slot, ItemID.Invalid, 0);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_LockedSlot_ReturnsSlotLocked_SlotUnchanged_NoEvent()
        {
            // Arrange
            const int slot = 5;
            _inventory.SeedSlotForTesting(Player, slot, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, slot);

            // Act — no LogAssert.Expect: a locked slot must not log anything.
            var result = _inventory.MoveItemOut(Player, slot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotLocked, result.Code);
            Assert.AreEqual(ItemID.Invalid, result.ItemId);
            AssertSlot(Player, slot, BronzeSwordItemId, 1);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_NegativeIndex_ReturnsSlotEmpty_LogsError_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemOut: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.MoveItemOut(Player, -1);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(ItemID.Invalid, result.ItemId);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_IndexTwenty_ReturnsSlotEmpty_LogsError_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemOut: slotIndex 20 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.MoveItemOut(Player, 20);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_UnregisteredCharacter_ReturnsSlotEmpty_LogsError_NoEvent()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemOut: .*is not a registered character"));

            // Act
            var result = _inventory.MoveItemOut(UnregisteredPlayer, 0);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void MoveItemOut_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_SlotUnchanged()
        {
            // Arrange — a pickup into an empty slot triggers dispatch; the handler tries MoveItemOut.
            const int slot = 5;
            _inventory.SeedSlotForTesting(Player, slot, BronzeSwordItemId, 1);
            _inventory.OnInventoryChanged += _ => _inventory.MoveItemOut(Player, slot);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(Player, slot, BronzeSwordItemId, 1);
        }

        // -----------------------------------------------------------------------
        // MoveItemIn.
        // -----------------------------------------------------------------------

        [Test]
        public void MoveItemIn_TwoEmptySlots_PlacesInLowestIndex_ReturnsSuccessWithIndex_OneEvent()
        {
            // Arrange — occupy every slot except 3 and 8.
            FillBag(Player);
            _inventory.SeedSlotForTesting(Player, 3, ItemID.Invalid, 0);
            _inventory.SeedSlotForTesting(Player, 8, ItemID.Invalid, 0);

            // Act
            var result = _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, result.SlotIndex);
            AssertSlot(Player, 3, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            Assert.AreEqual(3, _events[0][0].SlotIndex);
            Assert.AreEqual(BronzeSwordItemId, _events[0][0].ItemId);
            Assert.AreEqual(1, _events[0][0].Quantity);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void MoveItemIn_FullBag_ReturnsFailed_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot(Player);

            // Act
            var result = _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(-1, result.SlotIndex);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count, "MoveItemIn must never fire OnInventoryFull.");
        }

        [Test]
        public void MoveItemIn_ExecutionTimeCheck_SlotFilledAfterHasFreeSlotQuery_ReturnsFailed()
        {
            // Arrange — exactly one empty slot; HasFreeSlot is true at query time.
            FillBagExceptSlot(Player, emptySlot: 19);
            Assert.IsTrue(_inventory.HasFreeSlot(Player), "Query-time check must see the free slot.");

            // Act — the slot is claimed (via Pickup) before MoveItemIn executes.
            var pickup = _inventory.Pickup(Player, HPPotionItemId, 1);
            var result = _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Assert — the item is not silently lost; the caller (Equipment System) keeps it.
            Assert.IsTrue(pickup.Success);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(-1, result.SlotIndex);
        }

        [Test]
        public void MoveItemIn_ItemAlreadyPresentInAnotherSlot_NeverMerges_LandsInNextEmptySlot()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.SlotIndex);
            AssertSlot(Player, 0, BronzeSwordItemId, 1);
            AssertSlot(Player, 1, BronzeSwordItemId, 1);
        }

        [Test]
        public void MoveItemIn_ItemIDInvalid_ReturnsFailed_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemIn: .*not a known item"));

            // Act
            var result = _inventory.MoveItemIn(Player, ItemID.Invalid);

            // Assert
            Assert.IsFalse(result.Success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void MoveItemIn_UnknownItemId_ReturnsFailed_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemIn: .*not a known item"));

            // Act
            var result = _inventory.MoveItemIn(Player, UnregisteredItemId);

            // Assert
            Assert.IsFalse(result.Success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void MoveItemIn_DatabaseNotReady_ReturnsFailed_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            _itemDatabase.IsReady = false;
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemIn: .*not a known item"));

            // Act
            var result = _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(result.Success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void MoveItemIn_UnregisteredCharacter_ReturnsFailed_LogsError_NoOnInventoryFull()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemIn: .*is not a registered character"));

            // Act
            var result = _inventory.MoveItemIn(UnregisteredPlayer, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(-1, result.SlotIndex);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void MoveItemIn_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_NoMutation()
        {
            // Arrange — a pickup fills slot 0; the handler tries MoveItemIn, which would land in slot 1.
            _inventory.OnInventoryChanged += _ => _inventory.MoveItemIn(Player, BronzeSwordItemId);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(Player, 1, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // ForceInsert.
        // -----------------------------------------------------------------------

        [Test]
        public void ForceInsert_OneEmptySlot_ReturnsTrue_PlacesItem_OneEvent()
        {
            // Arrange
            FillBagExceptSlot(Player, emptySlot: 7);

            // Act
            bool success = _inventory.ForceInsert(Player, BronzeSwordItemId);

            // Assert
            Assert.IsTrue(success);
            AssertSlot(Player, 7, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_FullBag_ReturnsFalse_NoMutation_FiresOnInventoryFullOnce()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot(Player);

            // Act
            bool success = _inventory.ForceInsert(Player, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(1, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_ItemAlreadyPresentInAnotherSlot_NeverMerges_LandsInNextEmptySlot()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);

            // Act
            bool success = _inventory.ForceInsert(Player, BronzeSwordItemId);

            // Assert
            Assert.IsTrue(success);
            AssertSlot(Player, 0, BronzeSwordItemId, 1);
            AssertSlot(Player, 1, BronzeSwordItemId, 1);
        }

        [Test]
        public void ForceInsert_ItemIDInvalid_ReturnsFalse_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ForceInsert: .*not a known item"));

            // Act
            bool success = _inventory.ForceInsert(Player, ItemID.Invalid);

            // Assert
            Assert.IsFalse(success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_UnknownItemId_ReturnsFalse_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ForceInsert: .*not a known item"));

            // Act
            bool success = _inventory.ForceInsert(Player, UnregisteredItemId);

            // Assert
            Assert.IsFalse(success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_DatabaseNotReady_ReturnsFalse_LogsError_NoMutation_NoEvents_NoOnInventoryFull()
        {
            // Arrange
            _itemDatabase.IsReady = false;
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ForceInsert: .*not a known item"));

            // Act
            bool success = _inventory.ForceInsert(Player, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(success);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_UnregisteredCharacter_ReturnsFalse_LogsError_NoOnInventoryFull()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ForceInsert: .*is not a registered character"));

            // Act
            bool success = _inventory.ForceInsert(UnregisteredPlayer, BronzeSwordItemId);

            // Assert
            Assert.IsFalse(success);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count);
        }

        [Test]
        public void ForceInsert_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_NoMutation()
        {
            // Arrange — a pickup fills slot 0; the handler tries ForceInsert, which would land in slot 1.
            _inventory.OnInventoryChanged += _ => _inventory.ForceInsert(Player, BronzeSwordItemId);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(Player, 1, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Dedup window (GDD Rule 4.10) — ForceInsert shares Pickup's window; success
        // does not reset it; expiry re-arms it.
        // -----------------------------------------------------------------------

        [Test]
        public void ForceInsert_Dedup_SharedWindowWithPickup_SuccessDoesNotReset_ExpiresAfterWindow_Rule4_10()
        {
            // Arrange — tick 0, bag full.
            _tick = 0u;
            FillBag(Player);

            // Act / Assert — first full-bag ForceInsert fires once.
            Assert.IsFalse(_inventory.ForceInsert(Player, BronzeSwordItemId));
            Assert.AreEqual(1, _fullEvents.Count, "Tick 0: first blocked ForceInsert notifies.");

            // A second full-bag ForceInsert within the window fires nothing.
            _tick = 100u;
            Assert.IsFalse(_inventory.ForceInsert(Player, BronzeSwordItemId));
            Assert.AreEqual(1, _fullEvents.Count, "Tick 100: inside the window, suppressed.");

            // A failed Pickup shares the same window.
            _tick = 200u;
            var pickup = _inventory.Pickup(Player, HPPotionItemId, 1);
            Assert.AreEqual(PickupFailReason.InventoryFull, pickup.Reason);
            Assert.AreEqual(1, _fullEvents.Count, "Tick 200: a blocked Pickup shares ForceInsert's window.");

            // A successful ForceInsert in between does not reset the window: free a slot, then insert.
            _tick = 300u;
            _inventory.RemoveItem(Player, 0);
            Assert.IsTrue(_inventory.ForceInsert(Player, BronzeSwordItemId), "A freed slot lets ForceInsert succeed.");
            Assert.AreEqual(1, _fullEvents.Count, "A successful ForceInsert must not reset the dedup window.");
            Assert.IsTrue(_inventory.IsFull(Player), "The successful insert re-fills the bag.");

            // Tick 599 is still inside the original [0, 600) window: no further notification.
            _tick = 599u;
            Assert.IsFalse(_inventory.ForceInsert(Player, BronzeSwordItemId));
            Assert.AreEqual(1, _fullEvents.Count, "Tick 599: still inside the original window.");

            // Tick 600: the original window has expired; notifies again.
            _tick = 600u;
            Assert.IsFalse(_inventory.ForceInsert(Player, BronzeSwordItemId));
            Assert.AreEqual(2, _fullEvents.Count, "Tick 600: window expired, notifies again.");
        }

        // -----------------------------------------------------------------------
        // MoveItemOut — a stacked slot is rejected, never silently lost (decided 2026-09-27).
        // -----------------------------------------------------------------------

        [Test]
        public void MoveItemOut_StackedConsumable_ReturnsSlotEmpty_LogsError_NoMutation()
        {
            // Arrange
            const int slot = 4;
            _inventory.SeedSlotForTesting(Player, slot, HPPotionItemId, 45);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] MoveItemOut: slot 4 for .* holds a stack of 45 "));

            // Act
            var result = _inventory.MoveItemOut(Player, slot);

            // Assert
            Assert.AreEqual(MoveItemOutCode.SlotEmpty, result.Code);
            Assert.AreEqual(ItemID.Invalid, result.ItemId);
            AssertSlot(Player, slot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Never merge — proven with a stackable item that has room to spare.
        // -----------------------------------------------------------------------

        [Test]
        public void MoveItemIn_StackableItemPartialStackExists_NeverMerges_LandsInNewSlotAtQuantityOne()
        {
            // Arrange — slot 0 holds 50/99 HP Potions, so a merge would be possible.
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);

            // Act
            var result = _inventory.MoveItemIn(Player, HPPotionItemId);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.SlotIndex);
            AssertSlot(Player, 0, HPPotionItemId, 50);
            AssertSlot(Player, 1, HPPotionItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
        }

        [Test]
        public void ForceInsert_StackableItemPartialStackExists_NeverMerges_LandsInNewSlotAtQuantityOne()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);

            // Act
            bool inserted = _inventory.ForceInsert(Player, HPPotionItemId);

            // Assert
            Assert.IsTrue(inserted);
            AssertSlot(Player, 0, HPPotionItemId, 50);
            AssertSlot(Player, 1, HPPotionItemId, 1);
            Assert.AreEqual(1, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Guard order — item validation precedes the bag-full check.
        // -----------------------------------------------------------------------

        [Test]
        public void ForceInsert_InvalidItemOnFullBag_ReturnsFalse_NoOnInventoryFull()
        {
            // Arrange
            FillBag(Player);
            var before = Snapshot(Player);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] ForceInsert: .*not a known item"));

            // Act
            bool inserted = _inventory.ForceInsert(Player, UnregisteredItemId);

            // Assert — a caller bug, not a full bag: validation must fail before notifying.
            Assert.IsFalse(inserted);
            AssertSlotsUnchanged(Player, before);
            Assert.AreEqual(0, _events.Count);
            Assert.AreEqual(0, _fullEvents.Count, "Validation failure must never fire OnInventoryFull.");
        }

        // -----------------------------------------------------------------------
        // Isolation — placement and the dedup window are per character.
        // -----------------------------------------------------------------------

        [Test]
        public void ForceInsert_Dedup_TwoCharacters_WindowsAreIndependent()
        {
            // Arrange — both bags full.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            FillBag(Player);
            FillBag(secondPlayer);

            // Act — tick 0: first character notifies; tick 100: second character, inside the first's window.
            _tick = 0u;
            bool first = _inventory.ForceInsert(Player, BronzeSwordItemId);
            _tick = 100u;
            bool second = _inventory.ForceInsert(secondPlayer, BronzeSwordItemId);

            // Assert — each character has its own window.
            Assert.IsFalse(first);
            Assert.IsFalse(second);
            Assert.AreEqual(2, _fullEvents.Count);
            Assert.AreEqual(Player, _fullEvents[0]);
            Assert.AreEqual(secondPlayer, _fullEvents[1]);
        }

        [Test]
        public void MoveItemIn_TwoCharacters_PlacementIsIndependent()
        {
            // Arrange — first character's slot 0 is occupied; second character's bag is empty.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.MoveItemIn(secondPlayer, BronzeSwordItemId);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(0, result.SlotIndex, "Second character's lowest empty slot is 0.");
            AssertSlot(secondPlayer, 0, BronzeSwordItemId, 1);
            AssertSlot(Player, 0, BronzeSwordItemId, 1);
            AssertSlot(Player, 1, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Round trip — out then in lands in the lowest empty slot, not the original.
        // -----------------------------------------------------------------------

        [Test]
        public void MoveItemOut_ThenMoveItemIn_ReturnsToLowestEmptySlot_NotOriginalSlot()
        {
            // Arrange — sword in slot 7; slots 0–6 empty.
            const int originalSlot = 7;
            _inventory.SeedSlotForTesting(Player, originalSlot, BronzeSwordItemId, 1);

            // Act
            var outResult = _inventory.MoveItemOut(Player, originalSlot);
            var inResult = _inventory.MoveItemIn(Player, outResult.ItemId);

            // Assert
            Assert.AreEqual(MoveItemOutCode.Success, outResult.Code);
            Assert.IsTrue(inResult.Success);
            Assert.AreEqual(0, inResult.SlotIndex);
            AssertSlot(Player, 0, BronzeSwordItemId, 1);
            AssertSlot(Player, originalSlot, ItemID.Invalid, 0);
            Assert.AreEqual(2, _events.Count);
        }
    }
}

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
    /// EditMode unit tests for Inventory System Story 006 — Slot Move: Merge, Swap & Relocate
    /// (GDD Rule 7, Discard and Move Edge Cases).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_MoveMergeSwap_Tests
    {
        private const byte MaxEnhancementLevel = 10;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID HPPotionItemId = new ItemID(3001u);     // Consumable, StackLimit 99
        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u);  // Equipment, StackLimit 1
        private static readonly ItemID IronSwordItemId = new ItemID(3003u);    // Equipment, StackLimit 1

        // Slot conventions mirrored from the story's QA Test Cases.
        private const int SlotA = 2;
        private const int SlotB = 9;

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
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
                ItemDefinitionBuilder.Build(IronSwordItemId.RawValue, "Iron Sword", ItemCategory.Equipment, stackLimit: 1),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, MaxEnhancementLevel);
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

        private static SlotChange FindEntry(SlotChange[] entries, int slotIndex)
        {
            foreach (var entry in entries)
            {
                if (entry.SlotIndex == slotIndex)
                    return entry;
            }
            Assert.Fail($"No event entry found for slot {slotIndex}.");
            throw new InvalidOperationException("unreachable");
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

        private static void AssertResultSlot(InventorySlot slot, ItemID itemId, int quantity, string label)
        {
            Assert.AreEqual(itemId, slot.ItemId, $"{label} ItemId.");
            Assert.AreEqual(quantity, slot.Quantity, $"{label} Quantity.");
        }

        private void AssertAllSlotsEmptyExceptSlotZero()
        {
            for (int i = 1; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                AssertSlot(i, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // AC-INV-8 — merge with room to spare: full transfer, source empties.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_MergeWithRoomToSpare_TransfersWholeStack_SourceEmpties_OneEvent_AC_INV_8()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 30);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 80, "ToSlot");
            AssertSlot(SlotB, HPPotionItemId, 80);
            AssertSlot(SlotA, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(FindEntry(_events[0], SlotB), SlotB, HPPotionItemId, 80);
            AssertEntry(FindEntry(_events[0], SlotA), SlotA, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // AC-INV-14 — merge overflow: destination caps at StackLimit, remainder stays in source.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_MergeOverflow_DestCapsAtStackLimit_SourceKeepsRemainder_OneEvent_AC_INV_14()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 70);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 60);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 31, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 99, "ToSlot");
            AssertSlot(SlotB, HPPotionItemId, 99);
            AssertSlot(SlotA, HPPotionItemId, 31);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(FindEntry(_events[0], SlotB), SlotB, HPPotionItemId, 99);
            AssertEntry(FindEntry(_events[0], SlotA), SlotA, HPPotionItemId, 31);
        }

        // -----------------------------------------------------------------------
        // Full-destination merge — nothing to transfer, no-op success, no event.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_MergeFullDestination_Stackable_NoOpSuccess_NoEvent_FullDestinationMerge()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 99);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 99);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 99, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 99, "ToSlot");
            AssertSlot(SlotA, HPPotionItemId, 99);
            AssertSlot(SlotB, HPPotionItemId, 99);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Move_MergeFullDestination_Equipment_NoOpSuccess_NoEvent_FullDestinationMerge()
        {
            // Arrange — two identical Bronze Swords (StackLimit 1); destination already "full".
            _inventory.SeedSlotForTesting(Player, SlotA, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, SlotB, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertSlot(SlotA, BronzeSwordItemId, 1);
            AssertSlot(SlotB, BronzeSwordItemId, 1);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // AC-INV-9 — locked source / locked destination.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_SourceLocked_ReturnsSourceLocked_BothSlotsUnchanged_NoEvent_AC_INV_9()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 30);
            _inventory.LockSlot(Player, SlotA);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.SourceLocked, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 50, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 30, "ToSlot");
            AssertSlot(SlotA, HPPotionItemId, 50);
            AssertSlot(SlotB, HPPotionItemId, 30);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Move_DestLocked_ReturnsDestLocked_BothSlotsUnchanged_NoEvent_AC_INV_9()
        {
            // Arrange — slot 2 is locked and is the destination of this call.
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 30);
            _inventory.LockSlot(Player, SlotA);

            // Act
            var result = _inventory.Move(Player, SlotB, SlotA);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.DestLocked, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 30, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 50, "ToSlot");
            AssertSlot(SlotA, HPPotionItemId, 50);
            AssertSlot(SlotB, HPPotionItemId, 30);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Rule 7.20 — swap (equipment/equipment, and different item categories).
        // -----------------------------------------------------------------------

        [Test]
        public void Move_EquipmentSwap_SwapsSlots_OneEvent_Rule7_20()
        {
            // Arrange
            const int bronzeSlot = 0;
            const int ironSlot = 1;
            _inventory.SeedSlotForTesting(Player, bronzeSlot, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, ironSlot, IronSwordItemId, 1);

            // Act
            var result = _inventory.Move(Player, bronzeSlot, ironSlot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, IronSwordItemId, 1, "FromSlot");
            AssertResultSlot(result.ToSlot, BronzeSwordItemId, 1, "ToSlot");
            AssertSlot(bronzeSlot, IronSwordItemId, 1);
            AssertSlot(ironSlot, BronzeSwordItemId, 1);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(FindEntry(_events[0], bronzeSlot), bronzeSlot, IronSwordItemId, 1);
            AssertEntry(FindEntry(_events[0], ironSlot), ironSlot, BronzeSwordItemId, 1);
        }

        [Test]
        public void Move_DifferentItemSwap_SwapsSlots_OneEvent_Rule7_20()
        {
            // Arrange
            const int potionSlot = 4;
            const int swordSlot = 5;
            _inventory.SeedSlotForTesting(Player, potionSlot, HPPotionItemId, 10);
            _inventory.SeedSlotForTesting(Player, swordSlot, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.Move(Player, potionSlot, swordSlot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, BronzeSwordItemId, 1, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 10, "ToSlot");
            AssertSlot(potionSlot, BronzeSwordItemId, 1);
            AssertSlot(swordSlot, HPPotionItemId, 10);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(FindEntry(_events[0], potionSlot), potionSlot, BronzeSwordItemId, 1);
            AssertEntry(FindEntry(_events[0], swordSlot), swordSlot, HPPotionItemId, 10);
        }

        // -----------------------------------------------------------------------
        // Relocate to empty destination.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_RelocateToEmptyDestination_MovesWholeStack_SourceBecomesEmpty_OneEvent_Relocate()
        {
            // Arrange
            const int potionSlot = 4;
            const int emptySlot = 12;
            _inventory.SeedSlotForTesting(Player, potionSlot, HPPotionItemId, 10);

            // Act
            var result = _inventory.Move(Player, potionSlot, emptySlot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 10, "ToSlot");
            AssertSlot(potionSlot, ItemID.Invalid, 0);
            AssertSlot(emptySlot, HPPotionItemId, 10);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(2, _events[0].Length);
            AssertEntry(FindEntry(_events[0], potionSlot), potionSlot, ItemID.Invalid, 0);
            AssertEntry(FindEntry(_events[0], emptySlot), emptySlot, HPPotionItemId, 10);
        }

        // -----------------------------------------------------------------------
        // Same slot — no-op success regardless of occupied/empty/locked state.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_SameSlot_Occupied_NoOpSuccess_NoEvent_SameSlot()
        {
            // Arrange
            const int slot = 4;
            _inventory.SeedSlotForTesting(Player, slot, HPPotionItemId, 10);

            // Act
            var result = _inventory.Move(Player, slot, slot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 10, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 10, "ToSlot");
            AssertSlot(slot, HPPotionItemId, 10);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Move_SameSlot_Empty_NoOpSuccess_NoEvent_SameSlot()
        {
            // Arrange
            const int slot = 15;

            // Act
            var result = _inventory.Move(Player, slot, slot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, ItemID.Invalid, 0, "ToSlot");
            AssertSlot(slot, ItemID.Invalid, 0);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Move_SameSlot_Locked_NoOpSuccess_NoEvent_SameSlot()
        {
            // Arrange
            const int slot = 4;
            _inventory.SeedSlotForTesting(Player, slot, HPPotionItemId, 10);
            _inventory.LockSlot(Player, slot);

            // Act — same-slot short-circuit runs before the lock check.
            var result = _inventory.Move(Player, slot, slot);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(MoveFailReason.None, result.Reason);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 10, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 10, "ToSlot");
            AssertSlot(slot, HPPotionItemId, 10);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Validation — out-of-range indices.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_NegativeFromIndex_ReturnsInvalidSlot_LogsWarning_AllSlotsUnchanged_Validation()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] Move: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.Move(Player, -1, 3);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.InvalidSlot, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, ItemID.Invalid, 0, "ToSlot");
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Move_ToIndexTwenty_ReturnsInvalidSlot_LogsWarning_AllSlotsUnchanged_Validation()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] Move: slotIndex 20 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.Move(Player, 3, 20);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.InvalidSlot, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, ItemID.Invalid, 0, "ToSlot");
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Validation — empty source (client-triggerable, must not log).
        // -----------------------------------------------------------------------

        [Test]
        public void Move_EmptySource_ReturnsInvalidSlot_NoLog_BothSlotsUnchanged_Validation()
        {
            // Arrange — slot 15 is empty; slot 3 holds an item so the echoed dest isn't trivially empty.
            const int emptySlot = 15;
            const int destSlot = 3;
            _inventory.SeedSlotForTesting(Player, destSlot, HPPotionItemId, 5);

            // Act — no LogAssert.Expect: any unexpected log call fails this test.
            var result = _inventory.Move(Player, emptySlot, destSlot);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.InvalidSlot, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 5, "ToSlot");
            AssertSlot(emptySlot, ItemID.Invalid, 0);
            AssertSlot(destSlot, HPPotionItemId, 5);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Validation — unregistered character.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_UnregisteredCharacter_ReturnsInvalidSlot_LogsError_NoEvent_Validation()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Move: .*is not a registered character"));

            // Act
            var result = _inventory.Move(UnregisteredPlayer, 0, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.InvalidSlot, result.Reason);
            AssertResultSlot(result.FromSlot, ItemID.Invalid, 0, "FromSlot");
            AssertResultSlot(result.ToSlot, ItemID.Invalid, 0, "ToSlot");
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Re-entrancy — Move called synchronously from an OnInventoryChanged
        // subscriber throws and does not mutate either target slot.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_BothTargetSlotsUnchanged()
        {
            // Arrange — slots A/B hold occupied stacks of different items; a pickup into an empty
            // slot (a third item) triggers dispatch, and the handler tries to move A onto B.
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, SlotB, BronzeSwordItemId, 1);
            _inventory.OnInventoryChanged += _ => _inventory.Move(Player, SlotA, SlotB);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, IronSwordItemId, 1));
            AssertSlot(SlotA, HPPotionItemId, 50);
            AssertSlot(SlotB, BronzeSwordItemId, 1);
        }

        // -----------------------------------------------------------------------
        // MoveResult.Fail contract guard.
        // -----------------------------------------------------------------------

        [Test]
        public void MoveResult_Fail_WithNoneReason_LogsAssertion()
        {
            // Arrange
            LogAssert.Expect(LogType.Assert, new Regex(@"^MoveResult\.Fail requires a non-None reason\."));

            // Act
            var result = MoveResult.Fail(MoveFailReason.None, InventorySlot.Empty, InventorySlot.Empty);

            // Assert — the guard reports the misuse; the result is still a failure.
            Assert.IsFalse(result.Success);
        }

        // -----------------------------------------------------------------------
        // Validation order — source lock is checked before destination lock.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_SourceAndDestBothLocked_ReturnsSourceLocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, SlotB, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, SlotA);
            _inventory.LockSlot(Player, SlotB);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(MoveFailReason.SourceLocked, result.Reason);
            AssertSlot(SlotA, HPPotionItemId, 50);
            AssertSlot(SlotB, BronzeSwordItemId, 1);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Boundary — the last valid slot index is a legal endpoint in both directions.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_RelocateToSlotNineteen_AndBack_Succeeds()
        {
            // Arrange
            const int lastSlot = InventoryConstants.INVENTORY_SLOT_COUNT - 1;
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 10);

            // Act
            var toLast = _inventory.Move(Player, SlotA, lastSlot);
            var fromLast = _inventory.Move(Player, lastSlot, SlotA);

            // Assert
            Assert.IsTrue(toLast.Success);
            Assert.AreEqual(MoveFailReason.None, toLast.Reason);
            AssertResultSlot(toLast.ToSlot, HPPotionItemId, 10, "ToSlot (slot 19)");
            Assert.IsTrue(fromLast.Success);
            AssertSlot(SlotA, HPPotionItemId, 10);
            AssertSlot(lastSlot, ItemID.Invalid, 0);
            Assert.AreEqual(2, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Isolation — per-character slots, locks, and events.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_TwoRegisteredCharacters_EachInventoryIndependent()
        {
            // Arrange — identical layouts; only the first character's source is locked.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(secondPlayer, SlotA, HPPotionItemId, 50);
            _inventory.LockSlot(Player, SlotA);

            // Act
            var secondResult = _inventory.Move(secondPlayer, SlotA, SlotB);
            var firstResult = _inventory.Move(Player, SlotA, SlotB);

            // Assert — second character's move applied; first character's lock still rejects.
            Assert.IsTrue(secondResult.Success);
            Assert.AreEqual(MoveFailReason.SourceLocked, firstResult.Reason);
            Assert.AreEqual(ItemID.Invalid, _inventory.GetSlot(secondPlayer, SlotA).ItemId);
            Assert.AreEqual(50, _inventory.GetSlot(secondPlayer, SlotB).Quantity);
            AssertSlot(SlotA, HPPotionItemId, 50);
            AssertSlot(SlotB, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(secondPlayer, _eventCharacterIds[0]);
        }

        // -----------------------------------------------------------------------
        // Pending-change buffer — no-op moves leave nothing behind.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_AfterNoOpMoves_SubsequentMoveEventHasOnlyExpectedEntries()
        {
            // Arrange — full-destination pair for the no-op merge, plus a relocatable stack.
            const int fullA = 5;
            const int fullB = 6;
            const int relocateTo = 12;
            _inventory.SeedSlotForTesting(Player, fullA, HPPotionItemId, 99);
            _inventory.SeedSlotForTesting(Player, fullB, HPPotionItemId, 99);
            _inventory.SeedSlotForTesting(Player, SlotA, BronzeSwordItemId, 1);

            // Act — two no-ops, then a real relocate.
            _inventory.Move(Player, fullA, fullA);
            _inventory.Move(Player, fullA, fullB);
            var result = _inventory.Move(Player, SlotA, relocateTo);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, _events.Count, "No-op moves must not fire events.");
            Assert.AreEqual(2, _events[0].Length, "No stale entries may leak from the no-op moves.");
            AssertEntry(FindEntry(_events[0], SlotA), SlotA, ItemID.Invalid, 0);
            AssertEntry(FindEntry(_events[0], relocateTo), relocateTo, BronzeSwordItemId, 1);
        }

        // -----------------------------------------------------------------------
        // Merge direction — the smaller stack as source.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_MergeReverseDirection_TransfersCorrectly()
        {
            // Arrange — the mirror of AC-INV-14: the smaller stack (60) is the source, the larger (70) the destination.
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 60);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 70);

            // Act
            var result = _inventory.Move(Player, SlotA, SlotB);

            // Assert — B caps at 99 (29 transferred), A keeps 31.
            Assert.IsTrue(result.Success);
            AssertSlot(SlotB, HPPotionItemId, 99);
            AssertSlot(SlotA, HPPotionItemId, 31);
            AssertResultSlot(result.FromSlot, HPPotionItemId, 31, "FromSlot");
            AssertResultSlot(result.ToSlot, HPPotionItemId, 99, "ToSlot");
            Assert.AreEqual(1, _events.Count);
            AssertEntry(FindEntry(_events[0], SlotA), SlotA, HPPotionItemId, 31);
            AssertEntry(FindEntry(_events[0], SlotB), SlotB, HPPotionItemId, 99);
        }

        // -----------------------------------------------------------------------
        // Side effects — filling a stack to capacity never triggers bag-full.
        // -----------------------------------------------------------------------

        [Test]
        public void Move_FillingDestinationToCapacity_DoesNotFireOnInventoryFull()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, SlotA, HPPotionItemId, 70);
            _inventory.SeedSlotForTesting(Player, SlotB, HPPotionItemId, 60);
            int fullEvents = 0;
            _inventory.OnInventoryFull += _ => fullEvents++;

            // Act — overflow merge, then a no-op onto the now-full stack.
            var overflow = _inventory.Move(Player, SlotA, SlotB);
            var noOp = _inventory.Move(Player, SlotA, SlotB);

            // Assert
            Assert.IsTrue(overflow.Success);
            Assert.IsTrue(noOp.Success);
            AssertSlot(SlotB, HPPotionItemId, 99);
            Assert.AreEqual(0, fullEvents, "Move must never fire OnInventoryFull.");
        }
    }
}

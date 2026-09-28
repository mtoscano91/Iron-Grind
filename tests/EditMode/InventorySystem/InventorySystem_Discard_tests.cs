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
    /// EditMode unit tests for Inventory System Story 005 — Discard (GDD Rule 6, Discard and
    /// Move Edge Cases).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_Discard_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u); // Equipment, StackLimit 1
        private static readonly ItemID HPPotionItemId = new ItemID(3001u);   // Consumable, StackLimit 99

        // Slot conventions mirrored from the story's QA Test Cases and from the SlotLocks sibling
        // suite: slot 6 hosts the stackable HP Potion scenarios, slot 0 hosts the equipment
        // scenario, slot 10 is used as an always-empty in-range slot.
        private const int HpPotionSlot = 6;
        private const int EquipmentSlot = 0;
        private const int EmptySlot = 10;

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
                ItemDefinitionBuilder.Build(BronzeSwordItemId.RawValue, "Bronze Sword", ItemCategory.Equipment, stackLimit: 1),
                ItemDefinitionBuilder.Build(HPPotionItemId.RawValue, "HP Potion", ItemCategory.Consumable, stackLimit: 99),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u);
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

        private void AssertAllSlotsEmptyExceptSlotZero()
        {
            for (int i = 1; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                AssertSlot(i, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // AC-INV-4 — locked slot.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_LockedSlot_ReturnsSlotLocked_NoMutation_NoEvent_AC_INV_4()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            _inventory.LockSlot(Player, HpPotionSlot);

            // Act
            var result = _inventory.Discard(Player, HpPotionSlot, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.SlotLocked, result.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count, "A rejected discard must never fire OnInventoryChanged.");
        }

        // -----------------------------------------------------------------------
        // AC-INV-7a — partial discard.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_PartialQuantity_ReturnsSuccess_SlotReducedToRemainder_OneEventEntry_AC_INV_7a()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act
            var result = _inventory.Discard(Player, HpPotionSlot, 20);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(DiscardFailReason.None, result.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 25);
            Assert.AreEqual(1, _events.Count, "A successful discard must fire exactly one OnInventoryChanged.");
            Assert.AreEqual(Player, _eventCharacterIds[0]);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], HpPotionSlot, HPPotionItemId, 25);
        }

        // -----------------------------------------------------------------------
        // AC-INV-7b — full-stack discard empties the slot.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_FullQuantity_ReturnsSuccess_SlotBecomesEmpty_HasItemFalseAfter_AC_INV_7b()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act
            var result = _inventory.Discard(Player, HpPotionSlot, 45);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(DiscardFailReason.None, result.Reason);
            AssertSlot(HpPotionSlot, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], HpPotionSlot, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId), "HasItem must be false once the only stack is fully discarded.");
        }

        // -----------------------------------------------------------------------
        // AC-INV-7c — quantity above current stack, and boundary edge values.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_QuantityFarExceedsSlot_ReturnsInvalidQuantity_NoMutation_NoEvent_AC_INV_7c()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act
            var result = _inventory.Discard(Player, HpPotionSlot, 100);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.InvalidQuantity, result.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Discard_InvalidQuantityEdgeValues_ReturnsInvalidQuantity_NoMutation_NoEvent_AC_INV_7c_Edge()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act — one unit over the stack, zero, and negative.
            var resultOneOver = _inventory.Discard(Player, HpPotionSlot, 46);
            var resultZero = _inventory.Discard(Player, HpPotionSlot, 0);
            var resultNegative = _inventory.Discard(Player, HpPotionSlot, -1);

            // Assert
            Assert.IsFalse(resultOneOver.Success);
            Assert.AreEqual(DiscardFailReason.InvalidQuantity, resultOneOver.Reason);
            Assert.IsFalse(resultZero.Success);
            Assert.AreEqual(DiscardFailReason.InvalidQuantity, resultZero.Reason);
            Assert.IsFalse(resultNegative.Success);
            Assert.AreEqual(DiscardFailReason.InvalidQuantity, resultNegative.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Empty in-range slot — client-triggerable, must not log.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_EmptyInRangeSlot_ReturnsSlotEmpty_NoLog_NoEvent_AC_Rule6_18()
        {
            // Act
            var result = _inventory.Discard(Player, EmptySlot, 1);

            // Assert — no LogAssert.Expect: any unexpected log call fails this test.
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.SlotEmpty, result.Reason);
            AssertSlot(EmptySlot, ItemID.Invalid, 0);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Out-of-range slotIndex — server warning, all slots unchanged.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_NegativeIndex_ReturnsSlotEmpty_LogsWarning_AllSlotsUnchanged_AC_Rule6_18()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] Discard: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.Discard(Player, -1, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.SlotEmpty, result.Reason);
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void Discard_IndexTwenty_ReturnsSlotEmpty_LogsWarning_AllSlotsUnchanged_AC_Rule6_18()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] Discard: slotIndex 20 is out of range \[0, 20\)\."));

            // Act
            var result = _inventory.Discard(Player, 20, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.SlotEmpty, result.Reason);
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Unregistered character — server error, no event.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_UnregisteredCharacter_ReturnsSlotEmpty_LogsError_NoEvent_AC_UnregisteredCharacter()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] Discard: .*is not a registered character"));

            // Act
            var result = _inventory.Discard(UnregisteredPlayer, 0, 1);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.AreEqual(DiscardFailReason.SlotEmpty, result.Reason);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Rule 6.17 — discarding an equipment item (StackLimit 1) empties the slot.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_Equipment_QuantityOne_SlotBecomesEmpty_OneEventEntry_AC_Rule6_17()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, EquipmentSlot, BronzeSwordItemId, 1);

            // Act
            var result = _inventory.Discard(Player, EquipmentSlot, 1);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.AreEqual(DiscardFailReason.None, result.Reason);
            AssertSlot(EquipmentSlot, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], EquipmentSlot, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Mutation-seam re-entrancy — Discard called synchronously from an
        // OnInventoryChanged subscriber throws and does not mutate its target slot.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException_TargetSlotUnchanged()
        {
            // Arrange — slot 6 holds HP Potions; a pickup into an empty slot (Bronze Sword, a
            // different item) triggers dispatch; the handler tries to discard from slot 6.
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            _inventory.OnInventoryChanged += _ => _inventory.Discard(Player, HpPotionSlot, 10);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, BronzeSwordItemId, 1));
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
        }

        // -----------------------------------------------------------------------
        // Same-tick DiscardRequest vs LockSlot race — resolved by FIFO call order.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_ThenLockSlot_DiscardSucceeds_SubsequentLockIsNoOp_AC_FIFORace()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] LockSlot: slot 6 for .* is empty; no lock applied\."));

            // Act — discard fully empties the slot before the lock request arrives.
            var discardResult = _inventory.Discard(Player, HpPotionSlot, 45);
            _inventory.LockSlot(Player, HpPotionSlot);

            // Assert
            Assert.IsTrue(discardResult.Success);
            Assert.AreEqual(DiscardFailReason.None, discardResult.Reason);
            AssertSlot(HpPotionSlot, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, HpPotionSlot), "LockSlot on the now-empty slot must be a no-op.");
        }

        [Test]
        public void LockSlot_ThenDiscard_DiscardRejectedSlotLocked_AC_FIFORace()
        {
            // Arrange — the lock request arrives before the discard request.
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            _inventory.LockSlot(Player, HpPotionSlot);

            // Act
            var discardResult = _inventory.Discard(Player, HpPotionSlot, 45);

            // Assert
            Assert.IsFalse(discardResult.Success);
            Assert.AreEqual(DiscardFailReason.SlotLocked, discardResult.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count, "A rejected discard must never fire OnInventoryChanged.");
        }

        // -----------------------------------------------------------------------
        // Validation order — lock is checked before quantity bounds.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_LockedSlotWithInvalidQuantity_ReturnsSlotLocked_NotInvalidQuantity()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            _inventory.LockSlot(Player, HpPotionSlot);

            // Act — quantity is out of bounds, but the lock must win.
            var resultOver = _inventory.Discard(Player, HpPotionSlot, 100);
            var resultZero = _inventory.Discard(Player, HpPotionSlot, 0);

            // Assert
            Assert.AreEqual(DiscardFailReason.SlotLocked, resultOver.Reason);
            Assert.AreEqual(DiscardFailReason.SlotLocked, resultZero.Reason);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Sequential calls — each successful discard fires its own independent event.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_CalledTwiceSequentially_EachFiresIndependentSingleEntryEvent()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act — partial, then the remainder.
            var first = _inventory.Discard(Player, HpPotionSlot, 20);
            var second = _inventory.Discard(Player, HpPotionSlot, 25);

            // Assert
            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);
            AssertSlot(HpPotionSlot, ItemID.Invalid, 0);
            Assert.AreEqual(2, _events.Count);
            Assert.AreEqual(1, _events[0].Length, "The pending-change buffer must reset between calls.");
            Assert.AreEqual(1, _events[1].Length, "The pending-change buffer must reset between calls.");
            AssertEntry(_events[0][0], HpPotionSlot, HPPotionItemId, 25);
            AssertEntry(_events[1][0], HpPotionSlot, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // Isolation — only the targeted slot / character is mutated.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_ItemAlsoExistsInAnotherSlot_OnlyTargetSlotMutated()
        {
            // Arrange — two HP Potion stacks.
            const int otherPotionSlot = 2;
            _inventory.SeedSlotForTesting(Player, otherPotionSlot, HPPotionItemId, 30);
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);

            // Act
            var result = _inventory.Discard(Player, HpPotionSlot, 45);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(HpPotionSlot, ItemID.Invalid, 0);
            AssertSlot(otherPotionSlot, HPPotionItemId, 30);
            Assert.IsTrue(_inventory.HasItem(Player, HPPotionItemId), "The other stack must remain.");
            Assert.AreEqual(1, _events[0].Length);
        }

        [Test]
        public void Discard_SecondRegisteredCharacter_DoesNotAffectFirstCharacterSlots()
        {
            // Arrange — both characters hold the same item in the same slot index.
            var secondPlayer = new CharacterID(1002u);
            _inventory.RegisterCharacter(secondPlayer);
            _inventory.SeedSlotForTesting(Player, HpPotionSlot, HPPotionItemId, 45);
            _inventory.SeedSlotForTesting(secondPlayer, HpPotionSlot, HPPotionItemId, 45);

            // Act
            var result = _inventory.Discard(secondPlayer, HpPotionSlot, 20);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(HpPotionSlot, HPPotionItemId, 45);
            var secondSlot = _inventory.GetSlot(secondPlayer, HpPotionSlot);
            Assert.AreEqual(25, secondSlot.Quantity);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(secondPlayer, _eventCharacterIds[0]);
        }

        // -----------------------------------------------------------------------
        // Side effects — discard never triggers the bag-full notification.
        // -----------------------------------------------------------------------

        [Test]
        public void Discard_Success_DoesNotFireOnInventoryFull()
        {
            // Arrange — fill every slot so the bag is full before discarding.
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 45);
            int fullEvents = 0;
            _inventory.OnInventoryFull += _ => fullEvents++;

            // Act
            var partial = _inventory.Discard(Player, HpPotionSlot, 20);
            var full = _inventory.Discard(Player, EmptySlot, 45); // EmptySlot is occupied in this test only.

            // Assert
            Assert.IsTrue(partial.Success);
            Assert.IsTrue(full.Success);
            Assert.AreEqual(0, fullEvents, "Discard must never fire OnInventoryFull.");
        }

        // -----------------------------------------------------------------------
        // DiscardResult.Fail contract guard.
        // -----------------------------------------------------------------------

        [Test]
        public void DiscardResult_Fail_WithNoneReason_LogsAssertion()
        {
            // Arrange
            LogAssert.Expect(LogType.Assert, new Regex(@"^DiscardResult\.Fail requires a non-None reason\."));

            // Act
            var result = DiscardResult.Fail(DiscardFailReason.None);

            // Assert — the guard reports the misuse; the result is still a failure.
            Assert.IsFalse(result.Success);
        }
    }
}

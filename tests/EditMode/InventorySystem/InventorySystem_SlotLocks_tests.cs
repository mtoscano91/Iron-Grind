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
    /// EditMode unit tests for Inventory System Story 004 — Slot Locks (Enhancement Reservation)
    /// and <c>RemoveItem</c> (GDD Rule 5, States and Transitions: Occupied-Locked, Lock State Edge
    /// Cases, RemoveItem out-of-range edge case).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_SlotLocks_Tests
    {
        private const byte MaxEnhancementLevel = 10;

        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID UnregisteredPlayer = new CharacterID(1999u);

        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u); // Equipment, StackLimit 1
        private static readonly ItemID HPPotionItemId = new ItemID(3001u);   // Consumable, StackLimit 99

        // Occupies slots in seeded "full bag" states. Seeding bypasses the Item Database, so this
        // ID needs no registered definition; it only has to differ from every item under test.
        private static readonly ItemID FillerItemId = new ItemID(3500u);

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

        private void FillAllSlotsExcept(params int[] emptySlots)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (Array.IndexOf(emptySlots, i) < 0)
                    _inventory.SeedSlotForTesting(Player, i, FillerItemId, 1);
            }
        }

        // -----------------------------------------------------------------------
        // Lock/unlock round-trip — AC Rule 5.12/5.13.
        // -----------------------------------------------------------------------

        [Test]
        public void LockSlot_OccupiedSlot_IsSlotLockedTrue_ContentsUnchanged_NoEvent_AC_Rule5_12()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);

            // Act
            _inventory.LockSlot(Player, 3);

            // Assert
            Assert.IsTrue(_inventory.IsSlotLocked(Player, 3));
            AssertSlot(3, BronzeSwordItemId, 1);
            Assert.AreEqual(0, _events.Count, "Locking alone must never fire OnInventoryChanged.");
        }

        [Test]
        public void UnlockSlot_PreviouslyLockedSlot_IsSlotLockedFalse_ContentsUnchanged_NoEvent_AC_Rule5_13()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, 3);

            // Act
            _inventory.UnlockSlot(Player, 3);

            // Assert
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 3));
            AssertSlot(3, BronzeSwordItemId, 1);
            Assert.AreEqual(0, _events.Count, "Unlocking alone must never fire OnInventoryChanged.");
        }

        [Test]
        public void LockSlot_OtherSlotsLockStateUnaffected_AC_Rule5_12_EdgeCase()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 10);

            // Act
            _inventory.LockSlot(Player, 3);

            // Assert
            Assert.IsTrue(_inventory.IsSlotLocked(Player, 3));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 4), "Locking slot 3 must not lock slot 4.");
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (i != 3)
                    Assert.IsFalse(_inventory.IsSlotLocked(Player, i), $"Slot {i} must remain unlocked.");
            }
        }

        // -----------------------------------------------------------------------
        // Lock empty slot — Lock State Edge Cases.
        // -----------------------------------------------------------------------

        [Test]
        public void LockSlot_EmptySlot_NoLockSet_LogsWarning_AC_LockStateEdgeCases()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] LockSlot: slot 5 for .* is empty; no lock applied\."));

            // Act
            _inventory.LockSlot(Player, 5);

            // Assert
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 5));
        }

        [Test]
        public void LockSlot_EmptySlot_LaterPickupLandsUnlocked_AC_LockStateEdgeCases()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[InventoryService\] LockSlot: slot 5 for .* is empty; no lock applied\."));
            FillAllSlotsExcept(5);
            _inventory.LockSlot(Player, 5);

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 10);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(5, HPPotionItemId, 10);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 5), "A slot that failed to lock while empty must not surface as locked once occupied.");
        }

        // -----------------------------------------------------------------------
        // Double unlock — defensive Enhancement timeout paths must be safe.
        // -----------------------------------------------------------------------

        [Test]
        public void UnlockSlot_CalledTwiceOnNeverLockedSlot_NoExceptionStateUnchanged_AC_LockStateEdgeCases()
        {
            // Act / Assert
            Assert.DoesNotThrow(() => _inventory.UnlockSlot(Player, 3));
            Assert.DoesNotThrow(() => _inventory.UnlockSlot(Player, 3));

            // Assert
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 3));
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // RemoveItem on a locked slot — States and Transitions: the only path from
        // Occupied-Locked to Empty.
        // -----------------------------------------------------------------------

        [Test]
        public void RemoveItem_LockedSlot_ClearsSlotAndLock_FiresOneEvent_AC_RemoveItemLocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, 3);

            // Act
            _inventory.RemoveItem(Player, 3);

            // Assert
            AssertSlot(3, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 3));
            Assert.AreEqual(1, _events.Count, "RemoveItem must fire exactly one OnInventoryChanged.");
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 3, ItemID.Invalid, 0);
        }

        [Test]
        public void RemoveItem_LockedMultiUnitStack_ClearsEntireStackAndLock_FiresOneEvent_AC_RemoveItemLocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 6, HPPotionItemId, 35);
            _inventory.LockSlot(Player, 6);

            // Act
            _inventory.RemoveItem(Player, 6);

            // Assert
            AssertSlot(6, ItemID.Invalid, 0);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 6), "RemoveItem must release the lock on a multi-unit stack too.");
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 6, ItemID.Invalid, 0);
        }

        // -----------------------------------------------------------------------
        // RemoveItem on an unlocked stack, and on an empty slot.
        // -----------------------------------------------------------------------

        [Test]
        public void RemoveItem_UnlockedStack_ClearsEntireStackRegardlessOfQuantity_FiresOneEvent_AC_RemoveItemUnlocked()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 4, HPPotionItemId, 20);

            // Act
            _inventory.RemoveItem(Player, 4);

            // Assert
            AssertSlot(4, ItemID.Invalid, 0);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length);
            AssertEntry(_events[0][0], 4, ItemID.Invalid, 0);
        }

        [Test]
        public void RemoveItem_EmptyInRangeSlot_NoOp_NoEvent_AC_RemoveItemUnlocked_EdgeCase()
        {
            // Act
            _inventory.RemoveItem(Player, 7);

            // Assert
            AssertSlot(7, ItemID.Invalid, 0);
            Assert.AreEqual(0, _events.Count, "RemoveItem on an already-empty slot must fire no event.");
        }

        // -----------------------------------------------------------------------
        // RemoveItem out of range.
        // -----------------------------------------------------------------------

        [Test]
        public void RemoveItem_NegativeIndex_LogsError_NoOp_NoEvent_AC_RemoveItemOutOfRange()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] RemoveItem: slotIndex -1 is out of range \[0, 20\)\."));

            // Act
            Assert.DoesNotThrow(() => _inventory.RemoveItem(Player, -1));

            // Assert
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void RemoveItem_IndexTwenty_LogsError_NoOp_NoEvent_AC_RemoveItemOutOfRange()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 0, BronzeSwordItemId, 1);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] RemoveItem: slotIndex 20 is out of range \[0, 20\)\."));

            // Act
            Assert.DoesNotThrow(() => _inventory.RemoveItem(Player, 20));

            // Assert
            AssertSlot(0, BronzeSwordItemId, 1);
            AssertAllSlotsEmptyExceptSlotZero();
            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Unregistered character — LockSlot / UnlockSlot / RemoveItem.
        // -----------------------------------------------------------------------

        [Test]
        public void LockSlot_UnregisteredCharacter_LogsError_NoOp_AC_UnregisteredCharacter()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] LockSlot: .*is not a registered character"));

            Assert.DoesNotThrow(() => _inventory.LockSlot(UnregisteredPlayer, 0));

            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void UnlockSlot_UnregisteredCharacter_LogsError_NoOp_AC_UnregisteredCharacter()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] UnlockSlot: .*is not a registered character"));

            Assert.DoesNotThrow(() => _inventory.UnlockSlot(UnregisteredPlayer, 0));

            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void RemoveItem_UnregisteredCharacter_LogsError_NoOp_NoEvent_AC_UnregisteredCharacter()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] RemoveItem: .*is not a registered character"));

            Assert.DoesNotThrow(() => _inventory.RemoveItem(UnregisteredPlayer, 0));

            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // Out-of-range LockSlot / UnlockSlot (consistency extension of RemoveItem's rule).
        // -----------------------------------------------------------------------

        [Test]
        public void LockSlot_OutOfRangeIndex_LogsError_NoOp_IsSlotLockedFalse()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] LockSlot: slotIndex 20 is out of range \[0, 20\)\."));
            Assert.DoesNotThrow(() => _inventory.LockSlot(Player, 20));

            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] LockSlot: slotIndex -1 is out of range \[0, 20\)\."));
            Assert.DoesNotThrow(() => _inventory.LockSlot(Player, -1));

            Assert.AreEqual(0, _events.Count);
        }

        [Test]
        public void UnlockSlot_OutOfRangeIndex_LogsError_NoOp()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] UnlockSlot: slotIndex 20 is out of range \[0, 20\)\."));
            Assert.DoesNotThrow(() => _inventory.UnlockSlot(Player, 20));

            LogAssert.Expect(LogType.Error, new Regex(@"^\[InventoryService\] UnlockSlot: slotIndex -1 is out of range \[0, 20\)\."));
            Assert.DoesNotThrow(() => _inventory.UnlockSlot(Player, -1));

            Assert.AreEqual(0, _events.Count);
        }

        // -----------------------------------------------------------------------
        // All 20 slots locked — not an error state.
        // -----------------------------------------------------------------------

        [Test]
        public void HasFreeSlot_AllTwentySlotsLocked_ReturnsFalse_AC_AllLocked()
        {
            // Arrange
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                _inventory.SeedSlotForTesting(Player, i, FillerItemId, 1);
                _inventory.LockSlot(Player, i);
            }

            // Act / Assert
            Assert.IsFalse(_inventory.HasFreeSlot(Player));
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                Assert.IsTrue(_inventory.IsSlotLocked(Player, i), $"Slot {i} must be locked.");
            Assert.AreEqual(0, _events.Count, "Locking every slot must never fire OnInventoryChanged.");
        }

        // -----------------------------------------------------------------------
        // Pickup skips a locked partial stack (Story 002 guard, testable for the
        // first time now that a lock-mutation API exists).
        // -----------------------------------------------------------------------

        [Test]
        public void Pickup_LockedPartialStack_SkippedInFavorOfNextUnlockedPartialStack_AC_Story002Guard()
        {
            // Arrange — slot 0 HP Potion 50/99 locked, slot 1 HP Potion 40/99 unlocked.
            _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 50);
            _inventory.SeedSlotForTesting(Player, 1, HPPotionItemId, 40);
            _inventory.LockSlot(Player, 0);
            _events.Clear(); // discard the (empty) event list from locking; locking fires nothing anyway.

            // Act
            var result = _inventory.Pickup(Player, HPPotionItemId, 10);

            // Assert
            Assert.IsTrue(result.Success);
            AssertSlot(0, HPPotionItemId, 50);
            AssertSlot(1, HPPotionItemId, 50);
            Assert.AreEqual(1, _events.Count);
            Assert.AreEqual(1, _events[0].Length, "Only the unlocked slot 1 is topped up; the locked slot 0 is never touched.");
            AssertEntry(_events[0][0], 1, HPPotionItemId, 50);
        }

        // -----------------------------------------------------------------------
        // Re-registration clears locks (RegisterCharacter resets all locks).
        // -----------------------------------------------------------------------

        [Test]
        public void RegisterCharacter_ReRegisteredWithLockedSlot_AllLocksCleared()
        {
            // Arrange
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, 3);

            // Act
            _inventory.RegisterCharacter(Player);

            // Assert
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
                Assert.IsFalse(_inventory.IsSlotLocked(Player, i), $"Slot {i} must be unlocked after re-registration.");
        }

        // -----------------------------------------------------------------------
        // Re-entrancy guard — every mutator throws when called from an
        // OnInventoryChanged subscriber (ADR-010 mutation-seam contract).
        // -----------------------------------------------------------------------

        [Test]
        public void LockSlot_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException()
        {
            // Arrange — a pickup into slot 0 triggers dispatch; the handler tries to lock it.
            _inventory.OnInventoryChanged += _ => _inventory.LockSlot(Player, 0);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(0, HPPotionItemId, 1);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 0), "The re-entrant LockSlot must not have set the lock.");
        }

        [Test]
        public void UnlockSlot_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException()
        {
            // Arrange — slot 3 locked; a pickup into slot 0 triggers dispatch; the handler tries to unlock slot 3.
            _inventory.SeedSlotForTesting(Player, 3, BronzeSwordItemId, 1);
            _inventory.LockSlot(Player, 3);
            _inventory.OnInventoryChanged += _ => _inventory.UnlockSlot(Player, 3);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            Assert.IsTrue(_inventory.IsSlotLocked(Player, 3), "The re-entrant UnlockSlot must not have cleared the lock.");
        }

        [Test]
        public void RemoveItem_CalledFromOnInventoryChangedSubscriber_ThrowsInvalidOperationException()
        {
            // Arrange — a pickup into slot 0 triggers dispatch; the handler tries to remove it.
            _inventory.OnInventoryChanged += _ => _inventory.RemoveItem(Player, 0);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.Pickup(Player, HPPotionItemId, 1));
            AssertSlot(0, HPPotionItemId, 1);
        }
    }
}

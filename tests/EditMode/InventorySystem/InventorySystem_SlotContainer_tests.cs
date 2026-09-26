using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.InventorySystem
{
    /// <summary>
    /// EditMode unit tests for Inventory System Story 001 — slot container, core types, and the
    /// read API (<c>GetSlot</c>, <c>IsFull</c>, <c>FilledSlots</c>, <c>HasFreeSlot</c>,
    /// <c>IsSlotLocked</c>, <c>HasItem</c>) plus the <c>OnInventoryChanged</c> Tier 2 broadcast
    /// event contract (ADR-010 Decision 3).
    /// </summary>
    [TestFixture]
    internal sealed class InventorySystem_SlotContainer_Tests
    {
        private static readonly CharacterID Player = new CharacterID(1001u);
        private static readonly CharacterID OtherPlayer = new CharacterID(1002u);
        private static readonly ItemID HPPotionItemId = new ItemID(3001u);
        private static readonly ItemID BronzeSwordItemId = new ItemID(3002u);

        private InventoryService _inventory;
        private List<CapturedDispatch> _capturedEvents;

        /// <summary>
        /// Values copied out of an <see cref="InventoryChangedEventArgs"/> during the synchronous
        /// dispatch. The args themselves must never be retained past the handler call (their
        /// buffer is service-owned and reused), so the recorder snapshots everything a test needs,
        /// exactly as a real subscriber (network sync, persistence dirty-tracking) must.
        /// </summary>
        private sealed class CapturedDispatch
        {
            public CharacterID CharacterId;
            public int Count;
            public readonly List<SlotChange> ViaIndexer = new List<SlotChange>();
            public readonly List<SlotChange> ViaForeach = new List<SlotChange>();
            public bool IndexerPastCountThrew;
            public bool EnumeratorCurrentPastEndThrew;
        }

        /// <summary>Distinct exception type so tests can tell a subscriber failure apart from the service's own guards.</summary>
        private sealed class SubscriberFailureException : Exception
        {
            public SubscriberFailureException() : base("Simulated subscriber failure.") { }
        }

        // Second recorder — proves multiple subscribers observe the same dispatch.
        private List<CapturedDispatch> _secondRecorderEvents;

        // No [TearDown] unsubscribe needed: SetUp constructs a fresh InventoryService per test,
        // so no subscription outlives its test.
        [SetUp]
        public void SetUp()
        {
            _inventory = new InventoryService();
            _capturedEvents = new List<CapturedDispatch>();
            _secondRecorderEvents = new List<CapturedDispatch>();
        }

        private void OnInventoryChangedRecorder(InventoryChangedEventArgs args) => _capturedEvents.Add(Capture(args));

        private void OnInventoryChangedSecondRecorder(InventoryChangedEventArgs args) => _secondRecorderEvents.Add(Capture(args));

        private void OnInventoryChangedThrowingHandler(InventoryChangedEventArgs args) => throw new SubscriberFailureException();

        /// <summary>
        /// Copies everything a test needs out of <paramref name="args"/> during the synchronous
        /// dispatch, including probes of the indexer and enumerator guards past <c>Count</c>.
        /// </summary>
        private static CapturedDispatch Capture(InventoryChangedEventArgs args)
        {
            var captured = new CapturedDispatch { CharacterId = args.CharacterID, Count = args.Count };
            for (int i = 0; i < args.Count; i++)
                captured.ViaIndexer.Add(args[i]);
            foreach (var change in args)
                captured.ViaForeach.Add(change);
            try
            {
                var _ = args[args.Count];
            }
            catch (ArgumentOutOfRangeException)
            {
                captured.IndexerPastCountThrew = true;
            }

            var enumerator = args.GetEnumerator();
            while (enumerator.MoveNext()) { }
            try
            {
                var _ = enumerator.Current;
            }
            catch (InvalidOperationException)
            {
                captured.EnumeratorCurrentPastEndThrew = true;
            }
            return captured;
        }

        private void OnInventoryChangedSeedingHandler(InventoryChangedEventArgs args)
        {
            _inventory.SeedSlotForTesting(args.CharacterID, 0, HPPotionItemId, 1);
        }

        private void OnInventoryChangedReentrantHandler(InventoryChangedEventArgs args)
        {
            _inventory.EmitInventoryChanged(args.CharacterID);
        }

        // -----------------------------------------------------------------------
        // Rule 1.1 — a newly created character inventory has exactly 20 slots
        // (indices 0-19); every slot reads ItemID.Invalid, Quantity = 0.
        // INVENTORY_SLOT_COUNT == 20 is a named constant.
        // -----------------------------------------------------------------------

        [Test]
        public void GetSlot_NewInventory_AllTwentySlotsAreEmpty_AC_Rule1_1()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Assert
            Assert.AreEqual(20, InventoryConstants.INVENTORY_SLOT_COUNT, "INVENTORY_SLOT_COUNT must be the named constant 20 (GDD Rule 1.1).");
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                var slot = _inventory.GetSlot(Player, i);
                Assert.AreEqual(ItemID.Invalid, slot.ItemId, $"Slot {i} must be ItemID.Invalid on a new inventory.");
                Assert.AreEqual(0, slot.Quantity, $"Slot {i} must have Quantity 0 on a new inventory.");
                Assert.IsTrue(slot.IsEmpty, $"Slot {i} must report IsEmpty on a new inventory.");
            }
        }

        [Test]
        public void GetSlot_IndexNineteen_IsValidAndReadsEmpty_AC_Rule1_1_EdgeCase()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act
            var slot = _inventory.GetSlot(Player, 19);

            // Assert
            Assert.AreEqual(InventorySlot.Empty.ItemId, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
        }

        [Test]
        public void GetSlot_IndexTwenty_ReturnsEmptyAndLogsError_NoStateChange_AC_Rule1_TR_inv_001()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] GetSlot: slotIndex 20 is out of range \[0, 20\)\."));
            var slot = _inventory.GetSlot(Player, 20);

            // Assert
            Assert.AreEqual(InventorySlot.Empty.ItemId, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
            // No state change: a subsequent valid read is unaffected.
            Assert.IsTrue(_inventory.GetSlot(Player, 0).IsEmpty);
        }

        [Test]
        public void GetSlot_IndexNegativeOne_ReturnsEmptyAndLogsError_NoStateChange_AC_Rule1_TR_inv_001()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] GetSlot: slotIndex -1 is out of range \[0, 20\)\."));
            var slot = _inventory.GetSlot(Player, -1);

            // Assert
            Assert.AreEqual(InventorySlot.Empty.ItemId, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
            Assert.IsTrue(_inventory.GetSlot(Player, 0).IsEmpty);
        }

        // -----------------------------------------------------------------------
        // F-INV-3 — IsFull / FilledSlots for 0, 19, and 20 occupied slots.
        // -----------------------------------------------------------------------

        [Test]
        public void IsFullAndFilledSlots_ZeroOccupied_ReturnsFalseAndZero_AC_F_INV_3()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Assert
            Assert.IsFalse(_inventory.IsFull(Player));
            Assert.AreEqual(0, _inventory.FilledSlots(Player));
        }

        [Test]
        public void IsFullAndFilledSlots_NineteenOccupied_ReturnsFalseAndNineteen_AC_F_INV_3()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            for (int i = 0; i < 19; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(_inventory.IsFull(Player));
            Assert.AreEqual(19, _inventory.FilledSlots(Player));
        }

        [Test]
        public void IsFullAndFilledSlots_TwentyOccupiedIncludingPartialStacks_ReturnsTrueAndTwenty_AC_F_INV_3()
        {
            // Arrange — mix of full stacks, partial consumable stacks, and equipment (StackLimit=1)
            // to prove IsFull only cares about Quantity > 0, not stack fullness (F-INV-3 note).
            _inventory.RegisterCharacter(Player);
            for (int i = 0; i < 18; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 99);
            _inventory.SeedSlotForTesting(Player, 18, HPPotionItemId, 1); // partial stack
            _inventory.SeedSlotForTesting(Player, 19, BronzeSwordItemId, 1);

            // Assert
            Assert.IsTrue(_inventory.IsFull(Player));
            Assert.AreEqual(20, _inventory.FilledSlots(Player));
        }

        // -----------------------------------------------------------------------
        // HasFreeSlot — gap at index 0, gap at index 19, and fully occupied.
        // -----------------------------------------------------------------------

        [Test]
        public void HasFreeSlot_GapAtIndexZero_ReturnsTrue_AC_Rule8_23()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            for (int i = 1; i < 20; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(_inventory.HasFreeSlot(Player));
        }

        [Test]
        public void HasFreeSlot_GapAtIndexNineteen_ReturnsTrue_AC_Rule8_23()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            for (int i = 0; i < 19; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 1);

            // Assert
            Assert.IsTrue(_inventory.HasFreeSlot(Player));
        }

        [Test]
        public void HasFreeSlot_AllTwentyOccupied_ReturnsFalse_AC_Rule8_23()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            for (int i = 0; i < 20; i++)
                _inventory.SeedSlotForTesting(Player, i, HPPotionItemId, 1);

            // Assert
            Assert.IsFalse(_inventory.HasFreeSlot(Player));
        }

        // -----------------------------------------------------------------------
        // IsSlotLocked default — all 20 slots false on a new inventory.
        // -----------------------------------------------------------------------

        [Test]
        public void IsSlotLocked_NewInventory_AllTwentySlotsAreFalse_AC_Rule5_14()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Assert
            for (int i = 0; i < 20; i++)
                Assert.IsFalse(_inventory.IsSlotLocked(Player, i), $"Slot {i} must be unlocked on a new inventory.");
        }

        [Test]
        public void IsSlotLocked_OutOfRangeIndex_ReturnsFalseAndLogsError_AC_Rule5_14_EdgeCase()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act / Assert — index 20
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] IsSlotLocked: slotIndex 20 is out of range \[0, 20\)\."));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 20));

            // Act / Assert — index -1
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] IsSlotLocked: slotIndex -1 is out of range \[0, 20\)\."));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, -1));
        }

        // -----------------------------------------------------------------------
        // HasItem — HP Potion in slot 7, a different item, and an empty inventory.
        // -----------------------------------------------------------------------

        [Test]
        public void HasItem_ItemPresentInSlotSeven_ReturnsTrue_AC_ConsumableUseReadSurface()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 5);

            // Assert
            Assert.IsTrue(_inventory.HasItem(Player, HPPotionItemId));
        }

        [Test]
        public void HasItem_DifferentItemNotPresent_ReturnsFalse_AC_ConsumableUseReadSurface()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.SeedSlotForTesting(Player, 7, HPPotionItemId, 5);

            // Assert
            Assert.IsFalse(_inventory.HasItem(Player, BronzeSwordItemId));
        }

        [Test]
        public void HasItem_EmptyInventory_ReturnsFalse_AC_ConsumableUseReadSurface()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Assert
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId));
        }

        [Test]
        public void HasItem_ItemIdInvalid_ReturnsFalseWithNoLog_AC_ConsumableUseReadSurface()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Assert — ItemID.Invalid can never be "held"; this is not a caller bug, so no log is expected.
            Assert.IsFalse(_inventory.HasItem(Player, ItemID.Invalid));
        }

        // -----------------------------------------------------------------------
        // InventoryChangedEvent shape — 2 changes, including an empty-slot change,
        // read via indexer and via allocation-free foreach.
        // -----------------------------------------------------------------------

        [Test]
        public void OnInventoryChanged_TwoChangesEmitted_SubscriberReceivesCharacterIdCountAndEntriesInOrder_AC_InventoryChangedEventContract()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act
            _inventory.RecordSlotChange(Player, 2, HPPotionItemId, 99);
            _inventory.RecordSlotChange(Player, 5, ItemID.Invalid, 0); // edge case: empty-slot change
            _inventory.EmitInventoryChanged(Player);

            // Assert
            Assert.AreEqual(1, _capturedEvents.Count, "OnInventoryChanged must fire exactly once for a single EmitInventoryChanged call.");
            var args = _capturedEvents[0];
            Assert.AreEqual(Player, args.CharacterId);
            Assert.AreEqual(2, args.Count);
            var entries = args.ViaIndexer;

            Assert.AreEqual(2, entries[0].SlotIndex);
            Assert.AreEqual(HPPotionItemId, entries[0].ItemId);
            Assert.AreEqual(99, entries[0].Quantity);

            Assert.AreEqual(5, entries[1].SlotIndex);
            Assert.AreEqual(ItemID.Invalid, entries[1].ItemId);
            Assert.AreEqual(0u, entries[1].ItemId.RawValue, "Edge case: an empty-slot change reports itemId == 0.");
            Assert.AreEqual(0, entries[1].Quantity, "Edge case: an empty-slot change reports quantity == 0.");
        }

        [Test]
        public void OnInventoryChanged_ForeachIteration_YieldsSameEntriesAsIndexer_AC_InventoryChangedEventContract()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act
            _inventory.RecordSlotChange(Player, 2, HPPotionItemId, 99);
            _inventory.RecordSlotChange(Player, 5, ItemID.Invalid, 0);
            _inventory.EmitInventoryChanged(Player);

            // Assert
            var args = _capturedEvents[0];
            var viaIndexer = args.ViaIndexer;
            var viaForeach = args.ViaForeach;

            Assert.AreEqual(args.Count, viaForeach.Count);
            for (int i = 0; i < args.Count; i++)
            {
                Assert.AreEqual(viaIndexer[i].SlotIndex, viaForeach[i].SlotIndex, $"Entry {i} slotIndex must match between foreach and indexer.");
                Assert.AreEqual(viaIndexer[i].ItemId, viaForeach[i].ItemId, $"Entry {i} itemId must match between foreach and indexer.");
                Assert.AreEqual(viaIndexer[i].Quantity, viaForeach[i].Quantity, $"Entry {i} quantity must match between foreach and indexer.");
            }
        }

        [Test]
        public void InventoryChangedEventArgs_IndexerPastCount_ThrowsArgumentOutOfRangeException_AC_InventoryChangedEventContract()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act
            _inventory.RecordSlotChange(Player, 2, HPPotionItemId, 99);
            _inventory.EmitInventoryChanged(Player);
            // Assert — the recorder probed args[Count] (index 1 here) during dispatch.
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(1, _capturedEvents[0].Count);
            Assert.IsTrue(_capturedEvents[0].IndexerPastCountThrew,
                "Indexer at i == Count must throw ArgumentOutOfRangeException rather than read a stale buffer entry.");
        }

        [Test]
        public void InventoryChangedEventArgs_EnumeratorCurrentAfterEnd_ThrowsInvalidOperationException_AC_InventoryChangedEventContract()
        {
            // Arrange — a prior 2-change dispatch leaves a stale entry at buffer index 1.
            _inventory.RegisterCharacter(Player);
            _inventory.RecordSlotChange(Player, 2, HPPotionItemId, 99);
            _inventory.RecordSlotChange(Player, 5, BronzeSwordItemId, 1);
            _inventory.EmitInventoryChanged(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act — a 1-change dispatch; the recorder reads Current after MoveNext returned false.
            _inventory.RecordSlotChange(Player, 3, HPPotionItemId, 4);
            _inventory.EmitInventoryChanged(Player);

            // Assert
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.IsTrue(_capturedEvents[0].EnumeratorCurrentPastEndThrew,
                "Enumerator.Current past Count must throw rather than return the stale entry from the prior dispatch.");
        }

        // -----------------------------------------------------------------------
        // InventoryChangedEvent re-entrancy guard.
        // -----------------------------------------------------------------------

        [Test]
        public void EmitInventoryChanged_SubscriberReEmitsDuringDispatch_ThrowsInvalidOperationExceptionToCaller_AC_InventoryChangedEventContract()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedReentrantHandler;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 10);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.EmitInventoryChanged(Player));
        }

        [Test]
        public void EmitInventoryChanged_AfterReentrancyException_DispatchFlagAndChangeCountAreReset_AC_InventoryChangedEventContract()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedReentrantHandler;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 10);
            Assert.Throws<InvalidOperationException>(() => _inventory.EmitInventoryChanged(Player));

            _inventory.OnInventoryChanged -= OnInventoryChangedReentrantHandler;
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act — if _isDispatching had not been reset by the finally block, this call would
            // itself throw InvalidOperationException. If _changeCount had not been reset, Count
            // below would be 2 (stale entry + this fresh one), not 1.
            _inventory.RecordSlotChange(Player, 1, BronzeSwordItemId, 1);
            _inventory.EmitInventoryChanged(Player);

            // Assert
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(1, _capturedEvents[0].Count, "A fresh emit after the reentrancy exception must report Count == 1, not a stale accumulated count.");
        }

        [Test]
        public void SeedSlotForTesting_CalledDuringDispatch_ThrowsInvalidOperationException_AC_InventoryChangedEventContract()
        {
            // Arrange — a subscriber that attempts a different mutation seam (not just Emit)
            // during dispatch must also trip the re-entrancy guard (design note: ThrowIfDispatching
            // is shared by Emit, RecordSlotChange, and SeedSlotForTesting).
            _inventory.RegisterCharacter(Player);
            _inventory.OnInventoryChanged += OnInventoryChangedSeedingHandler;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 10);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.EmitInventoryChanged(Player));
        }

        // -----------------------------------------------------------------------
        // Unregistered-character read defaults.
        // -----------------------------------------------------------------------

        [Test]
        public void GetSlot_UnregisteredCharacter_ReturnsEmptyAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] GetSlot: .*is not a registered character"));
            var slot = _inventory.GetSlot(Player, 0);
            Assert.AreEqual(InventorySlot.Empty.ItemId, slot.ItemId);
            Assert.AreEqual(0, slot.Quantity);
        }

        [Test]
        public void IsFull_UnregisteredCharacter_ReturnsFalseAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] IsFull: .*is not a registered character"));
            Assert.IsFalse(_inventory.IsFull(Player));
        }

        [Test]
        public void FilledSlots_UnregisteredCharacter_ReturnsZeroAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] FilledSlots: .*is not a registered character"));
            Assert.AreEqual(0, _inventory.FilledSlots(Player));
        }

        [Test]
        public void HasFreeSlot_UnregisteredCharacter_ReturnsFalseAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] HasFreeSlot: .*is not a registered character"));
            Assert.IsFalse(_inventory.HasFreeSlot(Player));
        }

        [Test]
        public void IsSlotLocked_UnregisteredCharacter_ReturnsFalseAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] IsSlotLocked: .*is not a registered character"));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 0));
        }

        [Test]
        public void HasItem_UnregisteredCharacter_ReturnsFalseAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[InventoryService] HasItem: .*is not a registered character"));
            Assert.IsFalse(_inventory.HasItem(Player, HPPotionItemId));
        }

        // -----------------------------------------------------------------------
        // RegisterCharacter bootstrap-seam semantics: re-registering resets to empty.
        // -----------------------------------------------------------------------

        [Test]
        public void RegisterCharacter_CalledAgainOnSameCharacter_ResetsInventoryToEmpty()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            _inventory.SeedSlotForTesting(Player, 3, HPPotionItemId, 10);
            Assert.AreEqual(1, _inventory.FilledSlots(Player), "Precondition: one slot seeded before re-registration.");

            // Act
            _inventory.RegisterCharacter(Player);

            // Assert
            Assert.AreEqual(0, _inventory.FilledSlots(Player), "Re-registering must reset the inventory to fully empty.");
            Assert.IsTrue(_inventory.GetSlot(Player, 3).IsEmpty);
            Assert.IsFalse(_inventory.IsSlotLocked(Player, 3), "Re-registering must also reset all locks.");
        }

        // -----------------------------------------------------------------------
        // SeedSlotForTesting phantom-slot invariant enforcement.
        // -----------------------------------------------------------------------

        [Test]
        public void SeedSlotForTesting_ValidItemWithZeroQuantity_ThrowsArgumentException()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.SeedSlotForTesting(Player, 0, HPPotionItemId, 0));
        }

        [Test]
        public void SeedSlotForTesting_InvalidItemWithPositiveQuantity_ThrowsArgumentException()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);

            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.SeedSlotForTesting(Player, 0, ItemID.Invalid, 5));
        }

        // -----------------------------------------------------------------------
        // RecordSlotChange overflow guard.
        // -----------------------------------------------------------------------

        [Test]
        public void RecordSlotChange_MoreThanTwentyChanges_ThrowsInvalidOperationException()
        {
            // Arrange
            _inventory.RegisterCharacter(Player);
            for (int i = 0; i < 20; i++)
                _inventory.RecordSlotChange(Player, i, HPPotionItemId, 1);

            // Act / Assert — the 21st call overflows the INVENTORY_SLOT_COUNT-capacity buffer.
            Assert.Throws<InvalidOperationException>(() => _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1));
        }

        // -----------------------------------------------------------------------
        // RecordSlotChange validation — same invariants as SeedSlotForTesting.
        // -----------------------------------------------------------------------

        [TestCase(20)]
        [TestCase(-1)]
        public void RecordSlotChange_OutOfRangeSlotIndex_ThrowsArgumentOutOfRangeException(int slotIndex)
        {
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => _inventory.RecordSlotChange(Player, slotIndex, HPPotionItemId, 1));
        }

        [Test]
        public void RecordSlotChange_NegativeQuantity_ThrowsArgumentOutOfRangeException()
        {
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => _inventory.RecordSlotChange(Player, 0, HPPotionItemId, -1));
        }

        [Test]
        public void RecordSlotChange_ValidItemWithZeroQuantity_ThrowsArgumentException()
        {
            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 0));
        }

        [Test]
        public void RecordSlotChange_InvalidItemWithPositiveQuantity_ThrowsArgumentException()
        {
            // Act / Assert
            Assert.Throws<ArgumentException>(() => _inventory.RecordSlotChange(Player, 0, ItemID.Invalid, 5));
        }

        // -----------------------------------------------------------------------
        // Pending changes are bound to one character (no cross-character leak).
        // -----------------------------------------------------------------------

        [Test]
        public void RecordSlotChange_DifferentCharacterWhileChangesPending_ThrowsInvalidOperationException()
        {
            // Arrange
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.RecordSlotChange(OtherPlayer, 1, HPPotionItemId, 1));
        }

        [Test]
        public void EmitInventoryChanged_CharacterMismatch_ThrowsAndDiscardsPendingChanges()
        {
            // Arrange
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1);

            // Act
            Assert.Throws<InvalidOperationException>(() => _inventory.EmitInventoryChanged(OtherPlayer));
            _inventory.RecordSlotChange(OtherPlayer, 4, BronzeSwordItemId, 1);
            _inventory.EmitInventoryChanged(OtherPlayer);

            // Assert — nothing fired for the mismatched emit; Player's stale entry never leaked.
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(OtherPlayer, _capturedEvents[0].CharacterId);
            Assert.AreEqual(1, _capturedEvents[0].Count);
            Assert.AreEqual(4, _capturedEvents[0].ViaIndexer[0].SlotIndex);
        }

        [Test]
        public void DiscardPendingChanges_AfterRecording_NextDispatchContainsOnlyFreshChanges()
        {
            // Arrange — an aborted mutation for Player.
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1);
            _inventory.RecordSlotChange(Player, 1, HPPotionItemId, 1);

            // Act
            _inventory.DiscardPendingChanges();
            _inventory.RecordSlotChange(OtherPlayer, 7, BronzeSwordItemId, 1);
            _inventory.EmitInventoryChanged(OtherPlayer);

            // Assert
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(OtherPlayer, _capturedEvents[0].CharacterId);
            Assert.AreEqual(1, _capturedEvents[0].Count);
            Assert.AreEqual(7, _capturedEvents[0].ViaIndexer[0].SlotIndex);
        }

        [Test]
        public void DiscardPendingChanges_CalledDuringDispatch_ThrowsInvalidOperationException()
        {
            // Arrange
            _inventory.OnInventoryChanged += OnInventoryChangedDiscardingHandler;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _inventory.EmitInventoryChanged(Player));
        }

        private void OnInventoryChangedDiscardingHandler(InventoryChangedEventArgs args) => _inventory.DiscardPendingChanges();

        // -----------------------------------------------------------------------
        // Emit edge cases: zero changes, throwing subscriber, multiple subscribers.
        // -----------------------------------------------------------------------

        [Test]
        public void EmitInventoryChanged_NoPendingChanges_FiresNoEvent()
        {
            // Arrange
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;

            // Act
            _inventory.EmitInventoryChanged(Player);

            // Assert
            Assert.AreEqual(0, _capturedEvents.Count, "A mutation that changed nothing must broadcast nothing.");
        }

        [Test]
        public void EmitInventoryChanged_SubscriberThrows_ExceptionPropagatesAndDispatchStateIsReset()
        {
            // Arrange
            _inventory.OnInventoryChanged += OnInventoryChangedThrowingHandler;
            _inventory.RecordSlotChange(Player, 0, HPPotionItemId, 1);
            _inventory.RecordSlotChange(Player, 1, HPPotionItemId, 1);

            // Act
            Assert.Throws<SubscriberFailureException>(() => _inventory.EmitInventoryChanged(Player));
            _inventory.OnInventoryChanged -= OnInventoryChangedThrowingHandler;
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;
            _inventory.RecordSlotChange(Player, 2, BronzeSwordItemId, 1);
            _inventory.EmitInventoryChanged(Player);

            // Assert — not stuck dispatching, and no stale entries from the failed dispatch.
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(1, _capturedEvents[0].Count);
            Assert.AreEqual(2, _capturedEvents[0].ViaIndexer[0].SlotIndex);
        }

        [Test]
        public void EmitInventoryChanged_TwoSubscribers_BothReceiveIdenticalEntries()
        {
            // Arrange
            _inventory.OnInventoryChanged += OnInventoryChangedRecorder;
            _inventory.OnInventoryChanged += OnInventoryChangedSecondRecorder;
            _inventory.RecordSlotChange(Player, 2, HPPotionItemId, 99);
            _inventory.RecordSlotChange(Player, 5, ItemID.Invalid, 0);

            // Act
            _inventory.EmitInventoryChanged(Player);

            // Assert
            Assert.AreEqual(1, _capturedEvents.Count);
            Assert.AreEqual(1, _secondRecorderEvents.Count);
            var first = _capturedEvents[0];
            var second = _secondRecorderEvents[0];
            Assert.AreEqual(first.CharacterId, second.CharacterId);
            Assert.AreEqual(2, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first.ViaIndexer[i].SlotIndex, second.ViaIndexer[i].SlotIndex);
                Assert.AreEqual(first.ViaIndexer[i].ItemId, second.ViaIndexer[i].ItemId);
                Assert.AreEqual(first.ViaIndexer[i].Quantity, second.ViaIndexer[i].Quantity);
            }
        }
    }
}

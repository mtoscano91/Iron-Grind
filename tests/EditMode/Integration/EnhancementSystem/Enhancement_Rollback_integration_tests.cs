using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// EditMode integration tests for Enhancement System Story 005: rollback and the pending window
    /// (design/gdd/enhancement-system.md CR-ENH-7, CR-ENH-11, CR-ENH-15 Rollback, EC-ENH-6;
    /// AC-ENH-7, AC-ENH-23, AC-ENH-34, AC-ENH-35). Uses a real <see cref="InventoryService"/> behind a
    /// recording decorator, a scripted random source and an always-active NPC session stub.
    /// "Pending" means <c>BeginAttempt</c> returned a pending attempt and neither
    /// <c>CompleteAttempt</c> nor <c>RollBackAttempt</c> has run.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_Rollback_Integration_Tests
    {
        private const uint RAW_PLAYER = 1001u;
        private const uint RAW_OTHER_PLAYER = 1002u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int HIGHER_ITEM_SLOT = 3;
        private const int EMPTY_SLOT = 9;

        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_THREE = 3;
        private const byte LEVEL_FOUR = 4;
        private const int STACK_OF_ONE = 1;
        private const int STACK_OF_TWO = 2;
        private const int STACK_OF_THREE = 3;

        // Draws. P_s[2] = 0.85, P_s[4] = 0.65: DRAW_SUCCESS is a success at any level, the others destroy.
        private const double DRAW_SUCCESS = 0.00;
        private const double DRAW_DESTRUCTION_AT_LEVEL_TWO = 0.90;
        private const double DRAW_DESTRUCTION_AT_LEVEL_FOUR = 0.99;

        private const string WRITE_FAILED_PATTERN = @"^\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private const string ITEM_RESTORE_FAILED_PATTERN = @"^\[EnhancementService\] CriticalEnhancementRollbackFailed: item restore failed";
        private const string SCROLL_RESTORE_FAILED_PATTERN = @"^\[EnhancementService\] CriticalEnhancementRollbackFailed: scroll restore failed";

        private static readonly CharacterID Player = new CharacterID(RAW_PLAYER);
        private static readonly CharacterID OtherPlayer = new CharacterID(RAW_OTHER_PLAYER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private RecordingInventoryDecorator _recording;
        private StubNpcSessions _sessions;
        private ScriptedRandom _random;
        private EnhancementService _service;
        private List<ItemDefinition> _definitions;
        private int _inventoryFullEvents;

        // Throwing subscriber state (exception-during-rollback tests): 0 means disarmed.
        private int _throwOnEventNumber;
        private int _eventsSinceArmed;

        [SetUp]
        public void SetUp()
        {
            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(BRONZE_SCROLL_ID, "Bronze Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Bronze)),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);
            _inventory.RegisterCharacter(OtherPlayer);

            _inventoryFullEvents = 0;
            _inventory.OnInventoryFull += _ => _inventoryFullEvents++;

            _throwOnEventNumber = 0;
            _eventsSinceArmed = 0;

            _recording = new RecordingInventoryDecorator(_inventory);
            _sessions = new StubNpcSessions();
            _random = new ScriptedRandom();
            _service = new EnhancementService(_recording, _itemDatabase, EnhancementConfig.Default, _sessions, _random);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var definition in _definitions)
                Object.DestroyImmediate(definition);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void SeedSword(CharacterID who, int slot, byte level)
        {
            _inventory.SeedSlotForTesting(who, slot, BronzeSwordId, 1, level);
        }

        private void SeedScrolls(CharacterID who, int slot, int quantity)
        {
            _inventory.SeedSlotForTesting(who, slot, BronzeScrollId, quantity);
        }

        // Valid baseline for Player: Bronze sword in ITEM_SLOT, Bronze scrolls in SCROLL_SLOT.
        private void SeedBronzeRequest(byte level, int scrollQuantity)
        {
            SeedSword(Player, ITEM_SLOT, level);
            SeedScrolls(Player, SCROLL_SLOT, scrollQuantity);
        }

        private EnhancementAttemptStart Begin(double draw)
        {
            _random.Enqueue(draw);
            return _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);
        }

        // Begins an attempt and asserts it is pending with the expected outcome.
        private void BeginPending(double draw, EnhancementOutcome expected)
        {
            var start = Begin(draw);
            Assert.IsTrue(start.IsPending, "Arrange: the attempt must be pending.");
            Assert.AreEqual(expected, start.Outcome, "Arrange: pending outcome.");
        }

        // Every critical error must name the character, the item, its previous level and the scroll
        // (Story 005 criteria), so each expectation pins them after the message prefix.
        private static Regex WithIdentifiers(string prefixPattern)
        {
            return new Regex(
                prefixPattern + ".*" + Regex.Escape(Player.ToString())
                + ".*item " + Regex.Escape(BronzeSwordId.ToString())
                + @".*level \d+"
                + ".*scroll " + Regex.Escape(BronzeScrollId.ToString()));
        }

        private static void ExpectWriteFailedLog()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, WithIdentifiers(WRITE_FAILED_PATTERN));
        }

        private static void ExpectItemRestoreFailedLog()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, WithIdentifiers(ITEM_RESTORE_FAILED_PATTERN));
        }

        private static void ExpectScrollRestoreFailedLog()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, WithIdentifiers(SCROLL_RESTORE_FAILED_PATTERN));
        }

        private void AssertSlotEmpty(CharacterID who, int slot, string label)
        {
            var s = _inventory.GetSlot(who, slot);
            Assert.AreEqual(ItemID.Invalid, s.ItemId, label + " ItemId.");
            Assert.AreEqual(0, s.Quantity, label + " Quantity.");
            Assert.AreEqual(0, s.EnhancementLevel, label + " EnhancementLevel.");
        }

        private int CountSwords(CharacterID who)
        {
            int count = 0;
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_inventory.GetSlot(who, i).ItemId == BronzeSwordId)
                    count += _inventory.GetSlot(who, i).Quantity;
            }
            return count;
        }

        private int CountScrolls(CharacterID who)
        {
            int count = 0;
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_inventory.GetSlot(who, i).ItemId == BronzeScrollId)
                    count += _inventory.GetSlot(who, i).Quantity;
            }
            return count;
        }

        // Slot index of the (single) sword, or -1.
        private int FindSword(CharacterID who)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_inventory.GetSlot(who, i).ItemId == BronzeSwordId)
                    return i;
            }
            return -1;
        }

        // Comparable form of the character's snapshot: (slot, raw item id, quantity, level) per entry.
        private List<(int Slot, uint Item, int Quantity, byte Level)> Capture(CharacterID who)
        {
            var result = new List<(int Slot, uint Item, int Quantity, byte Level)>();
            foreach (var entry in _inventory.ExportSnapshot(who).Slots)
                result.Add((entry.SlotIndex, entry.ItemId, entry.Quantity, entry.EnhancementLevel));
            return result;
        }

        private InventorySnapshotEntry? FindEntry(CharacterID who, int slot)
        {
            foreach (var entry in _inventory.ExportSnapshot(who).Slots)
            {
                if (entry.SlotIndex == slot)
                    return entry;
            }
            return null;
        }

        // Subscribes (after BeginAttempt) a handler on the real inventory that throws on the n-th event
        // raised after arming.
        private void ArmThrowingSubscriber(int throwOnEventNumber)
        {
            _eventsSinceArmed = 0;
            _throwOnEventNumber = throwOnEventNumber;
            _inventory.OnInventoryChanged += _ =>
            {
                if (_throwOnEventNumber == 0)
                    return;
                _eventsSinceArmed++;
                if (_eventsSinceArmed == _throwOnEventNumber)
                    throw new NotSupportedException("Subscriber failure.");
            };
        }

        // =====================================================================
        // Bag state after a rollback (AC-ENH-34, AC-ENH-23)
        // =====================================================================

        [Test]
        public void RollBackAttempt_AfterSuccessOutcome_ItemBackAtPreviousLevelUnlockedScrollsRestored_AC_ENH_34()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Arrange: pending level.");
            Assert.AreEqual(STACK_OF_TWO, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Arrange: pending scrolls.");
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Slot 0 item.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Slot 0 level.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Slot 0 must be unlocked.");
            Assert.AreEqual(BronzeScrollId, _inventory.GetSlot(Player, SCROLL_SLOT).ItemId, "Slot 1 item.");
            Assert.AreEqual(STACK_OF_THREE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Slot 1 quantity.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(0, _inventoryFullEvents, "OnInventoryFull count.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_AfterDestruction_ItemBackOnceAtPreviousLevelUnlockedScrollsRestored_AC_ENH_23()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_FOUR, STACK_OF_THREE);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_FOUR, EnhancementOutcome.Destruction);
            AssertSlotEmpty(Player, ITEM_SLOT, "Arrange: pending slot 0");
            Assert.AreEqual(STACK_OF_TWO, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Arrange: pending scrolls.");
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            Assert.AreEqual(1, CountSwords(Player), "The item must be in the bag exactly once.");
            int swordSlot = FindSword(Player);
            Assert.GreaterOrEqual(swordSlot, 0, "Sword slot.");
            Assert.AreEqual(LEVEL_FOUR, _inventory.GetSlot(Player, swordSlot).EnhancementLevel, "Restored level.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, swordSlot), "Restored slot must be unlocked.");
            Assert.AreEqual(BronzeScrollId, _inventory.GetSlot(Player, SCROLL_SLOT).ItemId, "Slot 1 item.");
            Assert.AreEqual(STACK_OF_THREE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Slot 1 quantity.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_DestructionEmptiedScrollStackItemInLowerSlot_BothBackInTheirSlots()
        {
            // Arrange: item in slot 0, a stack of one scroll in slot 1.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_ONE);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            AssertSlotEmpty(Player, SCROLL_SLOT, "Arrange: emptied scroll slot");
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Slot 0 item.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Slot 0 level.");
            Assert.AreEqual(BronzeScrollId, _inventory.GetSlot(Player, SCROLL_SLOT).ItemId, "Slot 1 item.");
            Assert.AreEqual(STACK_OF_ONE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Slot 1 quantity.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_DestructionEmptiedScrollStackItemInHigherSlot_ContentsRestoredPositionsMaySwap()
        {
            // Arrange: one scroll in slot 1, item in slot 3. Contents only: the two positions may swap.
            SeedScrolls(Player, SCROLL_SLOT, STACK_OF_ONE);
            SeedSword(Player, HIGHER_ITEM_SLOT, LEVEL_TWO);
            _random.Enqueue(DRAW_DESTRUCTION_AT_LEVEL_TWO);
            var start = _service.BeginAttempt(Player, HIGHER_ITEM_SLOT, SCROLL_SLOT);
            Assert.IsTrue(start.IsPending, "Arrange: the attempt must be pending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, start.Outcome, "Arrange: pending outcome.");
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            Assert.AreEqual(1, CountSwords(Player), "The item must be in the bag exactly once.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, FindSword(Player)).EnhancementLevel, "Restored level.");
            Assert.AreEqual(STACK_OF_ONE, CountScrolls(Player), "Exactly one scroll in total.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Rollback order
        // =====================================================================

        [Test]
        public void RollBackAttempt_AfterSuccessOutcome_CallsSetLevelThenPickupThenUnlock()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "SetEnhancementLevel", "SetEnhancementLevel", "Pickup", "UnlockSlot" },
                _recording.Calls, "Call sequence.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_AfterDestruction_CallsForceInsertThenPickupThenUnlock()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "RemoveItem", "ForceInsert", "Pickup", "UnlockSlot" },
                _recording.Calls, "Call sequence.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Critical log
        // =====================================================================

        [TestCase(DRAW_SUCCESS, EnhancementOutcome.Success)]
        [TestCase(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction)]
        public void RollBackAttempt_EitherOutcome_LogsExactlyOneCriticalWriteFailedError(double draw, EnhancementOutcome expected)
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(draw, expected);
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert: the one expected error is consumed; anything further would fail here.
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // A rollback call fails
        // =====================================================================

        [Test]
        public void RollBackAttempt_ItemRestoreFailsAfterDestruction_LogsBothErrorsStillPicksUpAndUnlocksAndEnds()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            _recording.FailForceInsert = true;
            ExpectItemRestoreFailedLog();
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "RemoveItem", "ForceInsert", "Pickup", "UnlockSlot" },
                _recording.Calls, "Call sequence.");
            Assert.AreEqual(0, CountSwords(Player), "The item was not restored.");
            Assert.AreEqual(STACK_OF_THREE, CountScrolls(Player), "The scroll was restored.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_ScrollRestoreFails_LogsBothErrorsItemRestoredStillUnlocksAndEnds()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            _recording.FailPickup = true;
            ExpectScrollRestoreFailedLog();
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "RemoveItem", "ForceInsert", "Pickup", "UnlockSlot" },
                _recording.Calls, "Call sequence.");
            Assert.AreEqual(1, CountSwords(Player), "The item was restored.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, FindSword(Player)).EnhancementLevel, "Restored level.");
            Assert.AreEqual(STACK_OF_TWO, CountScrolls(Player), "The scroll was not restored.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_LevelRestoreFailsAfterSuccess_LogsBothErrorsScrollRestoredStillUnlocksAndEnds()
        {
            // Arrange: the switch goes on only after BeginAttempt, which itself sets the level.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            _recording.FailSetEnhancementLevel = true;
            ExpectItemRestoreFailedLog();
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "SetEnhancementLevel", "SetEnhancementLevel", "Pickup", "UnlockSlot" },
                _recording.Calls, "Call sequence.");
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "The level was not restored.");
            Assert.AreEqual(STACK_OF_THREE, CountScrolls(Player), "The scroll was restored.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // No result / nothing pending
        // =====================================================================

        [Test]
        public void CompleteAttempt_AfterRollBackAttempt_ThrowsInvalidOperation()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ExpectWriteFailedLog();
            _service.RollBackAttempt(Player);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_NothingPending_ThrowsInvalidOperationAndChangesNothing()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.RollBackAttempt(Player));
            Assert.AreEqual(0, _recording.Calls.Count, "No inventory call may be made.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.AreEqual(STACK_OF_THREE, CountScrolls(Player), "Scrolls unchanged.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_CalledTwice_SecondThrowsInvalidOperation()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ExpectWriteFailedLog();
            _service.RollBackAttempt(Player);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.RollBackAttempt(Player));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_OnlyOtherCharacterPending_ThrowsAndLeavesOtherAttemptPending()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            int callsAfterBegin = _recording.Calls.Count;

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.RollBackAttempt(OtherPlayer));
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "Player's attempt must stay pending.");
            Assert.AreEqual(callsAfterBegin, _recording.Calls.Count, "No inventory call may be made.");
            Assert.IsTrue(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Player's item slot must stay locked.");
            Assert.DoesNotThrow(() => _service.CompleteAttempt(Player), "Player's attempt must still complete.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Pending window (AC-ENH-7, AC-ENH-35)
        // =====================================================================

        [Test]
        public void Move_ItemSlotWhilePendingSuccess_FailsSourceLockedThenSucceedsAfterComplete_AC_ENH_7()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Act
            var blocked = _inventory.Move(Player, ITEM_SLOT, EMPTY_SLOT);

            // Assert: blocked while pending, nothing moved.
            Assert.IsTrue(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot locked while pending.");
            Assert.IsFalse(blocked.Success, "Move.Success.");
            Assert.AreEqual(MoveFailReason.SourceLocked, blocked.Reason, "Move.Reason.");
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Source item unchanged.");
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Source level unchanged.");
            AssertSlotEmpty(Player, EMPTY_SLOT, "Destination slot");

            // Act
            _service.CompleteAttempt(Player);
            var allowed = _inventory.Move(Player, ITEM_SLOT, EMPTY_SLOT);

            // Assert: unlocked, the same move now applies.
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot unlocked after complete.");
            Assert.IsTrue(allowed.Success, "Second Move.Success.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Source slot after move");
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, EMPTY_SLOT).ItemId, "Moved item.");
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, EMPTY_SLOT).EnhancementLevel, "Moved level.");
        }

        [Test]
        public void ExportSnapshot_PendingSuccess_ShowsNewLevelAndOneScrollLeft_AC_ENH_35()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_TWO);

            // Act
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            var itemEntry = FindEntry(Player, ITEM_SLOT);
            var scrollEntry = FindEntry(Player, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(itemEntry.HasValue, "Slot 0 entry must exist.");
            Assert.AreEqual(BRONZE_SWORD_ID, itemEntry.Value.ItemId, "Slot 0 item.");
            Assert.AreEqual(LEVEL_THREE, itemEntry.Value.EnhancementLevel, "Slot 0 level.");
            Assert.IsTrue(scrollEntry.HasValue, "Slot 1 entry must exist.");
            Assert.AreEqual(STACK_OF_ONE, scrollEntry.Value.Quantity, "Slot 1 quantity.");
        }

        [Test]
        public void ExportSnapshot_PendingDestruction_HasNoItemEntryAndOneScrollLeft_AC_ENH_35()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_TWO);

            // Act
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            var itemEntry = FindEntry(Player, ITEM_SLOT);
            var scrollEntry = FindEntry(Player, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(itemEntry.HasValue, "Slot 0 must have no entry.");
            Assert.IsTrue(scrollEntry.HasValue, "Slot 1 entry must exist.");
            Assert.AreEqual(STACK_OF_ONE, scrollEntry.Value.Quantity, "Slot 1 quantity.");
        }

        [TestCase(DRAW_SUCCESS, EnhancementOutcome.Success)]
        [TestCase(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction)]
        public void RollBackAttempt_ItemInLowerSlot_SnapshotEqualsSnapshotBeforeBeginAttempt(double draw, EnhancementOutcome expected)
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            var before = Capture(Player);
            BeginPending(draw, expected);
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(before, Capture(Player), "Snapshot after rollback.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Attempt after rollback / in-progress flag
        // =====================================================================

        [Test]
        public void BeginAttempt_AfterRollBackAttempt_NewAttemptOnSameItemAndScrollIsPending()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ExpectWriteFailedLog();
            _service.RollBackAttempt(Player);

            // Act
            var again = Begin(DRAW_SUCCESS);

            // Assert
            Assert.IsTrue(again.IsPending, "IsPending.");
            Assert.AreEqual(LEVEL_THREE, again.NewLevel, "NewLevel (+2 to +3).");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(2, _random.DrawCount, "Draws.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void IsAttemptInProgress_TrueWhilePendingFalseAfterRollBackAttempt()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "Before BeginAttempt.");
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            bool whilePending = _service.IsAttemptInProgress(Player);
            ExpectWriteFailedLog();

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            Assert.IsTrue(whilePending, "While pending.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "After RollBackAttempt.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Exception during rollback
        // =====================================================================

        [Test]
        public void RollBackAttempt_SubscriberThrowsDuringItemRestore_ScrollStillRestored_PropagatesAndEndsAttemptWithSlotUnlocked()
        {
            // Arrange: the first event raised during the rollback is the level restore.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ArmThrowingSubscriber(1);
            ExpectWriteFailedLog();

            // Act / Assert
            Assert.Throws<NotSupportedException>(() => _service.RollBackAttempt(Player));
            Assert.AreEqual(STACK_OF_THREE, CountScrolls(Player), "The scroll restore must still run after the item restore threw.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player), "Nothing pending to complete.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_SubscriberThrowsDuringScrollRestore_PropagatesAndEndsAttemptWithSlotUnlocked()
        {
            // Arrange: the second event raised during the rollback is the scroll restore.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            ArmThrowingSubscriber(2);
            ExpectWriteFailedLog();

            // Act / Assert
            Assert.Throws<NotSupportedException>(() => _service.RollBackAttempt(Player));
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "The item restore had already run.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player), "Nothing pending to complete.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

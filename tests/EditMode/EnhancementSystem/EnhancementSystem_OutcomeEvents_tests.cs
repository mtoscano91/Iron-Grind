using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Tests.EditMode.Integration.EnhancementSystem;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.EnhancementSystem
{
    /// <summary>
    /// EditMode tests for Enhancement System Story 007: the outcome events and the +9 broadcast trigger
    /// raised by <see cref="EnhancementService.CompleteAttempt"/> (design/gdd/enhancement-system.md
    /// CR-ENH-11, CR-ENH-14, CR-ENH-15 steps 7-9, EC-ENH-8, AC-ENH-18 server side). Uses a real
    /// <see cref="InventoryService"/> behind a recording decorator, a scripted random source and an
    /// always-active NPC session stub.
    /// </summary>
    [TestFixture]
    internal sealed class EnhancementSystem_OutcomeEvents_Tests
    {
        private const uint RAW_PLAYER = 1001u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int SCROLL_QUANTITY = 3;

        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_THREE = 3;
        private const byte LEVEL_EIGHT = 8;
        private const byte LEVEL_NINE = 9;

        // Draws. P_s[2] = 0.85, P_s[8] = 0.12: DRAW_SUCCESS succeeds at any level; the others destroy.
        private const double DRAW_SUCCESS = 0.00;
        private const double DRAW_DESTRUCTION_AT_LEVEL_TWO = 0.90;
        private const double DRAW_DESTRUCTION_AT_LEVEL_EIGHT = 0.99;

        private const int ONE_EVENT = 1;
        private const int NO_EVENTS = 0;
        private const byte EXPECTED_BROADCAST_LEVEL = 9;

        private const string WRITE_FAILED_PATTERN = @"^\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private const string SUBSCRIBER_EXCEPTION_PATTERN = "NotSupportedException";

        private const string LOG_SUCCESS = "Success";
        private const string LOG_DESTRUCTION = "Destruction";
        private const string LOG_BROADCAST = "Broadcast";
        private const string CALL_UNLOCK_SLOT = "UnlockSlot";

        private static readonly CharacterID Player = new CharacterID(RAW_PLAYER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private RecordingInventoryDecorator _recording;
        private StubNpcSessions _sessions;
        private ScriptedRandomProvider _random;
        private EnhancementService _service;
        private List<ItemDefinition> _definitions;

        private List<EnhancementSuccessEventArgs> _successEvents;
        private List<EnhancementDestructionEventArgs> _destructionEvents;
        private List<EnhancementBroadcastEventArgs> _broadcastEvents;

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
                ItemDefinitionBuilder.Build(IRON_SCROLL_ID, "Iron Enhancement Scroll", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE,
                    scrollData: ScrollData.CreateForTesting(GearTier.Iron)),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);

            _recording = new RecordingInventoryDecorator(_inventory);
            _sessions = new StubNpcSessions();
            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_recording, _itemDatabase, EnhancementConfig.Default, _sessions, _random);

            _successEvents = new List<EnhancementSuccessEventArgs>();
            _destructionEvents = new List<EnhancementDestructionEventArgs>();
            _broadcastEvents = new List<EnhancementBroadcastEventArgs>();
            _service.OnEnhancementSuccess += e => _successEvents.Add(e);
            _service.OnEnhancementDestruction += e => _destructionEvents.Add(e);
            _service.OnEnhancementBroadcastLevelReached += e => _broadcastEvents.Add(e);
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

        // Valid baseline: Bronze sword at the given level in ITEM_SLOT, Bronze scrolls in SCROLL_SLOT.
        private void SeedBronzeRequest(byte level)
        {
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, BronzeSwordId, 1, level);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, new ItemID(BRONZE_SCROLL_ID), SCROLL_QUANTITY);
        }

        private EnhancementAttemptStart Begin(double draw)
        {
            _random.EnqueueDouble(draw);
            return _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);
        }

        private void BeginPending(double draw, EnhancementOutcome expected)
        {
            var start = Begin(draw);
            Assert.IsTrue(start.IsPending, "Arrange: the attempt must be pending.");
            Assert.AreEqual(expected, start.Outcome, "Arrange: pending outcome.");
        }

        private void AssertNoEvents(string label)
        {
            Assert.AreEqual(NO_EVENTS, _successEvents.Count, label + " success events.");
            Assert.AreEqual(NO_EVENTS, _destructionEvents.Count, label + " destruction events.");
            Assert.AreEqual(NO_EVENTS, _broadcastEvents.Count, label + " broadcast events.");
        }

        private static void ExpectSubscriberExceptionLog()
        {
            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex(SUBSCRIBER_EXCEPTION_PATTERN));
        }

        private static void ExpectWriteFailedLog()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(WRITE_FAILED_PATTERN));
        }

        // =====================================================================
        // Success and destruction events
        // =====================================================================

        [Test]
        public void CompleteAttempt_SuccessOutcome_RaisesOneSuccessEventWithCharacterItemAndNewLevel()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(ONE_EVENT, _successEvents.Count, "Success events.");
            Assert.AreEqual(Player, _successEvents[0].CharacterId, "CharacterId.");
            Assert.AreEqual(BronzeSwordId, _successEvents[0].ItemId, "ItemId.");
            Assert.AreEqual(LEVEL_THREE, _successEvents[0].NewLevel, "NewLevel.");
            Assert.AreEqual(NO_EVENTS, _destructionEvents.Count, "Destruction events.");
            Assert.AreEqual(NO_EVENTS, _broadcastEvents.Count, "Broadcast events.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_DestructionOutcome_RaisesOneDestructionEventWithCharacterAndItem()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(ONE_EVENT, _destructionEvents.Count, "Destruction events.");
            Assert.AreEqual(Player, _destructionEvents[0].CharacterId, "CharacterId.");
            Assert.AreEqual(BronzeSwordId, _destructionEvents[0].ItemId, "ItemId.");
            Assert.AreEqual(NO_EVENTS, _successEvents.Count, "Success events.");
            Assert.AreEqual(NO_EVENTS, _broadcastEvents.Count, "Broadcast events.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // +9 trigger (AC-ENH-18)
        // =====================================================================

        [Test]
        public void CompleteAttempt_SuccessFromLevelEightToNine_RaisesOneBroadcastWithIdsAndLevelNine_AC_ENH_18()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(ONE_EVENT, _broadcastEvents.Count, "Broadcast events.");
            Assert.AreEqual(Player, _broadcastEvents[0].CharacterId, "CharacterId.");
            Assert.AreEqual(BronzeSwordId, _broadcastEvents[0].ItemId, "ItemId.");
            Assert.AreEqual(EnhancementConstants.SERVER_BROADCAST_LEVEL, _broadcastEvents[0].Level, "Level.");
            Assert.AreEqual(ONE_EVENT, _successEvents.Count, "Success events.");
            Assert.AreEqual(LEVEL_NINE, _successEvents[0].NewLevel, "Success NewLevel.");
            Assert.AreEqual(NO_EVENTS, _destructionEvents.Count, "Destruction events.");
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(9)]
        public void CompleteAttempt_SuccessIntoOtherLevelThanNine_RaisesNoBroadcast(int startingLevel)
        {
            // Arrange
            SeedBronzeRequest((byte)startingLevel);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(NO_EVENTS, _broadcastEvents.Count, "Broadcast events.");
            Assert.AreEqual(ONE_EVENT, _successEvents.Count, "Success events.");
            Assert.AreEqual(startingLevel + 1, _successEvents[0].NewLevel, "Success NewLevel.");
            Assert.AreEqual(NO_EVENTS, _destructionEvents.Count, "Destruction events.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_FailedLevelEightToNine_RaisesDestructionAndNoBroadcast_AC_ENH_18()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_EIGHT, EnhancementOutcome.Destruction);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(ONE_EVENT, _destructionEvents.Count, "Destruction events.");
            Assert.AreEqual(BronzeSwordId, _destructionEvents[0].ItemId, "ItemId.");
            Assert.AreEqual(NO_EVENTS, _broadcastEvents.Count, "Broadcast events.");
            Assert.AreEqual(NO_EVENTS, _successEvents.Count, "Success events.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Order (CR-ENH-15 steps 7-9)
        // =====================================================================

        [Test]
        public void CompleteAttempt_SuccessToLevelNine_UnlocksThenRaisesSuccessThenBroadcastWithAttemptOver()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            var order = new List<string>();
            var endsWithUnlock = new List<bool>();
            var attemptInProgress = new List<bool>();
            var slotLocked = new List<bool>();

            void Observe(string name)
            {
                order.Add(name);
                endsWithUnlock.Add(
                    _recording.Calls.Count > 0 && _recording.Calls[_recording.Calls.Count - 1] == CALL_UNLOCK_SLOT);
                attemptInProgress.Add(_service.IsAttemptInProgress(Player));
                slotLocked.Add(_inventory.IsSlotLocked(Player, ITEM_SLOT));
            }

            _service.OnEnhancementSuccess += _ => Observe(LOG_SUCCESS);
            _service.OnEnhancementBroadcastLevelReached += _ => Observe(LOG_BROADCAST);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(new[] { LOG_SUCCESS, LOG_BROADCAST }, order, "Handler order.");
            for (int i = 0; i < order.Count; i++)
            {
                Assert.IsTrue(endsWithUnlock[i], order[i] + ": UnlockSlot must already have been called.");
                Assert.IsFalse(attemptInProgress[i], order[i] + ": the attempt must already be over.");
                Assert.IsFalse(slotLocked[i], order[i] + ": the item slot must already be unlocked.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_DestructionOutcome_RaisesDestructionAfterUnlockWithAttemptOver()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);

            var order = new List<string>();
            bool endsWithUnlock = false;
            bool attemptInProgress = true;
            bool slotLocked = true;
            _service.OnEnhancementDestruction += _ =>
            {
                order.Add(LOG_DESTRUCTION);
                endsWithUnlock = _recording.Calls.Count > 0
                    && _recording.Calls[_recording.Calls.Count - 1] == CALL_UNLOCK_SLOT;
                attemptInProgress = _service.IsAttemptInProgress(Player);
                slotLocked = _inventory.IsSlotLocked(Player, ITEM_SLOT);
            };

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            CollectionAssert.AreEqual(new[] { LOG_DESTRUCTION }, order, "Handler order.");
            Assert.IsTrue(endsWithUnlock, "UnlockSlot must already have been called.");
            Assert.IsFalse(attemptInProgress, "The attempt must already be over.");
            Assert.IsFalse(slotLocked, "The item slot must already be unlocked.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Nothing before the commit, nothing on a rollback (CR-ENH-11)
        // =====================================================================

        [Test]
        public void BeginAttempt_PendingSuccess_RaisesNoEvents()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);

            // Act
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Assert
            AssertNoEvents("After a pending success");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_PendingDestruction_RaisesNoEvents()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);

            // Act
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);

            // Assert
            AssertNoEvents("After a pending destruction");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_TierMismatchRejection_RaisesNoEvents()
        {
            // Arrange: Bronze sword with an Iron scroll.
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, BronzeSwordId, 1, LEVEL_TWO);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, new ItemID(IRON_SCROLL_ID), SCROLL_QUANTITY);

            // Act
            var start = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(start.IsPending, "The attempt must be rejected.");
            Assert.AreEqual(EnhancementResultCode.RejectedTierMismatch, start.RejectionCode, "Rejection code.");
            AssertNoEvents("After a rejection");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_PendingSuccess_RaisesNoEvents()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            ExpectWriteFailedLog();
            AssertNoEvents("After a rolled-back success");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RollBackAttempt_PendingDestruction_RaisesNoEvents()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);

            // Act
            _service.RollBackAttempt(Player);

            // Assert
            ExpectWriteFailedLog();
            AssertNoEvents("After a rolled-back destruction");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // A throwing subscriber is contained (EC-ENH-8)
        // =====================================================================

        [Test]
        public void CompleteAttempt_ThrowingSuccessSubscriber_LogsExceptionAndReturnsNormalResult()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            _service.OnEnhancementSuccess += _ => throw new NotSupportedException("Subscriber failure.");
            ExpectSubscriberExceptionLog();

            // Act
            EnhancementAttemptResult result = _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(EnhancementOutcome.Success, result.Outcome, "Outcome.");
            Assert.AreEqual(LEVEL_THREE, result.NewLevel, "NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Success, result.ResultCode, "ResultCode.");
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Bag item.");
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Bag level.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "The attempt must be over.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_ThrowingDestructionSubscriber_LogsExceptionAndReturnsNormalResult()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO);
            BeginPending(DRAW_DESTRUCTION_AT_LEVEL_TWO, EnhancementOutcome.Destruction);
            _service.OnEnhancementDestruction += _ => throw new NotSupportedException("Subscriber failure.");
            ExpectSubscriberExceptionLog();

            // Act
            EnhancementAttemptResult result = _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(EnhancementOutcome.Destruction, result.Outcome, "Outcome.");
            Assert.AreEqual(0, result.NewLevel, "NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Destruction, result.ResultCode, "ResultCode.");
            Assert.AreEqual(ItemID.Invalid, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "The item stays destroyed.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "The attempt must be over.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_ThrowingBroadcastSubscriber_LogsExceptionAndSuccessEventWasStillRaised()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            _service.OnEnhancementBroadcastLevelReached += _ => throw new NotSupportedException("Subscriber failure.");
            ExpectSubscriberExceptionLog();

            // Act
            EnhancementAttemptResult result = _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(EnhancementOutcome.Success, result.Outcome, "Outcome.");
            Assert.AreEqual(LEVEL_NINE, result.NewLevel, "NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Success, result.ResultCode, "ResultCode.");
            Assert.AreEqual(ONE_EVENT, _successEvents.Count, "The success event must have been raised before the trigger.");
            Assert.AreEqual(LEVEL_NINE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Bag level.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CompleteAttempt_ThrowingSuccessSubscriber_StillRaisesBroadcastAtLevelNine()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_EIGHT);
            BeginPending(DRAW_SUCCESS, EnhancementOutcome.Success);
            _service.OnEnhancementSuccess += _ => throw new NotSupportedException("Subscriber failure.");
            ExpectSubscriberExceptionLog();

            // Act
            EnhancementAttemptResult result = _service.CompleteAttempt(Player);

            // Assert
            Assert.AreEqual(ONE_EVENT, _broadcastEvents.Count, "Broadcast events.");
            Assert.AreEqual(BronzeSwordId, _broadcastEvents[0].ItemId, "Broadcast ItemId.");
            Assert.AreEqual(LEVEL_NINE, _broadcastEvents[0].Level, "Broadcast Level.");
            Assert.AreEqual(EnhancementOutcome.Success, result.Outcome, "Outcome.");
            Assert.AreEqual(LEVEL_NINE, result.NewLevel, "NewLevel.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Constant (CR-ENH-14)
        // =====================================================================

        [Test]
        public void EnhancementConstants_ServerBroadcastLevel_IsNine()
        {
            // Arrange / Act
            byte level = EnhancementConstants.SERVER_BROADCAST_LEVEL;

            // Assert
            Assert.AreEqual(EXPECTED_BROADCAST_LEVEL, level);
        }
    }
}

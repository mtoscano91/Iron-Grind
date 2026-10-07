using System;
using System.Collections.Generic;
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
    /// EditMode integration tests for Enhancement System Story 004: attempt sequence and outcome
    /// resolution (design/gdd/enhancement-system.md CR-ENH-6 to CR-ENH-10, CR-ENH-15 steps 3-7,
    /// CR-ENH-18; AC-ENH-8, AC-ENH-9, AC-ENH-10, AC-ENH-11, AC-ENH-12, AC-ENH-33, AC-ENH-36).
    /// Uses a real <see cref="InventoryService"/> behind a recording decorator, a scripted random
    /// source and an always-active NPC session stub. "Run the attempt" means
    /// <c>BeginAttempt</c> returning a pending attempt followed by <c>CompleteAttempt</c>.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_AttemptSequence_Integration_Tests
    {
        private const uint RAW_PLAYER = 1001u;
        private const uint RAW_OTHER_PLAYER = 1002u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint IRON_SWORD_ID = 4002u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint IRON_SCROLL_ID = 4006u;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 3;
        private const int SECOND_ITEM_SLOT = 6;
        private const int SECOND_SCROLL_SLOT = 7;
        private const int LOW_SCROLL_SLOT = 2;
        private const int HIGH_SCROLL_SLOT = 5;

        private const byte LEVEL_ZERO = 0;
        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_FOUR = 4;
        private const byte LEVEL_NINE = 9;
        private const byte LEVEL_ONE = 1;
        private const int STACK_OF_ONE = 1;
        private const int STACK_OF_FIVE = 5;
        private const int STACK_OF_FIVE_AFTER_ONE_USED = 4;

        // Draws, from the GDD acceptance criteria. P_s[0] = 0.95, P_s[2] = 0.85, P_s[4] = 0.65.
        private const double DRAW_MINIMUM = 0.00;               // Always below any P_s.
        private const double DRAW_JUST_BELOW_P_S_2 = 0.849;     // Success at +2 (boundary).
        private const double DRAW_EQUAL_TO_P_S_2 = 0.85;        // Destruction at +2 (draw == P_s).
        private const double DRAW_ABOVE_P_S_2 = 0.90;           // AC-ENH-10.
        private const double DRAW_ABOVE_P_S_0 = 0.96;           // AC-ENH-12.
        private const double DRAW_ABOVE_P_S_4 = 0.99;           // AC-ENH-11.

        private static readonly CharacterID Player = new CharacterID(RAW_PLAYER);
        private static readonly CharacterID OtherPlayer = new CharacterID(RAW_OTHER_PLAYER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID IronSwordId = new ItemID(IRON_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);
        private static readonly ItemID IronScrollId = new ItemID(IRON_SCROLL_ID);

        private byte _maxLevel;
        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private RecordingInventoryDecorator _recording;
        private StubNpcSessions _sessions;
        private ScriptedRandom _random;
        private EnhancementService _service;
        private List<ItemDefinition> _definitions;
        private int _inventoryEvents;

        [SetUp]
        public void SetUp()
        {
            _maxLevel = EnhancementConfig.Default.MaxEnhancementLevel;

            _definitions = new List<ItemDefinition>
            {
                ItemDefinitionBuilder.Build(BRONZE_SWORD_ID, "Bronze Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze)),
                ItemDefinitionBuilder.Build(IRON_SWORD_ID, "Iron Sword", ItemCategory.Equipment,
                    isUpgradeable: true, stackLimit: 1,
                    equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Iron)),
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
            _inventory.RegisterCharacter(OtherPlayer);

            _inventoryEvents = 0;
            _inventory.OnInventoryChanged += _ => _inventoryEvents++;

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

        private void SeedSword(CharacterID who, int slot, ItemID swordId, byte level)
        {
            _inventory.SeedSlotForTesting(who, slot, swordId, 1, level);
        }

        private void SeedScrolls(CharacterID who, int slot, ItemID scrollId, int quantity)
        {
            _inventory.SeedSlotForTesting(who, slot, scrollId, quantity);
        }

        // Valid baseline for Player: Bronze sword at the given level in ITEM_SLOT, Bronze scrolls in SCROLL_SLOT.
        private void SeedBronzeRequest(byte level, int scrollQuantity)
        {
            SeedSword(Player, ITEM_SLOT, BronzeSwordId, level);
            SeedScrolls(Player, SCROLL_SLOT, BronzeScrollId, scrollQuantity);
        }

        private EnhancementAttemptStart Begin(double draw)
        {
            _random.Enqueue(draw);
            return _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);
        }

        private void AssertSlotEmpty(CharacterID who, int slot, string label)
        {
            var s = _inventory.GetSlot(who, slot);
            Assert.AreEqual(ItemID.Invalid, s.ItemId, label + " ItemId.");
            Assert.AreEqual(0, s.Quantity, label + " Quantity.");
            Assert.AreEqual(0, s.EnhancementLevel, label + " EnhancementLevel.");
        }

        // Rejected request: not pending, expected code, no draw, nothing recorded as a mutating call, no event.
        private void AssertRejectedBeginChangedNothing(EnhancementAttemptStart start, EnhancementResultCode expected)
        {
            Assert.IsFalse(start.IsPending, "IsPending.");
            Assert.AreEqual(expected, start.RejectionCode, "RejectionCode.");
            Assert.AreEqual(0, _random.DrawCount, "DrawCount.");
            Assert.AreEqual(0, _recording.Calls.Count, "No mutating inventory call may be made.");
            Assert.AreEqual(0, _inventoryEvents, "OnInventoryChanged count.");
        }

        // =====================================================================
        // Constructor
        // =====================================================================

        [Test]
        public void Constructor_NullRandom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new EnhancementService(_recording, _itemDatabase, EnhancementConfig.Default, _sessions, null));
        }

        // =====================================================================
        // Outcomes (AC-ENH-9, 10, 11, 12)
        // =====================================================================

        [Test]
        public void BeginAndCompleteAttempt_SuccessDrawAtLevelTwo_LevelThreeScrollGoneUnlocked_AC_ENH_9()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_ONE);

            // Act
            var start = Begin(DRAW_MINIMUM);
            var result = _service.CompleteAttempt(Player);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Success, start.Outcome, "Pending outcome.");
            Assert.AreEqual(3, start.NewLevel, "Pending NewLevel.");
            Assert.AreEqual(EnhancementOutcome.Success, result.Outcome, "Result outcome.");
            Assert.AreEqual(3, result.NewLevel, "Result NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Success, result.ResultCode, "Result code.");
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Sword still present.");
            Assert.AreEqual(3, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Slot level.");
            AssertSlotEmpty(Player, SCROLL_SLOT, "Scroll slot");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAndCompleteAttempt_DestructionDrawAtLevelTwo_ItemGoneScrollGoneUnlocked_AC_ENH_10()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_ONE);

            // Act
            var start = Begin(DRAW_ABOVE_P_S_2);
            var result = _service.CompleteAttempt(Player);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, start.Outcome, "Pending outcome.");
            Assert.AreEqual(0, start.NewLevel, "Pending NewLevel.");
            Assert.AreEqual(EnhancementOutcome.Destruction, result.Outcome, "Result outcome.");
            Assert.AreEqual(0, result.NewLevel, "Result NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Destruction, result.ResultCode, "Result code.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Item slot");
            AssertSlotEmpty(Player, SCROLL_SLOT, "Scroll slot");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAndCompleteAttempt_DestructionDrawAtLevelFour_ItemGoneScrollGoneUnlocked_AC_ENH_11()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_FOUR, STACK_OF_ONE);

            // Act
            var start = Begin(DRAW_ABOVE_P_S_4);
            var result = _service.CompleteAttempt(Player);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, start.Outcome, "Pending outcome.");
            Assert.AreEqual(EnhancementOutcome.Destruction, result.Outcome, "Result outcome.");
            Assert.AreEqual(0, result.NewLevel, "Result NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Destruction, result.ResultCode, "Result code.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Item slot");
            AssertSlotEmpty(Player, SCROLL_SLOT, "Scroll slot");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAndCompleteAttempt_DestructionDrawAtLevelZero_ItemGoneScrollGone_AC_ENH_12()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_ZERO, STACK_OF_ONE);

            // Act
            var start = Begin(DRAW_ABOVE_P_S_0);
            var result = _service.CompleteAttempt(Player);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, start.Outcome, "Pending outcome.");
            Assert.AreEqual(EnhancementOutcome.Destruction, result.Outcome, "Result outcome.");
            Assert.AreEqual(0, result.NewLevel, "Result NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Destruction, result.ResultCode, "Result code.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Item slot");
            AssertSlotEmpty(Player, SCROLL_SLOT, "Scroll slot");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
        }

        [TestCase(DRAW_JUST_BELOW_P_S_2, EnhancementOutcome.Success)]
        [TestCase(DRAW_EQUAL_TO_P_S_2, EnhancementOutcome.Destruction)]
        public void BeginAttempt_DrawAroundPsAtLevelTwo_ResolvesAtBoundary(double draw, EnhancementOutcome expected)
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            var start = Begin(draw);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(expected, start.Outcome, "Outcome.");
        }

        [TestCase(DRAW_MINIMUM, true)]
        [TestCase(DRAW_ABOVE_P_S_2, false)]
        public void BeginAndCompleteAttempt_StackOfFiveScrolls_LeavesFour_AC_ENH_33(double draw, bool expectSuccess)
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            var start = Begin(draw);
            _service.CompleteAttempt(Player);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(BronzeScrollId, _inventory.GetSlot(Player, SCROLL_SLOT).ItemId, "Scroll item.");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll quantity.");
            if (expectSuccess)
            {
                Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Sword item.");
                Assert.AreEqual(3, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level.");
            }
            else
            {
                AssertSlotEmpty(Player, ITEM_SLOT, "Item slot");
            }
        }

        [Test]
        public void BeginAttempt_ScrollsInLowerSlotToo_ConsumesLowestIndexStack()
        {
            // Arrange: scrolls in slots 2 and 5; the request names slot 5.
            SeedSword(Player, ITEM_SLOT, BronzeSwordId, LEVEL_TWO);
            SeedScrolls(Player, LOW_SCROLL_SLOT, BronzeScrollId, STACK_OF_FIVE);
            SeedScrolls(Player, HIGH_SCROLL_SLOT, BronzeScrollId, STACK_OF_FIVE);
            _random.Enqueue(DRAW_MINIMUM);

            // Act
            var start = _service.BeginAttempt(Player, ITEM_SLOT, HIGH_SCROLL_SLOT);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Success, start.Outcome, "Outcome.");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, LOW_SCROLL_SLOT).Quantity, "Lower stack.");
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, HIGH_SCROLL_SLOT).Quantity, "Named stack untouched.");
        }

        // =====================================================================
        // Scroll gone at step 4 (AC-ENH-36)
        // =====================================================================

        [Test]
        public void BeginAttempt_OnlyScrollStackLocked_RejectedScrollNotFound_ThenAcceptedAfterUnlock_AC_ENH_36()
        {
            // Arrange: validation passes, but ConsumeItem does not count a locked stack.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            _inventory.LockSlot(Player, SCROLL_SLOT);
            _inventoryEvents = 0;

            // Act
            _random.Enqueue(DRAW_MINIMUM);
            var rejected = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert: nothing changed, no draw, the item lock was taken back.
            Assert.IsFalse(rejected.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedScrollNotFound, rejected.RejectionCode, "RejectionCode.");
            Assert.AreEqual(0, _random.DrawCount, "DrawCount.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scrolls unchanged.");
            Assert.AreEqual(0, _inventoryEvents, "OnInventoryChanged count.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            CollectionAssert.AreEqual(new[] { "LockSlot", "ConsumeItem", "UnlockSlot" }, _recording.Calls, "Call sequence.");

            // Act: a following valid request, once the scroll stack is unlocked.
            _inventory.UnlockSlot(Player, SCROLL_SLOT);
            var accepted = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(accepted.IsPending, "Second request must be pending.");
            Assert.AreEqual(EnhancementOutcome.Success, accepted.Outcome, "Second outcome.");
            Assert.AreEqual(1, _random.DrawCount, "DrawCount after second request.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Pending state and step order
        // =====================================================================

        [Test]
        public void BeginAttempt_SuccessOutcome_BagShowsNewLevelScrollConsumedItemStillLocked_InProgress()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_MINIMUM);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Success, start.Outcome, "Outcome.");
            Assert.AreEqual(3, start.NewLevel, "NewLevel.");
            Assert.AreEqual(3, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level already applied.");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll already consumed.");
            Assert.IsTrue(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot stays locked until CompleteAttempt.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_DestructionOutcome_SlotEmptyAndUnlockedScrollConsumed_InProgress()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_ABOVE_P_S_2);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, start.Outcome, "Outcome.");
            Assert.AreEqual(0, start.NewLevel, "NewLevel.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Item slot");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll already consumed.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "RemoveItem clears the lock.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAndCompleteAttempt_Success_CallsLockConsumeSetLevelThenUnlockOnlyInComplete()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            Begin(DRAW_MINIMUM);
            var callsAfterBegin = _recording.Calls.ToArray();
            _service.CompleteAttempt(Player);
            var callsAfterComplete = _recording.Calls.ToArray();

            // Assert
            CollectionAssert.AreEqual(new[] { "LockSlot", "ConsumeItem", "SetEnhancementLevel" }, callsAfterBegin, "After BeginAttempt.");
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "SetEnhancementLevel", "UnlockSlot" }, callsAfterComplete, "After CompleteAttempt.");
            Assert.AreEqual(1, _random.DrawCount, "Exactly one draw.");
        }

        [Test]
        public void BeginAndCompleteAttempt_Destruction_CallsLockConsumeRemoveThenUnlockOnlyInComplete()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            Begin(DRAW_ABOVE_P_S_2);
            var callsAfterBegin = _recording.Calls.ToArray();
            _service.CompleteAttempt(Player);
            var callsAfterComplete = _recording.Calls.ToArray();

            // Assert
            CollectionAssert.AreEqual(new[] { "LockSlot", "ConsumeItem", "RemoveItem" }, callsAfterBegin, "After BeginAttempt.");
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "RemoveItem", "UnlockSlot" }, callsAfterComplete, "After CompleteAttempt.");
            Assert.AreEqual(1, _random.DrawCount, "Exactly one draw.");
        }

        [Test]
        public void BeginAttempt_PendingAttempt_FiresTwoInventoryEvents_ConsumeThenApply()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            _inventoryEvents = 0;

            // Act
            Begin(DRAW_MINIMUM);

            // Assert: one event for the scroll consumption, one for the level change; locking is silent.
            Assert.AreEqual(2, _inventoryEvents, "OnInventoryChanged count.");
        }

        // =====================================================================
        // Draws
        // =====================================================================

        [Test]
        public void BeginAttempt_PendingAttempt_DrawsExactlyOnce()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);

            // Act
            Begin(DRAW_MINIMUM);

            // Assert
            Assert.AreEqual(1, _random.DrawCount, "DrawCount.");
        }

        [Test]
        public void BeginAttempt_NoNpcSession_RejectedNoNPCSession_NoDraw_NothingChanged()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            _sessions.Active = false;

            // Act
            var start = Begin(DRAW_MINIMUM);

            // Assert
            AssertRejectedBeginChangedNothing(start, EnhancementResultCode.RejectedNoNPCSession);
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_TierMismatch_RejectedTierMismatch_NoDraw_NothingChanged()
        {
            // Arrange: Bronze sword with an Iron scroll.
            SeedSword(Player, ITEM_SLOT, BronzeSwordId, LEVEL_TWO);
            SeedScrolls(Player, SCROLL_SLOT, IronScrollId, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_MINIMUM);

            // Assert
            AssertRejectedBeginChangedNothing(start, EnhancementResultCode.RejectedTierMismatch);
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scrolls unchanged.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must not be locked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_ItemAtMaxLevel_RejectedAtMaxLevel_NoDraw_NothingChanged()
        {
            // Arrange
            SeedBronzeRequest(_maxLevel, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_MINIMUM);

            // Assert
            AssertRejectedBeginChangedNothing(start, EnhancementResultCode.RejectedAtMaxLevel);
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scrolls unchanged.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_ConcurrentSecondRequest_RejectedConcurrentAttempt_NoSecondDraw_NothingChanged()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);
            int eventsAfterFirst = _inventoryEvents;
            int callsAfterFirst = _recording.Calls.Count;

            // Act
            _random.Enqueue(DRAW_MINIMUM);
            var second = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsFalse(second.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, second.RejectionCode, "RejectionCode.");
            Assert.AreEqual(1, _random.DrawCount, "Only the first attempt drew.");
            Assert.AreEqual(eventsAfterFirst, _inventoryEvents, "OnInventoryChanged count.");
            Assert.AreEqual(callsAfterFirst, _recording.Calls.Count, "No further mutating inventory call.");
        }

        // =====================================================================
        // CompleteAttempt guards
        // =====================================================================

        [Test]
        public void CompleteAttempt_NothingPending_ThrowsInvalidOperation()
        {
            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player));
        }

        [Test]
        public void CompleteAttempt_CalledTwiceAfterOneAttempt_SecondThrows()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);
            _service.CompleteAttempt(Player);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player));
        }

        [Test]
        public void CompleteAttempt_OnlyOtherCharacterPending_ThrowsAndLeavesOtherAttemptPending()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(OtherPlayer));
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "Player's attempt must stay pending.");
        }

        [Test]
        public void IsAttemptInProgress_AfterComplete_IsFalse()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);

            // Act
            _service.CompleteAttempt(Player);

            // Assert
            Assert.IsFalse(_service.IsAttemptInProgress(Player));
        }

        // =====================================================================
        // Concurrent attempts (AC-ENH-8)
        // =====================================================================

        [Test]
        public void BeginAttempt_SecondItemWhileFirstPending_RejectedConcurrentAttempt_FirstCompletesUnaffected_AC_ENH_8()
        {
            // Arrange: first attempt on ITEM_SLOT / SCROLL_SLOT; a second valid pair elsewhere.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            SeedSword(Player, SECOND_ITEM_SLOT, BronzeSwordId, LEVEL_ONE);
            SeedScrolls(Player, SECOND_SCROLL_SLOT, BronzeScrollId, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);

            // Act
            _random.Enqueue(DRAW_MINIMUM);
            var second = _service.BeginAttempt(Player, SECOND_ITEM_SLOT, SECOND_SCROLL_SLOT);
            var firstResult = _service.CompleteAttempt(Player);

            // Assert
            Assert.IsFalse(second.IsPending, "Second IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, second.RejectionCode, "Second RejectionCode.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, SECOND_ITEM_SLOT), "Second item must not be locked.");
            Assert.AreEqual(LEVEL_ONE, _inventory.GetSlot(Player, SECOND_ITEM_SLOT).EnhancementLevel, "Second item level unchanged.");
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, SECOND_SCROLL_SLOT).Quantity, "Second scrolls untouched.");
            Assert.AreEqual(1, _random.DrawCount, "No draw for the rejected request.");
            Assert.AreEqual(EnhancementOutcome.Success, firstResult.Outcome, "First outcome.");
            Assert.AreEqual(3, firstResult.NewLevel, "First NewLevel.");
            Assert.AreEqual(EnhancementResultCode.Success, firstResult.ResultCode, "First ResultCode.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "Nothing pending afterwards.");
        }

        [Test]
        public void BeginAttempt_DifferentCharacterWhilePlayerPending_IsPendingNormally()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            SeedSword(OtherPlayer, ITEM_SLOT, BronzeSwordId, LEVEL_TWO);
            SeedScrolls(OtherPlayer, SCROLL_SLOT, BronzeScrollId, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);

            // Act
            _random.Enqueue(DRAW_ABOVE_P_S_2);
            var other = _service.BeginAttempt(OtherPlayer, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(other.IsPending, "Other IsPending.");
            Assert.AreEqual(EnhancementOutcome.Destruction, other.Outcome, "Other outcome.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "Player still pending.");
            Assert.IsTrue(_service.IsAttemptInProgress(OtherPlayer), "Other pending.");
            Assert.AreEqual(2, _random.DrawCount, "One draw per pending attempt.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_AfterCompletion_NewAttemptOnSameItemAccepted()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            Begin(DRAW_MINIMUM);
            _service.CompleteAttempt(Player);

            // Act
            var again = Begin(DRAW_MINIMUM);

            // Assert
            Assert.IsTrue(again.IsPending, "IsPending.");
            Assert.AreEqual(4, again.NewLevel, "NewLevel (+3 to +4).");
            Assert.AreEqual(2, _random.DrawCount, "Draws.");
        }

        // =====================================================================
        // Level cap
        // =====================================================================

        [Test]
        public void BeginAttempt_LevelNineSuccess_ReachesMax_ThenNextAttemptRejectedAtMaxLevel()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_NINE, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_MINIMUM);
            var result = _service.CompleteAttempt(Player);
            _random.Enqueue(DRAW_MINIMUM);
            var next = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(_maxLevel, start.NewLevel, "Pending NewLevel.");
            Assert.AreEqual(_maxLevel, result.NewLevel, "Result NewLevel.");
            Assert.AreEqual(_maxLevel, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Slot level.");
            Assert.IsFalse(next.IsPending, "Next IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedAtMaxLevel, next.RejectionCode, "Next RejectionCode.");
            Assert.AreEqual(1, _random.DrawCount, "No draw for the capped item.");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Only one scroll used.");
        }

        // =====================================================================
        // Broken invariant
        // =====================================================================

        [Test]
        public void BeginAttempt_SetEnhancementLevelFails_UnlocksItemNothingPendingAndThrows()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            _recording.FailSetEnhancementLevel = true;
            _random.Enqueue(DRAW_MINIMUM);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.AreEqual(STACK_OF_FIVE_AFTER_ONE_USED, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "The scroll is not restored.");
            CollectionAssert.AreEqual(
                new[] { "LockSlot", "ConsumeItem", "SetEnhancementLevel", "UnlockSlot" }, _recording.Calls, "Call sequence.");
            Assert.Throws<InvalidOperationException>(() => _service.CompleteAttempt(Player), "Nothing pending to complete.");
        }

        [Test]
        public void BeginAttempt_InventorySubscriberThrowsDuringScrollConsumption_UnlocksItemNothingPendingAndRethrows()
        {
            // Arrange: the inventory lets a subscriber's exception propagate out of ConsumeItem. With
            // nothing pending, only BeginAttempt itself can take the item lock back.
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_FIVE);
            _random.Enqueue(DRAW_MINIMUM);
            _inventory.OnInventoryChanged += _ => throw new NotSupportedException("Subscriber failure.");

            // Act / Assert
            Assert.Throws<NotSupportedException>(() => _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT));
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(0, _random.DrawCount, "No draw: the exception came before it.");
            CollectionAssert.AreEqual(new[] { "LockSlot", "ConsumeItem", "UnlockSlot" }, _recording.Calls, "Call sequence.");
        }

        // =====================================================================
        // Rejections pass through
        // =====================================================================

        [Test]
        public void BeginAttempt_ValidationRejection_PassesCodeThrough_NotPending_NothingChanged()
        {
            // Arrange: Iron sword with a Bronze scroll.
            SeedSword(Player, ITEM_SLOT, IronSwordId, LEVEL_TWO);
            SeedScrolls(Player, SCROLL_SLOT, BronzeScrollId, STACK_OF_FIVE);

            // Act
            var start = Begin(DRAW_MINIMUM);

            // Assert
            AssertRejectedBeginChangedNothing(start, EnhancementResultCode.RejectedTierMismatch);
            Assert.AreEqual(IronSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Sword still present.");
            Assert.AreEqual(LEVEL_TWO, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.AreEqual(STACK_OF_FIVE, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scrolls unchanged.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

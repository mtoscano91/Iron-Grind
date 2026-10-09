using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.NpcInteraction;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// EditMode integration tests for Enhancement System Story 006: NPC interaction session
    /// (design/gdd/enhancement-system.md CR-ENH-16, CR-ENH-17; AC-ENH-27, AC-ENH-28, AC-ENH-29,
    /// AC-ENH-30, AC-ENH-39; design/gdd/npc-shop.md CR-SHOP-3). Uses the real
    /// <see cref="NpcInteractionSessionTracker"/> (stub town hub query, manual clock) as the session
    /// source of a real <see cref="EnhancementService"/> over a real <see cref="InventoryService"/>
    /// and a scripted random source. "Pending" means <c>BeginAttempt</c> returned a pending attempt
    /// and <c>CompleteAttempt</c> has not run.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_NpcInteractionSession_Integration_Tests
    {
        private const uint RAW_PLAYER = 1001u;
        private const uint RAW_OTHER_PLAYER = 1002u;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;

        private const byte LEVEL_TWO = 2;
        private const byte LEVEL_THREE = 3;
        private const int STACK_OF_THREE = 3;
        private const int STACK_OF_TWO = 2;

        private const double DRAW_SUCCESS = 0.00;

        private const double SESSION_LIFETIME_SECONDS = 300.0;
        private const double CLOCK_START = 1000.0;

        private const uint ENHANCEMENT_NPC_ID = 7u;
        private const uint OTHER_NPC_ID = 12u;
        private const uint ARBITRARY_NPC_ID = 123456u;

        private const double REOPEN_DELAY = 200.0;
        private const double JUST_BEFORE_SECOND_EXPIRY = 499.9;
        private const double SECOND_EXPIRY = 500.0;
        private const double WELL_AFTER_FIRST_EXPIRY = 301.0;
        private const double WITHIN_SECOND_LIFETIME = 450.0;
        private const double OTHER_PLAYER_OPEN_DELAY = 100.0;
        private const double FIRST_PLAYER_EXPIRED_ONLY = 350.0;
        private const double BOTH_EXPIRED = 401.0;
        private const double SMALL_STEP = 10.0;
        private const int ACTIVITY_READS = 25;

        private static readonly CharacterID Player = new CharacterID(RAW_PLAYER);
        private static readonly CharacterID OtherPlayer = new CharacterID(RAW_OTHER_PLAYER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);

        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private RecordingInventoryDecorator _recording;
        private StubTownHubQuery _townHub;
        private ManualClock _clock;
        private NpcInteractionSessionTracker _tracker;
        private ScriptedRandomProvider _random;
        private EnhancementService _service;
        private List<ItemDefinition> _definitions;
        private int _inventoryEvents;

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

            _inventoryEvents = 0;
            _inventory.OnInventoryChanged += _ => _inventoryEvents++;

            _recording = new RecordingInventoryDecorator(_inventory);
            _townHub = new StubTownHubQuery();
            _clock = new ManualClock { Now = CLOCK_START };
            _tracker = new NpcInteractionSessionTracker(_townHub, () => _clock.Now, SESSION_LIFETIME_SECONDS);
            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_recording, _itemDatabase, EnhancementConfig.Default, _tracker, _random);
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

        // Valid baseline for Player: Bronze sword in ITEM_SLOT, Bronze scrolls in SCROLL_SLOT.
        private void SeedBronzeRequest(byte level, int scrollQuantity)
        {
            _inventory.SeedSlotForTesting(Player, ITEM_SLOT, BronzeSwordId, 1, level);
            _inventory.SeedSlotForTesting(Player, SCROLL_SLOT, BronzeScrollId, scrollQuantity);
        }

        private EnhancementAttemptStart Begin(double draw)
        {
            _random.EnqueueDouble(draw);
            return _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);
        }

        private void OpenAsPlayer(uint npcId = ENHANCEMENT_NPC_ID)
        {
            var result = _tracker.Open(Player, npcId);
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, result, "Arrange: open result.");
        }

        // A valid request against a session that must not satisfy validation: rejected with
        // RejectedNoNPCSession, no draw, nothing locked, nothing consumed, no mutating call, no event.
        private void AssertBeginRejectedNoSession()
        {
            int scrollsBefore = _inventory.GetSlot(Player, SCROLL_SLOT).Quantity;
            byte levelBefore = _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel;

            var start = Begin(DRAW_SUCCESS);

            Assert.IsFalse(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedNoNPCSession, start.RejectionCode, "RejectionCode.");
            Assert.AreEqual(0, _random.DrawCount, "DrawCount.");
            Assert.AreEqual(0, _recording.Calls.Count, "No mutating inventory call may be made.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must not be locked.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, SCROLL_SLOT), "Scroll slot must not be locked.");
            Assert.AreEqual(scrollsBefore, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Scroll count unchanged.");
            Assert.AreEqual(BronzeSwordId, _inventory.GetSlot(Player, ITEM_SLOT).ItemId, "Item unchanged.");
            Assert.AreEqual(levelBefore, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Level unchanged.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(0, _inventoryEvents, "OnInventoryChanged count.");
        }

        // Opens, then begins a pending Success attempt at +2 with three scrolls.
        private void OpenAndBeginPending()
        {
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            OpenAsPlayer();
            var start = Begin(DRAW_SUCCESS);
            Assert.IsTrue(start.IsPending, "Arrange: the attempt must be pending.");
            Assert.AreEqual(EnhancementOutcome.Success, start.Outcome, "Arrange: pending outcome.");
        }

        // The normal result of the pending Success attempt at +2.
        private void AssertCompletesNormally()
        {
            var result = _service.CompleteAttempt(Player);

            Assert.AreEqual(EnhancementOutcome.Success, result.Outcome, "Result outcome.");
            Assert.AreEqual(LEVEL_THREE, result.NewLevel, "Result NewLevel.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.IsFalse(_inventory.IsSlotLocked(Player, ITEM_SLOT), "Item slot must be unlocked.");
            Assert.AreEqual(LEVEL_THREE, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Final level.");
            Assert.AreEqual(STACK_OF_TWO, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Final scrolls.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // Open (AC-ENH-28, CR-ENH-16)
        // =====================================================================

        [Test]
        public void Open_InTownHub_ReturnsOpenedAndSessionIsActive_AC_ENH_28()
        {
            // Act
            var result = _tracker.Open(Player, ENHANCEMENT_NPC_ID);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, result, "Open result.");
            Assert.IsTrue(_tracker.IsActive(Player), "IsActive.");
        }

        [Test]
        public void Open_NotInTownHub_ReturnsRejectedNotInTownHubAndSessionStaysInactive()
        {
            // Arrange
            _townHub.InTownHub = false;

            // Act
            var result = _tracker.Open(Player, ENHANCEMENT_NPC_ID);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.RejectedNotInTownHub, result, "Open result.");
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
            Assert.IsFalse(_tracker.TryGetActiveNpcId(Player, out uint npcId), "TryGetActiveNpcId.");
            Assert.AreEqual(0u, npcId, "npcId on failure.");
        }

        [Test]
        public void Open_NotInTownHubOnlyForThatCharacter_RejectsThatCharacterAndOpensTheOther()
        {
            // Arrange
            _townHub.OutsideTownHub.Add(Player);

            // Act
            var rejected = _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            var opened = _tracker.Open(OtherPlayer, ENHANCEMENT_NPC_ID);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.RejectedNotInTownHub, rejected, "Player result.");
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, opened, "Other player result.");
            Assert.IsFalse(_tracker.IsActive(Player), "Player IsActive.");
            Assert.IsTrue(_tracker.IsActive(OtherPlayer), "Other player IsActive.");
        }

        [Test]
        public void Open_RejectedWhileSessionActive_LeavesSessionActiveWithOriginalNpcIdAndLifetime()
        {
            // Arrange: open at T, leave the hub, try again at T + 200.
            OpenAsPlayer(ENHANCEMENT_NPC_ID);
            _clock.Advance(REOPEN_DELAY);
            _townHub.InTownHub = false;

            // Act
            var result = _tracker.Open(Player, OTHER_NPC_ID);

            // Assert: still the original session, still expiring at T + 300.
            Assert.AreEqual(NpcInteractionOpenResult.RejectedNotInTownHub, result, "Open result.");
            Assert.IsTrue(_tracker.IsActive(Player), "IsActive after rejected open.");
            Assert.IsTrue(_tracker.TryGetActiveNpcId(Player, out uint npcId), "TryGetActiveNpcId.");
            Assert.AreEqual(ENHANCEMENT_NPC_ID, npcId, "Original npcId kept.");
            _clock.Now = CLOCK_START + SESSION_LIFETIME_SECONDS;
            Assert.IsFalse(_tracker.IsActive(Player), "Original lifetime must still apply.");
        }

        // =====================================================================
        // Close (AC-ENH-29)
        // =====================================================================

        [Test]
        public void Close_AfterOpen_SessionInactiveAndAttemptRejectedNoNpcSession_AC_ENH_29()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            OpenAsPlayer();

            // Act
            _tracker.Close(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
            AssertBeginRejectedNoSession();
        }

        [Test]
        public void Close_WithNoSession_DoesNotThrowAndStaysInactive()
        {
            // Act / Assert
            Assert.DoesNotThrow(() => _tracker.Close(Player));
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
        }

        // =====================================================================
        // Zone transition (AC-ENH-30) and session end
        // =====================================================================

        [Test]
        public void NotifyZoneTransition_AfterOpen_SessionInactiveAndAttemptRejectedNoNpcSession_AC_ENH_30()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            OpenAsPlayer();

            // Act
            _tracker.NotifyZoneTransition(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
            AssertBeginRejectedNoSession();
        }

        [Test]
        public void NotifyZoneTransition_WithNoSession_DoesNotThrowAndStaysInactive()
        {
            // Act / Assert
            Assert.DoesNotThrow(() => _tracker.NotifyZoneTransition(Player));
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
        }

        [Test]
        public void NotifySessionEnded_AfterOpen_SessionInactive()
        {
            // Arrange
            OpenAsPlayer();

            // Act
            _tracker.NotifySessionEnded(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
            Assert.IsFalse(_tracker.TryGetActiveNpcId(Player, out uint npcId), "TryGetActiveNpcId.");
            Assert.AreEqual(0u, npcId, "npcId after session end.");
        }

        [Test]
        public void NotifySessionEnded_WithNoSession_DoesNotThrowAndStaysInactive()
        {
            // Act / Assert
            Assert.DoesNotThrow(() => _tracker.NotifySessionEnded(Player));
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
        }

        // =====================================================================
        // Wall-clock lifetime (AC-ENH-39)
        // =====================================================================

        [TestCase(0.0, true)]
        [TestCase(299.0, true)]
        [TestCase(299.9, true)]
        [TestCase(300.0, false)]
        [TestCase(300.1, false)]
        [TestCase(301.0, false)]
        public void IsActive_ElapsedSinceOpen_ActiveOnlyBeforeLifetimeEnds_AC_ENH_39(double elapsed, bool expectedActive)
        {
            // Arrange
            OpenAsPlayer();

            // Act
            _clock.Now = CLOCK_START + elapsed;

            // Assert
            Assert.AreEqual(expectedActive, _tracker.IsActive(Player), "IsActive.");
        }

        [Test]
        public void BeginAttempt_AfterLifetimeExpired_RejectedNoNpcSessionNothingLockedNothingConsumed_AC_ENH_39()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            OpenAsPlayer();
            _clock.Advance(WELL_AFTER_FIRST_EXPIRY);

            // Act / Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
            AssertBeginRejectedNoSession();
        }

        [Test]
        public void IsActive_RepeatedReadsAndActivityBeforeExpiry_DoesNotExtendLifetime()
        {
            // Arrange
            OpenAsPlayer();

            // Act: steady reads of the session and of its npcId, with the clock moving in small steps.
            while (_clock.Now - CLOCK_START + SMALL_STEP < SESSION_LIFETIME_SECONDS)
            {
                _clock.Advance(SMALL_STEP);
                for (int i = 0; i < ACTIVITY_READS; i++)
                {
                    Assert.IsTrue(_tracker.IsActive(Player), "Still active before the deadline.");
                    Assert.IsTrue(_tracker.TryGetActiveNpcId(Player, out _), "npcId readable before the deadline.");
                }
            }
            _clock.Now = CLOCK_START + WELL_AFTER_FIRST_EXPIRY;

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive at T + 301.");
            Assert.IsFalse(_tracker.TryGetActiveNpcId(Player, out _), "TryGetActiveNpcId at T + 301.");
        }

        [Test]
        public void IsActive_ReadAtExpiryThenLaterRead_StaysInactive()
        {
            // Arrange: expiry is observed, then later reads agree (no resurrection by reading).
            OpenAsPlayer();
            _clock.Now = CLOCK_START + SESSION_LIFETIME_SECONDS;
            Assert.IsFalse(_tracker.IsActive(Player), "Arrange: expired.");

            // Act
            _clock.Advance(SMALL_STEP);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "IsActive.");
        }

        // =====================================================================
        // Re-open (CR-SHOP-3)
        // =====================================================================

        [Test]
        public void Open_AgainWhileActive_RestartsLifetime()
        {
            // Arrange: open at T, open again at T + 200.
            OpenAsPlayer();
            _clock.Advance(REOPEN_DELAY);
            var second = _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, second, "Arrange: second open.");

            // Act / Assert: the first lifetime would have ended at T + 300.
            _clock.Now = CLOCK_START + WITHIN_SECOND_LIFETIME;
            Assert.IsTrue(_tracker.IsActive(Player), "Active at T + 450.");
            _clock.Now = CLOCK_START + JUST_BEFORE_SECOND_EXPIRY;
            Assert.IsTrue(_tracker.IsActive(Player), "Active at T + 499.9.");
            _clock.Now = CLOCK_START + SECOND_EXPIRY;
            Assert.IsFalse(_tracker.IsActive(Player), "Inactive at T + 500.");
        }

        [Test]
        public void Open_AgainWithDifferentNpc_ReplacesStoredNpcId()
        {
            // Arrange
            OpenAsPlayer(ENHANCEMENT_NPC_ID);

            // Act
            var result = _tracker.Open(Player, OTHER_NPC_ID);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, result, "Open result.");
            Assert.IsTrue(_tracker.IsActive(Player), "IsActive.");
            Assert.IsTrue(_tracker.TryGetActiveNpcId(Player, out uint npcId), "TryGetActiveNpcId.");
            Assert.AreEqual(OTHER_NPC_ID, npcId, "Stored npcId.");
        }

        [Test]
        public void Open_AfterExpiry_StartsNewSession()
        {
            // Arrange
            OpenAsPlayer();
            _clock.Advance(WELL_AFTER_FIRST_EXPIRY);
            Assert.IsFalse(_tracker.IsActive(Player), "Arrange: expired.");

            // Act
            var result = _tracker.Open(Player, ENHANCEMENT_NPC_ID);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, result, "Open result.");
            Assert.IsTrue(_tracker.IsActive(Player), "IsActive.");
        }

        // =====================================================================
        // Enhancement integration (AC-ENH-27, positive path)
        // =====================================================================

        [Test]
        public void BeginAttempt_NeverOpened_RejectedNoNpcSession_AC_ENH_27()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);

            // Act / Assert
            AssertBeginRejectedNoSession();
        }

        [Test]
        public void BeginAttempt_AfterOpenInTownHub_AcceptedAndPending()
        {
            // Arrange
            SeedBronzeRequest(LEVEL_TWO, STACK_OF_THREE);
            OpenAsPlayer();

            // Act
            var start = Begin(DRAW_SUCCESS);

            // Assert
            Assert.IsTrue(start.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementOutcome.Success, start.Outcome, "Outcome.");
            Assert.AreEqual(LEVEL_THREE, start.NewLevel, "NewLevel.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress.");
            Assert.AreEqual(1, _random.DrawCount, "DrawCount.");
        }

        // =====================================================================
        // In-flight attempts are not cancelled (CR-ENH-17)
        // =====================================================================

        [Test]
        public void CompleteAttempt_SessionClosedWhilePending_CompletesWithNormalResult()
        {
            // Arrange
            OpenAndBeginPending();

            // Act
            _tracker.Close(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Arrange: session closed.");
            AssertCompletesNormally();
        }

        [Test]
        public void CompleteAttempt_SessionExpiredWhilePending_CompletesWithNormalResult()
        {
            // Arrange
            OpenAndBeginPending();

            // Act
            _clock.Advance(WELL_AFTER_FIRST_EXPIRY);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Arrange: session expired.");
            AssertCompletesNormally();
        }

        [Test]
        public void CompleteAttempt_SessionReplacedByOtherNpcWhilePending_CompletesWithNormalResult()
        {
            // Arrange
            OpenAndBeginPending();

            // Act
            _tracker.Open(Player, OTHER_NPC_ID);

            // Assert
            Assert.IsTrue(_tracker.IsActive(Player), "Arrange: replacement session active.");
            AssertCompletesNormally();
        }

        [Test]
        public void CompleteAttempt_ZoneTransitionWhilePending_CompletesWithNormalResult()
        {
            // Arrange
            OpenAndBeginPending();

            // Act
            _tracker.NotifyZoneTransition(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Arrange: session cleared.");
            AssertCompletesNormally();
        }

        // =====================================================================
        // Two players
        // =====================================================================

        [Test]
        public void Close_OnePlayer_DoesNotAffectOtherPlayersSession()
        {
            // Arrange
            _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            _tracker.Open(OtherPlayer, OTHER_NPC_ID);

            // Act
            _tracker.Close(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Player IsActive.");
            Assert.IsTrue(_tracker.IsActive(OtherPlayer), "Other player IsActive.");
            Assert.IsTrue(_tracker.TryGetActiveNpcId(OtherPlayer, out uint npcId), "Other player TryGetActiveNpcId.");
            Assert.AreEqual(OTHER_NPC_ID, npcId, "Other player npcId.");
        }

        [Test]
        public void NotifyZoneTransitionAndSessionEnded_OnePlayer_DoNotAffectOtherPlayersSession()
        {
            // Arrange
            _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            _tracker.Open(OtherPlayer, ENHANCEMENT_NPC_ID);

            // Act
            _tracker.NotifyZoneTransition(Player);
            _tracker.NotifySessionEnded(Player);

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Player IsActive.");
            Assert.IsTrue(_tracker.IsActive(OtherPlayer), "Other player IsActive.");
        }

        [Test]
        public void IsActive_OnePlayersSessionExpires_DoesNotAffectOtherPlayerOpenedLater()
        {
            // Arrange: Player opens at T, OtherPlayer at T + 100.
            _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            _clock.Advance(OTHER_PLAYER_OPEN_DELAY);
            _tracker.Open(OtherPlayer, ENHANCEMENT_NPC_ID);

            // Act: T + 350 is past Player's deadline (T + 300) and before OtherPlayer's (T + 400).
            _clock.Now = CLOCK_START + FIRST_PLAYER_EXPIRED_ONLY;

            // Assert
            Assert.IsFalse(_tracker.IsActive(Player), "Player IsActive.");
            Assert.IsTrue(_tracker.IsActive(OtherPlayer), "Other player IsActive.");

            // Act: both past their deadlines.
            _clock.Now = CLOCK_START + BOTH_EXPIRED;

            // Assert
            Assert.IsFalse(_tracker.IsActive(OtherPlayer), "Other player IsActive after its own deadline.");
        }

        [Test]
        public void Open_OnePlayerReopens_DoesNotRestartOtherPlayersLifetime()
        {
            // Arrange
            _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            _tracker.Open(OtherPlayer, ENHANCEMENT_NPC_ID);
            _clock.Advance(REOPEN_DELAY);

            // Act
            _tracker.Open(Player, ENHANCEMENT_NPC_ID);
            _clock.Now = CLOCK_START + SESSION_LIFETIME_SECONDS;

            // Assert
            Assert.IsTrue(_tracker.IsActive(Player), "Player IsActive (restarted).");
            Assert.IsFalse(_tracker.IsActive(OtherPlayer), "Other player IsActive (original deadline).");
        }

        // =====================================================================
        // npcId stored, not validated
        // =====================================================================

        [TestCase(0u)]
        [TestCase(ARBITRARY_NPC_ID)]
        [TestCase(uint.MaxValue)]
        public void Open_AnyNpcIdInTownHub_SucceedsAndStoresTheId(uint npcIdToOpen)
        {
            // Act
            var result = _tracker.Open(Player, npcIdToOpen);

            // Assert
            Assert.AreEqual(NpcInteractionOpenResult.NPCInteractionOpened, result, "Open result.");
            Assert.IsTrue(_tracker.IsActive(Player), "IsActive.");
            Assert.IsTrue(_tracker.TryGetActiveNpcId(Player, out uint stored), "TryGetActiveNpcId.");
            Assert.AreEqual(npcIdToOpen, stored, "Stored npcId.");
        }

        [Test]
        public void TryGetActiveNpcId_AfterSeveralOpens_ReturnsLastOpenedThenFalseAndZeroAfterClose()
        {
            // Arrange
            _tracker.Open(Player, 0u);
            _tracker.Open(Player, ARBITRARY_NPC_ID);
            _tracker.Open(Player, uint.MaxValue);

            // Act / Assert: the last id opened.
            Assert.IsTrue(_tracker.TryGetActiveNpcId(Player, out uint last), "TryGetActiveNpcId while active.");
            Assert.AreEqual(uint.MaxValue, last, "Last opened npcId.");

            // Act
            _tracker.Close(Player);

            // Assert
            Assert.IsFalse(_tracker.TryGetActiveNpcId(Player, out uint afterClose), "TryGetActiveNpcId after close.");
            Assert.AreEqual(0u, afterClose, "npcId after close.");
        }

        [Test]
        public void TryGetActiveNpcId_NeverOpened_ReturnsFalseAndZero()
        {
            // Act
            bool found = _tracker.TryGetActiveNpcId(Player, out uint npcId);

            // Assert
            Assert.IsFalse(found, "TryGetActiveNpcId.");
            Assert.AreEqual(0u, npcId, "npcId.");
        }

        [Test]
        public void TryGetActiveNpcId_AfterExpiry_ReturnsFalseAndZero()
        {
            // Arrange
            OpenAsPlayer(ARBITRARY_NPC_ID);
            _clock.Now = CLOCK_START + SESSION_LIFETIME_SECONDS;

            // Act
            bool found = _tracker.TryGetActiveNpcId(Player, out uint npcId);

            // Assert
            Assert.IsFalse(found, "TryGetActiveNpcId.");
            Assert.AreEqual(0u, npcId, "npcId.");
        }

        // =====================================================================
        // Constructor guards
        // =====================================================================

        [Test]
        public void Constructor_NullTownHub_ThrowsArgumentNull()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() =>
                new NpcInteractionSessionTracker(null, () => _clock.Now, SESSION_LIFETIME_SECONDS));
        }

        [Test]
        public void Constructor_NullClock_ThrowsArgumentNull()
        {
            // Act / Assert
            Assert.Throws<ArgumentNullException>(() =>
                new NpcInteractionSessionTracker(_townHub, null, SESSION_LIFETIME_SECONDS));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void Constructor_NonPositiveNaNOrInfiniteLifetime_ThrowsArgumentOutOfRange(double lifetime)
        {
            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NpcInteractionSessionTracker(_townHub, () => _clock.Now, lifetime));
        }
    }
}

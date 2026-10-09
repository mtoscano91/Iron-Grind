using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.EnhancementSystem;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.Networking;
using IronGrind.Tests.EditMode.Randomness;
using NUnit.Framework;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.Tests.EditMode.Integration.EnhancementSystem
{
    /// <summary>
    /// EditMode integration tests for Enhancement System Story 009: attempt exclusivity and held
    /// requests (design/gdd/enhancement-system.md CR-ENH-18, AC-ENH-38 cases A and B; ADR-011
    /// Decision 4; ADR-014 Decisions 4-6). Composes a real <see cref="InboundRequestDispatcher"/>
    /// (through <see cref="InboundDispatchHarness"/>), <see cref="CharacterMutationGate"/>,
    /// <see cref="TickCompletionQueue"/>, <see cref="IrreversibleOutcomeCoordinator"/>,
    /// <see cref="EnhancementService"/> and <see cref="InventoryService"/>. The write is a
    /// <see cref="TaskCompletionSource{TResult}"/> the test completes; nothing is awaited. Each test tick
    /// runs <c>Drain</c> and then <c>DispatchTick</c> (ADR-014 Decision 6). The held request types are
    /// test-only stand-ins for the bag move and discard messages, which do not exist in production yet.
    /// </summary>
    [TestFixture]
    internal sealed class Enhancement_AttemptExclusivity_Integration_Tests
    {
        private const uint CLIENT_PLAYER = InboundTestIds.ClientOne;
        private const uint CLIENT_OTHER = InboundTestIds.ClientTwo;

        private const uint BRONZE_SWORD_ID = 4001u;
        private const uint BRONZE_SCROLL_ID = 4005u;
        private const uint FILLER_ID = 4900u;
        private const int STACK_LIMIT_CONSUMABLE = 99;

        // Test-only held request types: R-OD, client to server, not rate limited, bodies of two bytes.
        private const ushort TYPE_HELD_MOVE = 0xE201;
        private const ushort TYPE_HELD_DISCARD = 0xE202;
        private const ushort BODY_BYTES = 2;

        private const int ITEM_SLOT = 0;
        private const int SCROLL_SLOT = 1;
        private const int FIRST_FILLER_SLOT = 2;
        private const int SECOND_FILLER_SLOT = 3;

        private const byte LEVEL_FOUR = 4;
        private const int STACK_OF_TWO = 2;

        // P_s[4] = 0.65: this draw destroys the item at level 4.
        private const double DRAW_DESTRUCTION_AT_LEVEL_FOUR = 0.99;

        private const string KIND_MOVE = "Move";
        private const string KIND_DISCARD = "Discard";

        private const string ENHANCEMENT_WRITE_FAILED_PATTERN = @"^\[EnhancementService\] CriticalEnhancementWriteFailed: ";
        private static readonly string CoordinatorWriteFailedPattern =
            $@"^\[IrreversibleOutcomeCoordinator\] PersistenceWriteFailed: clientId={CLIENT_PLAYER} .*write returned a non-success result";

        private static readonly CharacterID Player = InboundDispatchHarness.CharacterOf(CLIENT_PLAYER);
        private static readonly CharacterID OtherPlayer = InboundDispatchHarness.CharacterOf(CLIENT_OTHER);

        private static readonly ItemID BronzeSwordId = new ItemID(BRONZE_SWORD_ID);
        private static readonly ItemID BronzeScrollId = new ItemID(BRONZE_SCROLL_ID);
        private static readonly ItemID FillerId = new ItemID(FILLER_ID);

        /// <summary>The write result the fake persistence call returns.</summary>
        private enum WriteResult
        {
            Committed,
            DatabaseError
        }

        /// <summary>One call of a held-type handler, copied out of the context.</summary>
        private sealed class HandledRequest
        {
            public string Kind;
            public CharacterID Character;
            public bool WasHeld;
            public bool Success;
            public int OutcomesDeliveredBefore;
        }

        private List<ItemDefinition> _definitions;
        private StubItemDatabase _itemDatabase;
        private InventoryService _inventory;
        private ScriptedRandomProvider _random;
        private EnhancementService _service;
        private InboundDispatchHarness _harness;
        private TickCompletionQueue _queue;
        private IrreversibleOutcomeCoordinator _coordinator;
        private TaskCompletionSource<WriteResult> _write;
        private List<HandledRequest> _handled;
        private List<uint> _disconnectedClients;
        private List<uint> _preservedClients;
        private int _outcomesDelivered;
        private uint _tick;
        private uint _sequence;

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
                ItemDefinitionBuilder.Build(FILLER_ID, "Filler Material", ItemCategory.Consumable,
                    stackLimit: STACK_LIMIT_CONSUMABLE),
            };

            _itemDatabase = new StubItemDatabase();
            foreach (var definition in _definitions)
                _itemDatabase.Add(definition);

            _inventory = new InventoryService(_itemDatabase, () => 0u, EnhancementConfig.Default.MaxEnhancementLevel);
            _inventory.RegisterCharacter(Player);
            _inventory.RegisterCharacter(OtherPlayer);

            _random = new ScriptedRandomProvider();
            _service = new EnhancementService(_inventory, _itemDatabase, EnhancementConfig.Default, new StubNpcSessions(), _random);

            _harness = new InboundDispatchHarness(gate: new CharacterMutationGate());
            _harness.Routing.AddRow(TYPE_HELD_MOVE, "HeldMove", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            _harness.Routing.AddRow(TYPE_HELD_DISCARD, "HeldDiscard", NetworkChannel.ReliableOrdered, MessageDirection.ClientToServer);
            _harness.Dispatcher.Register(
                new InboundRequestDescriptor(TYPE_HELD_MOVE, RpcTypeTag.SetTarget, true, BODY_BYTES), HandleMove);
            _harness.Dispatcher.Register(
                new InboundRequestDescriptor(TYPE_HELD_DISCARD, RpcTypeTag.SetTarget, true, BODY_BYTES), HandleDiscard);
            _harness.Dispatcher.Seal();
            _harness.AddClient(CLIENT_PLAYER);
            _harness.AddClient(CLIENT_OTHER);

            _queue = new TickCompletionQueue();
            _coordinator = new IrreversibleOutcomeCoordinator(_queue, _harness.Gate);

            _write = new TaskCompletionSource<WriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _handled = new List<HandledRequest>();
            _disconnectedClients = new List<uint>();
            _preservedClients = new List<uint>();
            _outcomesDelivered = 0;
            _tick = 0u;
            _sequence = 0u;
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

        // The filler stack in slot n holds n items, so a stack identifies the slot it came from.
        private static int FillerQuantity(int slot) => slot;

        // AC-ENH-38 bag: Bronze item at +4 in slot 0, a stack of two Bronze scrolls in slot 1, slots 2 to 19 occupied.
        private void SeedAcEnh38Bag(CharacterID who)
        {
            _inventory.SeedSlotForTesting(who, ITEM_SLOT, BronzeSwordId, 1, LEVEL_FOUR);
            _inventory.SeedSlotForTesting(who, SCROLL_SLOT, BronzeScrollId, STACK_OF_TWO);
            SeedFillers(who);
        }

        private void SeedFillers(CharacterID who)
        {
            for (int slot = FIRST_FILLER_SLOT; slot < InventoryConstants.INVENTORY_SLOT_COUNT; slot++)
                _inventory.SeedSlotForTesting(who, slot, FillerId, FillerQuantity(slot));
        }

        // Starts the destruction attempt through the coordinator; the write stays open until the test completes it.
        private void BeginDestructionAttempt()
        {
            _random.EnqueueDouble(DRAW_DESTRUCTION_AT_LEVEL_FOUR);
            var result = _coordinator.Begin<EnhancementAttemptStart, WriteResult>(
                CLIENT_PLAYER,
                Player,
                () => true,
                () => { },
                () => _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT),
                (start, token) => _write.Task,
                writeResult => writeResult == WriteResult.Committed,
                start =>
                {
                    _service.CompleteAttempt(Player);
                    _outcomesDelivered++;
                },
                () => _service.RollBackAttempt(Player),
                (clientId, reason) =>
                {
                    _disconnectedClients.Add(clientId);
                    _harness.Dispatcher.RemoveConnection(clientId);
                },
                (clientId, ttlSeconds) => _preservedClients.Add(clientId));

            Assert.AreEqual(IrreversibleOutcomeBeginResult.Started, result, "Arrange: the coordinator must start the write.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "Arrange: the attempt must be pending.");
            Assert.AreEqual(0, _inventory.GetSlot(Player, ITEM_SLOT).Quantity, "Arrange: the destroyed item has left slot 0.");
            Assert.AreEqual(STACK_OF_TWO - 1, _inventory.GetSlot(Player, SCROLL_SLOT).Quantity, "Arrange: one scroll consumed.");
        }

        private void HandleMove(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            var move = _inventory.Move(context.CharacterId, body[0], body[1]);
            _handled.Add(new HandledRequest
            {
                Kind = KIND_MOVE,
                Character = context.CharacterId,
                WasHeld = context.WasHeld,
                Success = move.Success,
                OutcomesDeliveredBefore = _outcomesDelivered,
            });
        }

        private void HandleDiscard(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            var discard = _inventory.Discard(context.CharacterId, body[0], body[1]);
            _handled.Add(new HandledRequest
            {
                Kind = KIND_DISCARD,
                Character = context.CharacterId,
                WasHeld = context.WasHeld,
                Success = discard.Success,
                OutcomesDeliveredBefore = _outcomesDelivered,
            });
        }

        private void SendMove(uint clientId, int fromSlot, int toSlot)
        {
            _sequence++;
            Assert.IsTrue(
                _harness.SendRequest(clientId, TYPE_HELD_MOVE, _sequence, new[] { (byte)fromSlot, (byte)toSlot }),
                "The move request must be accepted into the inbox.");
        }

        private void SendDiscard(uint clientId, int slot, int quantity)
        {
            _sequence++;
            Assert.IsTrue(
                _harness.SendRequest(clientId, TYPE_HELD_DISCARD, _sequence, new[] { (byte)slot, (byte)quantity }),
                "The discard request must be accepted into the inbox.");
        }

        // One server tick in the order of ADR-014 Decision 6: completions first, then the request dispatcher.
        private void RunTick()
        {
            _tick++;
            _harness.TickSource.Tick = _tick;
            _queue.Drain(_tick);
            _harness.Dispatcher.DispatchTick(_tick);
        }

        private void AssertSlot(CharacterID who, int slot, ItemID expectedItem, int expectedQuantity, string label)
        {
            var actual = _inventory.GetSlot(who, slot);
            Assert.AreEqual(expectedItem, actual.ItemId, label + " item.");
            Assert.AreEqual(expectedQuantity, actual.Quantity, label + " quantity.");
        }

        private void AssertSlotEmpty(CharacterID who, int slot, string label)
        {
            AssertSlot(who, slot, ItemID.Invalid, 0, label);
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

        private int FindSword(CharacterID who)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                if (_inventory.GetSlot(who, i).ItemId == BronzeSwordId)
                    return i;
            }
            return -1;
        }

        private static void ExpectFailedWriteLogs()
        {
            // Occurrence order: the rollback logs first, then the coordinator's failure protocol.
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(ENHANCEMENT_WRITE_FAILED_PATTERN));
            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(CoordinatorWriteFailedPattern));
        }

        // =====================================================================
        // AC-ENH-38, case A: the write fails
        // =====================================================================

        [Test]
        public void HeldMove_WriteReturnsDatabaseError_ItemAndScrollsRestoredClientDisconnectedMoveNeverRuns_AC_ENH_38_CaseA()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();
            SendMove(CLIENT_PLAYER, FIRST_FILLER_SLOT, ITEM_SLOT);

            // Act: the move arrives while the write is in flight.
            RunTick();

            // Assert: in flight.
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress while in flight.");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 2 while in flight");
            Assert.AreEqual(1, _harness.Dispatcher.HeldCount(Player), "HeldCount while in flight.");
            Assert.AreEqual(0, _handled.Count, "The move must not run while in flight.");

            // Act: the write fails; the completion is delivered on the next tick.
            ExpectFailedWriteLogs();
            _write.SetResult(WriteResult.DatabaseError);
            RunTick();

            // Assert: rolled back, client disconnected, held move discarded.
            Assert.AreEqual(1, CountSwords(Player), "The item must be in the bag exactly once.");
            Assert.AreEqual(ITEM_SLOT, FindSword(Player), "The item returns to the freed slot 0.");
            Assert.AreEqual(LEVEL_FOUR, _inventory.GetSlot(Player, ITEM_SLOT).EnhancementLevel, "Restored level.");
            AssertSlot(Player, SCROLL_SLOT, BronzeScrollId, STACK_OF_TWO, "Slot 1");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 2 after rollback");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress after rollback.");
            CollectionAssert.AreEqual(new[] { CLIENT_PLAYER }, _disconnectedClients, "Disconnected clients.");
            CollectionAssert.AreEqual(new[] { CLIENT_PLAYER }, _preservedClients, "Clients with a preserved session.");
            Assert.AreEqual(0, _handled.Count, "The move must never run.");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount after the next DispatchTick.");

            // Act: one more tick; the discarded move must not be released late.
            RunTick();

            // Assert
            Assert.AreEqual(0, _handled.Count, "The move must not run on a later tick.");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount one tick later.");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 2 one tick later");

            // Only the two expected errors were logged, so there was no CriticalEnhancementRollbackFailed.
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // AC-ENH-38, case B: the write succeeds
        // =====================================================================

        [Test]
        public void HeldMove_WriteSucceeds_MoveRunsOnTheSameTickAfterTheOutcomeAndTakesFreedSlot_AC_ENH_38_CaseB()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();
            SendMove(CLIENT_PLAYER, FIRST_FILLER_SLOT, ITEM_SLOT);
            RunTick();
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "IsAttemptInProgress while in flight.");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 2 while in flight");

            // Act
            _write.SetResult(WriteResult.Committed);
            RunTick();

            // Assert
            Assert.AreEqual(1, _outcomesDelivered, "The destruction outcome must be delivered once.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress after the outcome.");
            Assert.AreEqual(1, _handled.Count, "The move must run on the tick that delivered the outcome.");
            Assert.AreEqual(1, _handled[0].OutcomesDeliveredBefore, "The outcome must be delivered before the move runs.");
            Assert.IsTrue(_handled[0].WasHeld, "WasHeld.");
            Assert.IsTrue(_handled[0].Success, "Move success.");
            AssertSlot(Player, ITEM_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 0 holds the item formerly in slot 2");
            AssertSlotEmpty(Player, FIRST_FILLER_SLOT, "Slot 2");
            Assert.AreEqual(0, CountSwords(Player), "The destroyed item stays destroyed.");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount after release.");
            CollectionAssert.IsEmpty(_disconnectedClients, "Disconnected clients.");
            LogAssert.NoUnexpectedReceived();
        }

        // =====================================================================
        // CR-ENH-18: held requests
        // =====================================================================

        [Test]
        public void HeldMove_WhileWriteInFlight_NotAppliedAndHeldThenAppliedAfterWriteSucceeds_CR_ENH_18()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();

            // Act: a move for another slot of the same character arrives while in flight.
            SendMove(CLIENT_PLAYER, SECOND_FILLER_SLOT, ITEM_SLOT);
            RunTick();

            // Assert: held, bag unchanged.
            Assert.AreEqual(1, _harness.Dispatcher.HeldCount(Player), "HeldCount while in flight.");
            Assert.AreEqual(0, _handled.Count, "Handler calls while in flight.");
            AssertSlot(Player, SECOND_FILLER_SLOT, FillerId, FillerQuantity(SECOND_FILLER_SLOT), "Slot 3 while in flight");
            AssertSlotEmpty(Player, ITEM_SLOT, "Slot 0 while in flight");

            // Act
            _write.SetResult(WriteResult.Committed);
            RunTick();

            // Assert: applied after the write succeeded.
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount after release.");
            Assert.AreEqual(1, _handled.Count, "Handler calls after release.");
            Assert.IsTrue(_handled[0].Success, "Move success.");
            AssertSlot(Player, ITEM_SLOT, FillerId, FillerQuantity(SECOND_FILLER_SLOT), "Slot 0 after release");
            AssertSlotEmpty(Player, SECOND_FILLER_SLOT, "Slot 3 after release");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HeldRequests_MoveThenDiscardOfTheSameSlot_AppliedInArrivalOrderBeforeNewRequestOfTheReleaseTick()
        {
            // Arrange: move 2 to 0 then discard slot 0. Applied in the other order the discard would find
            // slot 0 empty and the moved stack would stay in slot 0.
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();
            SendMove(CLIENT_PLAYER, FIRST_FILLER_SLOT, ITEM_SLOT);
            SendDiscard(CLIENT_PLAYER, ITEM_SLOT, FillerQuantity(FIRST_FILLER_SLOT));
            RunTick();
            Assert.AreEqual(2, _harness.Dispatcher.HeldCount(Player), "HeldCount while in flight.");
            Assert.AreEqual(0, _handled.Count, "Handler calls while in flight.");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 2 while in flight");

            // Act: a new request arrives on the tick that releases the held ones.
            SendMove(CLIENT_PLAYER, SECOND_FILLER_SLOT, FIRST_FILLER_SLOT);
            _write.SetResult(WriteResult.Committed);
            RunTick();

            // Assert
            Assert.AreEqual(3, _handled.Count, "Handler calls.");
            Assert.AreEqual(KIND_MOVE, _handled[0].Kind, "First call kind.");
            Assert.IsTrue(_handled[0].WasHeld, "First call WasHeld.");
            Assert.AreEqual(KIND_DISCARD, _handled[1].Kind, "Second call kind.");
            Assert.IsTrue(_handled[1].WasHeld, "Second call WasHeld.");
            Assert.IsTrue(_handled[1].Success, "The discard must find the moved stack in slot 0.");
            Assert.AreEqual(KIND_MOVE, _handled[2].Kind, "Third call kind.");
            Assert.IsFalse(_handled[2].WasHeld, "The new request is not held.");
            AssertSlotEmpty(Player, ITEM_SLOT, "Slot 0 after move and discard");
            AssertSlot(Player, FIRST_FILLER_SLOT, FillerId, FillerQuantity(SECOND_FILLER_SLOT),
                "Slot 2 holds the stack of the new move, into the slot the held move vacated");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount after release.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void BeginAttempt_WhileWriteInFlight_RejectedConcurrentAttemptAndNotHeld()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();
            SendMove(CLIENT_PLAYER, FIRST_FILLER_SLOT, ITEM_SLOT);
            RunTick();
            int heldBefore = _harness.Dispatcher.HeldCount(Player);
            Assert.AreEqual(1, heldBefore, "Arrange: one request held.");

            // Act
            var second = _service.BeginAttempt(Player, ITEM_SLOT, SCROLL_SLOT);

            // Assert: rejected, not held.
            Assert.IsFalse(second.IsPending, "IsPending.");
            Assert.AreEqual(EnhancementResultCode.RejectedConcurrentAttempt, second.RejectionCode, "RejectionCode.");
            Assert.AreEqual(heldBefore, _harness.Dispatcher.HeldCount(Player), "HeldCount must not change.");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "The first attempt stays pending.");

            // Act: the first attempt's write succeeds.
            _write.SetResult(WriteResult.Committed);
            RunTick();

            // Assert: the rejected call did not disturb the first attempt or the held request.
            Assert.AreEqual(1, _outcomesDelivered, "The first attempt's outcome must be delivered once.");
            Assert.IsFalse(_service.IsAttemptInProgress(Player), "IsAttemptInProgress after the outcome.");
            Assert.AreEqual(1, _handled.Count, "The held move must run once.");
            Assert.IsTrue(_handled[0].WasHeld, "WasHeld.");
            Assert.IsTrue(_handled[0].Success, "Move success.");
            AssertSlot(Player, ITEM_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Slot 0 after release");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount after release.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HeldTypeRequest_OtherCharacterDuringWindow_RunsOnTheTickItArrivesNotHeld()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            SeedFillers(OtherPlayer);
            BeginDestructionAttempt();

            // Act: both characters send a move on the same tick; the player's arrives first.
            SendMove(CLIENT_PLAYER, SECOND_FILLER_SLOT, ITEM_SLOT);
            SendMove(CLIENT_OTHER, FIRST_FILLER_SLOT, ITEM_SLOT);
            RunTick();

            // Assert: the player's move is held, the other character's runs.
            Assert.AreEqual(1, _handled.Count, "Handler calls.");
            Assert.AreEqual(OtherPlayer, _handled[0].Character, "Character.");
            Assert.IsFalse(_handled[0].WasHeld, "WasHeld.");
            Assert.IsTrue(_handled[0].Success, "Move success.");
            AssertSlot(OtherPlayer, ITEM_SLOT, FillerId, FillerQuantity(FIRST_FILLER_SLOT), "Other character slot 0");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(OtherPlayer), "Other character HeldCount.");
            Assert.AreEqual(1, _harness.Dispatcher.HeldCount(Player), "Player HeldCount.");
            AssertSlot(Player, SECOND_FILLER_SLOT, FillerId, FillerQuantity(SECOND_FILLER_SLOT), "Player slot 3");
            Assert.IsTrue(_service.IsAttemptInProgress(Player), "The player's attempt is still pending.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void HeldTypeRequest_ArrivesAfterOutcomeDelivered_RunsWithWasHeldFalse()
        {
            // Arrange
            SeedAcEnh38Bag(Player);
            BeginDestructionAttempt();
            _write.SetResult(WriteResult.Committed);
            RunTick();
            Assert.AreEqual(1, _outcomesDelivered, "Arrange: the outcome must be delivered.");
            Assert.AreEqual(0, _handled.Count, "Arrange: nothing was held.");

            // Act
            SendMove(CLIENT_PLAYER, SECOND_FILLER_SLOT, ITEM_SLOT);
            RunTick();

            // Assert
            Assert.AreEqual(1, _handled.Count, "Handler calls.");
            Assert.IsFalse(_handled[0].WasHeld, "WasHeld.");
            Assert.IsTrue(_handled[0].Success, "Move success.");
            AssertSlot(Player, ITEM_SLOT, FillerId, FillerQuantity(SECOND_FILLER_SLOT), "Slot 0");
            Assert.AreEqual(0, _harness.Dispatcher.HeldCount(Player), "HeldCount.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}

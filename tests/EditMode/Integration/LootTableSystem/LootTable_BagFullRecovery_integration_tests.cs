using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.InventorySystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.LootTableSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.LootTableSystem
{
    /// <summary>
    /// EditMode integration tests for Loot Table Story 008: bag-full blocked notice, in-radius retry
    /// and expiry warning (design/gdd/loot-table-system.md CR-LT-13.2, CR-LT-13.3, AC-LT-9 retry part,
    /// AC-LT-22, AC-LT-23, AC-LT-24 first half), using a real <see cref="GroundItemService"/> over a
    /// real <see cref="InventoryService"/> with a seeded item database.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_BagFullRecovery_Integration_Tests
    {
        private const uint FILLER_A_ID = 2001u;
        private const uint FILLER_B_ID = 2002u;
        private const uint DROP_ID = 2003u;
        private const uint DROP_B_ID = 2004u;
        private const uint UNKNOWN_ID = 2999u;
        private const string FILLER_A_NAME = "Filler A";
        private const string FILLER_B_NAME = "Filler B";
        private const string DROP_NAME = "Iron Sword";
        private const string DROP_B_NAME = "Iron Shield";
        private const uint RAW_CHAR_42 = 42u;
        private const uint RAW_CHAR_43 = 43u;

        private const uint SPAWN_TICK = 100u;
        private const uint ASSIGNED_TICK = SPAWN_TICK + 1u;
        private const uint ENTRY_TICK = ASSIGNED_TICK + 1u;
        private const uint NEXT_TICK = ENTRY_TICK + 1u;
        private const uint LEAVE_TICK = ENTRY_TICK + 1u;
        private const uint DISCARD_TICK = LEAVE_TICK + 1u;
        private const uint REENTRY_TICK = DISCARD_TICK + 1u;
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint TICK_BEFORE_EXPIRY = EXPIRY_TICK - 1u;
        private const uint WARNING_TICK = EXPIRY_TICK - (uint)LootTableConstants.EXPIRY_WARNING_TICKS;
        private const uint TICK_BEFORE_WARNING = WARNING_TICK - 1u;

        private const float FAR_DISTANCE = 10f;
        private const float NEAR_DISTANCE = 1f;
        private const float NO_OFFSET = 0f;
        private const int PICKUP_QUANTITY = 1;
        private const int DISCARD_QUANTITY = 1;
        private const int EMPTY_QUANTITY = 0;
        private const int SLOT_COUNT = InventoryConstants.INVENTORY_SLOT_COUNT;
        private const int SWAP_SLOT = 0;
        private const int SWAP_PARTNER_SLOT = 1;
        private const int FREED_SLOT = 5;
        private const int NO_EVENTS = 0;
        private const int ONE_EVENT = 1;
        private const int TWO_EVENTS = 2;
        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int THREE_CALLS = 3;
        private const string SUBSCRIBER_FAILURE = "subscriber failure";
        private const string LOOKUP_FAILURE = "name lookup failure";

        private static readonly CharacterID Char42 = new CharacterID(RAW_CHAR_42);
        private static readonly CharacterID Char43 = new CharacterID(RAW_CHAR_43);
        private static readonly ItemID FillerA = new ItemID(FILLER_A_ID);
        private static readonly ItemID FillerB = new ItemID(FILLER_B_ID);
        private static readonly ItemID DropItem = new ItemID(DROP_ID);
        private static readonly ItemID DropItemB = new ItemID(DROP_B_ID);
        private static readonly ItemID UnknownItem = new ItemID(UNKNOWN_ID);

        // Not the origin, so a distance computed from the character's position alone would fail.
        private static readonly Vector3 ItemPosition = new Vector3(20f, 5f, -8f);
        private static readonly Vector3 FarPosition = ItemPosition + new Vector3(FAR_DISTANCE, NO_OFFSET, NO_OFFSET);
        private static readonly Vector3 NearPosition = ItemPosition + new Vector3(NEAR_DISTANCE, NO_OFFSET, NO_OFFSET);

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private readonly List<GroundItemService> _services = new List<GroundItemService>();

        [TearDown]
        public void TearDown()
        {
            foreach (GroundItemService service in _services)
            {
                service.Dispose();
            }
            _services.Clear();
            foreach (ItemDefinition def in _created)
            {
                if (def != null)
                {
                    UnityEngine.Object.DestroyImmediate(def);
                }
            }
            _created.Clear();
        }

        // -----------------------------------------------------------------------
        // Rig
        // -----------------------------------------------------------------------

        private sealed class Rig
        {
            public InventoryService Inventory;
            public GroundItemService Ground;
            public SettablePositionProvider Positions;
            public readonly List<BagFullPickupBlockedEventArgs> Blocked = new List<BagFullPickupBlockedEventArgs>();
            public readonly List<GroundItemExpiryWarningEventArgs> Warnings = new List<GroundItemExpiryWarningEventArgs>();

            // How many OnInventoryChanged events the real inventory raised.
            public int InventoryEvents;

            public void RecordBlocked(BagFullPickupBlockedEventArgs args) => Blocked.Add(args);

            public void RecordWarning(GroundItemExpiryWarningEventArgs args) => Warnings.Add(args);

            public void CountInventoryEvent(InventoryChangedEventArgs args) => InventoryEvents++;
        }

        // The same service over the shared recording fake: Pickup calls and HasFreeSlot questions
        // can be counted, and the test decides when the inventory "changes".
        private sealed class FakeRig
        {
            public RecordingInventoryService Inventory;
            public GroundItemService Ground;
            public SettablePositionProvider Positions;
            public readonly List<BagFullPickupBlockedEventArgs> Blocked = new List<BagFullPickupBlockedEventArgs>();
            public readonly List<GroundItemExpiryWarningEventArgs> Warnings = new List<GroundItemExpiryWarningEventArgs>();

            public void RecordBlocked(BagFullPickupBlockedEventArgs args) => Blocked.Add(args);

            public void RecordWarning(GroundItemExpiryWarningEventArgs args) => Warnings.Add(args);
        }

        private sealed class ThrowingItemDatabase : IItemDatabase
        {
            public bool IsReady => true;

            public event Action OnDatabaseReady
            {
                add { }
                remove { }
            }

            public ItemDefinition GetItem(ItemID id) => throw new InvalidOperationException(LOOKUP_FAILURE);

            public bool TryGetItem(ItemID id, out ItemDefinition item) => throw new InvalidOperationException(LOOKUP_FAILURE);

            public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category) => new List<ItemDefinition>();
        }

        private FakeRig BuildFakeRig(IItemDatabase names = null)
        {
            var rig = new FakeRig
            {
                Inventory = new RecordingInventoryService(),
                Positions = new SettablePositionProvider(),
            };
            rig.Ground = new GroundItemService(
                new LootEquipmentCache(new EmptyItemDatabase()),
                rig.Inventory,
                rig.Positions,
                names ?? new EmptyItemDatabase());
            rig.Ground.OnBagFullPickupBlocked += rig.RecordBlocked;
            rig.Ground.OnGroundItemExpiryWarning += rig.RecordWarning;
            _services.Add(rig.Ground);
            return rig;
        }

        // Fake rig in the blocked state: one failed entry for 42 (one Pickup call, one notice).
        private static GroundItemID BuildFakeBlockedState(FakeRig rig)
        {
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            return id;
        }

        private ItemDefinition BuildEquipment(uint id, string name)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id,
                name,
                ItemCategory.Equipment,
                stackLimit: 1,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));
            _created.Add(def);
            return def;
        }

        private Rig BuildRig()
        {
            var database = new StubItemDatabase();
            database.Add(BuildEquipment(FILLER_A_ID, FILLER_A_NAME));
            database.Add(BuildEquipment(FILLER_B_ID, FILLER_B_NAME));
            database.Add(BuildEquipment(DROP_ID, DROP_NAME));
            database.Add(BuildEquipment(DROP_B_ID, DROP_B_NAME));

            var rig = new Rig
            {
                Inventory = new InventoryService(database, () => 0u),
                Positions = new SettablePositionProvider(),
            };
            rig.Inventory.OnInventoryChanged += rig.CountInventoryEvent;
            rig.Inventory.RegisterCharacter(Char42);
            rig.Ground = new GroundItemService(new LootEquipmentCache(database), rig.Inventory, rig.Positions, database);
            rig.Ground.OnBagFullPickupBlocked += rig.RecordBlocked;
            rig.Ground.OnGroundItemExpiryWarning += rig.RecordWarning;
            _services.Add(rig.Ground);
            return rig;
        }

        // Fills all 20 slots with non-stackable items. Slot 0 holds a different item than the others,
        // so a Move between slot 0 and slot 1 is a swap (an event) and not a no-op merge.
        private static void FillBag(Rig rig, CharacterID character)
        {
            for (int slot = 0; slot < SLOT_COUNT; slot++)
            {
                ItemID item = slot == SWAP_SLOT ? FillerB : FillerA;
                rig.Inventory.SeedSlotForTesting(character, slot, item, PICKUP_QUANTITY);
            }
            Assert.AreEqual(SLOT_COUNT, rig.Inventory.FilledSlots(character));
            Assert.IsFalse(rig.Inventory.HasFreeSlot(character));
        }

        private static GroundItemID SpawnAssignedFar(Rig rig, ItemID item)
        {
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(item, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            return id;
        }

        // The blocked state: bag full, item assigned, assignee inside the radius, one notice raised.
        private static GroundItemID BuildBlockedState(Rig rig)
        {
            FillBag(rig, Char42);
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            return id;
        }

        private static void Discard(Rig rig, int slot)
        {
            DiscardResult result = rig.Inventory.Discard(Char42, slot, DISCARD_QUANTITY);
            Assert.IsTrue(result.Success);
        }

        private static GroundItem Get(Rig rig, GroundItemID id)
        {
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            return item;
        }

        private static GroundItem GetFake(FakeRig rig, GroundItemID id)
        {
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            return item;
        }

        // -----------------------------------------------------------------------
        // AC-LT-22: blocked notice
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AssigneeEntersRadiusWithFullBag_RaisesOneBlockedNoticeWithPayload()
        {
            // Arrange
            Rig rig = BuildRig();
            FillBag(rig, Char42);
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert: the remaining ticks are computed from the spawn tick and the TTL constant,
            // not read back from the service
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            BagFullPickupBlockedEventArgs notice = rig.Blocked[0];
            Assert.AreEqual(Char42, notice.Recipient);
            Assert.AreEqual(id, notice.GroundItemId);
            Assert.AreEqual(DropItem, notice.ItemId);
            Assert.AreEqual(DROP_NAME, notice.DisplayName);
            Assert.AreEqual(EXPIRY_TICK - ENTRY_TICK, notice.RemainingTicks);
            Assert.AreEqual(GroundItemState.Assigned, Get(rig, id).State);
        }

        [Test]
        public void Tick_AssigneeEntersRadiusWithFreeSlot_DeliversAndRaisesNoBlockedNotice()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
        }

        // -----------------------------------------------------------------------
        // AC-LT-23 / AC-LT-9: in-radius retry
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_BlockedAssigneeDiscardsSlotWhileInside_DeliversOnNextTickWithoutSecondNotice()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);

            // Act / Assert: the discard raises OnInventoryChanged, and nothing is retried inside it —
            // the item is still on the ground until the next Tick
            Assert.DoesNotThrow(() => Discard(rig, FREED_SLOT));
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));

            // Act: one Tick, with no movement
            Assert.DoesNotThrow(() => rig.Ground.Tick(NEXT_TICK));

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
            Assert.AreEqual(SLOT_COUNT, rig.Inventory.FilledSlots(Char42));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        [Test]
        public void Tick_InventoryChangeFreesNoSlot_DoesNotRetry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            int eventsBefore = rig.InventoryEvents;

            // Act: swapping two occupied slots really is an inventory change, but frees nothing
            MoveResult move = rig.Inventory.Move(Char42, SWAP_SLOT, SWAP_PARTNER_SLOT);
            Assert.IsTrue(move.Success);
            Assert.AreEqual(eventsBefore + ONE_EVENT, rig.InventoryEvents);
            rig.Ground.Tick(NEXT_TICK);

            // Assert (that no Pickup was attempted is proven on the fake, below)
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        // The real inventory cannot show whether a Pickup was attempted: a retry that fails on a
        // full bag looks the same as no retry. These three count the calls on the recording fake.
        [Test]
        public void Tick_AssigneeInventoryChangedWithoutFreeSlot_AsksForFreeSlotAndAttemptsNoPickup()
        {
            // Arrange
            FakeRig rig = BuildFakeRig();
            GroundItemID id = BuildFakeBlockedState(rig);
            rig.Inventory.FreeSlot = false;

            // Act
            rig.Inventory.RaiseInventoryChanged(Char42);
            rig.Ground.Tick(NEXT_TICK);

            // Assert: the gate was consulted once and stopped the retry
            Assert.AreEqual(ONE_CALL, rig.Inventory.HasFreeSlotCalls.Count);
            Assert.AreEqual(Char42, rig.Inventory.HasFreeSlotCalls[0]);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_OtherCharacterInventoryChangedWithFreeSlot_AsksNothingAndAttemptsNoPickup()
        {
            // Arrange
            FakeRig rig = BuildFakeRig();
            GroundItemID id = BuildFakeBlockedState(rig);
            rig.Inventory.FreeSlot = true;

            // Act
            rig.Inventory.RaiseInventoryChanged(Char43);
            rig.Ground.Tick(NEXT_TICK);

            // Assert
            Assert.AreEqual(NO_CALLS, rig.Inventory.HasFreeSlotCalls.Count);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_InRadiusRetryFailsAgain_StaysBlockedWithoutSecondNoticeAndCanRetryLater()
        {
            // Arrange: a free slot is reported, but the pickup still fails on a full bag
            FakeRig rig = BuildFakeRig();
            GroundItemID id = BuildFakeBlockedState(rig);
            rig.Inventory.FreeSlot = true;

            // Act: first retry
            rig.Inventory.RaiseInventoryChanged(Char42);
            rig.Ground.Tick(NEXT_TICK);

            // Assert: attempted, failed silently, still on the ground
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(GroundItemState.Assigned, GetFake(rig, id).State);

            // Act / Assert: a tick without a new change retries nothing
            rig.Ground.Tick(NEXT_TICK + 1u);
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);

            // Act / Assert: still blocked, so the next change retries again and delivers
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            rig.Inventory.RaiseInventoryChanged(Char42);
            rig.Ground.Tick(NEXT_TICK + 2u);
            Assert.AreEqual(THREE_CALLS, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        [Test]
        public void Tick_OtherCharacterInventoryChanges_DoesNotRetry()
        {
            // Arrange: 42 has a free slot, but the seed raises no inventory event for 42
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            rig.Inventory.SeedSlotForTesting(Char42, FREED_SLOT, ItemID.Invalid, EMPTY_QUANTITY);
            Assert.IsTrue(rig.Inventory.HasFreeSlot(Char42));
            rig.Inventory.RegisterCharacter(Char43);

            // Act: a different character's inventory changes
            PickupResult other = rig.Inventory.Pickup(Char43, FillerA, PICKUP_QUANTITY);
            Assert.IsTrue(other.Success);
            rig.Ground.Tick(NEXT_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));
        }

        [Test]
        public void Tick_SlotFreedThenRefilledBeforeTick_DoesNotRetryAndRaisesNoSecondNotice()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            Discard(rig, FREED_SLOT);
            PickupResult refill = rig.Inventory.Pickup(Char42, FillerA, PICKUP_QUANTITY);
            Assert.IsTrue(refill.Success);
            Assert.IsFalse(rig.Inventory.HasFreeSlot(Char42));

            // Act
            rig.Ground.Tick(NEXT_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        [Test]
        public void Tick_TwoBlockedItemsAndOneFreedSlot_DeliversExactlyOne()
        {
            // Arrange: two different items blocked for the same character
            Rig rig = BuildRig();
            FillBag(rig, Char42);
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID first = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            GroundItemID second = rig.Ground.Spawn(DropItemB, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(TWO_EVENTS, rig.Blocked.Count);

            // Act
            Discard(rig, FREED_SLOT);
            rig.Ground.Tick(NEXT_TICK);

            // Assert: the free slot is asked for per item, so only one fits
            bool firstLeft = rig.Ground.TryGetGroundItem(first, out _);
            bool secondLeft = rig.Ground.TryGetGroundItem(second, out _);
            Assert.AreNotEqual(firstLeft, secondLeft);
            Assert.AreNotEqual(rig.Inventory.HasItem(Char42, DropItem), rig.Inventory.HasItem(Char42, DropItemB));
            Assert.AreEqual(SLOT_COUNT, rig.Inventory.FilledSlots(Char42));
            Assert.AreEqual(TWO_EVENTS, rig.Blocked.Count);
        }

        [Test]
        public void Tick_InventoryChangesDuringATick_IsKeptForTheNextTick()
        {
            // Arrange: the blocked notice's subscriber discards a slot during the entry Tick, after
            // that Tick took its snapshot of inventory changes
            Rig rig = BuildRig();
            FillBag(rig, Char42);
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Ground.OnBagFullPickupBlocked += _ => Discard(rig, FREED_SLOT);
            rig.Positions.Set(Char42, NearPosition);

            // Act / Assert: nothing is delivered in the Tick that raised the change
            rig.Ground.Tick(ENTRY_TICK);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));

            // Act / Assert: the change was not lost; the next Tick retries
            rig.Ground.Tick(NEXT_TICK);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
        }

        [Test]
        public void Tick_BlockedAssigneeDiscardsThenLeavesBeforeTheTick_DoesNotRetry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);

            // Act: the slot is freed, but the assignee is outside when the Tick runs
            Discard(rig, FREED_SLOT);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(NEXT_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));
        }

        [Test]
        public void Tick_BlockedAssigneeDisconnectsThenFreesSlot_NoRetryUntilReconnectInside()
        {
            // Arrange: a disconnect (no position) counts as leaving the radius
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            rig.Positions.Remove(Char42);
            rig.Ground.Tick(LEAVE_TICK);

            // Act / Assert: freeing a slot while disconnected retries nothing
            Discard(rig, FREED_SLOT);
            rig.Ground.Tick(DISCARD_TICK);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));

            // Act / Assert: reconnecting on the item is a new entry and delivers it
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
        }

        [Test]
        public void Tick_AssigneeReentersWithBagStillFull_RaisesSecondNoticeWithNewRemainingTicks()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);

            // Act
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);

            // Assert: one notice per blocked entry
            Assert.AreEqual(TWO_EVENTS, rig.Blocked.Count);
            Assert.AreEqual(id, rig.Blocked[1].GroundItemId);
            Assert.AreEqual(EXPIRY_TICK - REENTRY_TICK, rig.Blocked[1].RemainingTicks);
        }

        // Regression (code review): a Tick re-entered from inside a retry's pickup must not wipe the
        // outer Tick's pending entry pickups.
        [Test]
        public void Tick_ReenteredDuringRetryPickup_StillPerformsTheEntryPickupOfTheSameTick()
        {
            // Arrange: X is blocked for 42; Y is assigned to 43, who is still far away
            FakeRig rig = BuildFakeRig();
            rig.Positions.Set(Char43, FarPosition);
            GroundItemID y = rig.Ground.Spawn(DropItemB, ItemPosition, Char43, SPAWN_TICK);
            GroundItemID x = BuildFakeBlockedState(rig);
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            rig.Inventory.FreeSlot = true;
            bool reentered = false;
            rig.Inventory.OnPickup = (character, item) =>
            {
                if (!reentered)
                {
                    reentered = true;
                    rig.Ground.Tick(NEXT_TICK);
                }
            };

            // Act: on one tick, 42's inventory has changed (a retry) and 43 enters the radius (an entry)
            rig.Inventory.RaiseInventoryChanged(Char42);
            rig.Positions.Set(Char43, NearPosition);
            rig.Ground.Tick(NEXT_TICK);

            // Assert: the failed entry, the retry of X, and the entry pickup of Y
            Assert.IsTrue(reentered);
            Assert.AreEqual(THREE_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(Char43, rig.Inventory.Calls[2].Character);
            Assert.AreEqual(DropItemB, rig.Inventory.Calls[2].Item);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(x, out _));
            Assert.IsFalse(rig.Ground.TryGetGroundItem(y, out _));
        }

        // -----------------------------------------------------------------------
        // CR-LT-13.2 exit
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_BlockedAssigneeLeavesRadiusThenFreesSlot_NoPickupUntilReentry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);

            // Act: free a slot while outside
            Discard(rig, FREED_SLOT);
            rig.Ground.Tick(DISCARD_TICK);

            // Assert: no pickup while outside
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));

            // Act: come back
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);

            // Assert: Story 007's retry on re-entry delivers it, with no new notice
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        // -----------------------------------------------------------------------
        // AC-LT-24 (first half) and CR-LT-13.3
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AssignedItemReachesWarningThreshold_RaisesOneWarningWithPayload()
        {
            // Arrange: expiry is 601 ticks ahead of the current tick
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Ground.Tick(TICK_BEFORE_WARNING);
            Assert.AreEqual(NO_EVENTS, rig.Warnings.Count);
            Assert.AreEqual(EXPIRY_TICK - TICK_BEFORE_WARNING, (uint)LootTableConstants.EXPIRY_WARNING_TICKS + 1u);

            // Act: advance one tick
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
            GroundItemExpiryWarningEventArgs warning = rig.Warnings[0];
            Assert.AreEqual(Char42, warning.Recipient);
            Assert.AreEqual(id, warning.GroundItemId);
            Assert.AreEqual(DropItem, warning.ItemId);
            Assert.AreEqual(DROP_NAME, warning.DisplayName);
            Assert.AreEqual((uint)LootTableConstants.EXPIRY_WARNING_TICKS, warning.RemainingTicks);
        }

        [Test]
        public void Tick_AfterWarning_RaisesNoSecondWarning()
        {
            // Arrange
            Rig rig = BuildRig();
            SpawnAssignedFar(rig, DropItem);
            rig.Ground.Tick(WARNING_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);

            // Act
            rig.Ground.Tick(WARNING_TICK + 1u);
            rig.Ground.Tick(WARNING_TICK + 2u);
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
        }

        [Test]
        public void Tick_ItemDeliveredBeforeThreshold_RaisesNoWarning()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));

            // Act
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Warnings.Count);
        }

        [Test]
        public void Tick_ItemPickedUpOnThresholdTick_RaisesNoWarning()
        {
            // Arrange: the assignee arrives exactly on the warning tick
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Ground.Tick(TICK_BEFORE_WARNING);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(WARNING_TICK);

            // Assert: the pickup step runs before the warning step
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsTrue(rig.Inventory.HasItem(Char42, DropItem));
            Assert.AreEqual(NO_EVENTS, rig.Warnings.Count);
        }

        // Claiming is only visible to a Tick that runs inside the pickup call. That nested Tick is on
        // the threshold tick and must not warn about the item being claimed.
        [Test]
        public void Tick_ReenteredOnThresholdTickWhileItemIsClaiming_RaisesNoWarning()
        {
            // Arrange: the assignee arrives exactly on the warning tick
            FakeRig rig = BuildFakeRig();
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(TICK_BEFORE_WARNING);
            rig.Positions.Set(Char42, NearPosition);
            GroundItemState stateDuringPickup = GroundItemState.Despawned;
            rig.Inventory.OnPickup = (character, item) =>
            {
                rig.Ground.Tick(WARNING_TICK);
                stateDuringPickup = GetFake(rig, id).State;
            };

            // Act
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(GroundItemState.Claiming, stateDuringPickup);
            Assert.AreEqual(NO_EVENTS, rig.Warnings.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_BlockedItemWithAssigneeInsideReachesThreshold_StillRaisesTheWarning()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);

            // Act
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
            Assert.AreEqual(id, rig.Warnings[0].GroundItemId);
            Assert.AreEqual(Char42, rig.Warnings[0].Recipient);
        }

        [Test]
        public void Tick_NameLookupThrows_LogsAndStillRaisesTheWarningWithEmptyNameAndExpiresItems()
        {
            // Arrange
            FakeRig rig = BuildFakeRig(new ThrowingItemDatabase());
            var despawned = new List<GroundItemDespawnedEventArgs>();
            rig.Ground.OnGroundItemDespawned += despawned.Add;
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            LogAssert.Expect(LogType.Exception, new Regex(LOOKUP_FAILURE));

            // Act / Assert: the warning goes out without a name
            Assert.DoesNotThrow(() => rig.Ground.Tick(WARNING_TICK));
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
            Assert.AreEqual(string.Empty, rig.Warnings[0].DisplayName);

            // Act / Assert: the rest of the lifecycle is unaffected
            rig.Ground.Tick(EXPIRY_TICK);
            Assert.AreEqual(ONE_EVENT, despawned.Count);
            Assert.AreEqual(id, despawned[0].GroundItemId);
        }

        [Test]
        public void Tick_WarningForItemUnknownToDatabase_HasEmptyDisplayName()
        {
            // Arrange
            Rig rig = BuildRig();
            SpawnAssignedFar(rig, UnknownItem);

            // Act
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
            Assert.AreEqual(UnknownItem, rig.Warnings[0].ItemId);
            Assert.AreEqual(string.Empty, rig.Warnings[0].DisplayName);
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_PickupFailsOnExpiryTick_RaisesNoBlockedNoticeAndItemDespawns()
        {
            // Arrange
            Rig rig = BuildRig();
            FillBag(rig, Char42);
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(EXPIRY_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Dispose_AfterDispose_InventoryChangeDoesNotTriggerRetry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBlockedState(rig);
            rig.Ground.Dispose();

            // Act
            Discard(rig, FREED_SLOT);
            rig.Ground.Tick(NEXT_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Inventory.HasItem(Char42, DropItem));
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            // Arrange
            Rig rig = BuildRig();

            // Act / Assert
            Assert.DoesNotThrow(() =>
            {
                rig.Ground.Dispose();
                rig.Ground.Dispose();
            });
        }

        [Test]
        public void Tick_BlockedNoticeSubscriberThrows_LogsAndTickStillCompletes()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Ground.OnBagFullPickupBlocked += _ => throw new InvalidOperationException(SUBSCRIBER_FAILURE);
            FillBag(rig, Char42);
            GroundItemID id = SpawnAssignedFar(rig, DropItem);
            rig.Positions.Set(Char42, NearPosition);
            LogAssert.Expect(LogType.Exception, new Regex(SUBSCRIBER_FAILURE));

            // Act
            Assert.DoesNotThrow(() => rig.Ground.Tick(ENTRY_TICK));

            // Assert: the Tick completed, the notice was raised once, and the item is still on the ground
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(GroundItemState.Assigned, Get(rig, id).State);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }
    }
}

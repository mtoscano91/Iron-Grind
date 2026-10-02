using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using IronGrind.Tests.EditMode.LootTableSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.LootTableSystem
{
    /// <summary>
    /// EditMode integration tests for Loot Table Story 007: proximity pickup and bag-full drop fate
    /// (design/gdd/loot-table-system.md CR-LT-7, CR-LT-13 base rule, AC-LT-8, AC-LT-9, AC-LT-18),
    /// using a real <see cref="GroundItemService"/>, a recording inventory fake and a settable
    /// position provider.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_ProximityPickup_Integration_Tests
    {
        private const uint BRONZE_ID = 1001u;
        private const uint RAW_ITEM_X = 101u;
        private const uint RAW_ITEM_Y = 102u;
        private const uint RAW_CHAR_42 = 42u;
        private const uint RAW_CHAR_A = 11u;
        private const uint RAW_CHAR_B = 12u;
        private const uint RAW_CHAR_C = 13u;
        private const uint RAW_CHAR_D = 14u;

        private const uint SPAWN_TICK = 100u;
        private const uint ASSIGNED_TICK = SPAWN_TICK + 1u;
        private const uint ENTRY_TICK = ASSIGNED_TICK + 1u;
        private const uint LEAVE_TICK = ENTRY_TICK + 1u;
        private const uint REENTRY_TICK = LEAVE_TICK + 1u;
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint TICK_BEFORE_EXPIRY = EXPIRY_TICK - 1u;
        private const uint TICK_AFTER_EXPIRY = EXPIRY_TICK + 1u;

        private const float FAR_DISTANCE = 10f;
        private const float NEAR_DISTANCE = 1f;
        private const float BOUNDARY_EPSILON = 0.01f;
        private const float AT_RADIUS = LootTableConstants.PICKUP_RADIUS_UNITS;
        private const float JUST_BEYOND_RADIUS = LootTableConstants.PICKUP_RADIUS_UNITS + BOUNDARY_EPSILON;
        private const float NO_OFFSET = 0f;
        private const int PICKUP_QUANTITY = 1;
        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int THREE_CALLS = 3;
        private const int NO_EVENTS = 0;
        private const int ONE_EVENT = 1;
        private const int NO_ERRORS = 0;
        private const int ONE_ERROR = 1;
        private const int STAY_INSIDE_TICKS = 5;
        private const int ENTRIES = 3;
        private const string PICKUP_FAILURE = "pickup failure";

        private static readonly CharacterID Char42 = new CharacterID(RAW_CHAR_42);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly CharacterID CharD = new CharacterID(RAW_CHAR_D);
        private static readonly ItemID ItemX = new ItemID(RAW_ITEM_X);
        private static readonly ItemID ItemY = new ItemID(RAW_ITEM_Y);

        // Not the origin, so a distance computed from the character's position alone would fail.
        private static readonly Vector3 ItemPosition = new Vector3(20f, 5f, -8f);
        private static readonly Vector3 FarPosition = ItemPosition + new Vector3(FAR_DISTANCE, NO_OFFSET, NO_OFFSET);
        private static readonly Vector3 NearPosition = ItemPosition + new Vector3(NEAR_DISTANCE, NO_OFFSET, NO_OFFSET);

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private int _errorCount;

        [SetUp]
        public void SetUp()
        {
            _errorCount = 0;
            Application.logMessageReceived += CountError;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CountError;
            foreach (ItemDefinition def in _created)
            {
                if (def != null)
                {
                    UnityEngine.Object.DestroyImmediate(def);
                }
            }
            _created.Clear();
        }

        // LogAssert.Expect only proves an error arrived; this counts them so "exactly one" can be asserted.
        private void CountError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _errorCount++;
            }
        }

        // -----------------------------------------------------------------------
        // Rig
        // -----------------------------------------------------------------------

        private sealed class FakeItemDatabase : IItemDatabase
        {
            private readonly List<ItemDefinition> _equipment;

            public FakeItemDatabase(List<ItemDefinition> equipment)
            {
                _equipment = equipment;
            }

            public bool IsReady => true;

            public event Action OnDatabaseReady
            {
                add { }
                remove { }
            }

            public ItemDefinition GetItem(ItemID id) => null;

            public bool TryGetItem(ItemID id, out ItemDefinition item)
            {
                item = null;
                return false;
            }

            public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
            {
                return category == ItemCategory.Equipment ? _equipment : new List<ItemDefinition>();
            }
        }

        private sealed class Rig
        {
            public GroundItemService Ground;
            public RecordingInventoryService Inventory;
            public SettablePositionProvider Positions;
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();

            // How many pickup calls had been made when each despawn event fired.
            public readonly List<int> PickupCallsAtDespawn = new List<int>();

            public void RecordDespawn(GroundItemDespawnedEventArgs args)
            {
                Despawned.Add(args);
                PickupCallsAtDespawn.Add(Inventory.Calls.Count);
            }
        }

        private Rig BuildRig()
        {
            ItemDefinition bronze = ItemDefinitionBuilder.Build(
                BRONZE_ID,
                "Item" + BRONZE_ID,
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, GearTier.Bronze));
            _created.Add(bronze);
            var cache = new LootEquipmentCache(new FakeItemDatabase(new List<ItemDefinition> { bronze }));

            var rig = new Rig
            {
                Inventory = new RecordingInventoryService(),
                Positions = new SettablePositionProvider(),
            };
            rig.Ground = new GroundItemService(cache, rig.Inventory, rig.Positions, new EmptyItemDatabase());
            rig.Ground.OnGroundItemDespawned += rig.RecordDespawn;
            return rig;
        }

        // Spawns an item for the character and ticks until it is Assigned, with the character far away.
        private static GroundItemID SpawnAssignedFar(Rig rig, CharacterID assignee, ItemID item)
        {
            rig.Positions.Set(assignee, FarPosition);
            GroundItemID id = rig.Ground.Spawn(item, ItemPosition, assignee, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            return id;
        }

        private static GroundItem Get(Rig rig, GroundItemID id)
        {
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            return item;
        }

        // -----------------------------------------------------------------------
        // AC-LT-8
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AssigneeEntersRadiusWithFreeSlot_PicksUpOnceAndRecordIsGone()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(Char42, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(ItemX, rig.Inventory.Calls[0].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[0].Quantity);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(NO_ERRORS, _errorCount);
        }

        [Test]
        public void Tick_AssigneeEntersRadiusBagFull_CallsPickupOnceAndItemStaysAssignedWithSameExpiry()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            uint expiryBefore = Get(rig, id).ExpiryTick;
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            GroundItem item = Get(rig, id);
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(Char42, item.AssignedTo);
            Assert.AreEqual(expiryBefore, item.ExpiryTick);
            Assert.AreEqual(LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS, item.PauseBudgetRemaining);
            Assert.AreEqual(NO_ERRORS, _errorCount);
        }

        [Test]
        public void Tick_NonAssigneeEntersRadius_TriggersNoPickup()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(CharB, NearPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(GroundItemState.Assigned, Get(rig, id).State);
        }

        // -----------------------------------------------------------------------
        // AC-LT-9
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_BagFullThenAssigneeLeavesAndReentersWithFreeSlot_RetriesAndDelivers()
        {
            // Arrange: first entry fails on a full bag
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);

            // Act / Assert: leaving and freeing a slot attempts nothing while outside
            rig.Positions.Set(Char42, FarPosition);
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            rig.Ground.Tick(LEAVE_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);

            // Act: come back before expiry
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);

            // Assert
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(Char42, rig.Inventory.Calls[1].Character);
            Assert.AreEqual(ItemX, rig.Inventory.Calls[1].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[1].Quantity);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
        }

        [Test]
        public void Tick_BagFullAssigneeStaysInsideRadius_DoesNotRetry()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);

            // Act: even with a free slot, standing inside is not a new entry
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            for (uint i = 1u; i <= STAY_INSIDE_TICKS; i++)
            {
                rig.Ground.Tick(ENTRY_TICK + i);
            }

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(GroundItemState.Assigned, Get(rig, id).State);
        }

        [Test]
        public void Tick_BagFullAssigneeNeverReturns_DespawnsExactlyOnceAtExpiryTick()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);

            // Act / Assert: alive one tick before expiry
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));

            // Act / Assert: despawned on the expiry tick itself
            rig.Ground.Tick(EXPIRY_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));

            // Act / Assert: nothing more on a later tick
            rig.Ground.Tick(TICK_AFTER_EXPIRY);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
        }

        [Test]
        public void Tick_BagFullAssigneeEntersSeveralTimes_AttemptsOncePerEntry()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            SpawnAssignedFar(rig, Char42, ItemX);
            uint tick = ENTRY_TICK;

            // Act: in, out, in, out, in, out
            for (int i = 0; i < ENTRIES; i++)
            {
                rig.Positions.Set(Char42, NearPosition);
                rig.Ground.Tick(tick++);
                rig.Positions.Set(Char42, FarPosition);
                rig.Ground.Tick(tick++);
            }

            // Assert
            Assert.AreEqual(THREE_CALLS, rig.Inventory.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // AC-LT-18
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_FourFullBagsAllOnBothItems_OnlyEachAssigneeIsCalledAndNothingIsReassigned()
        {
            // Arrange: X assigned to A, Y assigned to B; C and D are party members with no item
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID x = rig.Ground.Spawn(ItemX, ItemPosition, CharA, SPAWN_TICK);
            GroundItemID y = rig.Ground.Spawn(ItemY, ItemPosition, CharB, SPAWN_TICK);
            rig.Positions.Set(CharA, FarPosition);
            rig.Positions.Set(CharB, FarPosition);
            rig.Positions.Set(CharC, FarPosition);
            rig.Positions.Set(CharD, FarPosition);
            rig.Ground.Tick(ASSIGNED_TICK);
            uint expiryX = Get(rig, x).ExpiryTick;
            uint expiryY = Get(rig, y).ExpiryTick;
            rig.Positions.Set(CharA, ItemPosition);
            rig.Positions.Set(CharB, ItemPosition);
            rig.Positions.Set(CharC, ItemPosition);
            rig.Positions.Set(CharD, ItemPosition);

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert: exactly two calls, compared as a set (the order within a tick is unspecified)
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new RecordingInventoryService.PickupCall(CharA, ItemX, PICKUP_QUANTITY),
                    new RecordingInventoryService.PickupCall(CharB, ItemY, PICKUP_QUANTITY),
                },
                rig.Inventory.Calls);
            GroundItem itemX = Get(rig, x);
            GroundItem itemY = Get(rig, y);
            Assert.AreEqual(GroundItemState.Assigned, itemX.State);
            Assert.AreEqual(CharA, itemX.AssignedTo);
            Assert.AreEqual(expiryX, itemX.ExpiryTick);
            Assert.AreEqual(GroundItemState.Assigned, itemY.State);
            Assert.AreEqual(CharB, itemY.AssignedTo);
            Assert.AreEqual(expiryY, itemY.ExpiryTick);
        }

        [Test]
        public void Tick_TwoItemsAssignedToSameCharacterBothInRange_PicksUpBoth()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID x = SpawnAssignedFar(rig, Char42, ItemX);
            GroundItemID y = rig.Ground.Spawn(ItemY, ItemPosition, Char42, ASSIGNED_TICK);
            rig.Ground.Tick(ENTRY_TICK);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(LEAVE_TICK);

            // Assert: compared as a set (the order within a tick is unspecified)
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new RecordingInventoryService.PickupCall(Char42, ItemX, PICKUP_QUANTITY),
                    new RecordingInventoryService.PickupCall(Char42, ItemY, PICKUP_QUANTITY),
                },
                rig.Inventory.Calls);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(x, out _));
            Assert.IsFalse(rig.Ground.TryGetGroundItem(y, out _));
        }

        // -----------------------------------------------------------------------
        // Disconnect
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AssigneeHasNoPosition_TriggersNothingAndItemStaysAssignedUntilExpiry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = rig.Ground.Spawn(ItemX, ItemPosition, Char42, SPAWN_TICK);

            // Act
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Ground.Tick(ENTRY_TICK);
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);

            // Assert: still assigned, and the timer was not touched
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            GroundItem item = Get(rig, id);
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(Char42, item.AssignedTo);
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);

            // Act / Assert: the timer kept running, so the item despawns on its expiry tick
            rig.Ground.Tick(EXPIRY_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
        }

        [Test]
        public void Tick_AssigneeReconnectsInsideRadius_CountsAsEnteringAndPicksUp()
        {
            // Arrange: disconnected (no position) while assigned
            Rig rig = BuildRig();
            GroundItemID id = rig.Ground.Spawn(ItemX, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);

            // Act: reconnect right on the item
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(LEAVE_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_BagFullAssigneeDisconnectsInsideThenReconnectsInside_RetriesOnReconnect()
        {
            // Arrange: a failed pickup with the assignee standing on the item
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);

            // Act: disconnect (position unknown = outside), then reconnect at the same spot
            rig.Positions.Remove(Char42);
            rig.Ground.Tick(LEAVE_TICK);
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);

            // Assert
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        // -----------------------------------------------------------------------
        // Pickup before expiry
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AssigneeEntersOnExpiryTickWithFreeSlot_PicksUpAndDoesNotDespawn()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(EXPIRY_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
        }

        [Test]
        public void Tick_AssigneeEntersOnExpiryTickWithFullBag_TriesPickupThenDespawnsOnSameTick()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Ground.Tick(TICK_BEFORE_EXPIRY);
            rig.Positions.Set(Char42, NearPosition);

            // Act
            rig.Ground.Tick(EXPIRY_TICK);

            // Assert: the pickup call had already been made when the despawn event fired
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(ONE_CALL, rig.PickupCallsAtDespawn[0]);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_AfterSuccessfulPickup_RaisesNoDespawnEventThroughExpiry()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));

            // Act
            rig.Ground.Tick(EXPIRY_TICK);
            rig.Ground.Tick(TICK_AFTER_EXPIRY);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        // The distance is 3D: an offset along the height axis counts exactly like a horizontal one.
        [TestCase(AT_RADIUS, NO_OFFSET, NO_OFFSET, ONE_CALL)]
        [TestCase(JUST_BEYOND_RADIUS, NO_OFFSET, NO_OFFSET, NO_CALLS)]
        [TestCase(NO_OFFSET, AT_RADIUS, NO_OFFSET, ONE_CALL)]
        [TestCase(NO_OFFSET, JUST_BEYOND_RADIUS, NO_OFFSET, NO_CALLS)]
        [TestCase(NO_OFFSET, NO_OFFSET, JUST_BEYOND_RADIUS, NO_CALLS)]
        public void Tick_AssigneeAtOffsetFromItem_PicksUpOnlyWithinInclusive3DRadius(float dx, float dy, float dz, int expectedCalls)
        {
            // Arrange
            Rig rig = BuildRig();
            SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, ItemPosition + new Vector3(dx, dy, dz));

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert
            Assert.AreEqual(expectedCalls, rig.Inventory.Calls.Count);
        }

        [Test]
        public void Tick_AssigneeAlreadyOnItemAtSpawn_IsPickedUpOnFirstLaterTickNotOnSpawnTick()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Positions.Set(Char42, NearPosition);
            GroundItemID id = rig.Ground.Spawn(ItemX, ItemPosition, Char42, SPAWN_TICK);

            // Act / Assert: the spawn tick leaves the item Spawning, so nothing is attempted
            rig.Ground.Tick(SPAWN_TICK);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(GroundItemState.Spawning, Get(rig, id).State);

            // Act: the next tick makes it Assigned and the assignee counts as entering
            rig.Ground.Tick(ASSIGNED_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_PickupFailsWithReasonOtherThanInventoryFull_ReturnsToAssignedAndLogsOneErrorPerEntry()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.UnknownItem);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            uint expiryBefore = Get(rig, id).ExpiryTick;
            rig.Positions.Set(Char42, NearPosition);
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Tick.*" + nameof(PickupFailReason.UnknownItem)));

            // Act: the entry tick, then one more tick with the assignee still inside
            rig.Ground.Tick(ENTRY_TICK);
            rig.Ground.Tick(LEAVE_TICK);

            // Assert: one attempt and one error for the entry, not one per tick
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            GroundItem item = Get(rig, id);
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(Char42, item.AssignedTo);
            Assert.AreEqual(expiryBefore, item.ExpiryTick);
        }

        [Test]
        public void Tick_PickupThrows_LogsRemovesItemAnnouncesDespawnAndNeverRetries()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Inventory.ThrowOnPickup = new InvalidOperationException(PICKUP_FAILURE);
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            LogAssert.Expect(LogType.Exception, new Regex(PICKUP_FAILURE));
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Tick.*threw"));

            // Act
            Assert.DoesNotThrow(() => rig.Ground.Tick(ENTRY_TICK));

            // Assert: the outcome is unknown, so the item is gone and clients are told
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);

            // Act / Assert: leaving and re-entering can never deliver it a second time
            rig.Inventory.ThrowOnPickup = null;
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(REENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        [Test]
        public void Tick_PickupCallbackReentersSpawnAndTick_DoesNotThrowAndStateStaysConsistent()
        {
            // Arrange: a subscriber of OnInventoryChanged calling back into the service, in effect
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            GroundItemID reentrant = GroundItemID.Invalid;
            rig.Inventory.OnPickup = (character, item) =>
            {
                reentrant = rig.Ground.Spawn(ItemY, ItemPosition, CharB, ENTRY_TICK);
                rig.Ground.Tick(LEAVE_TICK);
            };

            // Act
            Assert.DoesNotThrow(() => rig.Ground.Tick(ENTRY_TICK));

            // Assert: the claimed item is gone, claimed once, and the item spawned re-entrantly is live
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreNotEqual(GroundItemID.Invalid, reentrant);
            Assert.AreEqual(GroundItemState.Assigned, Get(rig, reentrant).State);
        }

        [Test]
        public void Tick_PickupCallbackReentersTickAtExpiry_DoesNotExpireTheClaimInFlight()
        {
            // Arrange: while the pickup is in flight, a nested Tick runs at the item's expiry tick
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssignedFar(rig, Char42, ItemX);
            rig.Positions.Set(Char42, NearPosition);
            GroundItemState stateDuringPickup = GroundItemState.Despawned;
            rig.Inventory.OnPickup = (character, item) =>
            {
                rig.Ground.Tick(EXPIRY_TICK);
                stateDuringPickup = Get(rig, id).State;
            };

            // Act
            rig.Ground.Tick(ENTRY_TICK);

            // Assert: the nested tick left the claim alone, and the pickup then delivered the item
            Assert.AreEqual(GroundItemState.Claiming, stateDuringPickup);
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }
    }
}

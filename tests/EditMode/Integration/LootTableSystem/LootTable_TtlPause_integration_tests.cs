using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.LootTableSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.LootTableSystem
{
    /// <summary>
    /// EditMode integration tests for Loot Table Story 009: TTL pause on app background
    /// (design/gdd/loot-table-system.md CR-LT-13.1, CR-LT-13.3 re-fire, AC-LT-21, AC-LT-24 second
    /// half), using a real <see cref="GroundItemService"/> over the shared recording inventory fake.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_TtlPause_Integration_Tests
    {
        private const uint RAW_CHAR_42 = 42u;
        private const uint RAW_CHAR_43 = 43u;
        private const uint DROP_ID = 3001u;
        private const uint DROP_B_ID = 3002u;
        private const uint DROP_C_ID = 3003u;

        private const uint SPAWN_TICK = 100u;
        private const uint ASSIGNED_TICK = SPAWN_TICK + 1u;
        private const uint ENTRY_TICK = ASSIGNED_TICK + 1u;
        private const uint LEAVE_TICK = ENTRY_TICK + 1u;
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint WARNING_TICK = EXPIRY_TICK - (uint)LootTableConstants.EXPIRY_WARNING_TICKS;
        private const int POOL = LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS;

        private const uint BACKGROUND_TICK = ENTRY_TICK;
        private const uint PAUSE_FIRST = 500u;
        private const uint PAUSE_SECOND = 800u;
        private const uint PAUSE_THIRD = 100u;
        private const uint PAUSE_SHORT = 200u;
        private const uint PAUSE_WARNING = 300u;
        private const uint HELD_LEAD = 100u;
        private const uint LATE_RETURN = 200u;
        private const uint LATE_PAUSE = 300u;
        private const uint BAD_TICK_GAP = 500u;
        private const uint LATER_BACKGROUND = 1000u;
        private const uint PAUSE_OVER_BUDGET = 2000u;
        private const uint PAUSE_LONG = 1100u;
        private const uint NO_PAUSE = 0u;
        private const uint PAST_THRESHOLD_LEAD = 500u;
        private const uint DISCONNECT_AFTER = 50u;

        private const float FAR_DISTANCE = 10f;
        private const float NEAR_DISTANCE = 1f;
        private const float NO_OFFSET = 0f;
        private const float OTHER_ITEM_DISTANCE = 100f;

        private const int NO_EVENTS = 0;
        private const int ONE_EVENT = 1;
        private const int TWO_EVENTS = 2;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int NO_BUDGET = 0;

        private static readonly CharacterID Char42 = new CharacterID(RAW_CHAR_42);
        private static readonly CharacterID Char43 = new CharacterID(RAW_CHAR_43);
        private static readonly ItemID DropItem = new ItemID(DROP_ID);
        private static readonly ItemID DropItemB = new ItemID(DROP_B_ID);
        private static readonly ItemID DropItemC = new ItemID(DROP_C_ID);

        private static readonly Vector3 ItemPosition = new Vector3(20f, 5f, -8f);
        private static readonly Vector3 FarPosition = ItemPosition + new Vector3(FAR_DISTANCE, NO_OFFSET, NO_OFFSET);
        private static readonly Vector3 NearPosition = ItemPosition + new Vector3(NEAR_DISTANCE, NO_OFFSET, NO_OFFSET);
        private static readonly Vector3 OtherItemPosition = ItemPosition + new Vector3(OTHER_ITEM_DISTANCE, NO_OFFSET, NO_OFFSET);

        private readonly List<GroundItemService> _services = new List<GroundItemService>();

        [TearDown]
        public void TearDown()
        {
            foreach (GroundItemService service in _services)
            {
                service.Dispose();
            }
            _services.Clear();
        }

        // -----------------------------------------------------------------------
        // Rig
        // -----------------------------------------------------------------------

        private sealed class Rig
        {
            public RecordingInventoryService Inventory;
            public GroundItemService Ground;
            public SettablePositionProvider Positions;
            public readonly List<GroundItemExpiryWarningEventArgs> Warnings = new List<GroundItemExpiryWarningEventArgs>();
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();

            public void RecordWarning(GroundItemExpiryWarningEventArgs args) => Warnings.Add(args);

            public void RecordDespawn(GroundItemDespawnedEventArgs args) => Despawned.Add(args);
        }

        private Rig BuildRig()
        {
            var rig = new Rig
            {
                Inventory = new RecordingInventoryService(),
                Positions = new SettablePositionProvider(),
            };
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            rig.Ground = new GroundItemService(
                new LootEquipmentCache(new EmptyItemDatabase()),
                rig.Inventory,
                rig.Positions,
                new EmptyItemDatabase());
            rig.Ground.OnGroundItemExpiryWarning += rig.RecordWarning;
            rig.Ground.OnGroundItemDespawned += rig.RecordDespawn;
            _services.Add(rig.Ground);
            return rig;
        }

        // Bag-full state for 42: spawn, tick to Assigned, move 42 into the radius, tick -> one failed pickup.
        private static GroundItemID BuildBagFullItem(Rig rig)
        {
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            return id;
        }

        private static GroundItem Get(Rig rig, GroundItemID id)
        {
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            return item;
        }

        private static int CountWarningsFor(Rig rig, GroundItemID id)
        {
            int count = 0;
            foreach (GroundItemExpiryWarningEventArgs warning in rig.Warnings)
            {
                if (warning.GroundItemId == id)
                {
                    count++;
                }
            }
            return count;
        }

        private static void BackgroundFor(Rig rig, CharacterID character, uint from, uint pausedTicks)
        {
            rig.Ground.NotifyClientBackgrounded(character, from);
            rig.Ground.NotifyClientForegrounded(character, from + pausedTicks);
        }

        // -----------------------------------------------------------------------
        // AC-LT-21: extension and budget
        // -----------------------------------------------------------------------

        [Test]
        public void NotifyClientForegrounded_BagFullItemBackgrounded500Ticks_ExtendsExpiryAndSpendsBudget()
        {
            // Arrange: every assignment starts with the full budget
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            Assert.AreEqual(POOL, Get(rig, id).PauseBudgetRemaining);

            // Act
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK);
            rig.Ground.NotifyClientForegrounded(Char42, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert: expected values come from the spawn tick and the constants, not read back first
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_FIRST, item.ExpiryTick);
            Assert.AreEqual(POOL - (int)PAUSE_FIRST, item.PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientForegrounded_FirstPauseLongerThanTheBudget_ExtendsByTheBudgetOnly()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);

            // Act
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_OVER_BUDGET);

            // Assert
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + (uint)POOL, item.ExpiryTick);
            Assert.AreEqual(NO_BUDGET, item.PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientForegrounded_SameTickAsTheBackground_ExtendsNothingAndClearsTheRecord()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);

            // Act: zero ticks paused
            BackgroundFor(rig, Char42, BACKGROUND_TICK, NO_PAUSE);

            // Assert
            Assert.AreEqual(EXPIRY_TICK, Get(rig, id).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, id).PauseBudgetRemaining);

            // Act / Assert: the record was cleared, so a later pair is measured from its own start
            BackgroundFor(rig, Char42, BACKGROUND_TICK + LATER_BACKGROUND, PAUSE_SHORT);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT, Get(rig, id).ExpiryTick);
        }

        [Test]
        public void NotifyClientForegrounded_TwoBagFullItemsWithDifferentBudgets_ExtendsEachByItsOwnBudget()
        {
            // Arrange: X is bag-full and spends 200 of its budget; W only becomes bag-full afterwards
            Rig rig = BuildRig();
            GroundItemID w = rig.Ground.Spawn(DropItemB, OtherItemPosition, Char42, SPAWN_TICK);
            GroundItemID x = BuildBagFullItem(rig);
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_SHORT);
            rig.Positions.Set(Char42, OtherItemPosition);
            rig.Ground.Tick(BACKGROUND_TICK + PAUSE_SHORT);
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            uint secondStart = BACKGROUND_TICK + PAUSE_SHORT;

            // Act: 1100 ticks paused; X has 1000 left, W has 1200
            BackgroundFor(rig, Char42, secondStart, PAUSE_LONG);

            // Assert
            Assert.AreEqual(EXPIRY_TICK + (uint)POOL, Get(rig, x).ExpiryTick);
            Assert.AreEqual(NO_BUDGET, Get(rig, x).PauseBudgetRemaining);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_LONG, Get(rig, w).ExpiryTick);
            Assert.AreEqual(POOL - (int)PAUSE_LONG, Get(rig, w).PauseBudgetRemaining);
        }

        // CR-LT-13.1 as written: the whole background period counts, even the part before the
        // item's pickup failed. Recorded as a GDD question; this pins the current behaviour.
        [Test]
        public void NotifyClientForegrounded_ItemBecameBagFullWhileAlreadyBackgrounded_ExtendsByTheWholePeriod()
        {
            // Arrange: backgrounded first, standing far away
            Rig rig = BuildRig();
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Ground.NotifyClientBackgrounded(Char42, ASSIGNED_TICK);

            // Act: the pickup fails on a full bag only afterwards
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            rig.Ground.NotifyClientForegrounded(Char42, ASSIGNED_TICK + PAUSE_SHORT);

            // Assert
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT, Get(rig, id).ExpiryTick);
        }

        [Test]
        public void NotifyClientForegrounded_PickupFailedForAnotherReason_IsNotBagFullAndExtendsNothing()
        {
            // Arrange: the pickup fails, but not on a full bag
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.UnknownItem);
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Tick.*" + nameof(PickupFailReason.UnknownItem)));
            GroundItemID id = BuildBagFullItem(rig);

            // Act
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_SHORT);

            // Assert
            Assert.AreEqual(EXPIRY_TICK, Get(rig, id).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, id).PauseBudgetRemaining);
        }

        [Test]
        public void Tick_AssigneeReentersAfterAnExtension_StillDeliversTheItem()
        {
            // Arrange: bag-full, extended, then the assignee steps out
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_SHORT);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);

            // Act: room in the bag now, and the assignee comes back
            rig.Inventory.DefaultResult = PickupResult.Succeeded;
            rig.Positions.Set(Char42, NearPosition);
            rig.Ground.Tick(LEAVE_TICK + 1u);

            // Assert
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
        }

        [Test]
        public void NotifyClientForegrounded_SuccessivePauses_StopAtTheBudget()
        {
            // Arrange: 500 ticks already spent
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_FIRST);

            // Act: 800 more ticks, but only 700 remain
            BackgroundFor(rig, Char42, BACKGROUND_TICK + PAUSE_FIRST, PAUSE_SECOND);

            // Assert
            GroundItem afterSecond = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + (uint)POOL, afterSecond.ExpiryTick);
            Assert.AreEqual(0, afterSecond.PauseBudgetRemaining);

            // Act: a third pause finds the budget used up
            BackgroundFor(rig, Char42, BACKGROUND_TICK + PAUSE_FIRST + PAUSE_SECOND, PAUSE_THIRD);

            // Assert
            GroundItem afterThird = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + (uint)POOL, afterThird.ExpiryTick);
            Assert.AreEqual(0, afterThird.PauseBudgetRemaining);
        }

        // -----------------------------------------------------------------------
        // AC-LT-24 (second half): the warning re-fires after an extension
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ExpiryExtendedAfterFirstWarning_RaisesASecondWarningAtTheNewThreshold()
        {
            // Arrange: first warning has fired
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.Tick(WARNING_TICK);
            Assert.AreEqual(ONE_EVENT, CountWarningsFor(rig, id));

            // Act: extend by 300, then walk to just before the new threshold
            BackgroundFor(rig, Char42, WARNING_TICK, PAUSE_WARNING);
            uint newWarningTick = WARNING_TICK + PAUSE_WARNING;
            for (uint tick = WARNING_TICK + 1u; tick < newWarningTick; tick++)
            {
                rig.Ground.Tick(tick);
            }

            // Assert: none in between
            Assert.AreEqual(ONE_EVENT, CountWarningsFor(rig, id));

            // Act
            rig.Ground.Tick(newWarningTick);

            // Assert: exactly two in total, the second with the usual payload
            Assert.AreEqual(TWO_EVENTS, CountWarningsFor(rig, id));
            Assert.AreEqual(TWO_EVENTS, rig.Warnings.Count);
            Assert.AreEqual(Char42, rig.Warnings[1].Recipient);
            Assert.AreEqual(DropItem, rig.Warnings[1].ItemId);
            Assert.AreEqual((uint)LootTableConstants.EXPIRY_WARNING_TICKS, rig.Warnings[1].RemainingTicks);
        }

        [Test]
        public void Tick_ExtensionLeavesTheNewThresholdInThePast_RaisesNoSecondWarning()
        {
            // Arrange: the first warning has fired; a 200-tick pause ends 300 ticks before the
            // stored expiry, so the new threshold (E + 200 - 600) is already behind the current tick
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.Tick(WARNING_TICK);
            Assert.AreEqual(ONE_EVENT, CountWarningsFor(rig, id));
            uint backgroundAt = EXPIRY_TICK - PAST_THRESHOLD_LEAD;
            BackgroundFor(rig, Char42, backgroundAt, PAUSE_SHORT);
            uint newExpiry = EXPIRY_TICK + PAUSE_SHORT;
            Assert.AreEqual(newExpiry, Get(rig, id).ExpiryTick);

            // Act: every tick from the foreground to the new expiry
            for (uint tick = backgroundAt + PAUSE_SHORT; tick <= newExpiry; tick++)
            {
                rig.Ground.Tick(tick);
            }

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Warnings.Count);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        // -----------------------------------------------------------------------
        // CR-LT-13.1 scope
        // -----------------------------------------------------------------------

        [Test]
        public void NotifyClientForegrounded_ThreeItems_ExtendsOnlyTheBagFullItemOfTheCharacter()
        {
            // Arrange: X bag-full for 42; Y assigned to 42 but never attempted; Z bag-full for 43
            Rig rig = BuildRig();
            rig.Positions.Set(Char42, FarPosition);
            rig.Positions.Set(Char43, FarPosition);
            GroundItemID x = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            GroundItemID y = rig.Ground.Spawn(DropItemB, OtherItemPosition, Char42, SPAWN_TICK);
            GroundItemID z = rig.Ground.Spawn(DropItemC, ItemPosition, Char43, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Positions.Set(Char42, NearPosition);
            rig.Positions.Set(Char43, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
            var attempted = new HashSet<CharacterID> { rig.Inventory.Calls[0].Character, rig.Inventory.Calls[1].Character };
            Assert.IsTrue(attempted.SetEquals(new[] { Char42, Char43 }));

            // Act
            BackgroundFor(rig, Char42, BACKGROUND_TICK, PAUSE_SHORT);

            // Assert
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT, Get(rig, x).ExpiryTick);
            Assert.AreEqual(POOL - (int)PAUSE_SHORT, Get(rig, x).PauseBudgetRemaining);
            Assert.AreEqual(EXPIRY_TICK, Get(rig, y).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, y).PauseBudgetRemaining);
            Assert.AreEqual(EXPIRY_TICK, Get(rig, z).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, z).PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientForegrounded_NoRecordedBackground_ChangesNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);

            // Act
            rig.Ground.NotifyClientForegrounded(Char42, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);
            Assert.AreEqual(POOL, item.PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientForegrounded_AssigneeLeftRadiusAfterFullBag_StillExtends()
        {
            // Arrange: the item failed on a full bag, then 42 walked out (clears the in-radius flag)
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);

            // Act
            BackgroundFor(rig, Char42, LEAVE_TICK, PAUSE_SHORT);

            // Assert: bag-full is sticky
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT, item.ExpiryTick);
            Assert.AreEqual(POOL - (int)PAUSE_SHORT, item.PauseBudgetRemaining);
        }

        // -----------------------------------------------------------------------
        // Expiry while backgrounded
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_BagFullAssigneeBackgroundedNeverReturns_DespawnsAtExpiryPlusBudget()
        {
            // Arrange: the assignee walked out of the radius after the failed pickup (so only the
            // sticky bag-full flag is left), then backgrounded 100 ticks before the stored expiry
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Positions.Set(Char42, FarPosition);
            rig.Ground.Tick(LEAVE_TICK);
            rig.Ground.NotifyClientBackgrounded(Char42, EXPIRY_TICK - HELD_LEAD);

            // Act / Assert: held at and past the stored expiry tick. The item despawns when
            // currentTick reaches E + min(currentTick - backgroundedAt, budget); the background
            // started before E, so that first holds once the accrual is capped: at E + POOL.
            rig.Ground.Tick(EXPIRY_TICK);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            uint lastAliveTick = EXPIRY_TICK + (uint)POOL - 1u;
            rig.Ground.Tick(lastAliveTick);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);

            // Act: the budget is used up
            rig.Ground.Tick(EXPIRY_TICK + (uint)POOL);

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
        }

        [Test]
        public void NotifyClientForegrounded_AfterItemWasHeldPastExpiry_BooksTheExtensionAndItemLivesToThatTick()
        {
            // Arrange: backgrounded before the stored expiry, held at it
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, EXPIRY_TICK - HELD_LEAD);
            rig.Ground.Tick(EXPIRY_TICK);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));

            // Act: returns 200 ticks after the stored expiry, so 300 ticks were paused
            uint pausedTicks = HELD_LEAD + LATE_RETURN;
            rig.Ground.NotifyClientForegrounded(Char42, EXPIRY_TICK + LATE_RETURN);

            // Assert: booked once, no double counting
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + pausedTicks, item.ExpiryTick);
            Assert.AreEqual(POOL - (int)pausedTicks, item.PauseBudgetRemaining);

            // Act / Assert: lives on to the new expiry tick, then despawns once
            rig.Ground.Tick(EXPIRY_TICK + pausedTicks - 1u);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            rig.Ground.Tick(EXPIRY_TICK + pausedTicks);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        [Test]
        public void NotifyClientForegrounded_ExactlyWhenTheHeldAccrualReachesTheBudget_BooksTheWholeBudget()
        {
            // Arrange: backgrounded 100 ticks before E; the budget is used up at E + POOL - 100
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            uint backgroundAt = EXPIRY_TICK - HELD_LEAD;
            rig.Ground.NotifyClientBackgrounded(Char42, backgroundAt);
            uint returnTick = backgroundAt + (uint)POOL;
            rig.Ground.Tick(returnTick);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));

            // Act
            rig.Ground.NotifyClientForegrounded(Char42, returnTick);

            // Assert
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + (uint)POOL, item.ExpiryTick);
            Assert.AreEqual(NO_BUDGET, item.PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientForegrounded_AfterTheHeldItemDespawned_ChangesNothing()
        {
            // Arrange: held to the end of its budget and despawned
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, EXPIRY_TICK - HELD_LEAD);
            rig.Ground.Tick(EXPIRY_TICK + (uint)POOL);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));

            // Act
            Assert.DoesNotThrow(() => rig.Ground.NotifyClientForegrounded(Char42, EXPIRY_TICK + (uint)POOL + 1u));

            // Assert: not resurrected, not announced again
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        [Test]
        public void Tick_BagFullItemOfAnotherCharacterWhileSomeoneElseIsBackgrounded_DespawnsAtItsExpiryTick()
        {
            // Arrange: the item is 42's; it is 43 who is backgrounded
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char43, EXPIRY_TICK - HELD_LEAD);

            // Act
            rig.Ground.Tick(EXPIRY_TICK);

            // Assert: the hold is keyed on the item's own assignee
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        // -----------------------------------------------------------------------
        // Disconnect ends the pause (code review decision, 2026-10-02)
        // -----------------------------------------------------------------------

        [Test]
        public void NotifyClientDisconnected_WhileBackgrounded_BooksThePauseSoFarLikeAForeground()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK);

            // Act
            rig.Ground.NotifyClientDisconnected(Char42, BACKGROUND_TICK + PAUSE_SHORT);

            // Assert
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT, item.ExpiryTick);
            Assert.AreEqual(POOL - (int)PAUSE_SHORT, item.PauseBudgetRemaining);
        }

        [Test]
        public void Tick_AfterDisconnect_TimerRunsNormallyAndTheItemIsNoLongerHeld()
        {
            // Arrange: backgrounded 100 ticks before E, disconnected 50 ticks later
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            uint backgroundAt = EXPIRY_TICK - HELD_LEAD;
            rig.Ground.NotifyClientBackgrounded(Char42, backgroundAt);
            rig.Ground.NotifyClientDisconnected(Char42, backgroundAt + DISCONNECT_AFTER);
            uint newExpiry = EXPIRY_TICK + DISCONNECT_AFTER;
            Assert.AreEqual(newExpiry, Get(rig, id).ExpiryTick);

            // Act / Assert: alive up to the booked expiry, gone on it, although budget remains
            rig.Ground.Tick(newExpiry - 1u);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            rig.Ground.Tick(newExpiry);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        [Test]
        public void NotifyClientBackgrounded_AfterADisconnect_IsMeasuredFromTheNewTickNotTheOldOne()
        {
            // Arrange: a session that backgrounded and then dropped
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK);
            rig.Ground.NotifyClientDisconnected(Char42, BACKGROUND_TICK + PAUSE_SHORT);

            // Act: a later session backgrounds for 100 ticks
            uint laterStart = BACKGROUND_TICK + LATER_BACKGROUND;
            BackgroundFor(rig, Char42, laterStart, PAUSE_THIRD);

            // Assert: 200 from the first session plus 100, not the whole time since the first background
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK + PAUSE_SHORT + PAUSE_THIRD, item.ExpiryTick);
            Assert.AreEqual(POOL - (int)(PAUSE_SHORT + PAUSE_THIRD), item.PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientDisconnected_NoRecordedBackground_ChangesNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);

            // Act
            rig.Ground.NotifyClientDisconnected(Char42, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert
            Assert.AreEqual(EXPIRY_TICK, Get(rig, id).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, id).PauseBudgetRemaining);
        }

        [Test]
        public void Tick_UnblockedItemOfBackgroundedAssignee_DespawnsAtItsNormalExpiryTick()
        {
            // Arrange: assigned to 42, who is far away and never attempted the pickup
            Rig rig = BuildRig();
            rig.Positions.Set(Char42, FarPosition);
            GroundItemID id = rig.Ground.Spawn(DropItem, ItemPosition, Char42, SPAWN_TICK);
            rig.Ground.Tick(ASSIGNED_TICK);
            rig.Ground.NotifyClientBackgrounded(Char42, EXPIRY_TICK - HELD_LEAD);

            // Act
            rig.Ground.Tick(EXPIRY_TICK);

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        [Test]
        public void Tick_BagFullItemWithNoBudgetLeftAndAssigneeBackgrounded_DespawnsAtItsExpiryTick()
        {
            // Arrange: budget used up by one full-length pause
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            BackgroundFor(rig, Char42, BACKGROUND_TICK, (uint)POOL);
            uint newExpiry = EXPIRY_TICK + (uint)POOL;
            Assert.AreEqual(0, Get(rig, id).PauseBudgetRemaining);
            rig.Ground.NotifyClientBackgrounded(Char42, newExpiry - HELD_LEAD);

            // Act / Assert
            rig.Ground.Tick(newExpiry - 1u);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out _));
            rig.Ground.Tick(newExpiry);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        [Test]
        public void NotifyClientBackgrounded_CalledTwiceBeforeForeground_KeepsTheFirstTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);

            // Act
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK);
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK + PAUSE_SHORT);
            rig.Ground.NotifyClientForegrounded(Char42, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert: computed from the first tick (500), not the second (300)
            Assert.AreEqual(EXPIRY_TICK + PAUSE_FIRST, Get(rig, id).ExpiryTick);
        }

        [Test]
        public void NotifyClientForegrounded_TickBeforeTheBackgroundTick_ExtendsNothingAndClearsTheRecord()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK + BAD_TICK_GAP);

            // Act
            rig.Ground.NotifyClientForegrounded(Char42, BACKGROUND_TICK);

            // Assert: nothing extended
            Assert.AreEqual(EXPIRY_TICK, Get(rig, id).ExpiryTick);
            Assert.AreEqual(POOL, Get(rig, id).PauseBudgetRemaining);

            // Act: a later valid pair works, so the bad record was cleared
            uint laterStart = BACKGROUND_TICK + LATER_BACKGROUND;
            BackgroundFor(rig, Char42, laterStart, LATE_PAUSE);

            // Assert
            Assert.AreEqual(EXPIRY_TICK + LATE_PAUSE, Get(rig, id).ExpiryTick);
            Assert.AreEqual(POOL - (int)LATE_PAUSE, Get(rig, id).PauseBudgetRemaining);
        }

        [Test]
        public void NotifyClientBackgrounded_InvalidCharacter_LogsOneError()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] NotifyClientBackgrounded"));

            // Act
            rig.Ground.NotifyClientBackgrounded(CharacterID.Invalid, BACKGROUND_TICK);
            rig.Ground.NotifyClientForegrounded(CharacterID.Invalid, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert: the foreground found no record, so nothing changed and nothing else was logged
            Assert.AreEqual(EXPIRY_TICK, Get(rig, id).ExpiryTick);
        }

        [Test]
        public void Dispose_ClearsBackgroundRecords_ForegroundAfterwardsChangesNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = BuildBagFullItem(rig);
            rig.Ground.NotifyClientBackgrounded(Char42, BACKGROUND_TICK);

            // Act
            rig.Ground.Dispose();
            rig.Ground.NotifyClientForegrounded(Char42, BACKGROUND_TICK + PAUSE_FIRST);

            // Assert
            GroundItem item = Get(rig, id);
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);
            Assert.AreEqual(POOL, item.PauseBudgetRemaining);
        }
    }
}

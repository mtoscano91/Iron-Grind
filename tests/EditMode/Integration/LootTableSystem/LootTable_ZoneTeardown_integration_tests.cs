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
    /// EditMode integration tests for Loot Table Story 012: the zone teardown loot flush
    /// (design/gdd/loot-table-system.md teardown edge case, AC-LT-19, CR-LT-9, CR-LT-9.1, CR-LT-10).
    /// Uses a real <see cref="GroundItemService"/>, <see cref="LootEquipmentCache"/>,
    /// <see cref="LootDropDistributor"/>, <see cref="LootAuctionService"/>, <see cref="LootTeardownCoordinator"/>
    /// and <see cref="CurrencySystem"/> (wrapped by a call-recording <see cref="ICurrencyService"/>), with a
    /// stub party service, a per-character inventory fake and a counting <see cref="ILootTableService"/> stub.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_ZoneTeardown_Integration_Tests
    {
        private const uint STEEL_ID = 1003u;
        private const uint DARK_STEEL_ID = 1004u;
        private const int STEEL_PRICE = 90;
        private const int DARK_STEEL_PRICE = 270;
        private const uint RAW_PARTY = 1u;
        private const uint RAW_CHAR_A = 11u;
        private const uint RAW_CHAR_B = 12u;
        private const uint RAW_CHAR_C = 13u;
        private const uint RAW_CHAR_D = 14u;
        private const uint RAW_MOB = 500u;

        private const uint SPAWN_TICK = 100u;
        private const uint OPEN_TICK = SPAWN_TICK + 1u;
        private const uint WINDOW_CLOSE_TICK = OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS;
        private const uint INSIDE_WINDOW_OFFSET = 10u;
        private const uint FLUSH_BEFORE_CLOSE_TICK = OPEN_TICK + INSIDE_WINDOW_OFFSET;
        private const uint INSIDE_GRACE_OFFSET = 10u;
        private const uint FLUSH_INSIDE_GRACE_TICK = WINDOW_CLOSE_TICK + INSIDE_GRACE_OFFSET;
        private const uint ENTRY_TICK = OPEN_TICK + 1u;
        private const uint AFTER_DISPOSE_TICK = ENTRY_TICK + 1u;

        private const uint STARTING_GOLD = 1000u;
        private const uint SHORT_GOLD = 300u;
        private const uint BID_FOUR_HUNDRED = 400u;
        private const uint BID_THREE_FIFTY = 350u;
        private const uint BID_THREE_HUNDRED = 300u;
        private const uint BID_TICK_A = 100u;
        private const uint BID_TICK_B = 120u;
        private const uint BID_TICK_C = 80u;
        private const uint SHARE_OF_FOUR = 100u;
        private const uint SHARE_OF_FOUR_FROM_THREE_FIFTY = 87u;
        private const uint NO_GOLD = 0u;

        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const string AUCTION_FAILURE = "auction flush failure";
        private const int NO_EVENTS = 0;
        private const int ONE_EVENT = 1;
        private const int THREE_EVENTS = 3;
        private const int PICKUP_QUANTITY = 1;
        private const float FAR_DISTANCE = 1000f;

        private const string EVENT_ASSIGNED = "Assigned";
        private const string EVENT_RESOLVED = "Resolved";
        private const string EVENT_DESPAWNED = "Despawned";

        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly CharacterID CharD = new CharacterID(RAW_CHAR_D);
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
        private static readonly ItemID Steel = new ItemID(STEEL_ID);
        private static readonly ItemID DarkSteel = new ItemID(DARK_STEEL_ID);
        private static readonly Vector3 MobPosition = new Vector3(1f, 2f, 3f);
        private static readonly Vector3 ItemPosition = new Vector3(20f, 5f, -8f);
        private static readonly Vector3 FarPosition = ItemPosition + new Vector3(FAR_DISTANCE, 0f, 0f);
        private static readonly Vector3 NearPosition = ItemPosition;

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private Rig _rig;

        [SetUp]
        public void SetUp()
        {
            _rig = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_rig != null)
            {
                // Disposing twice is safe, so a test that already disposed is not affected.
                _rig.Coordinator.Dispose();
                _rig = null;
            }
            foreach (ItemDefinition def in _created)
            {
                if (def != null)
                {
                    UnityEngine.Object.DestroyImmediate(def);
                }
            }
            _created.Clear();
        }

        // Counts Clear() calls; every other member is unused by the teardown tests.
        private sealed class CountingLootTableService : ILootTableService
        {
            public int ClearCalls;

            public void RecordDamage(EntityID mobEntityId, CharacterID attacker, uint finalDamage)
            {
            }

            public void ClearMob(EntityID mobEntityId)
            {
            }

            public void ResolveMobDrop(EntityID mobEntityID, int tierShift)
            {
            }

            public void Clear()
            {
                ClearCalls++;
            }
        }

        // An auction service whose teardown step throws; every other member does nothing.
        private sealed class ThrowingAuctionService : ILootAuctionService
        {
            public event Action<LootBidUpdateEventArgs> OnLootBidUpdate
            {
                add { }
                remove { }
            }

            public event Action<AuctionResolvedEventArgs> OnAuctionResolved
            {
                add { }
                remove { }
            }

            public LootBidResult SubmitBid(CharacterID bidder, GroundItemID groundItemId, uint bidAmount, uint receivedTick)
            {
                return LootBidResult.NotAuctioning;
            }

            public bool TryGetBid(GroundItemID groundItemId, CharacterID bidder, out uint bidAmount, out uint receivedTick)
            {
                bidAmount = 0u;
                receivedTick = 0u;
                return false;
            }

            public void Tick(uint currentTick)
            {
            }

            public void ResolveAllForTeardown(uint teardownTick)
            {
                throw new InvalidOperationException(AUCTION_FAILURE);
            }

            public void Dispose()
            {
            }
        }

        private sealed class Rig
        {
            public MutablePartyService Parties;
            public RecordingInventoryService Inventory;
            public SettablePositionProvider Positions;
            public GroundItemService Ground;
            public LootEquipmentCache Cache;
            public LootDropDistributor Distributor;
            public LootAuctionService Auction;
            public CurrencySystem Currency;
            public RecordingCurrencyService Gold;
            public CountingLootTableService LootTable;
            public LootTeardownCoordinator Coordinator;
            public readonly List<GroundItemSpawnedEventArgs> Spawned = new List<GroundItemSpawnedEventArgs>();
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();
            public readonly List<GroundItemAssignedEventArgs> Assigned = new List<GroundItemAssignedEventArgs>();
            public readonly List<AuctionResolvedEventArgs> Resolved = new List<AuctionResolvedEventArgs>();
            public readonly List<BagFullPickupBlockedEventArgs> Blocked = new List<BagFullPickupBlockedEventArgs>();
            public readonly List<string> Order = new List<string>();
            public uint Tick = SPAWN_TICK;

            public uint ReadTick() => Tick;

            public void RecordSpawn(GroundItemSpawnedEventArgs args) => Spawned.Add(args);

            public void RecordBlocked(BagFullPickupBlockedEventArgs args) => Blocked.Add(args);

            public void RecordDespawn(GroundItemDespawnedEventArgs args)
            {
                Despawned.Add(args);
                Order.Add(EVENT_DESPAWNED);
            }

            public void RecordAssigned(GroundItemAssignedEventArgs args)
            {
                Assigned.Add(args);
                Order.Add(EVENT_ASSIGNED);
            }

            public void RecordResolved(AuctionResolvedEventArgs args)
            {
                Resolved.Add(args);
                Order.Add(EVENT_RESOLVED);
            }
        }

        private ItemDefinition BuildEquipment(uint id, GearTier tier, int sellPrice)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id,
                "Item" + id,
                ItemCategory.Equipment,
                sellPriceGold: sellPrice,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, tier));
            _created.Add(def);
            return def;
        }

        // A party of four (A, B, C, D), each registered with STARTING_GOLD; every bag has a free slot.
        private Rig BuildRig()
        {
            var equipment = new List<ItemDefinition>
            {
                BuildEquipment(STEEL_ID, GearTier.Steel, STEEL_PRICE),
                BuildEquipment(DARK_STEEL_ID, GearTier.DarkSteel, DARK_STEEL_PRICE),
            };
            var rig = new Rig();
            rig.Parties = new MutablePartyService(Party);
            rig.Parties.Members.Add(CharA);
            rig.Parties.Members.Add(CharB);
            rig.Parties.Members.Add(CharC);
            rig.Parties.Members.Add(CharD);
            rig.Inventory = new RecordingInventoryService();
            rig.Inventory.FreeSlot = true;
            rig.Positions = new SettablePositionProvider();
            rig.Cache = new LootEquipmentCache(new FakeEquipmentItemDatabase(equipment));
            rig.Currency = new CurrencySystem();
            rig.Currency.RegisterCharacter(CharA, STARTING_GOLD);
            rig.Currency.RegisterCharacter(CharB, STARTING_GOLD);
            rig.Currency.RegisterCharacter(CharC, STARTING_GOLD);
            rig.Currency.RegisterCharacter(CharD, STARTING_GOLD);
            rig.Gold = new RecordingCurrencyService(rig.Currency);
            rig.Ground = new GroundItemService(rig.Cache, rig.Inventory, rig.Positions, new EmptyItemDatabase());
            rig.Ground.OnGroundItemSpawned += rig.RecordSpawn;
            rig.Ground.OnGroundItemDespawned += rig.RecordDespawn;
            rig.Ground.OnGroundItemAssigned += rig.RecordAssigned;
            rig.Ground.OnBagFullPickupBlocked += rig.RecordBlocked;
            rig.Distributor = new LootDropDistributor(rig.Parties, rig.Cache, rig.Ground, rig.ReadTick);
            rig.Auction = new LootAuctionService(rig.Ground, rig.Parties, rig.Cache, rig.Gold, rig.Inventory);
            rig.Auction.OnAuctionResolved += rig.RecordResolved;
            rig.LootTable = new CountingLootTableService();
            rig.Coordinator = new LootTeardownCoordinator(rig.Auction, rig.Ground, rig.LootTable);
            _rig = rig;
            return rig;
        }

        // Drives one server tick the way the tick loop does: ground items first, then auctions.
        private static void RunTick(Rig rig, uint tick)
        {
            rig.Ground.Tick(tick);
            rig.Auction.Tick(tick);
        }

        private static GroundItemID Drop(Rig rig, ItemID item)
        {
            rig.Distributor.OnDropsResolved(Mob, Party, new[] { item }, MobPosition);
            return rig.Spawned[rig.Spawned.Count - 1].GroundItemId;
        }

        // The auction is open after one tick; its window closes at WINDOW_CLOSE_TICK.
        private static GroundItemID OpenAuction(Rig rig, ItemID item)
        {
            GroundItemID id = Drop(rig, item);
            RunTick(rig, OPEN_TICK);
            return id;
        }

        private static void Bid(Rig rig, GroundItemID id, CharacterID bidder, uint amount, uint receivedTick)
        {
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(bidder, id, amount, receivedTick));
        }

        // The DarkSteel auction of AC-LT-19: A 400, B 350, C 300.
        private static GroundItemID OpenDarkSteelAuctionWithThreeBids(Rig rig)
        {
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharB, BID_THREE_FIFTY, BID_TICK_B);
            Bid(rig, id, CharC, BID_THREE_HUNDRED, BID_TICK_C);
            return id;
        }

        // A DarkSteel auction with only A's bid (400) and B's bid (350).
        private static GroundItemID OpenDarkSteelAuctionWithTwoBids(Rig rig)
        {
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharB, BID_THREE_FIFTY, BID_TICK_B);
            return id;
        }

        private static void FillBag(Rig rig, CharacterID character)
        {
            rig.Inventory.FreeSlotByCharacter[character] = false;
        }

        // The character frees a slot and the inventory announces the change.
        private static void FreeASlot(Rig rig, CharacterID character)
        {
            rig.Inventory.FreeSlotByCharacter[character] = true;
            rig.Inventory.RaiseInventoryChanged(character);
        }

        // An Assigned item whose assignee is outside the pickup radius: spawned, then one tick.
        private static GroundItemID SpawnAssigned(Rig rig, ItemID item, CharacterID assignee)
        {
            rig.Positions.Set(assignee, FarPosition);
            GroundItemID id = rig.Ground.Spawn(item, ItemPosition, assignee, SPAWN_TICK);
            rig.Ground.Tick(OPEN_TICK);
            return id;
        }

        private static void AssertPoolPaid(Rig rig, uint share)
        {
            CharacterID[] recipients = { CharA, CharB, CharC, CharD };
            Assert.AreEqual(recipients.Length, rig.Gold.Adds.Count);
            for (int i = 0; i < recipients.Length; i++)
            {
                Assert.AreEqual(recipients[i], rig.Gold.Adds[i].Character);
                Assert.AreEqual(share, rig.Gold.Adds[i].Amount);
                Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Gold.Adds[i].Reason);
            }
        }

        private static void AssertResolvedOnce(Rig rig, GroundItemID id, CharacterID winner, uint share, bool fallback)
        {
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(id, rig.Resolved[0].GroundItemId);
            Assert.AreEqual(winner, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(share, rig.Resolved[0].GoldPerMember);
            Assert.AreEqual(fallback, rig.Resolved[0].IsRoundRobinFallback);
        }

        private static void AssertSingleSpend(Rig rig, CharacterID bidder, uint bid)
        {
            Assert.AreEqual(ONE_CALL, rig.Gold.Spends.Count);
            Assert.AreEqual(bidder, rig.Gold.Spends[0].Character);
            Assert.AreEqual(bid, rig.Gold.Spends[0].Amount);
            Assert.AreEqual(GoldTransactionReason.AuctionBid, rig.Gold.Spends[0].Reason);
        }

        private static void AssertSinglePickup(Rig rig, CharacterID winner, ItemID item)
        {
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(winner, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(item, rig.Inventory.Calls[0].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[0].Quantity);
        }

        private static void AssertNoRecord(Rig rig, GroundItemID id)
        {
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
        }

        // -----------------------------------------------------------------------
        // AC-LT-19: auction resolves at teardown
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_AuctionWithThreeBidsBeforeWindowClose_HighestBidderWinsAndNoRecordRemains()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            Assert.Less(FLUSH_BEFORE_CLOSE_TICK, WINDOW_CLOSE_TICK);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            AssertSinglePickup(rig, CharA, DarkSteel);
            AssertPoolPaid(rig, SHARE_OF_FOUR);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
            AssertNoRecord(rig, id);
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
        }

        [Test]
        public void FlushForZoneTeardown_AuctionWithZeroBids_RoundRobinAndResolvedAreRecordedBeforeTheDespawn()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);
            rig.Order.Clear();

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert: assigned once, cursor advanced once, no pickup, then the despawn
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(id, rig.Assigned[0].GroundItemId);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            int assignedAt = rig.Order.IndexOf(EVENT_ASSIGNED);
            int resolvedAt = rig.Order.IndexOf(EVENT_RESOLVED);
            int despawnedAt = rig.Order.IndexOf(EVENT_DESPAWNED);
            Assert.GreaterOrEqual(assignedAt, 0);
            Assert.GreaterOrEqual(resolvedAt, 0);
            Assert.Less(assignedAt, despawnedAt);
            Assert.Less(resolvedAt, despawnedAt);
            AssertNoRecord(rig, id);
        }

        // -----------------------------------------------------------------------
        // Full bag at teardown
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_TopBidderBagFull_NextBidderIsChargedAndWinsWithNoNotice()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            FillBag(rig, CharA);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert
            AssertSingleSpend(rig, CharB, BID_THREE_FIFTY);
            AssertSinglePickup(rig, CharB, DarkSteel);
            AssertPoolPaid(rig, SHARE_OF_FOUR_FROM_THREE_FIFTY);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR_FROM_THREE_FIFTY, false);
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
        }

        [Test]
        public void FlushForZoneTeardown_AuctionAlreadyInTheWinnerGraceOfAFullBag_NextBidderWinsAndNoSecondNotice()
        {
            // Arrange: the close tick starts A's grace and raises the one notice
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_INSIDE_GRACE_TICK);

            // Assert
            AssertSingleSpend(rig, CharB, BID_THREE_FIFTY);
            AssertSinglePickup(rig, CharB, DarkSteel);
            AssertPoolPaid(rig, SHARE_OF_FOUR_FROM_THREE_FIFTY);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR_FROM_THREE_FIFTY, false);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        [Test]
        public void FlushForZoneTeardown_GraceBidderFreedASlotWithNoTickSince_BidderIsChargedAndWins()
        {
            // Arrange: A's grace is running; A frees a slot and no tick follows
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            FreeASlot(rig, CharA);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_INSIDE_GRACE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            AssertSinglePickup(rig, CharA, DarkSteel);
            AssertPoolPaid(rig, SHARE_OF_FOUR);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
        }

        // -----------------------------------------------------------------------
        // Nobody can receive it
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_EveryBidderBagFull_NoGoldMovesAndTheItemIsDespawnedWithoutRoundRobin()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            FillBag(rig, CharA);
            FillBag(rig, CharB);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.AreEqual(NO_EVENTS, rig.Assigned.Count);
            Assert.AreEqual(NO_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(0, rig.Parties.Cursor);
            AssertNoRecord(rig, id);
        }

        // -----------------------------------------------------------------------
        // All ground items despawn
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_TwoAssignedItemsAndOneSpawning_ThreeDespawnEventsAndNoRecordRemains()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID first = rig.Ground.Spawn(Steel, ItemPosition, CharA, SPAWN_TICK);
            GroundItemID second = rig.Ground.Spawn(Steel, ItemPosition, CharB, SPAWN_TICK);
            rig.Ground.Tick(OPEN_TICK);
            GroundItemID spawning = rig.Ground.Spawn(Steel, ItemPosition, CharC, OPEN_TICK);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(first, out GroundItem firstItem));
            Assert.AreEqual(GroundItemState.Assigned, firstItem.State);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(second, out GroundItem secondItem));
            Assert.AreEqual(GroundItemState.Assigned, secondItem.State);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(spawning, out GroundItem spawningItem));
            Assert.AreEqual(GroundItemState.Spawning, spawningItem.State);

            // Act
            rig.Coordinator.FlushForZoneTeardown(OPEN_TICK);

            // Assert
            Assert.AreEqual(THREE_EVENTS, rig.Despawned.Count);
            var despawned = new HashSet<GroundItemID>();
            foreach (GroundItemDespawnedEventArgs args in rig.Despawned)
            {
                despawned.Add(args.GroundItemId);
            }
            Assert.IsTrue(despawned.Contains(first));
            Assert.IsTrue(despawned.Contains(second));
            Assert.IsTrue(despawned.Contains(spawning));
            AssertNoRecord(rig, first);
            AssertNoRecord(rig, second);
            AssertNoRecord(rig, spawning);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // No double award
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_OneItemAlreadyDeliveredAndOneAssigned_OnlyTheRemainingItemIsDespawned()
        {
            // Arrange: A picks up the first item on entering its radius; B's item stays Assigned
            Rig rig = BuildRig();
            GroundItemID delivered = SpawnAssigned(rig, Steel, CharA);
            GroundItemID remaining = rig.Ground.Spawn(Steel, ItemPosition, CharB, SPAWN_TICK);
            rig.Ground.Tick(OPEN_TICK);
            rig.Positions.Set(CharA, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            AssertSinglePickup(rig, CharA, Steel);
            AssertNoRecord(rig, delivered);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(remaining, out GroundItem remainingItem));
            Assert.AreEqual(GroundItemState.Assigned, remainingItem.State);
            int pickupsBeforeFlush = rig.Inventory.Calls.Count;

            // Act
            rig.Coordinator.FlushForZoneTeardown(ENTRY_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(remaining, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(pickupsBeforeFlush, rig.Inventory.Calls.Count);
            AssertNoRecord(rig, remaining);
        }

        // -----------------------------------------------------------------------
        // Disposal
        // -----------------------------------------------------------------------

        [Test]
        public void Dispose_AssignedItemBlockedOnAFullBagThenASlotFrees_NoFurtherPickupAndTheItemStaysLive()
        {
            // Arrange: A stands on the item with a full bag; one blocked pickup was made
            Rig rig = BuildRig();
            FillBag(rig, CharA);
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = SpawnAssigned(rig, Steel, CharA);
            rig.Positions.Set(CharA, NearPosition);
            rig.Ground.Tick(ENTRY_TICK);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            rig.Coordinator.Dispose();
            rig.Inventory.DefaultResult = PickupResult.Succeeded;

            // Act: the inventory reports a free slot, then both services tick
            Assert.DoesNotThrow(() =>
            {
                FreeASlot(rig, CharA);
                RunTick(rig, AFTER_DISPOSE_TICK);
            });

            // Assert: the retry did not happen; nothing flushed the item
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
        }

        [Test]
        public void Dispose_RareDropSpawnedAfterwards_AuctionServiceDoesNotTrackItAndResolvesNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Coordinator.Dispose();
            rig.Ground.SpawnAuction(DarkSteel, ItemPosition, Party, SPAWN_TICK);
            rig.Ground.Tick(OPEN_TICK);

            // Act
            rig.Auction.Tick(WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // Failure paths at teardown (code review 2026-10-03)
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_EveryBiddersPaymentFails_RoundRobinRunsThenTheItemIsDespawned()
        {
            // Arrange: both bidders have room but hold less than their bids
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            rig.Currency.RegisterCharacter(CharA, SHORT_GOLD);
            rig.Currency.RegisterCharacter(CharB, SHORT_GOLD);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert: nobody was passed over for a full bag, so CR-LT-10 runs before the despawn
            Assert.AreEqual(TWO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(SHORT_GOLD, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(SHORT_GOLD, rig.Currency.GetBalance(CharB));
            AssertNoRecord(rig, id);
        }

        [Test]
        public void FlushForZoneTeardown_PartyReadThrows_ItemIsDespawnedAndTheFallbackIsAnnounced()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            rig.Parties.ThrowOnMembersRead = true;
            LogAssert.Expect(LogType.Exception, new Regex(MutablePartyService.MEMBERS_FAILURE_MESSAGE));

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert: no gold moved; the outcome is still announced once
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(NO_EVENTS, rig.Assigned.Count);
            AssertNoRecord(rig, id);
        }

        [Test]
        public void FlushForZoneTeardown_AuctionStepThrows_GroundItemsAreStillDespawnedAndRecordsCleared()
        {
            // Arrange: one Assigned item; a coordinator whose auction service fails at teardown
            Rig rig = BuildRig();
            GroundItemID id = SpawnAssigned(rig, Steel, CharA);
            var coordinator = new LootTeardownCoordinator(new ThrowingAuctionService(), rig.Ground, rig.LootTable);
            LogAssert.Expect(LogType.Exception, new Regex(AUCTION_FAILURE));

            // Act
            coordinator.FlushForZoneTeardown(ENTRY_TICK);

            // Assert: the later steps ran
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(ONE_CALL, rig.LootTable.ClearCalls);
            AssertNoRecord(rig, id);
        }

        // -----------------------------------------------------------------------
        // Damage records
        // -----------------------------------------------------------------------

        [Test]
        public void FlushForZoneTeardown_Called_ClearsTheLootTableServiceOnce()
        {
            // Arrange
            Rig rig = BuildRig();
            OpenDarkSteelAuctionWithThreeBids(rig);

            // Act
            rig.Coordinator.FlushForZoneTeardown(FLUSH_BEFORE_CLOSE_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.LootTable.ClearCalls);
        }
    }
}

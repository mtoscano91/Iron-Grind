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
    /// EditMode integration tests for Loot Table Story 011: auction resolution and the gold pool
    /// (design/gdd/loot-table-system.md CR-LT-9, CR-LT-10, CR-LT-12; AC-LT-12, AC-LT-13, AC-LT-14,
    /// AC-LT-16 auction half). Uses a real <see cref="GroundItemService"/>, <see cref="LootEquipmentCache"/>,
    /// <see cref="LootDropDistributor"/>, <see cref="LootAuctionService"/> and <see cref="CurrencySystem"/>
    /// (wrapped by a call-recording <see cref="ICurrencyService"/>), with a stub party service.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_AuctionResolution_Integration_Tests
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
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;

        // The first tick after the spawn comes late, so the window closes after the expiry tick.
        private const uint LATE_OPEN_TICK = EXPIRY_TICK - (uint)LootTableConstants.AUCTION_WINDOW_TICKS + 100u;

        private const uint STARTING_GOLD = 1000u;
        private const uint SHORT_GOLD = 300u;
        private const uint POOR_GOLD = 100u;
        private const uint BID_FOUR_HUNDRED = 400u;
        private const uint BID_THREE_FIFTY = 350u;
        private const uint BID_FIVE_HUNDRED = 500u;
        private const uint BID_SINGLE = 100u;
        private const uint BID_TICK_A = 100u;
        private const uint BID_TICK_B = 120u;
        private const uint BID_TICK_C = 80u;
        private const uint SHARE_OF_FOUR = 100u;
        private const uint SHARE_OF_THREE = 133u;
        private const uint SHARE_SINGLE_OF_FOUR = 25u;
        private const uint SHARE_THREE_FIFTY_OF_FOUR = 87u;
        private const uint NO_GOLD = 0u;

        private const uint BID_DARK_STEEL_FLOOR = (uint)DARK_STEEL_PRICE;
        private const uint NO_WINDOW_CLOSE_TICK = 0u;

        // An auction that opens just before the tick counter wraps and closes after it.
        private const uint WRAP_SPAWN_TICK = uint.MaxValue - 300u;
        private const uint WRAP_OPEN_TICK = WRAP_SPAWN_TICK + 1u;
        private const uint WRAP_CLOSE_TICK = unchecked(WRAP_OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS);
        private const uint BID_TICK_BEFORE_WRAP = uint.MaxValue - 10u;
        private const uint BID_TICK_AFTER_WRAP = 5u;

        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int FOUR_CALLS = 4;
        private const int ONE_EVENT = 1;
        private const int TWO_EVENTS = 2;
        private const int NO_EVENTS = 0;
        private const int PICKUP_QUANTITY = 1;
        private const int FIRST_MEMBER_CURSOR = 0;
        private const int SECOND_MEMBER_CURSOR = 1;
        private const int EMPTY_SLOT_CURSOR = 9;
        private const long PARTY_OF_FOUR = 4L;
        private const string ROUND_ROBIN_FAILURE = MutablePartyService.ROUND_ROBIN_FAILURE_MESSAGE;
        private const string MEMBERS_FAILURE = MutablePartyService.MEMBERS_FAILURE_MESSAGE;
        private const string POOL_FAILURE = RecordingCurrencyService.ADD_FAILURE_MESSAGE;
        private const string PICKUP_FAILURE = "pickup failure";

        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly CharacterID CharD = new CharacterID(RAW_CHAR_D);
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
        private static readonly ItemID Steel = new ItemID(STEEL_ID);
        private static readonly ItemID DarkSteel = new ItemID(DARK_STEEL_ID);
        private static readonly Vector3 MobPosition = new Vector3(1f, 2f, 3f);

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
                _rig.Auction.Dispose();
                _rig.Ground.Dispose();
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
            public readonly List<GroundItemSpawnedEventArgs> Spawned = new List<GroundItemSpawnedEventArgs>();
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();
            public readonly List<GroundItemAssignedEventArgs> Assigned = new List<GroundItemAssignedEventArgs>();
            public readonly List<AuctionResolvedEventArgs> Resolved = new List<AuctionResolvedEventArgs>();
            public readonly List<BagFullPickupBlockedEventArgs> Blocked = new List<BagFullPickupBlockedEventArgs>();
            public uint Tick = SPAWN_TICK;

            public void RecordBlocked(BagFullPickupBlockedEventArgs args) => Blocked.Add(args);

            public uint ReadTick() => Tick;

            public void RecordSpawn(GroundItemSpawnedEventArgs args) => Spawned.Add(args);

            public void RecordDespawn(GroundItemDespawnedEventArgs args) => Despawned.Add(args);

            public void RecordAssigned(GroundItemAssignedEventArgs args) => Assigned.Add(args);

            public void RecordResolved(AuctionResolvedEventArgs args) => Resolved.Add(args);
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

        // A party of four (A, B, C, D), each registered with STARTING_GOLD unless it is left out.
        private Rig BuildRig(bool registerCharA = true)
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
            if (registerCharA)
            {
                rig.Currency.RegisterCharacter(CharA, STARTING_GOLD);
            }
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
            _rig = rig;
            return rig;
        }

        // Drives one server tick the way the tick loop does: ground items first, then auctions.
        private static void RunTick(Rig rig, uint tick)
        {
            rig.Ground.Tick(tick);
            rig.Auction.Tick(tick);
        }

        // Drops the item at SPAWN_TICK (a Rare one opens an auction) and returns the ground item.
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

        // The first tick after the spawn comes late: the window closes after the expiry tick.
        private static GroundItemID OpenLateAuction(Rig rig, ItemID item)
        {
            GroundItemID id = Drop(rig, item);
            RunTick(rig, LATE_OPEN_TICK);
            return id;
        }

        private static void Bid(Rig rig, GroundItemID id, CharacterID bidder, uint amount, uint receivedTick)
        {
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(bidder, id, amount, receivedTick));
        }

        // The DarkSteel auction of AC-LT-12: submitted C, B, A so that the tick, not arrival, breaks A/B's tie.
        private static GroundItemID OpenDarkSteelAuctionWithThreeBids(Rig rig)
        {
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharC, BID_THREE_FIFTY, BID_TICK_C);
            Bid(rig, id, CharB, BID_FOUR_HUNDRED, BID_TICK_B);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            return id;
        }

        private static void AssertPoolPaid(Rig rig, uint share, params CharacterID[] recipients)
        {
            Assert.AreEqual(recipients.Length, rig.Gold.Adds.Count);
            for (int i = 0; i < recipients.Length; i++)
            {
                Assert.AreEqual(recipients[i], rig.Gold.Adds[i].Character);
                Assert.AreEqual(share, rig.Gold.Adds[i].Amount);
                Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Gold.Adds[i].Reason);
            }
        }

        // -----------------------------------------------------------------------
        // AC-LT-12 winner, tie-break, pool
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_DarkSteelAuctionAtWindowClose_EarlierOfTwoEqualBidsWinsAndPoolIsSplitFourWays()
        {
            // Arrange: A 400 @100, B 400 @120, C 350 @80, D nothing
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: A wins, the item is picked up once, floor(400 / 4) = 100 goes to each member
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(CharA, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(DarkSteel, rig.Inventory.Calls[0].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[0].Quantity);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(id, rig.Resolved[0].GroundItemId);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(SHARE_OF_FOUR, rig.Resolved[0].GoldPerMember);
            Assert.IsFalse(rig.Resolved[0].IsRoundRobinFallback);
            Assert.AreEqual(Party, rig.Resolved[0].PartyId);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharA, out _, out _));
        }

        [Test]
        public void Tick_DarkSteelWinnerHadAFreeSlotButPickupFindsTheBagFull_ItemStaysAssignedToWinnerAndPoolIsStillSplit()
        {
            // Arrange: HasFreeSlot answers true (rig default) but the pickup reports a full bag,
            // as if the slot was taken between the check and the pickup (the invariant path)
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            LogAssert.Expect(LogType.Error, new Regex("had a free slot but the pickup failed"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(CharA, rig.Inventory.Calls[0].Character);

            // A close at the window close tick keeps the original expiry tick
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
            Assert.IsFalse(rig.Resolved[0].IsRoundRobinFallback);
        }

        [Test]
        public void Tick_WinnerHadAFreeSlotButPickupFindsTheBagFullAndStandsOnTheItem_BlockedNoticeIsRaisedOnceAcrossTwoTicks()
        {
            // Arrange: A stands on the item; HasFreeSlot answers true but the pickup finds the bag full (invariant path)
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            rig.Positions.Set(CharA, MobPosition);
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            LogAssert.Expect(LogType.Error, new Regex("had a free slot but the pickup failed"));

            // Act: the award, then one more tick with A still standing there
            RunTick(rig, WINDOW_CLOSE_TICK);
            RunTick(rig, WINDOW_CLOSE_TICK + 1u);

            // Assert: the award was the one pickup attempt; A is not treated as newly entered
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(id, rig.Blocked[0].GroundItemId);
        }

        // -----------------------------------------------------------------------
        // AC-LT-13 gold neutrality and disqualification
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_DarkSteelAuctionAtWindowClose_SpendsTheWinningBidOnceAndTheFourBalanceChangesSumToZero()
        {
            // Arrange
            Rig rig = BuildRig();
            OpenDarkSteelAuctionWithThreeBids(rig);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: one debit of A's 400 for the auction, 100 back to each member
            Assert.AreEqual(ONE_CALL, rig.Gold.Spends.Count);
            Assert.AreEqual(CharA, rig.Gold.Spends[0].Character);
            Assert.AreEqual(BID_FOUR_HUNDRED, rig.Gold.Spends[0].Amount);
            Assert.AreEqual(GoldTransactionReason.AuctionBid, rig.Gold.Spends[0].Reason);
            Assert.AreEqual(STARTING_GOLD - BID_FOUR_HUNDRED + SHARE_OF_FOUR, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(STARTING_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharB));
            Assert.AreEqual(STARTING_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharC));
            Assert.AreEqual(STARTING_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharD));
            long total = (long)rig.Currency.GetBalance(CharA) + rig.Currency.GetBalance(CharB)
                + rig.Currency.GetBalance(CharC) + rig.Currency.GetBalance(CharD);
            Assert.AreEqual(PARTY_OF_FOUR * STARTING_GOLD, total);
        }

        [Test]
        public void Tick_HighestBidderHasOnlyThreeHundredGold_InsufficientFundsDisqualifiesAAndBWins()
        {
            // Arrange: A cannot cover 400
            Rig rig = BuildRig();
            rig.Currency.RegisterCharacter(CharA, SHORT_GOLD);
            OpenDarkSteelAuctionWithThreeBids(rig);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: A's debit fails, then B's succeeds
            Assert.AreEqual(TWO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(CharA, rig.Gold.Spends[0].Character);
            Assert.AreEqual(BID_FOUR_HUNDRED, rig.Gold.Spends[0].Amount);
            Assert.AreEqual(CharB, rig.Gold.Spends[1].Character);
            Assert.AreEqual(BID_FOUR_HUNDRED, rig.Gold.Spends[1].Amount);
            Assert.AreEqual(CharB, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharB, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(SHARE_OF_FOUR, rig.Resolved[0].GoldPerMember);
            Assert.AreEqual(SHORT_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(STARTING_GOLD - BID_FOUR_HUNDRED + SHARE_OF_FOUR, rig.Currency.GetBalance(CharB));
        }

        // -----------------------------------------------------------------------
        // AC-LT-14 zero bids
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_SteelAuctionWithZeroBidsAtWindowClose_AssignsByRoundRobinWithoutMovingGold()
        {
            // Arrange: party [A, B], cursor at A
            Rig rig = BuildRig();
            rig.Parties.Members.Remove(CharC);
            rig.Parties.Members.Remove(CharD);
            rig.Parties.Cursor = FIRST_MEMBER_CURSOR;
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);

            // A close at the window close tick keeps the original expiry tick; the window is cleared
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);
            Assert.AreEqual(NO_WINDOW_CLOSE_TICK, item.WindowCloseTick);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(SECOND_MEMBER_CURSOR, rig.Parties.Cursor);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(id, rig.Resolved[0].GroundItemId);
            Assert.AreEqual(CharacterID.Invalid, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(NO_GOLD, rig.Resolved[0].GoldPerMember);
            Assert.IsTrue(rig.Resolved[0].IsRoundRobinFallback);
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(id, rig.Assigned[0].GroundItemId);
            Assert.AreEqual(CharA, rig.Assigned[0].AssignedTo);
        }

        // -----------------------------------------------------------------------
        // AC-LT-16 auction half: expiryTick before windowCloseTick
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AuctionReachesExpiryTickBeforeWindowClose_ResolvesWithItsBidAndDoesNotDespawn()
        {
            // Arrange: the window opens late, so it would close after the expiry tick
            Rig rig = BuildRig();
            GroundItemID id = OpenLateAuction(rig, Steel);
            Assert.Greater(LATE_OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS, EXPIRY_TICK);
            Bid(rig, id, CharA, BID_SINGLE, LATE_OPEN_TICK);
            RunTick(rig, EXPIRY_TICK - 1u);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);

            // Act
            RunTick(rig, EXPIRY_TICK);

            // Assert: resolved normally at the expiry tick, nothing despawned
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(SHARE_SINGLE_OF_FOUR, rig.Resolved[0].GoldPerMember);
            Assert.IsFalse(rig.Resolved[0].IsRoundRobinFallback);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
        }

        [Test]
        public void Tick_AuctionReachesExpiryTickBeforeWindowCloseWithZeroBids_FallsBackToRoundRobinWithoutDespawning()
        {
            // Arrange
            Rig rig = BuildRig();
            OpenLateAuction(rig, Steel);

            // Act
            RunTick(rig, EXPIRY_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.IsTrue(rig.Resolved[0].IsRoundRobinFallback);
            Assert.AreEqual(CharacterID.Invalid, rig.Resolved[0].WinnerCharacterId);
        }

        // -----------------------------------------------------------------------
        // CR-LT-9 exhausted bidders
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_EveryBidderFailsTheSpend_FallsBackToRoundRobinAndNoGoldMoves()
        {
            // Arrange: A bids 400 and B 350, each holding 100
            Rig rig = BuildRig();
            rig.Currency.RegisterCharacter(CharA, POOR_GOLD);
            rig.Currency.RegisterCharacter(CharB, POOR_GOLD);
            GroundItemID id = OpenAuction(rig, Steel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharB, BID_THREE_FIFTY, BID_TICK_B);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: two failed spends, then the round-robin gives the item to A (cursor 0)
            Assert.AreEqual(TWO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(POOR_GOLD, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(POOR_GOLD, rig.Currency.GetBalance(CharB));
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.IsTrue(rig.Resolved[0].IsRoundRobinFallback);
            Assert.AreEqual(NO_GOLD, rig.Resolved[0].GoldPerMember);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);
        }

        // -----------------------------------------------------------------------
        // Party size at close
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_MemberLeftBeforeClose_PoolIsSplitAmongTheThreeRemainingMembers()
        {
            // Arrange: party of 4 at kill time; D leaves before the close; A wins with 400
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            rig.Parties.Members.Remove(CharD);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: N = 3, floor(400 / 3) = 133 each, nothing for D
            AssertPoolPaid(rig, SHARE_OF_THREE, CharA, CharB, CharC);
            Assert.AreEqual(SHARE_OF_THREE, rig.Resolved[0].GoldPerMember);
            Assert.AreEqual(STARTING_GOLD, rig.Currency.GetBalance(CharD));
        }

        [Test]
        public void Tick_HighestBidderLeftBeforeClose_ItsBidIsSkippedWithoutASpendAndTheNextBidWins()
        {
            // Arrange: D has the highest bid, then leaves
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharD, BID_FIVE_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_B);
            rig.Parties.Members.Remove(CharD);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.Gold.Spends.Count);
            Assert.AreEqual(CharA, rig.Gold.Spends[0].Character);
            Assert.AreEqual(BID_FOUR_HUNDRED, rig.Gold.Spends[0].Amount);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
            AssertPoolPaid(rig, SHARE_OF_THREE, CharA, CharB, CharC);
            Assert.AreEqual(STARTING_GOLD, rig.Currency.GetBalance(CharD));
        }

        // -----------------------------------------------------------------------
        // Unexpected spend error
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_HighestBidderNotRegisteredInTheCurrencyService_IsDisqualifiedWithAnErrorAndNextBidderWins()
        {
            // Arrange: A bids 400 but the currency service does not know A (CharacterNotFound)
            Rig rig = BuildRig(registerCharA: false);
            GroundItemID id = OpenAuction(rig, Steel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharB, BID_THREE_FIFTY, BID_TICK_B);

            // The failed spend, then the pool credit that A (still a member) cannot receive either
            LogAssert.Expect(LogType.Error, new Regex(@"\[CurrencySystem\] TrySpendGold"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: TrySpendGold.*CharacterNotFound"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[CurrencySystem\] AddGold"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: AddGold.*CharacterNotFound"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: B wins with 350 and the rest of the pool is still paid
            Assert.AreEqual(TWO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(CharA, rig.Gold.Spends[0].Character);
            Assert.AreEqual(CharB, rig.Gold.Spends[1].Character);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharB, rig.Resolved[0].WinnerCharacterId);
            Assert.AreEqual(SHARE_THREE_FIFTY_OF_FOUR, rig.Resolved[0].GoldPerMember);
            Assert.AreEqual(STARTING_GOLD - BID_THREE_FIFTY + SHARE_THREE_FIFTY_OF_FOUR, rig.Currency.GetBalance(CharB));
            Assert.AreEqual(STARTING_GOLD + SHARE_THREE_FIFTY_OF_FOUR, rig.Currency.GetBalance(CharC));
            Assert.AreEqual(STARTING_GOLD + SHARE_THREE_FIFTY_OF_FOUR, rig.Currency.GetBalance(CharD));
            Assert.AreEqual(FOUR_CALLS, rig.Gold.Adds.Count);
        }

        // -----------------------------------------------------------------------
        // Fresh TTL on an expiryTick close
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ZeroBidAuctionClosesAtExpiryTick_ItemIsAssignedWithAFreshExpiryTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenLateAuction(rig, Steel);

            // Act
            RunTick(rig, EXPIRY_TICK);

            // Assert: the assignee gets a full pickup window, nothing despawned
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);
            Assert.AreEqual(EXPIRY_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS, item.ExpiryTick);
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(item.ExpiryTick, rig.Assigned[0].ExpiryTick);
        }

        [Test]
        public void Tick_AuctionClosesAtExpiryTickWinnerHadAFreeSlotButPickupFindsTheBagFull_ItemIsAssignedToWinnerWithAFreshExpiryTick()
        {
            // Arrange: one valid bid; HasFreeSlot answers true but the pickup finds the bag full (invariant path)
            Rig rig = BuildRig();
            rig.Inventory.DefaultResult = PickupResult.Fail(PickupFailReason.InventoryFull);
            GroundItemID id = OpenLateAuction(rig, Steel);
            Bid(rig, id, CharA, BID_SINGLE, LATE_OPEN_TICK);
            LogAssert.Expect(LogType.Error, new Regex("had a free slot but the pickup failed"));

            // Act
            RunTick(rig, EXPIRY_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);
            Assert.AreEqual(EXPIRY_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS, item.ExpiryTick);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
        }

        // -----------------------------------------------------------------------
        // Failure paths: the item never stays Auctioning
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_PartyMembersReadThrows_NothingChangesAndTheAuctionResolvesOnTheNextTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Bid(rig, id, CharA, BID_SINGLE, OPEN_TICK);
            rig.Parties.ThrowOnMembersRead = true;
            LogAssert.Expect(LogType.Exception, new Regex(MEMBERS_FAILURE));

            // Act: the read fails at the close tick, then works one tick later
            RunTick(rig, WINDOW_CLOSE_TICK);
            int resolvedAfterFailure = rig.Resolved.Count;
            bool bidKept = rig.Auction.TryGetBid(id, CharA, out _, out _);
            bool stillAuctioning = rig.Ground.TryGetGroundItem(id, out GroundItem item)
                && item.State == GroundItemState.Auctioning;
            rig.Parties.ThrowOnMembersRead = false;
            RunTick(rig, WINDOW_CLOSE_TICK + 1u);

            // Assert
            Assert.AreEqual(NO_EVENTS, resolvedAfterFailure);
            Assert.IsTrue(bidKept);
            Assert.IsTrue(stillAuctioning);
            Assert.AreEqual(ONE_CALL, rig.Gold.Spends.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
        }

        [Test]
        public void Tick_RoundRobinThrowsOnAZeroBidAuction_ItemIsRemovedAndTheFallbackIsStillAnnounced()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            rig.Parties.ThrowOnCursorRead = true;
            LogAssert.Expect(LogType.Exception, new Regex(ROUND_ROBIN_FAILURE));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: .*failed before it could be assigned"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.AreEqual(NO_EVENTS, rig.Assigned.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.IsTrue(rig.Resolved[0].IsRoundRobinFallback);
        }

        [Test]
        public void Tick_PoolPaymentThrowsAfterTheWinnerPaid_ItemStillGoesToTheWinner()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            rig.Gold.ThrowOnAdd = true;
            LogAssert.Expect(LogType.Exception, new Regex(POOL_FAILURE));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: .*failed after winner .* paid"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: A paid and receives the item; nothing is refunded
            Assert.AreEqual(ONE_CALL, rig.Gold.Spends.Count);
            Assert.AreEqual(STARTING_GOLD - BID_FOUR_HUNDRED, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(CharA, rig.Inventory.Calls[0].Character);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
            Assert.IsFalse(rig.Resolved[0].IsRoundRobinFallback);
        }

        [Test]
        public void Tick_WinnerPickupThrows_ItemIsRemovedWithAnErrorAndThePoolIsStillPaid()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            rig.Inventory.ThrowOnPickup = new InvalidOperationException(PICKUP_FAILURE);
            LogAssert.Expect(LogType.Exception, new Regex(PICKUP_FAILURE));
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] .*pickup of .* threw"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: .*was not delivered to winner"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            Assert.AreEqual(STARTING_GOLD - BID_FOUR_HUNDRED + SHARE_OF_FOUR, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
        }

        [Test]
        public void Tick_NoMemberAtTheRoundRobinCursor_ItemIsRemovedAndTheCursorStillAdvances()
        {
            // Arrange: zero bids and a cursor that points at no member
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            rig.Parties.Cursor = EMPTY_SLOT_CURSOR;
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] Resolve: no member at round-robin cursor"));

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(NO_EVENTS, rig.Assigned.Count);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.IsTrue(rig.Resolved[0].IsRoundRobinFallback);
        }

        // -----------------------------------------------------------------------
        // Ordering and lifecycle
        // -----------------------------------------------------------------------

        private static CharacterID WinnerOf(Rig rig, GroundItemID id)
        {
            for (int i = 0; i < rig.Resolved.Count; i++)
            {
                if (rig.Resolved[i].GroundItemId == id)
                {
                    return rig.Resolved[i].WinnerCharacterId;
                }
            }
            Assert.Fail($"No OnAuctionResolved for {id}.");
            return CharacterID.Invalid;
        }

        [Test]
        public void Tick_TwoAuctionsDueOnTheSameTick_BothAreResolved()
        {
            // Arrange: two auctions opened on the same tick, each with one bid
            Rig rig = BuildRig();
            GroundItemID first = Drop(rig, Steel);
            GroundItemID second = Drop(rig, DarkSteel);
            RunTick(rig, OPEN_TICK);
            Bid(rig, first, CharA, BID_SINGLE, OPEN_TICK);
            Bid(rig, second, CharB, BID_DARK_STEEL_FLOOR, OPEN_TICK);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: the order of the two events is unspecified
            Assert.AreEqual(TWO_EVENTS, rig.Resolved.Count);
            Assert.AreEqual(CharA, WinnerOf(rig, first));
            Assert.AreEqual(CharB, WinnerOf(rig, second));
            Assert.AreEqual(TWO_CALLS, rig.Inventory.Calls.Count);
        }

        [Test]
        public void Tick_EqualBidsEitherSideOfATickWrap_TheBidBeforeTheWrapWins()
        {
            // Arrange: B's bid arrives first in the list but was received after the wrap
            Rig rig = BuildRig();
            rig.Tick = WRAP_SPAWN_TICK;
            GroundItemID id = Drop(rig, DarkSteel);
            RunTick(rig, WRAP_OPEN_TICK);
            Bid(rig, id, CharB, BID_FOUR_HUNDRED, BID_TICK_AFTER_WRAP);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_BEFORE_WRAP);

            // Act
            RunTick(rig, WRAP_CLOSE_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharA, rig.Resolved[0].WinnerCharacterId);
        }

        [Test]
        public void Tick_EqualBidsOnTheSameTick_TheBidThatArrivedFirstWins()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharB, BID_FOUR_HUNDRED, BID_TICK_A);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Resolved.Count);
            Assert.AreEqual(CharB, rig.Resolved[0].WinnerCharacterId);
        }

        [Test]
        public void Tick_AfterDispose_ResolvesNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            rig.Auction.Dispose();

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
        }
    }
}

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
    /// EditMode integration tests for Loot Table Story 013: the auction winner grace
    /// (design/gdd/loot-table-system.md CR-LT-9.1, CR-LT-10, CR-LT-12; AC-LT-25). Uses a real
    /// <see cref="GroundItemService"/>, <see cref="LootEquipmentCache"/>, <see cref="LootDropDistributor"/>,
    /// <see cref="LootAuctionService"/> and <see cref="CurrencySystem"/> (wrapped by a call-recording
    /// <see cref="ICurrencyService"/>), with a stub party service and a per-character inventory fake.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_AuctionWinnerGrace_Integration_Tests
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
        private const uint GRACE_TICKS = (uint)LootTableConstants.AUCTION_WINNER_GRACE_TICKS;
        private const uint TTL_TICKS = (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint FIRST_DEADLINE_TICK = WINDOW_CLOSE_TICK + GRACE_TICKS;
        private const uint SECOND_DEADLINE_TICK = FIRST_DEADLINE_TICK + GRACE_TICKS;
        private const uint INSIDE_GRACE_OFFSET = 10u;
        private const uint MID_GRACE_TICK = WINDOW_CLOSE_TICK + INSIDE_GRACE_OFFSET;
        private const uint LATER_GRACE_TICK = MID_GRACE_TICK + INSIDE_GRACE_OFFSET;

        // An auction that closes 300 ticks before the item's expiry tick, so its grace spans the expiry tick.
        private const uint CLOSE_BEFORE_EXPIRY_OFFSET = 300u;
        private const uint EARLY_CLOSE_TICK = EXPIRY_TICK - CLOSE_BEFORE_EXPIRY_OFFSET;
        private const uint EARLY_OPEN_TICK = EARLY_CLOSE_TICK - (uint)LootTableConstants.AUCTION_WINDOW_TICKS;
        private const uint EARLY_DEADLINE_TICK = EARLY_CLOSE_TICK + GRACE_TICKS;
        private const uint PAST_EXPIRY_TICK = EXPIRY_TICK + 1u;

        private const uint STARTING_GOLD = 1000u;
        private const uint SHORT_GOLD = 300u;
        private const uint BALANCE_ONE_SHORT = 399u;
        private const uint BID_FOUR_HUNDRED = 400u;
        private const uint BID_THREE_FIFTY = 350u;
        private const uint BID_TICK_A = 100u;
        private const uint BID_TICK_B = 120u;
        private const uint BID_TICK_C = 80u;
        private const uint SHARE_OF_FOUR = 100u;
        private const uint SHARE_OF_THREE = 133u;
        private const uint NO_GOLD = 0u;
        private const string MEMBERS_FAILURE = MutablePartyService.MEMBERS_FAILURE_MESSAGE;

        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int NO_EVENTS = 0;
        private const int ONE_EVENT = 1;
        private const int TWO_EVENTS = 2;
        private const int PICKUP_QUANTITY = 1;

        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly CharacterID CharD = new CharacterID(RAW_CHAR_D);
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
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

        // The auction opens late enough in the item's life that it closes before the expiry tick
        // while its grace runs past it.
        private static GroundItemID OpenEarlyClosingAuction(Rig rig, ItemID item)
        {
            GroundItemID id = Drop(rig, item);
            RunTick(rig, EARLY_OPEN_TICK);
            return id;
        }

        private static void Bid(Rig rig, GroundItemID id, CharacterID bidder, uint amount, uint receivedTick)
        {
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(bidder, id, amount, receivedTick));
        }

        // The DarkSteel auction of AC-LT-12: A 400 @100, B 400 @120, C 350 @80.
        private static GroundItemID OpenDarkSteelAuctionWithThreeBids(Rig rig)
        {
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Bid(rig, id, CharC, BID_THREE_FIFTY, BID_TICK_C);
            Bid(rig, id, CharB, BID_FOUR_HUNDRED, BID_TICK_B);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
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

        private static void AssertStillAuctioning(Rig rig, GroundItemID id)
        {
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
        }

        // -----------------------------------------------------------------------
        // Free slot: unchanged from Story 011
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_EveryBagHasAFreeSlotAtClose_AWinsOnTheCloseTickAsInStory011()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(CharA, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(DarkSteel, rig.Inventory.Calls[0].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[0].Quantity);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: grace starts
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_WinnerBagFullAtClose_StartsGraceAndMovesNoGold()
        {
            // Arrange: A connected with 1000g, bag full
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert: no gold moved, one notice for A with the full grace, the auction is still open
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(id, rig.Blocked[0].GroundItemId);
            Assert.AreEqual(CharA, rig.Blocked[0].Recipient);
            Assert.AreEqual(GRACE_TICKS, rig.Blocked[0].RemainingTicks);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        [Test]
        public void Tick_TicksInsideTheGraceWithNoInventoryChange_RaiseNoSecondNoticeAndMoveNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Act
            RunTick(rig, MID_GRACE_TICK);
            RunTick(rig, LATER_GRACE_TICK);
            RunTick(rig, FIRST_DEADLINE_TICK - 1u);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: slot freed
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ASlotFreesDuringTheGrace_ASpendsPoolsAndPicksUpOnTheFirstTickAfterTheChange()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            FreeASlot(rig, CharA);

            // Act
            RunTick(rig, MID_GRACE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.AreEqual(CharA, rig.Inventory.Calls[0].Character);
            Assert.AreEqual(DarkSteel, rig.Inventory.Calls[0].Item);
            Assert.AreEqual(PICKUP_QUANTITY, rig.Inventory.Calls[0].Quantity);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
            Assert.AreEqual(STARTING_GOLD - BID_FOUR_HUNDRED + SHARE_OF_FOUR, rig.Currency.GetBalance(CharA));
            Assert.AreEqual(STARTING_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharB));
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
        }

        [Test]
        public void Tick_InventoryChangeLeavesTheBagFull_StillInTheGraceWithNoSecondNotice()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            rig.Inventory.RaiseInventoryChanged(CharA);

            // Act
            RunTick(rig, MID_GRACE_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        [Test]
        public void Tick_ASlotFreesButACanNoLongerPay_ADisqualifiedAndBWinsOnThatTick()
        {
            // Arrange: A has 1000g at the close (the grace starts), then drops to 300g
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            rig.Currency.RegisterCharacter(CharA, SHORT_GOLD);
            FreeASlot(rig, CharA);

            // Act
            RunTick(rig, MID_GRACE_TICK);

            // Assert: A's spend fails, B's succeeds on the same tick
            Assert.AreEqual(TWO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(CharA, rig.Gold.Spends[0].Character);
            Assert.AreEqual(CharB, rig.Gold.Spends[1].Character);
            Assert.AreEqual(BID_FOUR_HUNDRED, rig.Gold.Spends[1].Amount);
            Assert.AreEqual(CharB, rig.Inventory.Calls[0].Character);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR, false);
        }

        [Test]
        public void Tick_PartyReadThrowsOnTheTickAfterASlotFrees_AStillWinsOnTheNextTick()
        {
            // Arrange: A frees a slot, then the tick that would act on it fails at the party read
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            FreeASlot(rig, CharA);
            rig.Parties.ThrowOnMembersRead = true;
            LogAssert.Expect(LogType.Exception, new Regex(MEMBERS_FAILURE));
            RunTick(rig, MID_GRACE_TICK);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            rig.Parties.ThrowOnMembersRead = false;

            // Act: no new inventory change is raised
            RunTick(rig, LATER_GRACE_TICK);

            // Assert: the earlier change was not lost
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: grace expires
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_GraceExpires_ADisqualifiedWithNoGoldMovedAndBWinsOnThatTick()
        {
            // Arrange: A never frees a slot; B has one
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            RunTick(rig, FIRST_DEADLINE_TICK - 1u);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);

            // Act
            RunTick(rig, FIRST_DEADLINE_TICK);

            // Assert: the only spend is B's
            AssertSingleSpend(rig, CharB, BID_FOUR_HUNDRED);
            Assert.AreEqual(CharB, rig.Inventory.Calls[0].Character);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR, false);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(STARTING_GOLD + SHARE_OF_FOUR, rig.Currency.GetBalance(CharA));
        }

        [Test]
        public void Tick_ASlotFreesOnTheDeadlineTick_AIsStillDisqualifiedAndBWins()
        {
            // Arrange: A makes room after the last tick inside the grace (user decision 2026-10-03:
            // the deadline is checked first)
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            RunTick(rig, FIRST_DEADLINE_TICK - 1u);
            FreeASlot(rig, CharA);

            // Act
            RunTick(rig, FIRST_DEADLINE_TICK);

            // Assert: no spend for A; B wins on the deadline tick
            AssertSingleSpend(rig, CharB, BID_FOUR_HUNDRED);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR, false);
        }

        [Test]
        public void Tick_GraceExpiresAndBsBagIsAlsoFull_BGetsItsOwnNoticeWithAFullGrace()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            FillBag(rig, CharB);
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Act
            RunTick(rig, FIRST_DEADLINE_TICK);

            // Assert: B starts a full grace on the tick A's expired; nothing is spent or resolved
            Assert.AreEqual(TWO_EVENTS, rig.Blocked.Count);
            Assert.AreEqual(CharB, rig.Blocked[1].Recipient);
            Assert.AreEqual(id, rig.Blocked[1].GroundItemId);
            Assert.AreEqual(GRACE_TICKS, rig.Blocked[1].RemainingTicks);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: cannot afford
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_WinnerBagFullAndBalanceOneBelowTheBid_NoNoticeNoSpendForAAndBWinsOnTheCloseTick()
        {
            // Arrange: A holds 399g against a 400g bid
            Rig rig = BuildRig();
            rig.Currency.RegisterCharacter(CharA, BALANCE_ONE_SHORT);
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
            AssertSingleSpend(rig, CharB, BID_FOUR_HUNDRED);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR, false);
        }

        [Test]
        public void Tick_WinnerBagFullAndBalanceExactlyTheBid_StartsTheGrace()
        {
            // Arrange: A holds exactly the 400g bid
            Rig rig = BuildRig();
            rig.Currency.RegisterCharacter(CharA, BID_FOUR_HUNDRED);
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);
            Assert.AreEqual(CharA, rig.Blocked[0].Recipient);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: disconnected
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_WinnerBagFullAndNotConnected_NoNoticeAndBWinsOnTheCloseTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            rig.Parties.NotConnected.Add(CharA);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
            AssertSingleSpend(rig, CharB, BID_FOUR_HUNDRED);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_FOUR, false);
        }

        [Test]
        public void Tick_WinnerNotConnectedButWithAFreeSlot_AWinsAsWithAnyFreeSlot()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            rig.Parties.NotConnected.Add(CharA);

            // Act
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            AssertPoolPaid(rig, SHARE_OF_FOUR, CharA, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
            Assert.AreEqual(NO_EVENTS, rig.Blocked.Count);
        }

        [Test]
        public void Tick_WinnerBecomesNotConnectedDuringTheGrace_StillReservedAndWinsWhenASlotFrees()
        {
            // Arrange: the grace starts with A connected, then A drops
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            rig.Parties.NotConnected.Add(CharA);
            RunTick(rig, MID_GRACE_TICK);
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            FreeASlot(rig, CharA);

            // Act
            RunTick(rig, LATER_GRACE_TICK);

            // Assert
            AssertSingleSpend(rig, CharA, BID_FOUR_HUNDRED);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_FOUR, false);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: all disqualified
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_EveryBidderHasAFullBagThroughBothGraces_FallsBackToRoundRobinWithAFreshExpiryTick()
        {
            // Arrange: A 400 and B 350, both bags full; A's grace ends, then B gets its own
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithTwoBids(rig);
            FillBag(rig, CharA);
            FillBag(rig, CharB);
            RunTick(rig, WINDOW_CLOSE_TICK);
            RunTick(rig, FIRST_DEADLINE_TICK);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            RunTick(rig, SECOND_DEADLINE_TICK - 1u);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);

            // Act
            RunTick(rig, SECOND_DEADLINE_TICK);

            // Assert: no gold moved, round-robin cursor (A) gets the item with a fresh expiry
            Assert.AreEqual(NO_CALLS, rig.Gold.Spends.Count);
            Assert.AreEqual(NO_CALLS, rig.Gold.Adds.Count);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(CharA, item.AssignedTo);
            Assert.AreEqual(SECOND_DEADLINE_TICK + TTL_TICKS, item.ExpiryTick);
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(id, rig.Assigned[0].GroundItemId);
            Assert.AreEqual(CharA, rig.Assigned[0].AssignedTo);
            Assert.AreEqual(SECOND_DEADLINE_TICK + TTL_TICKS, rig.Assigned[0].ExpiryTick);
        }

        // -----------------------------------------------------------------------
        // AC-LT-25: pool on the paying tick
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AMemberLeavesDuringTheGraceThenAFreesASlot_PoolIsSplitAmongTheThreeRemainingMembers()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            rig.Parties.Members.Remove(CharD);
            FreeASlot(rig, CharA);

            // Act
            RunTick(rig, MID_GRACE_TICK);

            // Assert: N = 3 on the paying tick, floor(400 / 3) = 133 each
            AssertPoolPaid(rig, SHARE_OF_THREE, CharA, CharB, CharC);
            AssertResolvedOnce(rig, id, CharA, SHARE_OF_THREE, false);
            Assert.AreEqual(STARTING_GOLD, rig.Currency.GetBalance(CharD));
        }

        // -----------------------------------------------------------------------
        // No despawn during a grace
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ExpiryTickFallsInsideTheGrace_ItemStaysLiveAndAuctioningPastIt()
        {
            // Arrange: the auction closes before the expiry tick, A's grace spans it
            Rig rig = BuildRig();
            GroundItemID id = OpenEarlyClosingAuction(rig, DarkSteel);
            Assert.Less(EARLY_CLOSE_TICK, EXPIRY_TICK);
            Assert.Greater(EARLY_DEADLINE_TICK, PAST_EXPIRY_TICK);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, EARLY_OPEN_TICK);
            FillBag(rig, CharA);
            RunTick(rig, EARLY_CLOSE_TICK);
            Assert.AreEqual(ONE_EVENT, rig.Blocked.Count);

            // Act
            RunTick(rig, EXPIRY_TICK);
            RunTick(rig, PAST_EXPIRY_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            Assert.AreEqual(NO_EVENTS, rig.Resolved.Count);
            AssertStillAuctioning(rig, id);
        }

        [Test]
        public void Tick_FallbackReachedAfterTheExpiryTickInsideAGrace_ItemIsAssignedWithAFreshExpiryTick()
        {
            // Arrange: A is the only bidder; the grace spans the expiry tick and A never frees a slot
            Rig rig = BuildRig();
            GroundItemID id = OpenEarlyClosingAuction(rig, DarkSteel);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, EARLY_OPEN_TICK);
            FillBag(rig, CharA);
            RunTick(rig, EARLY_CLOSE_TICK);
            RunTick(rig, PAST_EXPIRY_TICK);

            // Act
            RunTick(rig, EARLY_DEADLINE_TICK);

            // Assert
            Assert.AreEqual(NO_EVENTS, rig.Despawned.Count);
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(EARLY_DEADLINE_TICK + TTL_TICKS, item.ExpiryTick);
        }

        [Test]
        public void Tick_FallbackReachedBeforeTheExpiryTickAfterOneGrace_ItemIsAssignedWithAFreshExpiryTick()
        {
            // Arrange: A is the only bidder and never frees a slot; the fallback comes before the expiry tick
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);
            Assert.Less(FIRST_DEADLINE_TICK, EXPIRY_TICK);
            Bid(rig, id, CharA, BID_FOUR_HUNDRED, BID_TICK_A);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Act
            RunTick(rig, FIRST_DEADLINE_TICK);

            // Assert
            AssertResolvedOnce(rig, id, CharacterID.Invalid, NO_GOLD, true);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
            Assert.AreEqual(FIRST_DEADLINE_TICK + TTL_TICKS, item.ExpiryTick);
            Assert.AreEqual(ONE_EVENT, rig.Assigned.Count);
            Assert.AreEqual(FIRST_DEADLINE_TICK + TTL_TICKS, rig.Assigned[0].ExpiryTick);
        }

        // -----------------------------------------------------------------------
        // Leaver during a grace
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ReservedBidderLeavesThePartyDuringTheGrace_NoSpendForAAndBWinsOnThatTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);
            rig.Parties.Members.Remove(CharA);

            // Act
            RunTick(rig, MID_GRACE_TICK);

            // Assert: A is skipped without a spend; the pool goes to the three who remain
            AssertSingleSpend(rig, CharB, BID_FOUR_HUNDRED);
            AssertPoolPaid(rig, SHARE_OF_THREE, CharB, CharC, CharD);
            AssertResolvedOnce(rig, id, CharB, SHARE_OF_THREE, false);
            Assert.AreEqual(STARTING_GOLD, rig.Currency.GetBalance(CharA));
        }

        // -----------------------------------------------------------------------
        // Bids during a grace
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_AuctionWaitingInAGrace_ReturnsWindowClosed()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenDarkSteelAuctionWithThreeBids(rig);
            FillBag(rig, CharA);
            RunTick(rig, WINDOW_CLOSE_TICK);

            // Act: the received tick is inside the bid window, so only the grace can reject the bid
            LootBidResult result = rig.Auction.SubmitBid(CharD, id, BID_FOUR_HUNDRED, BID_TICK_B);

            // Assert
            Assert.AreEqual(LootBidResult.WindowClosed, result);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
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
    /// EditMode integration tests for Loot Table Story 010: Rare drop auction open, bid validation
    /// and broadcast (design/gdd/loot-table-system.md CR-LT-8; AC-LT-11, AC-LT-17). Uses a real
    /// <see cref="GroundItemService"/>, <see cref="LootEquipmentCache"/>, <see cref="LootDropDistributor"/>
    /// and <see cref="LootAuctionService"/>, with a stub party service.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_AuctionBids_Integration_Tests
    {
        private const uint BRONZE_ID = 1001u;
        private const uint STEEL_ID = 1003u;
        private const uint DARK_STEEL_ID = 1004u;
        private const uint CUSTOM_STEEL_ID = 1005u;
        private const uint FREE_DARK_STEEL_ID = 1006u;
        private const int BRONZE_PRICE = 10;
        private const int STEEL_PRICE = 90;
        private const int DARK_STEEL_PRICE = 270;

        // Not a GDD price: proves the floor is read from the item, not from a per-tier constant.
        private const int CUSTOM_STEEL_PRICE = 120;
        private const int FREE_PRICE = 0;
        private const uint RAW_PARTY = 1u;
        private const uint RAW_OTHER_PARTY = 2u;
        private const uint RAW_CHAR_A = 11u;
        private const uint RAW_CHAR_B = 12u;
        private const uint RAW_CHAR_C = 13u;
        private const uint RAW_MOB = 500u;
        private const uint UNKNOWN_GROUND_ITEM = 99999u;
        private const uint SPAWN_TICK = 100u;
        private const uint OPEN_TICK = SPAWN_TICK + 1u;
        private const uint WINDOW_CLOSE_TICK = OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS;
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint WARNING_TICK = EXPIRY_TICK - (uint)LootTableConstants.EXPIRY_WARNING_TICKS;

        private const uint BID_A_FIRST = 100u;
        private const uint BID_A_RAISED = 150u;
        private const uint BID_A_LOWER = 120u;
        private const uint BID_B = 95u;
        private const uint BID_LARGE = 500u;
        private const uint BID_UNDER_FLOOR = 50u;
        private const uint BID_ZERO = 0u;
        private const uint BID_ONE = 1u;

        private const uint LATE_OPEN_TICK = SPAWN_TICK + 5u;
        private const uint RAISE_TICK = OPEN_TICK + 10u;
        private const uint WRAP_SPAWN_TICK = uint.MaxValue - 300u;

        private const uint STARTING_GOLD = 1000u;
        private const int NO_LOGS = 0;
        private const int ONE_EVENT = 1;
        private const int ONE_CALL = 1;
        private const int NO_CALLS = 0;
        private const int ONE_UPDATE = 1;
        private const int TWO_UPDATES = 2;
        private const int NO_UPDATES = 0;
        private const int ONE_ITEM = 1;
        private const int NO_ITEMS = 0;
        private const int ONE_LOG = 1;
        private const string SUBSCRIBER_FAILURE = "bid subscriber failure";

        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly PartyID OtherParty = new PartyID(RAW_OTHER_PARTY);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
        private static readonly ItemID Bronze = new ItemID(BRONZE_ID);
        private static readonly ItemID Steel = new ItemID(STEEL_ID);
        private static readonly ItemID DarkSteel = new ItemID(DARK_STEEL_ID);
        private static readonly ItemID CustomSteel = new ItemID(CUSTOM_STEEL_ID);
        private static readonly ItemID FreeDarkSteel = new ItemID(FREE_DARK_STEEL_ID);
        private static readonly Vector3 MobPosition = new Vector3(1f, 2f, 3f);

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private Rig _rig;
        private int _auctionLogCount;

        [SetUp]
        public void SetUp()
        {
            _auctionLogCount = 0;
            _rig = null;
            Application.logMessageReceived += CountAuctionLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CountAuctionLog;
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

        // Counts the info-level lines of the auction service ("exactly one rejection log").
        private void CountAuctionLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log && condition.StartsWith("[LootAuctionService]", StringComparison.Ordinal))
            {
                _auctionLogCount++;
            }
        }

        // -----------------------------------------------------------------------
        // Fakes
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

        // One party of members, plus an outsider who belongs to a different party.
        private sealed class StubPartyService : IPartyService
        {
            public readonly List<CharacterID> Members = new List<CharacterID>();
            public readonly List<CharacterID> Outsiders = new List<CharacterID>();
            public int Cursor;
            public int GetRrNextIndexCalls;
            public int AdvanceCalls;

            public PartyID GetPartyID(CharacterID characterId)
            {
                if (Members.Contains(characterId))
                {
                    return Party;
                }
                return Outsiders.Contains(characterId) ? OtherParty : PartyID.Uninitialized;
            }

            public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
            {
                return new List<CharacterID>(Members);
            }

            public CharacterID GetMemberAtIndex(PartyID partyId, int index)
            {
                return index >= 0 && index < Members.Count ? Members[index] : CharacterID.Invalid;
            }

            public int GetRrNextIndex(PartyID partyId)
            {
                GetRrNextIndexCalls++;
                return Cursor;
            }

            public void AdvanceRrNextIndex(PartyID partyId)
            {
                AdvanceCalls++;
                Cursor = (Cursor + 1) % Members.Count;
            }

            public bool IsMemberConnected(CharacterID characterId) => true;
        }

        private sealed class Rig
        {
            public StubPartyService Parties;
            public RecordingInventoryService Inventory;
            public SettablePositionProvider Positions;
            public GroundItemService Ground;
            public LootEquipmentCache Cache;
            public LootDropDistributor Distributor;
            public LootAuctionService Auction;
            public CurrencySystem Currency;
            public readonly List<GroundItemSpawnedEventArgs> Spawned = new List<GroundItemSpawnedEventArgs>();
            public readonly List<GroundItemExpiryWarningEventArgs> Warnings = new List<GroundItemExpiryWarningEventArgs>();
            public readonly List<LootBidUpdateEventArgs> Updates = new List<LootBidUpdateEventArgs>();
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();
            public uint Tick = SPAWN_TICK;

            public uint ReadTick() => Tick;

            public void RecordDespawn(GroundItemDespawnedEventArgs args) => Despawned.Add(args);

            public void RecordSpawn(GroundItemSpawnedEventArgs args) => Spawned.Add(args);

            public void RecordWarning(GroundItemExpiryWarningEventArgs args) => Warnings.Add(args);

            public void RecordUpdate(LootBidUpdateEventArgs args) => Updates.Add(args);
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

        private Rig BuildRig()
        {
            var equipment = new List<ItemDefinition>
            {
                BuildEquipment(BRONZE_ID, GearTier.Bronze, BRONZE_PRICE),
                BuildEquipment(STEEL_ID, GearTier.Steel, STEEL_PRICE),
                BuildEquipment(DARK_STEEL_ID, GearTier.DarkSteel, DARK_STEEL_PRICE),
                BuildEquipment(CUSTOM_STEEL_ID, GearTier.Steel, CUSTOM_STEEL_PRICE),
                BuildEquipment(FREE_DARK_STEEL_ID, GearTier.DarkSteel, FREE_PRICE),
            };
            var rig = new Rig();
            rig.Parties = new StubPartyService();
            rig.Parties.Members.Add(CharA);
            rig.Parties.Members.Add(CharB);
            rig.Parties.Outsiders.Add(CharC);
            rig.Inventory = new RecordingInventoryService();
            rig.Inventory.FreeSlot = true;
            rig.Positions = new SettablePositionProvider();
            rig.Cache = new LootEquipmentCache(new FakeItemDatabase(equipment));
            rig.Ground = new GroundItemService(rig.Cache, rig.Inventory, rig.Positions, new EmptyItemDatabase());
            rig.Ground.OnGroundItemSpawned += rig.RecordSpawn;
            rig.Ground.OnGroundItemExpiryWarning += rig.RecordWarning;
            rig.Ground.OnGroundItemDespawned += rig.RecordDespawn;
            rig.Distributor = new LootDropDistributor(rig.Parties, rig.Cache, rig.Ground, rig.ReadTick);
            rig.Currency = new CurrencySystem();
            rig.Currency.RegisterCharacter(CharA, STARTING_GOLD);
            rig.Currency.RegisterCharacter(CharB, STARTING_GOLD);
            rig.Auction = new LootAuctionService(rig.Ground, rig.Parties, rig.Cache, rig.Currency, rig.Inventory);
            rig.Auction.OnLootBidUpdate += rig.RecordUpdate;
            _rig = rig;
            return rig;
        }

        // Resolves a one-item drop (a Rare one opens an auction) and returns the new ground item.
        private static GroundItemID ResolveOne(Rig rig, ItemID item)
        {
            rig.Distributor.OnDropsResolved(Mob, Party, new[] { item }, MobPosition);
            return rig.Spawned[rig.Spawned.Count - 1].GroundItemId;
        }

        // Resolves the drop at SPAWN_TICK and ticks once, so the auction is open.
        private static GroundItemID OpenAuction(Rig rig, ItemID item)
        {
            GroundItemID id = ResolveOne(rig, item);
            rig.Ground.Tick(OPEN_TICK);
            return id;
        }

        // -----------------------------------------------------------------------
        // AC-LT-11 open and floor
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_SteelAuctionBelowAndAtFloor_RejectsEightyNineAndAcceptsNinety()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);

            // Act
            LootBidResult below = rig.Auction.SubmitBid(CharA, id, (uint)STEEL_PRICE - 1u, OPEN_TICK);

            // Assert: the rejection is silent
            Assert.AreEqual(LootBidResult.BelowFloor, below);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
            Assert.AreEqual(NO_LOGS, _auctionLogCount);

            // Act
            LootBidResult atFloor = rig.Auction.SubmitBid(CharA, id, (uint)STEEL_PRICE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, atFloor);
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
            Assert.AreEqual(id, rig.Updates[0].GroundItemId);
            Assert.AreEqual(CharA, rig.Updates[0].BidderId);
            Assert.AreEqual((uint)STEEL_PRICE, rig.Updates[0].BidAmount);
            Assert.AreEqual(Party, rig.Updates[0].PartyId);
        }

        [Test]
        public void SubmitBid_DarkSteelAuction_RejectsTwoSixtyNineAndAcceptsTwoSeventy()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, DarkSteel);

            // Act
            LootBidResult below = rig.Auction.SubmitBid(CharA, id, (uint)DARK_STEEL_PRICE - 1u, OPEN_TICK);
            LootBidResult atFloor = rig.Auction.SubmitBid(CharA, id, (uint)DARK_STEEL_PRICE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.BelowFloor, below);
            Assert.AreEqual(LootBidResult.Accepted, atFloor);
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
        }

        [Test]
        public void SubmitBid_SteelItemWithItsOwnPrice_UsesThatItemsSellPriceAsTheFloor()
        {
            // Arrange: a Steel item priced 120, not the tier's usual 90
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, CustomSteel);

            // Act
            LootBidResult below = rig.Auction.SubmitBid(CharA, id, (uint)CUSTOM_STEEL_PRICE - 1u, OPEN_TICK);
            LootBidResult atFloor = rig.Auction.SubmitBid(CharA, id, (uint)CUSTOM_STEEL_PRICE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.BelowFloor, below);
            Assert.AreEqual(LootBidResult.Accepted, atFloor);
        }

        [Test]
        public void SubmitBid_ItemPricedZero_StillRejectsABidOfZero()
        {
            // Arrange: an authoring fault, a Rare item with SellPriceGold 0
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, FreeDarkSteel);

            // Act
            LootBidResult zero = rig.Auction.SubmitBid(CharA, id, BID_ZERO, OPEN_TICK);
            LootBidResult one = rig.Auction.SubmitBid(CharA, id, BID_ONE, OPEN_TICK);

            // Assert: a bid of 0 would otherwise count as a valid bid
            Assert.AreEqual(LootBidResult.BelowFloor, zero);
            Assert.AreEqual(LootBidResult.Accepted, one);
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
        }

        [Test]
        public void SubmitBid_TwoAuctionsAtOnce_KeepSeparateBidBooksAndFloors()
        {
            // Arrange
            Rig rig = BuildRig();
            rig.Distributor.OnDropsResolved(Mob, Party, new[] { Steel, DarkSteel }, MobPosition);
            GroundItemID steel = rig.Spawned[0].GroundItemId;
            GroundItemID darkSteel = rig.Spawned[1].GroundItemId;
            rig.Ground.Tick(OPEN_TICK);

            // Act: 100 clears the Steel floor but not the DarkSteel one
            LootBidResult onSteel = rig.Auction.SubmitBid(CharA, steel, BID_A_FIRST, OPEN_TICK);
            LootBidResult onDarkSteel = rig.Auction.SubmitBid(CharA, darkSteel, BID_A_FIRST, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, onSteel);
            Assert.AreEqual(LootBidResult.BelowFloor, onDarkSteel);
            Assert.IsTrue(rig.Auction.TryGetBid(steel, CharA, out uint stored, out _));
            Assert.AreEqual(BID_A_FIRST, stored);
            Assert.IsFalse(rig.Auction.TryGetBid(darkSteel, CharA, out _, out _));
        }

        // -----------------------------------------------------------------------
        // AC-LT-17 late bid
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_ReceivedTickAfterWindowClose_RejectsLogsOnceAndChangesNothing()
        {
            // Arrange: A has an accepted bid
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));
            LogAssert.Expect(LogType.Log, new Regex(@"\[LootAuctionService\] SubmitBid"));

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharB, id, BID_LARGE, WINDOW_CLOSE_TICK + 1u);

            // Assert
            Assert.AreEqual(LootBidResult.WindowClosed, result);
            Assert.AreEqual(ONE_LOG, _auctionLogCount);
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
            Assert.IsTrue(rig.Auction.TryGetBid(id, CharA, out uint amountA, out uint tickA));
            Assert.AreEqual(BID_A_FIRST, amountA);
            Assert.AreEqual(OPEN_TICK, tickA);
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharB, out _, out _));

            // Assert: no auction state changed either
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
            Assert.AreEqual(WINDOW_CLOSE_TICK, item.WindowCloseTick);
        }

        [Test]
        public void Tick_AfterTheWindowClosedWithoutAnAuctionTick_AuctionStaysAuctioning()
        {
            // Arrange: only the auction service's Tick resolves a closed auction; it is not called here
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            rig.Ground.Tick(WINDOW_CLOSE_TICK + 1u);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
            Assert.AreEqual(NO_ITEMS, rig.Despawned.Count);
        }

        [Test]
        public void SubmitBid_WindowCloseTickWrapsPastMaxValue_StillAcceptsOnItAndRejectsAfterIt()
        {
            // Arrange: the auction opens just before the tick counter wraps
            Rig rig = BuildRig();
            rig.Tick = WRAP_SPAWN_TICK;
            GroundItemID id = ResolveOne(rig, Steel);
            uint openTick = WRAP_SPAWN_TICK + 1u;
            rig.Ground.Tick(openTick);
            uint wrappedClose = unchecked(openTick + (uint)LootTableConstants.AUCTION_WINDOW_TICKS);
            Assert.Less(wrappedClose, openTick);
            LogAssert.Expect(LogType.Log, new Regex(@"\[LootAuctionService\] SubmitBid"));

            // Act
            LootBidResult onClose = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, wrappedClose);
            LootBidResult afterClose = rig.Auction.SubmitBid(CharB, id, BID_A_FIRST, wrappedClose + 1u);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, onClose);
            Assert.AreEqual(LootBidResult.WindowClosed, afterClose);
        }

        // -----------------------------------------------------------------------
        // Validation order: the first failing step decides the result
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_LateBidFromNonMember_IsRejectedAsWindowClosedAndLogged()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            LogAssert.Expect(LogType.Log, new Regex(@"\[LootAuctionService\] SubmitBid"));

            // Act: late AND not a member
            LootBidResult result = rig.Auction.SubmitBid(CharC, id, BID_LARGE, WINDOW_CLOSE_TICK + 1u);

            // Assert: the window is checked before membership
            Assert.AreEqual(LootBidResult.WindowClosed, result);
            Assert.AreEqual(ONE_LOG, _auctionLogCount);
        }

        [Test]
        public void SubmitBid_BelowFloorBidFromNonMember_IsRejectedAsNotPartyMember()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act: below the floor AND not a member
            LootBidResult result = rig.Auction.SubmitBid(CharC, id, BID_UNDER_FLOOR, OPEN_TICK);

            // Assert: membership is checked before the floor
            Assert.AreEqual(LootBidResult.NotPartyMember, result);
            Assert.AreEqual(NO_LOGS, _auctionLogCount);
        }

        [Test]
        public void SubmitBid_BelowFloorBidFromMemberWithAHigherBid_IsRejectedAsBelowFloor()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));

            // Act: below the floor AND not higher than the bidder's own bid
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_UNDER_FLOOR, OPEN_TICK);

            // Assert: the floor is checked before the own-bid comparison
            Assert.AreEqual(LootBidResult.BelowFloor, result);
        }

        [Test]
        public void SubmitBid_LateBidOnAnAssignedItem_IsRejectedAsNotAuctioningWithoutALog()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Bronze);

            // Act: not an auction AND late
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_LARGE, WINDOW_CLOSE_TICK + 1u);

            // Assert: the state is checked first, and only a closed window is logged
            Assert.AreEqual(LootBidResult.NotAuctioning, result);
            Assert.AreEqual(NO_LOGS, _auctionLogCount);
        }

        [Test]
        public void SubmitBid_InvalidBidder_IsRejectedAsNotPartyMember()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharacterID.Invalid, id, BID_LARGE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.NotPartyMember, result);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
        }

        // A data fault: the auctioned item is not in the service's equipment cache, so it has no
        // price. The result code reuses NotAuctioning; the error log is what identifies the cause.
        [Test]
        public void SubmitBid_AuctionedItemMissingFromTheCache_IsRejectedWithOneError()
        {
            // Arrange: a second auction service over an empty cache
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            using (var blind = new LootAuctionService(rig.Ground, rig.Parties, new LootEquipmentCache(new EmptyItemDatabase()), rig.Currency, rig.Inventory))
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[LootAuctionService\] SubmitBid.*equipment cache"));

                // Act
                LootBidResult result = blind.SubmitBid(CharA, id, BID_LARGE, OPEN_TICK);

                // Assert
                Assert.AreEqual(LootBidResult.NotAuctioning, result);
                Assert.IsFalse(blind.TryGetBid(id, CharA, out _, out _));
            }
        }

        [Test]
        public void SubmitBid_ReceivedTickBeforeTheAuctionOpened_IsAccepted()
        {
            // Arrange: only "not after the close tick" is checked; receivedTick is server-assigned
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, SPAWN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, result);
        }

        [Test]
        public void SubmitBid_ReceivedTickEqualsWindowClose_IsAccepted()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, WINDOW_CLOSE_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, result);
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
            Assert.AreEqual(NO_LOGS, _auctionLogCount);
        }

        // -----------------------------------------------------------------------
        // CR-LT-8 window length
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AuctionLeavesSpawningLaterThanSpawnTickPlusOne_WindowRunsFromThatTick()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = ResolveOne(rig, Steel);

            // Act: the first tick after the spawn comes five ticks later
            rig.Ground.Tick(LATE_OPEN_TICK);

            // Assert: the window is measured from the tick the auction opened, not the spawn tick
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
            Assert.AreEqual(LATE_OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS, item.WindowCloseTick);
        }

        [Test]
        public void Tick_AuctionLeavesSpawning_SetsWindowCloseTickToTickPlusWindowLength()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = ResolveOne(rig, Steel);

            // Act
            rig.Ground.Tick(OPEN_TICK);

            // Assert
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
            Assert.AreEqual(OPEN_TICK + (uint)LootTableConstants.AUCTION_WINDOW_TICKS, item.WindowCloseTick);
            Assert.AreEqual(Party, item.PartyId);
        }

        // -----------------------------------------------------------------------
        // CR-LT-8 revision
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_OwnBidRaisedRepeatedAndLowered_AcceptsOnlyTheRaise()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));

            // Act: the raise arrives on a later tick; the two rejected bids come later still
            LootBidResult raised = rig.Auction.SubmitBid(CharA, id, BID_A_RAISED, RAISE_TICK);
            LootBidResult repeated = rig.Auction.SubmitBid(CharA, id, BID_A_RAISED, RAISE_TICK + 1u);
            LootBidResult lowered = rig.Auction.SubmitBid(CharA, id, BID_A_LOWER, RAISE_TICK + 2u);

            // Assert: the stored bid is the raise, with the raise's own tick (Story 011's tie-break)
            Assert.AreEqual(LootBidResult.Accepted, raised);
            Assert.AreEqual(LootBidResult.NotHigherThanOwnBid, repeated);
            Assert.AreEqual(LootBidResult.NotHigherThanOwnBid, lowered);
            Assert.IsTrue(rig.Auction.TryGetBid(id, CharA, out uint stored, out uint storedTick));
            Assert.AreEqual(BID_A_RAISED, stored);
            Assert.AreEqual(RAISE_TICK, storedTick);
            Assert.AreEqual(TWO_UPDATES, rig.Updates.Count);
            Assert.AreEqual(BID_A_RAISED, rig.Updates[1].BidAmount);
            Assert.AreEqual(NO_LOGS, _auctionLogCount);
        }

        // -----------------------------------------------------------------------
        // CR-LT-8 eligibility
        // -----------------------------------------------------------------------

        [Test]
        public void SubmitBid_BidderInAnotherParty_RejectsAsNotPartyMemberWithoutUpdate()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharC, id, BID_LARGE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.NotPartyMember, result);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharC, out _, out _));
            Assert.AreEqual(NO_LOGS, _auctionLogCount);
        }

        [Test]
        public void SubmitBid_UnknownGroundItem_RejectsAsNotAuctioning()
        {
            // Arrange
            Rig rig = BuildRig();

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, new GroundItemID(UNKNOWN_GROUND_ITEM), BID_LARGE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.NotAuctioning, result);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
        }

        [Test]
        public void SubmitBid_AssignedCommonDrop_RejectsAsNotAuctioning()
        {
            // Arrange: a Bronze drop goes to a member and is Assigned after one tick
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Bronze);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_LARGE, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.NotAuctioning, result);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
        }

        // -----------------------------------------------------------------------
        // Spawn visibility
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_SteelDropInPartyOfTwo_SpawnEventIsAuctionForThePartyWithoutAssignee()
        {
            // Arrange
            Rig rig = BuildRig();

            // Act
            ResolveOne(rig, Steel);

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.IsTrue(rig.Spawned[0].IsAuction);
            Assert.AreEqual(Party, rig.Spawned[0].PartyId);
            Assert.AreEqual(CharacterID.Invalid, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.AdvanceCalls);
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        [Test]
        public void Constructor_AnyNullArgument_Throws()
        {
            // Arrange
            Rig rig = BuildRig();

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new LootAuctionService(null, rig.Parties, rig.Cache, rig.Currency, rig.Inventory));
            Assert.Throws<ArgumentNullException>(() => new LootAuctionService(rig.Ground, null, rig.Cache, rig.Currency, rig.Inventory));
            Assert.Throws<ArgumentNullException>(() => new LootAuctionService(rig.Ground, rig.Parties, null, rig.Currency, rig.Inventory));
            Assert.Throws<ArgumentNullException>(() => new LootAuctionService(rig.Ground, rig.Parties, rig.Cache, null, rig.Inventory));
            Assert.Throws<ArgumentNullException>(() => new LootAuctionService(rig.Ground, rig.Parties, rig.Cache, rig.Currency, null));
        }

        [Test]
        public void SubmitBid_DuringSpawnTick_RejectsAsNotAuctioning()
        {
            // Arrange: the item is still Spawning
            Rig rig = BuildRig();
            GroundItemID id = ResolveOne(rig, Steel);

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, SPAWN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.NotAuctioning, result);
            Assert.AreEqual(NO_UPDATES, rig.Updates.Count);
        }

        [Test]
        public void SubmitBid_TwoMembers_KeepIndependentBids()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);

            // Act: B's bid is lower than A's but above the floor
            LootBidResult a = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK);
            LootBidResult b = rig.Auction.SubmitBid(CharB, id, BID_B, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, a);
            Assert.AreEqual(LootBidResult.Accepted, b);
            Assert.IsTrue(rig.Auction.TryGetBid(id, CharA, out uint amountA, out _));
            Assert.IsTrue(rig.Auction.TryGetBid(id, CharB, out uint amountB, out _));
            Assert.AreEqual(BID_A_FIRST, amountA);
            Assert.AreEqual(BID_B, amountB);
            Assert.AreEqual(TWO_UPDATES, rig.Updates.Count);
        }

        [Test]
        public void Tick_AuctionItemReachesExpiry_ResolvesItDropsItsBidsAndLaterBidsAreRejected()
        {
            // Arrange: since Story 011 the ground item service never despawns an auction; the
            // auction service resolves it (A, who has the gold, wins and the item is delivered)
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));

            // Act
            rig.Ground.Tick(EXPIRY_TICK);
            rig.Auction.Tick(EXPIRY_TICK);

            // Assert: no despawn event, the item is delivered, the bids are gone, the auction no longer exists
            Assert.AreEqual(NO_ITEMS, rig.Despawned.Count);
            Assert.AreEqual(ONE_CALL, rig.Inventory.Calls.Count);
            Assert.IsFalse(rig.Ground.TryGetGroundItem(id, out _));
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharA, out _, out _));
            Assert.AreEqual(LootBidResult.NotAuctioning, rig.Auction.SubmitBid(CharA, id, BID_A_RAISED, EXPIRY_TICK));
        }

        [Test]
        public void DespawnAuctionItem_AuctionWithABid_DropsItsBidsAndLaterBidsAreRejected()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));

            // Act
            bool removed = rig.Ground.DespawnAuctionItem(id);

            // Assert: the despawn event cleans the auction service's bids up
            Assert.IsTrue(removed);
            Assert.AreEqual(ONE_EVENT, rig.Despawned.Count);
            Assert.AreEqual(id, rig.Despawned[0].GroundItemId);
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharA, out _, out _));
            Assert.AreEqual(LootBidResult.NotAuctioning, rig.Auction.SubmitBid(CharA, id, BID_A_RAISED, OPEN_TICK));
        }

        [Test]
        public void Tick_OneOfTwoAuctionsResolves_TheOtherKeepsItsBids()
        {
            // Arrange: a second auction opened later, so it expires later
            Rig rig = BuildRig();
            GroundItemID first = OpenAuction(rig, Steel);
            rig.Tick = OPEN_TICK;
            GroundItemID second = ResolveOne(rig, DarkSteel);
            rig.Ground.Tick(OPEN_TICK + 1u);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, first, BID_A_FIRST, OPEN_TICK + 1u));
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharB, second, (uint)DARK_STEEL_PRICE, OPEN_TICK + 1u));

            // Act: only the first reaches its window close tick (the second closes one tick later)
            rig.Ground.Tick(WINDOW_CLOSE_TICK);
            rig.Auction.Tick(WINDOW_CLOSE_TICK);

            // Assert
            Assert.IsFalse(rig.Auction.TryGetBid(first, CharA, out _, out _));
            Assert.IsTrue(rig.Auction.TryGetBid(second, CharB, out uint kept, out _));
            Assert.AreEqual((uint)DARK_STEEL_PRICE, kept);
        }

        [Test]
        public void Dispose_DropsEveryBidRejectsFurtherBidsAndIsIdempotent()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            Assert.AreEqual(LootBidResult.Accepted, rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK));

            // Act
            rig.Auction.Dispose();
            Assert.DoesNotThrow(() => rig.Auction.Dispose());

            // Assert: nothing would clean a bid up after Dispose, so none is kept and none is taken
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharA, out _, out _));
            Assert.AreEqual(LootBidResult.NotAuctioning, rig.Auction.SubmitBid(CharB, id, BID_LARGE, OPEN_TICK));
            Assert.IsFalse(rig.Auction.TryGetBid(id, CharB, out _, out _));
            Assert.AreEqual(ONE_UPDATE, rig.Updates.Count);
        }

        [Test]
        public void SubmitBid_UpdateSubscriberThrows_LogsExceptionAndStillReturnsAccepted()
        {
            // Arrange
            Rig rig = BuildRig();
            GroundItemID id = OpenAuction(rig, Steel);
            rig.Auction.OnLootBidUpdate += _ => throw new InvalidOperationException(SUBSCRIBER_FAILURE);
            LogAssert.Expect(LogType.Exception, new Regex(SUBSCRIBER_FAILURE));

            // Act
            LootBidResult result = rig.Auction.SubmitBid(CharA, id, BID_A_FIRST, OPEN_TICK);

            // Assert
            Assert.AreEqual(LootBidResult.Accepted, result);
            Assert.IsTrue(rig.Auction.TryGetBid(id, CharA, out uint stored, out _));
            Assert.AreEqual(BID_A_FIRST, stored);
        }

        [Test]
        public void SpawnAuction_InvalidItemOrUninitializedParty_LogsOneErrorEachAndSpawnsNothing()
        {
            // Arrange
            Rig rig = BuildRig();
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] SpawnAuction"));
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] SpawnAuction"));

            // Act
            GroundItemID noItem = rig.Ground.SpawnAuction(ItemID.Invalid, MobPosition, Party, SPAWN_TICK);
            GroundItemID noParty = rig.Ground.SpawnAuction(Steel, MobPosition, PartyID.Uninitialized, SPAWN_TICK);

            // Assert
            Assert.AreEqual(GroundItemID.Invalid, noItem);
            Assert.AreEqual(GroundItemID.Invalid, noParty);
            Assert.AreEqual(NO_ITEMS, rig.Spawned.Count);
        }

        [Test]
        public void Tick_AuctionItemWithMemberStandingOnIt_IsNeverPickedUpOrWarnedAbout()
        {
            // Arrange: A stands on the item from the start
            Rig rig = BuildRig();
            rig.Positions.Set(CharA, MobPosition);
            GroundItemID id = OpenAuction(rig, Steel);

            // Act
            rig.Ground.Tick(OPEN_TICK + 1u);
            rig.Ground.Tick(WARNING_TICK);

            // Assert
            Assert.AreEqual(NO_CALLS, rig.Inventory.Calls.Count);
            Assert.AreEqual(NO_CALLS, rig.Warnings.Count);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Auctioning, item.State);
        }
    }
}

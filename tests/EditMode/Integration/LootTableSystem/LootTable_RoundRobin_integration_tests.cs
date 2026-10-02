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
    /// EditMode integration tests for Loot Table Story 006: common drop round-robin assignment
    /// (design/gdd/loot-table-system.md CR-LT-5, CR-LT-6, CR-LT-10 fallback, CR-LT-11; party side
    /// design/gdd/party-system.md CR-PS-7), using a real <see cref="GroundItemService"/>, a real
    /// <see cref="LootEquipmentCache"/> and a stub party service that owns the cursor.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_RoundRobin_Integration_Tests
    {
        private const uint BRONZE_ID = 1001u;
        private const uint STEEL_ID = 1003u;
        private const uint DARK_STEEL_ID = 1004u;
        private const uint CONSUMABLE_ID = 9001u;
        private const uint RAW_PARTY = 1u;
        private const uint RAW_CHAR_A = 11u;
        private const uint RAW_CHAR_B = 12u;
        private const uint RAW_CHAR_C = 13u;
        private const uint RAW_CHAR_D = 14u;
        private const uint RAW_MOB = 500u;
        private const uint RAW_MOB_TYPE = 7u;
        private const uint RAW_ITEM_A = 101u;
        private const uint TICK = 100u;
        private const uint NEXT_TICK = TICK + 1u;
        private const uint EXPECTED_EXPIRY = TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const int MOB_MAX_HP = 300;
        private const uint KILLING_DAMAGE = 300u;
        private const int GOLD_MIN = 4;
        private const int GOLD_MAX = 8;
        private const float GUARANTEED_DROP = 1.0f;
        private const double HITTING_DRAW = 0.0;
        private const int NO_TIER_SHIFT = 0;
        private const int PRNG_SEED = 4242;

        private const int FIRST_INDEX = 0;
        private const int SECOND_INDEX = 1;
        private const int THIRD_INDEX = 2;
        private const int OUT_OF_RANGE_CURSOR = 5;

        private const int NO_ITEMS = 0;
        private const int ONE_ITEM = 1;
        private const int TWO_ITEMS = 2;
        private const int THREE_ITEMS = 3;
        private const int FOUR_ITEMS = 4;
        private const int NO_CALLS = 0;
        private const int ONE_CALL = 1;
        private const int TWO_CALLS = 2;
        private const int THREE_CALLS = 3;
        private const int FOUR_CALLS = 4;
        private const int NO_ERRORS = 0;
        private const int ONE_ERROR = 1;

        private const string CALL_GET_RR = "GetRr";
        private const string CALL_GET_MEMBER = "GetMember";
        private const string CALL_SPAWN = "Spawn";
        private const string CALL_ADVANCE = "Advance";
        private const string STUB_FAILURE = "party service failure";

        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR_A);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_B);
        private static readonly CharacterID CharC = new CharacterID(RAW_CHAR_C);
        private static readonly CharacterID CharD = new CharacterID(RAW_CHAR_D);
        private static readonly CharacterID[] JoinOrder = { CharA, CharB, CharC, CharD };
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
        private static readonly ItemID Bronze = new ItemID(BRONZE_ID);
        private static readonly ItemID Steel = new ItemID(STEEL_ID);
        private static readonly ItemID DarkSteel = new ItemID(DARK_STEEL_ID);
        private static readonly ItemID Consumable = new ItemID(CONSUMABLE_ID);
        private static readonly ItemID ItemA = new ItemID(RAW_ITEM_A);
        private static readonly Vector3 MobPosition = new Vector3(1f, 2f, 3f);

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

        // LogAssert.Expect only proves an error arrived; this counts them so "exactly one" and
        // "none" can be asserted.
        private void CountError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _errorCount++;
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

        // One party with a settable member list and cursor. The cursor is only written by the test
        // and by AdvanceRrNextIndex, the way the Party System would. Every call is counted, and
        // the round-robin calls are also recorded in order.
        private sealed class StubPartyService : IPartyService
        {
            public readonly List<CharacterID> Members = new List<CharacterID>();
            public readonly List<string> Calls = new List<string>();
            public int Cursor;
            public int? InvalidIndex;
            public int? ThrowOnMemberAtIndexCall;
            public bool ReturnNullMembers;

            public int GetPartyIdCalls;
            public int GetPartyMembersCalls;
            public int GetMemberAtIndexCalls;
            public int GetRrNextIndexCalls;
            public int AdvanceCalls;

            public StubPartyService(int cursor, params CharacterID[] members)
            {
                Cursor = cursor;
                Members.AddRange(members);
            }

            public PartyID GetPartyID(CharacterID characterId)
            {
                GetPartyIdCalls++;
                return Members.Contains(characterId) ? Party : PartyID.Uninitialized;
            }

            public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
            {
                GetPartyMembersCalls++;
                return ReturnNullMembers ? null : new List<CharacterID>(Members);
            }

            public CharacterID GetMemberAtIndex(PartyID partyId, int index)
            {
                GetMemberAtIndexCalls++;
                Calls.Add(CALL_GET_MEMBER);
                if (ThrowOnMemberAtIndexCall.HasValue && ThrowOnMemberAtIndexCall.Value == GetMemberAtIndexCalls)
                {
                    throw new InvalidOperationException(STUB_FAILURE);
                }
                if (InvalidIndex.HasValue && InvalidIndex.Value == index)
                {
                    return CharacterID.Invalid;
                }
                return index >= 0 && index < Members.Count ? Members[index] : CharacterID.Invalid;
            }

            public int GetRrNextIndex(PartyID partyId)
            {
                GetRrNextIndexCalls++;
                Calls.Add(CALL_GET_RR);
                return Cursor;
            }

            public void AdvanceRrNextIndex(PartyID partyId)
            {
                AdvanceCalls++;
                Calls.Add(CALL_ADVANCE);
                Cursor = (Cursor + 1) % Members.Count;
            }
        }

        private sealed class StubMobInfoProvider : IMobInfoProvider
        {
            public bool TryGetMob(EntityID mobEntityId, out MobInfo info)
            {
                info = new MobInfo(new MobTypeID(RAW_MOB_TYPE), MOB_MAX_HP, MobPosition);
                return mobEntityId == Mob;
            }
        }

        private sealed class NullCurrencyService : ICurrencyService
        {
            public event Action<GoldSyncEventArgs> OnGoldSync
            {
                add { }
                remove { }
            }

            public void RegisterCharacter(CharacterID charId, uint initialBalance) { }

            public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
            {
                return new GoldMutationResult(true, amount, 0u, GoldMutationError.None);
            }

            public uint GetBalance(CharacterID charId) => 0u;

            public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
            {
                return new GoldMutationResult(false, 0u, 0u, GoldMutationError.NotImplemented);
            }

            public GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount)
            {
                return new GoldMutationResult(false, 0u, 0u, GoldMutationError.NotImplemented);
            }
        }

        private sealed class ScriptedRandom : System.Random
        {
            private readonly double _draw;

            public ScriptedRandom(double draw) : base(PRNG_SEED)
            {
                _draw = draw;
            }

            public override double NextDouble() => _draw;
        }

        private sealed class Rig
        {
            public StubPartyService Parties;
            public GroundItemService Ground;
            public LootEquipmentCache Cache;
            public LootDropDistributor Distributor;
            public readonly List<GroundItemSpawnedEventArgs> Spawned = new List<GroundItemSpawnedEventArgs>();
            public uint Tick = TICK;
            public int TickReads;

            // The tick moves on every read, so a second read inside one call would show up as a
            // different spawn tick.
            public uint ReadTick()
            {
                TickReads++;
                return Tick++;
            }

            public void RecordSpawn(GroundItemSpawnedEventArgs args)
            {
                Spawned.Add(args);
                Parties.Calls.Add(CALL_SPAWN);
            }
        }

        private ItemDefinition BuildEquipment(uint id, GearTier tier)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id,
                "Item" + id,
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, tier));
            _created.Add(def);
            return def;
        }

        private Rig BuildRig(int cursor, params CharacterID[] members)
        {
            var equipment = new List<ItemDefinition>
            {
                BuildEquipment(BRONZE_ID, GearTier.Bronze),
                BuildEquipment(STEEL_ID, GearTier.Steel),
                BuildEquipment(DARK_STEEL_ID, GearTier.DarkSteel),
            };
            var rig = new Rig();
            rig.Parties = new StubPartyService(cursor, members);
            rig.Cache = new LootEquipmentCache(new FakeItemDatabase(equipment));
            rig.Ground = new GroundItemService(rig.Cache, new RecordingInventoryService(), new SettablePositionProvider(), new EmptyItemDatabase());
            rig.Ground.OnGroundItemSpawned += rig.RecordSpawn;
            rig.Distributor = new LootDropDistributor(rig.Parties, rig.Cache, rig.Ground, rig.ReadTick);
            return rig;
        }

        private static void Resolve(Rig rig, params ItemID[] drops)
        {
            rig.Distributor.OnDropsResolved(Mob, Party, drops, MobPosition);
        }

        // -----------------------------------------------------------------------
        // AC-LT-7
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_FourCommonDropsPartyOfThree_AssignsABCAAndLeavesCursorAtOne()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB, CharC);

            // Act
            Resolve(rig, Bronze, Bronze, Bronze, Bronze);

            // Assert
            Assert.AreEqual(FOUR_ITEMS, rig.Spawned.Count);
            CharacterID[] expected = { CharA, CharB, CharC, CharA };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], rig.Spawned[i].AssignedTo);
            }
            Assert.AreEqual(SECOND_INDEX, rig.Parties.Cursor);
            Assert.AreEqual(FOUR_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(NO_ERRORS, _errorCount);
        }

        [Test]
        public void OnDropsResolved_PartyOfTwoCursorZero_AssignsToAAndAdvancesToOne()
        {
            // Arrange: the state after C left a party whose cursor was 2
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Bronze);

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(SECOND_INDEX, rig.Parties.Cursor);
        }

        [Test]
        public void OnDropsResolved_PartyOfThreeCursorOne_AssignsToBAndAdvancesToTwo()
        {
            // Arrange: D joined a party of 2 whose cursor was 1
            Rig rig = BuildRig(SECOND_INDEX, CharA, CharB, CharD);

            // Act
            Resolve(rig, Bronze);

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharB, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(THIRD_INDEX, rig.Parties.Cursor);
        }

        // -----------------------------------------------------------------------
        // AC-LT-15
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_SoloPlayerDarkSteelDrop_AssignsToSoloThroughCommonPathWithoutAuction()
        {
            // Arrange: the item really is a Rare drop
            Rig rig = BuildRig(FIRST_INDEX, CharA);
            Assert.AreEqual(DropTier.Rare, rig.Cache.Classify(DarkSteel));

            // Act
            Resolve(rig, DarkSteel);
            rig.Ground.Tick(NEXT_TICK);

            // Assert: the Rare branch was entered (party size read once) and took the common path
            Assert.AreEqual(ONE_CALL, rig.Parties.GetPartyMembersCalls);
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(NO_ERRORS, _errorCount);

            // A party of two or more would get an auction (Story 010); a solo player never does.
            Assert.IsFalse(rig.Spawned[0].IsAuction);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(rig.Spawned[0].GroundItemId, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
        }

        // -----------------------------------------------------------------------
        // CR-LT-6
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_OneCommonDropCursorOne_AssignsToBAndAdvancesExactlyOnce()
        {
            // Arrange
            Rig rig = BuildRig(SECOND_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Bronze);

            // Assert: the cursor changed only through the stub's own advance (1 -> 0 over two members)
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharB, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(FIRST_INDEX, rig.Parties.Cursor);

            // Assert: one cursor read, one member lookup, and nothing else asked of the party
            Assert.AreEqual(ONE_CALL, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(ONE_CALL, rig.Parties.GetMemberAtIndexCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetPartyMembersCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetPartyIdCalls);
        }

        [Test]
        public void OnDropsResolved_ValidMember_CallsReadCursorThenMemberThenSpawnThenAdvance()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Bronze);

            // Assert
            CollectionAssert.AreEqual(
                new[] { CALL_GET_RR, CALL_GET_MEMBER, CALL_SPAWN, CALL_ADVANCE },
                rig.Parties.Calls);
        }

        // -----------------------------------------------------------------------
        // CR-LT-5 routing
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_BronzeAndConsumableInPartyOfThree_AssignsTwoDifferentMembersAndAdvancesTwice()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB, CharC);

            // Act
            Resolve(rig, Bronze, Consumable);

            // Assert
            Assert.AreEqual(TWO_ITEMS, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(CharB, rig.Spawned[1].AssignedTo);
            Assert.AreEqual(TWO_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetPartyMembersCalls);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void OnDropsResolved_CommonDropAnyPartySize_TakesRoundRobinWithoutReadingPartySize(int partySize)
        {
            // Arrange
            var members = new CharacterID[partySize];
            Array.Copy(JoinOrder, members, partySize);
            Rig rig = BuildRig(FIRST_INDEX, members);

            // Act
            Resolve(rig, Bronze);

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetPartyMembersCalls);
        }

        [Test]
        public void OnDropsResolved_CommonRareCommonInPartyOfThree_AuctionsTheRareOneWithoutTakingATurn()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB, CharC);

            // Act
            Resolve(rig, Bronze, Steel, Consumable);

            // Assert: list order kept; the two Common items take turns A and B; the Rare one is an auction
            Assert.AreEqual(THREE_ITEMS, rig.Spawned.Count);
            Assert.AreEqual(Bronze, rig.Spawned[0].ItemId);
            Assert.AreEqual(Steel, rig.Spawned[1].ItemId);
            Assert.AreEqual(Consumable, rig.Spawned[2].ItemId);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.IsFalse(rig.Spawned[0].IsAuction);
            Assert.IsTrue(rig.Spawned[1].IsAuction);
            Assert.AreEqual(Party, rig.Spawned[1].PartyId);
            Assert.AreEqual(CharacterID.Invalid, rig.Spawned[1].AssignedTo);
            Assert.AreEqual(CharB, rig.Spawned[2].AssignedTo);
            Assert.IsFalse(rig.Spawned[2].IsAuction);
            Assert.AreEqual(TWO_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(TWO_CALLS, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(THIRD_INDEX, rig.Parties.Cursor);
            Assert.AreEqual(ONE_CALL, rig.Parties.GetPartyMembersCalls);
        }

        [Test]
        public void OnDropsResolved_TwoRareDrops_ReadsPartySizeOnceAndOpensTwoAuctionsWithoutAdvancing()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Steel, DarkSteel);

            // Assert: two separate auctions for the party, in list order, and the cursor untouched
            Assert.AreEqual(TWO_ITEMS, rig.Spawned.Count);
            Assert.AreEqual(Steel, rig.Spawned[0].ItemId);
            Assert.AreEqual(DarkSteel, rig.Spawned[1].ItemId);
            Assert.IsTrue(rig.Spawned[0].IsAuction);
            Assert.IsTrue(rig.Spawned[1].IsAuction);
            Assert.AreEqual(Party, rig.Spawned[0].PartyId);
            Assert.AreEqual(Party, rig.Spawned[1].PartyId);
            Assert.AreNotEqual(rig.Spawned[0].GroundItemId, rig.Spawned[1].GroundItemId);
            Assert.AreEqual(ONE_CALL, rig.Parties.GetPartyMembersCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.AdvanceCalls);
        }

        // -----------------------------------------------------------------------
        // Invalid member
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_InvalidMemberAtFirstCursor_LogsOneErrorSkipsThatItemAndAssignsTheNext()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            rig.Parties.InvalidIndex = FIRST_INDEX;
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootDropDistributor\]"));

            // Act
            Resolve(rig, Bronze, Bronze);

            // Assert: one advance per item, so the rotation moved 0 -> 1 -> 0 and did not stall
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharB, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(TWO_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(TWO_CALLS, rig.Parties.GetMemberAtIndexCalls);
            Assert.AreEqual(FIRST_INDEX, rig.Parties.Cursor);
        }

        [Test]
        public void OnDropsResolved_InvalidMemberOnLastItem_LogsOneErrorAndKeepsTheEarlierAssignment()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            rig.Parties.InvalidIndex = SECOND_INDEX;
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootDropDistributor\]"));

            // Act
            Resolve(rig, Bronze, Bronze);

            // Assert
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(TWO_CALLS, rig.Parties.AdvanceCalls);
        }

        [Test]
        public void OnDropsResolved_CursorBeyondMemberCount_LogsOneErrorSpawnsNothingAndStillAdvances()
        {
            // Arrange
            Rig rig = BuildRig(OUT_OF_RANGE_CURSOR, CharA, CharB, CharC);
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootDropDistributor\]"));

            // Act
            Assert.DoesNotThrow(() => Resolve(rig, Bronze));

            // Assert
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(NO_ITEMS, rig.Spawned.Count);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
        }

        [Test]
        public void OnDropsResolved_InvalidMember_CallsReadCursorThenMemberThenAdvanceWithoutSpawn()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            rig.Parties.InvalidIndex = FIRST_INDEX;
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootDropDistributor\]"));

            // Act
            Resolve(rig, Bronze);

            // Assert
            CollectionAssert.AreEqual(
                new[] { CALL_GET_RR, CALL_GET_MEMBER, CALL_ADVANCE },
                rig.Parties.Calls);
        }

        // -----------------------------------------------------------------------
        // Robustness (code review, 2026-10-02)
        // -----------------------------------------------------------------------

        [Test]
        public void OnDropsResolved_PartyCallThrowsOnFirstItem_LogsExceptionAndStillAssignsTheNextItem()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            rig.Parties.ThrowOnMemberAtIndexCall = ONE_CALL;
            LogAssert.Expect(LogType.Exception, new Regex(STUB_FAILURE));

            // Act
            Assert.DoesNotThrow(() => Resolve(rig, Bronze, Bronze));

            // Assert: the failed item never advanced the cursor, so the next one goes to A
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
        }

        [Test]
        public void OnDropsResolved_RareDropAndNullMemberList_TakesCommonPathWithoutThrowing()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            rig.Parties.ReturnNullMembers = true;

            // Act
            Assert.DoesNotThrow(() => Resolve(rig, Steel));

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
        }

        [Test]
        public void OnDropsResolved_InvalidItemId_LogsOneErrorAndDoesNotConsumeARoundRobinTurn()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            LogAssert.Expect(LogType.Error, new Regex(@"\[LootDropDistributor\]"));

            // Act
            Resolve(rig, ItemID.Invalid, Bronze);

            // Assert: the invalid item read and advanced nothing, so Bronze still goes to A
            Assert.AreEqual(ONE_ERROR, _errorCount);
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(ONE_CALL, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        [Test]
        public void Constructor_AnyNullArgument_Throws()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA);

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => new LootDropDistributor(null, rig.Cache, rig.Ground, rig.ReadTick));
            Assert.Throws<ArgumentNullException>(() => new LootDropDistributor(rig.Parties, null, rig.Ground, rig.ReadTick));
            Assert.Throws<ArgumentNullException>(() => new LootDropDistributor(rig.Parties, rig.Cache, null, rig.ReadTick));
            Assert.Throws<ArgumentNullException>(() => new LootDropDistributor(rig.Parties, rig.Cache, rig.Ground, null));
        }

        [Test]
        public void OnDropsResolved_RareDropInPartyOfTwo_OpensAuctionWithoutConsumingARoundRobinTurn()
        {
            // Arrange
            Rig rig = BuildRig(SECOND_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Steel);

            // Assert: Story 010 (CR-LT-8): an auction for the party, no assignee, cursor untouched
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.IsTrue(rig.Spawned[0].IsAuction);
            Assert.AreEqual(Party, rig.Spawned[0].PartyId);
            Assert.AreEqual(CharacterID.Invalid, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetRrNextIndexCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.GetMemberAtIndexCalls);
            Assert.AreEqual(NO_CALLS, rig.Parties.AdvanceCalls);
            Assert.AreEqual(SECOND_INDEX, rig.Parties.Cursor);
            Assert.AreEqual(ONE_CALL, rig.Parties.GetPartyMembersCalls);
            Assert.AreEqual(NO_ERRORS, _errorCount);
        }

        [Test]
        public void OnDropsResolved_SpawnedItem_CarriesMobPositionAndExpiryFromInjectedTick()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);

            // Act
            Resolve(rig, Bronze);

            // Assert
            Assert.AreEqual(MobPosition, rig.Spawned[0].Position);
            Assert.AreEqual(EXPECTED_EXPIRY, rig.Spawned[0].ExpiryTick);
            Assert.IsTrue(rig.Ground.TryGetGroundItem(rig.Spawned[0].GroundItemId, out GroundItem item));
            Assert.AreEqual(TICK, item.SpawnTick);
            Assert.AreEqual(MobPosition, item.Position);
        }

        [Test]
        public void OnDropsResolved_FourDrops_ReadsTheTickOnceAndSpawnsAllOnThatTick()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB, CharC);

            // Act
            Resolve(rig, Bronze, Steel, Consumable, Bronze);

            // Assert
            Assert.AreEqual(ONE_CALL, rig.TickReads);
            Assert.AreEqual(FOUR_ITEMS, rig.Spawned.Count);
            foreach (GroundItemSpawnedEventArgs spawned in rig.Spawned)
            {
                Assert.AreEqual(EXPECTED_EXPIRY, spawned.ExpiryTick);
            }
        }

        [Test]
        public void ResolveMobDrop_TaggedMobThroughRealLootTableService_ProducesAssignedGroundItem()
        {
            // Arrange
            Rig rig = BuildRig(FIRST_INDEX, CharA, CharB);
            var table = new LootTableDefinition(new[] { new LootTableEntry(ItemA, GUARANTEED_DROP) }, GOLD_MIN, GOLD_MAX);
            var tables = new List<KeyValuePair<MobTypeID, LootTableDefinition>>
            {
                new KeyValuePair<MobTypeID, LootTableDefinition>(new MobTypeID(RAW_MOB_TYPE), table),
            };
            Assert.IsTrue(LootTableRegistry.TryCreate(tables, out LootTableRegistry registry, out _));
            var mobs = new StubMobInfoProvider();
            var tracker = new PartyTagTracker(rig.Parties, mobs, () => TICK);
            var loot = new LootTableService(
                registry, tracker, rig.Parties, mobs, new NullCurrencyService(),
                new ScriptedRandom(HITTING_DRAW), rig.Distributor);
            loot.RecordDamage(Mob, CharA, KILLING_DAMAGE);

            // Act
            loot.ResolveMobDrop(Mob, NO_TIER_SHIFT);

            // Assert
            Assert.AreEqual(ONE_ITEM, rig.Spawned.Count);
            Assert.AreEqual(ItemA, rig.Spawned[0].ItemId);
            Assert.AreEqual(CharA, rig.Spawned[0].AssignedTo);
            Assert.AreEqual(MobPosition, rig.Spawned[0].Position);
            Assert.AreEqual(ONE_CALL, rig.Parties.AdvanceCalls);
        }
    }
}

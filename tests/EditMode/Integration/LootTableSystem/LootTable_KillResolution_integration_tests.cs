using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Randomness;
using IronGrind.Tests.EditMode.LootTableSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.LootTableSystem
{
    /// <summary>
    /// EditMode integration tests for Loot Table Story 004: kill resolution and gold distribution
    /// (design/gdd/loot-table-system.md CR-LT-14, F-LT-1, CR-LT-4; entry point design/gdd/enemy-ai.md),
    /// using a recording currency service, stub party and mob providers, a recording drop sink and a
    /// random provider with a fixed <c>NextInt</c> result.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_KillResolution_Integration_Tests
    {
        private const int MOB_MAX_HP = 300;
        private const uint MOB_TYPE_RAW = 7u;
        private const int PRNG_SEED = 4242;
        private const int GOLD_MIN_SPLIT = 40;
        private const int GOLD_MAX_SPLIT = 60;
        private const int GOLD_DRAW_52 = 52;
        private const int GOLD_DRAW_51 = 51;
        private const uint GOLD_SHARE_OF_3 = 17u;
        private const uint GOLD_SHARE_OF_2 = 25u;
        private const int GOLD_MIN_ZERO_SHARE = 4;
        private const int GOLD_MAX_ZERO_SHARE = 8;
        private const int GOLD_DRAW_2 = 2;
        private const int GOLD_SOLO = 10;
        private const int TIER_SHIFT = 2;
        private const uint HIT_DAMAGE = 50u;

        private const uint MOB_TYPE_WITHOUT_TABLE_RAW = 99u;
        private const int NEGATIVE_GOLD_DRAW = -8;
        private const string DRAW_ROLL = "D";
        private const string DRAW_GOLD = "N";

        private static readonly EntityID Mob = new EntityID(500u);
        private static readonly EntityID MobWithoutTable = new EntityID(501u);
        private static readonly EntityID UnknownMob = new EntityID(999u);

        private int _warningCount;

        [SetUp]
        public void SetUp()
        {
            _warningCount = 0;
            Application.logMessageReceived += CountWarning;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CountWarning;
        }

        // LogAssert.Expect only proves a warning arrived; this counts them so "exactly one" and
        // "none" can be asserted.
        private void CountWarning(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
            {
                _warningCount++;
            }
        }
        private static readonly PartyID PartyA = new PartyID(1u);
        private static readonly PartyID PartySolo = new PartyID(3u);
        private static readonly CharacterID CharA1 = new CharacterID(11u);
        private static readonly CharacterID CharA2 = new CharacterID(12u);
        private static readonly CharacterID CharA3 = new CharacterID(13u);
        private static readonly CharacterID CharA4 = new CharacterID(14u);
        private static readonly CharacterID CharSolo = new CharacterID(31u);
        private static readonly ItemID ItemA = new ItemID(101u);
        private static readonly Vector3 MobPosition = new Vector3(1f, 2f, 3f);

        // -----------------------------------------------------------------------
        // Fakes
        // -----------------------------------------------------------------------

        private sealed class StubPartyService : IPartyService
        {
            public readonly Dictionary<CharacterID, PartyID> Map = new Dictionary<CharacterID, PartyID>();
            public readonly Dictionary<PartyID, List<CharacterID>> Members = new Dictionary<PartyID, List<CharacterID>>();

            public void AddMember(PartyID party, CharacterID character)
            {
                Map[character] = party;
                if (!Members.TryGetValue(party, out List<CharacterID> list))
                {
                    list = new List<CharacterID>();
                    Members[party] = list;
                }
                list.Add(character);
            }

            public PartyID GetPartyID(CharacterID characterId)
            {
                return Map.TryGetValue(characterId, out PartyID party) ? party : PartyID.Uninitialized;
            }

            public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
            {
                return Members.TryGetValue(partyId, out List<CharacterID> list) ? list : new List<CharacterID>();
            }

            public CharacterID GetMemberAtIndex(PartyID partyId, int index) => CharacterID.Invalid;

            public int GetRrNextIndex(PartyID partyId) => 0;

            public void AdvanceRrNextIndex(PartyID partyId) { }

            public bool IsMemberConnected(CharacterID characterId) => true;
        }

        private sealed class StubMobInfoProvider : IMobInfoProvider
        {
            public readonly Dictionary<EntityID, MobInfo> Mobs = new Dictionary<EntityID, MobInfo>();

            public bool TryGetMob(EntityID mobEntityId, out MobInfo info)
            {
                return Mobs.TryGetValue(mobEntityId, out info);
            }
        }

        private sealed class FakeTick
        {
            public uint Value;

            public uint Read() => Value;
        }

        private struct GoldCall
        {
            public CharacterID Character;
            public uint Amount;
            public GoldTransactionReason Reason;
        }

        private sealed class RecordingCurrencyService : ICurrencyService
        {
            public readonly List<GoldCall> AddGoldCalls = new List<GoldCall>();
            public CharacterID FailingCharacter;
            public GoldMutationError FailingError = GoldMutationError.CharacterNotFound;

            public event Action<GoldSyncEventArgs> OnGoldSync
            {
                add { }
                remove { }
            }

            public void RegisterCharacter(CharacterID charId, uint initialBalance) { }

            public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
            {
                AddGoldCalls.Add(new GoldCall { Character = charId, Amount = amount, Reason = reason });
                if (FailingCharacter == charId)
                {
                    return new GoldMutationResult(false, 0u, 0u, FailingError);
                }
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

        private struct SinkCall
        {
            public EntityID Mob;
            public PartyID Party;
            public List<ItemID> Drops;
            public Vector3 Position;
        }

        private sealed class RecordingDropSink : ILootDropSink
        {
            public readonly List<SinkCall> Calls = new List<SinkCall>();
            public bool ThrowOnCall;

            public void OnDropsResolved(EntityID mobEntityId, PartyID winningParty, IReadOnlyList<ItemID> drops, Vector3 position)
            {
                if (ThrowOnCall)
                {
                    throw new InvalidOperationException("Sink failure injected by the test.");
                }

                Calls.Add(new SinkCall
                {
                    Mob = mobEntityId,
                    Party = winningParty,
                    Drops = new List<ItemID>(drops),
                    Position = position,
                });
            }
        }

        // Counts every draw and records their order; NextInt(min, max) returns a fixed value so the
        // gold draw is controlled, and remembers the bounds it was asked for. Roll draws come from a
        // seeded SystemRandomProvider. Loot Table never draws a float, so NextFloat() throws.
        private sealed class FixedGoldRandom : IRandomProvider
        {
            private readonly IRandomProvider _inner;
            private readonly int _nextValue;

            public int NextDoubleCalls { get; private set; }

            public int NextCalls { get; private set; }

            public int TotalDraws => NextDoubleCalls + NextCalls;

            public int LastNextMin { get; private set; }

            public int LastNextMax { get; private set; }

            // One DRAW_ROLL per NextDouble() and one DRAW_GOLD per NextInt(min, max), in call order.
            public string DrawOrder { get; private set; } = string.Empty;

            public FixedGoldRandom(int seed, int nextValue)
            {
                _inner = new SystemRandomProvider(new System.Random(seed));
                _nextValue = nextValue;
            }

            public float NextFloat()
            {
                throw new InvalidOperationException("Loot Table must not draw a float.");
            }

            public double NextDouble()
            {
                NextDoubleCalls++;
                DrawOrder += DRAW_ROLL;
                return _inner.NextDouble();
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                NextCalls++;
                DrawOrder += DRAW_GOLD;
                LastNextMin = minInclusive;
                LastNextMax = maxExclusive;
                return _nextValue;
            }
        }

        // One fully wired, fresh fixture per call so a test can build several independent runs.
        private sealed class Rig
        {
            public readonly StubPartyService Parties = new StubPartyService();
            public readonly StubMobInfoProvider Mobs = new StubMobInfoProvider();
            public readonly RecordingCurrencyService Currency = new RecordingCurrencyService();
            public readonly RecordingDropSink Sink = new RecordingDropSink();
            public readonly FakeTick Tick = new FakeTick();
            public readonly FixedGoldRandom Random;
            public readonly LootTableRegistry Registry;
            public readonly PartyTagTracker Tracker;
            public readonly LootTableService Service;

            public Rig(LootTableDefinition table, int goldDraw)
            {
                var tables = new List<KeyValuePair<MobTypeID, LootTableDefinition>>
                {
                    new KeyValuePair<MobTypeID, LootTableDefinition>(new MobTypeID(MOB_TYPE_RAW), table),
                };
                Assert.IsTrue(LootTableRegistry.TryCreate(tables, new EmptyItemDatabase(), LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out _));
                Registry = registry;

                Mobs.Mobs[Mob] = new MobInfo(new MobTypeID(MOB_TYPE_RAW), MOB_MAX_HP, MobPosition);
                Mobs.Mobs[MobWithoutTable] = new MobInfo(new MobTypeID(MOB_TYPE_WITHOUT_TABLE_RAW), MOB_MAX_HP, MobPosition);
                Random = new FixedGoldRandom(PRNG_SEED, goldDraw);
                Tracker = new PartyTagTracker(Parties, Mobs, Tick.Read);
                Service = new LootTableService(Registry, Tracker, Parties, Mobs, Currency, Random, Sink);
            }

            public void Hit(CharacterID attacker)
            {
                Service.RecordDamage(Mob, attacker, HIT_DAMAGE);
            }
        }

        private static LootTableDefinition TableWithGuaranteedDrop(int goldMin, int goldMax)
        {
            return new LootTableDefinition(new[] { new LootTableEntry(ItemA, 1.0f) }, goldMin, goldMax);
        }

        private static Rig PartyOf(int memberCount, LootTableDefinition table, int goldDraw)
        {
            var rig = new Rig(table, goldDraw);
            CharacterID[] pool = { CharA1, CharA2, CharA3, CharA4 };
            for (int i = 0; i < memberCount; i++)
            {
                rig.Parties.AddMember(PartyA, pool[i]);
            }
            rig.Hit(CharA1);
            return rig;
        }

        private static Rig SoloRig(LootTableDefinition table, int goldDraw)
        {
            var rig = new Rig(table, goldDraw);
            rig.Parties.AddMember(PartySolo, CharSolo);
            return rig;
        }

        // -----------------------------------------------------------------------
        // AC-LT-10: gold split
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_PartyOfThreeDraw52_PaysSeventeenToEachMemberAndDiscardsRemainder()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(3, rig.Currency.AddGoldCalls.Count);
            CharacterID[] expected = { CharA1, CharA2, CharA3 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], rig.Currency.AddGoldCalls[i].Character);
                Assert.AreEqual(GOLD_SHARE_OF_3, rig.Currency.AddGoldCalls[i].Amount);
                Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Currency.AddGoldCalls[i].Reason);
            }
        }

        [Test]
        public void ResolveMobDrop_BaseGoldTwoPartyOfFour_PaysNothingAndLogsAuthoringViolation()
        {
            // TryCreate rejects GoldMin < 4, so a baseGold of 2 cannot come from a valid table. The
            // table stays valid (4..8) and the provider returns 2: the service must use whatever NextInt returns.
            Rig rig = PartyOf(4, TableWithGuaranteedDrop(GOLD_MIN_ZERO_SHARE, GOLD_MAX_ZERO_SHARE), GOLD_DRAW_2);
            LogAssert.Expect(LogType.Error, new Regex("no payable share"));

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
        }

        [Test]
        public void ResolveMobDrop_NegativeGoldDraw_PaysNothingAndLogsError()
        {
            // A negative share cast to uint would wrap to a huge award; it must be treated like zero.
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_MIN_ZERO_SHARE, GOLD_MAX_ZERO_SHARE), NEGATIVE_GOLD_DRAW);
            rig.Hit(CharSolo);
            LogAssert.Expect(LogType.Error, new Regex("no payable share"));

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
        }

        [Test]
        public void ResolveMobDrop_GoldDraw_UsesInclusiveRangeAndComesAfterTheRoll()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(GOLD_MIN_SPLIT, rig.Random.LastNextMin);
            Assert.AreEqual(GOLD_MAX_SPLIT + 1, rig.Random.LastNextMax, "The upper bound is exclusive, so GoldMax + 1 makes GoldMax drawable.");
            Assert.AreEqual(DRAW_ROLL + DRAW_GOLD, rig.Random.DrawOrder, "One roll draw for the single entry, then the gold draw.");
        }

        [Test]
        public void ResolveMobDrop_AddGoldFailsForOneMember_LogsErrorAndStillPaysTheOthers()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            rig.Currency.FailingCharacter = CharA1;
            LogAssert.Expect(LogType.Error, new Regex(@"AddGold for CharacterID\(11\) failed"));

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(3, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(CharA2, rig.Currency.AddGoldCalls[1].Character);
            Assert.AreEqual(CharA3, rig.Currency.AddGoldCalls[2].Character);
        }

        // -----------------------------------------------------------------------
        // AC-LT-20: solo full amount
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_SoloPlayer_PaysFullGoldOnceAndHandsDropToSinkInSameCall()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);
            rig.Hit(CharSolo);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(1, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(CharSolo, rig.Currency.AddGoldCalls[0].Character);
            Assert.AreEqual((uint)GOLD_SOLO, rig.Currency.AddGoldCalls[0].Amount);
            Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Currency.AddGoldCalls[0].Reason);
            Assert.AreEqual(1, rig.Sink.Calls.Count);
            Assert.AreEqual(Mob, rig.Sink.Calls[0].Mob);
            Assert.AreEqual(PartySolo, rig.Sink.Calls[0].Party);
            CollectionAssert.AreEqual(new[] { ItemA }, rig.Sink.Calls[0].Drops);
            Assert.AreEqual(MobPosition, rig.Sink.Calls[0].Position);
        }

        [Test]
        public void ResolveMobDrop_SameMobWithNoRecordedDamage_PaysNoGold()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
        }

        // -----------------------------------------------------------------------
        // AC-LT-5: no attacker
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_EmptyDamageRecord_ProducesNothingAndLogsNothing()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Sink.Calls.Count);
            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(0, _warningCount, "A kill with no attacker must log no warning.");
        }

        // -----------------------------------------------------------------------
        // CR-LT-14: party size at kill time
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_PartyOfTwoDraw51_PaysTwentyFiveToEach()
        {
            Rig rig = PartyOf(2, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_51);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(2, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(CharA1, rig.Currency.AddGoldCalls[0].Character);
            Assert.AreEqual(CharA2, rig.Currency.AddGoldCalls[1].Character);
            Assert.AreEqual(GOLD_SHARE_OF_2, rig.Currency.AddGoldCalls[0].Amount);
            Assert.AreEqual(GOLD_SHARE_OF_2, rig.Currency.AddGoldCalls[1].Amount);
            Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Currency.AddGoldCalls[0].Reason);
            Assert.AreEqual(GoldTransactionReason.MonsterDrop, rig.Currency.AddGoldCalls[1].Reason);
        }

        [Test]
        public void ResolveMobDrop_AllEntriesMiss_StillPaysGoldAndSinkReceivesNothing()
        {
            var table = new LootTableDefinition(new[] { new LootTableEntry(ItemA, 0.0f) }, GOLD_MIN_SPLIT, GOLD_MAX_SPLIT);
            Rig rig = PartyOf(2, table, GOLD_DRAW_51);

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(2, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(GOLD_SHARE_OF_2, rig.Currency.AddGoldCalls[0].Amount);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // Entry point / tierShift
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_TierShiftTwo_BehavesLikeZeroAndLogsOneWarning()
        {
            Rig baseline = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            Rig shifted = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);

            baseline.Service.ResolveMobDrop(Mob, 0);
            LogAssert.Expect(LogType.Warning, new Regex("tierShift=2.*ignored"));
            shifted.Service.ResolveMobDrop(Mob, TIER_SHIFT);

            Assert.AreEqual(baseline.Currency.AddGoldCalls.Count, shifted.Currency.AddGoldCalls.Count);
            for (int i = 0; i < baseline.Currency.AddGoldCalls.Count; i++)
            {
                Assert.AreEqual(baseline.Currency.AddGoldCalls[i].Character, shifted.Currency.AddGoldCalls[i].Character);
                Assert.AreEqual(baseline.Currency.AddGoldCalls[i].Amount, shifted.Currency.AddGoldCalls[i].Amount);
                Assert.AreEqual(baseline.Currency.AddGoldCalls[i].Reason, shifted.Currency.AddGoldCalls[i].Reason);
            }
            Assert.AreEqual(baseline.Sink.Calls.Count, shifted.Sink.Calls.Count);
            Assert.AreEqual(baseline.Sink.Calls[0].Party, shifted.Sink.Calls[0].Party);
            CollectionAssert.AreEqual(baseline.Sink.Calls[0].Drops, shifted.Sink.Calls[0].Drops);
            Assert.AreEqual(baseline.Sink.Calls[0].Position, shifted.Sink.Calls[0].Position);
            Assert.AreEqual(baseline.Random.DrawOrder, shifted.Random.DrawOrder, "A tier shift must not consume extra PRNG draws.");
            Assert.AreEqual(1, _warningCount, "Exactly one warning: the ignored tier shift.");
        }

        [Test]
        public void ResolveMobDrop_MobTypeWithoutTable_LogsErrorDistributesNothingAndClearsRecord()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);
            rig.Service.RecordDamage(MobWithoutTable, CharSolo, HIT_DAMAGE);
            LogAssert.Expect(LogType.Error, new Regex("no loot table"));

            rig.Service.ResolveMobDrop(MobWithoutTable, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
            Assert.IsFalse(rig.Tracker.TryGetTagOwner(MobWithoutTable, out _));
        }

        [Test]
        public void ResolveMobDrop_UnknownMobEntityId_LogsErrorAndDistributesNothing()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);
            LogAssert.Expect(LogType.Error, new Regex("not a known mob"));

            rig.Service.ResolveMobDrop(UnknownMob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
        }

        // -----------------------------------------------------------------------
        // Damage entry point
        // -----------------------------------------------------------------------

        [Test]
        public void RecordDamage_ThroughServiceThenResolve_PaysTheSoloParty()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);

            rig.Service.RecordDamage(Mob, CharSolo, HIT_DAMAGE);
            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(1, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(CharSolo, rig.Currency.AddGoldCalls[0].Character);
            Assert.AreEqual((uint)GOLD_SOLO, rig.Currency.AddGoldCalls[0].Amount);
        }

        // -----------------------------------------------------------------------
        // Empty owner party
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_OwnerPartyHasNoMembers_DistributesNothingDrawsNothingAndWarns()
        {
            Rig rig = PartyOf(2, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            rig.Parties.Members[PartyA] = new List<CharacterID>();
            LogAssert.Expect(LogType.Warning, new Regex("has no members"));

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
            Assert.AreEqual(0, rig.Random.TotalDraws);
            Assert.AreEqual(1, _warningCount, "Exactly one warning: the empty owner party.");
            Assert.IsFalse(rig.Tracker.TryGetTagOwner(Mob, out _), "The damage record must be cleared on this path too.");
        }

        // -----------------------------------------------------------------------
        // Record cleared
        // -----------------------------------------------------------------------

        [Test]
        public void ResolveMobDrop_CalledTwiceForSameMob_SecondCallDistributesNothing()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            rig.Service.ResolveMobDrop(Mob, 0);
            int goldCallsAfterFirst = rig.Currency.AddGoldCalls.Count;
            int sinkCallsAfterFirst = rig.Sink.Calls.Count;

            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(3, goldCallsAfterFirst);
            Assert.AreEqual(1, sinkCallsAfterFirst);
            Assert.AreEqual(goldCallsAfterFirst, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(sinkCallsAfterFirst, rig.Sink.Calls.Count);
        }

        [Test]
        public void ResolveMobDrop_SinkThrows_RecordIsAlreadyClearedSoARetryPaysNothing()
        {
            // The record is cleared before anything is paid: a failure part-way cannot lead to a second payout.
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            rig.Sink.ThrowOnCall = true;

            Assert.Throws<InvalidOperationException>(() => rig.Service.ResolveMobDrop(Mob, 0));
            int goldCallsAfterFailure = rig.Currency.AddGoldCalls.Count;
            rig.Sink.ThrowOnCall = false;
            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(3, goldCallsAfterFailure);
            Assert.AreEqual(goldCallsAfterFailure, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
        }

        [Test]
        public void ClearMob_AfterRecordedDamage_KillThenDistributesNothing()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);

            rig.Service.ClearMob(Mob);
            rig.Service.ResolveMobDrop(Mob, 0);

            Assert.AreEqual(0, rig.Currency.AddGoldCalls.Count);
            Assert.AreEqual(0, rig.Sink.Calls.Count);
        }

        [Test]
        public void Clear_WithRecordedDamage_RemovesEveryRecord()
        {
            Rig rig = PartyOf(3, TableWithGuaranteedDrop(GOLD_MIN_SPLIT, GOLD_MAX_SPLIT), GOLD_DRAW_52);
            rig.Service.RecordDamage(MobWithoutTable, CharA1, HIT_DAMAGE);

            rig.Service.Clear();

            Assert.IsFalse(rig.Tracker.TryGetTagOwner(Mob, out _));
            Assert.IsFalse(rig.Tracker.TryGetTagOwner(MobWithoutTable, out _));
        }

        // -----------------------------------------------------------------------
        // Constructor guards
        // -----------------------------------------------------------------------

        [Test]
        public void Constructor_AnyNullArgument_Throws()
        {
            Rig rig = SoloRig(TableWithGuaranteedDrop(GOLD_SOLO, GOLD_SOLO), GOLD_SOLO);

            Assert.Throws<ArgumentNullException>(() => new LootTableService(null, rig.Tracker, rig.Parties, rig.Mobs, rig.Currency, rig.Random, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, null, rig.Parties, rig.Mobs, rig.Currency, rig.Random, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, rig.Tracker, null, rig.Mobs, rig.Currency, rig.Random, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, rig.Tracker, rig.Parties, null, rig.Currency, rig.Random, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, rig.Tracker, rig.Parties, rig.Mobs, null, rig.Random, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, rig.Tracker, rig.Parties, rig.Mobs, rig.Currency, null, rig.Sink));
            Assert.Throws<ArgumentNullException>(() => new LootTableService(rig.Registry, rig.Tracker, rig.Parties, rig.Mobs, rig.Currency, rig.Random, null));
        }
    }
}

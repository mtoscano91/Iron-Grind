using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Integration.LootTableSystem
{
    /// <summary>
    /// EditMode integration tests for Loot Table Story 003: party tag damage record, threshold lock
    /// and fallback (design/gdd/loot-table-system.md CR-LT-3 / CR-LT-4 / F-LT-3), using a stub
    /// party service, a stub mob provider and a fake tick counter.
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_PartyTag_Integration_Tests
    {
        private const int MOB_MAX_HP = 300;
        private const int MIN_DESIGN_MAX_HP = 1;
        private const int MAX_DESIGN_MAX_HP = 9999;
        private static readonly EntityID Mob = new EntityID(500u);
        private static readonly EntityID SecondMob = new EntityID(501u);
        private static readonly EntityID ZeroHpMob = new EntityID(502u);
        private static readonly EntityID UnknownMob = new EntityID(999u);
        private static readonly PartyID PartyA = new PartyID(1u);
        private static readonly PartyID PartyB = new PartyID(2u);
        private static readonly PartyID PartySolo = new PartyID(3u);
        private static readonly CharacterID CharA1 = new CharacterID(11u);
        private static readonly CharacterID CharA2 = new CharacterID(12u);
        private static readonly CharacterID CharB = new CharacterID(21u);
        private static readonly CharacterID CharSolo = new CharacterID(31u);
        private static readonly CharacterID CharNoParty = new CharacterID(41u);

        private StubPartyService _parties;
        private StubMobInfoProvider _mobs;
        private FakeTick _tick;
        private PartyTagTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _parties = new StubPartyService();
            _parties.Map[CharA1] = PartyA;
            _parties.Map[CharA2] = PartyA;
            _parties.Map[CharB] = PartyB;
            _parties.Map[CharSolo] = PartySolo;
            _parties.Map[CharNoParty] = PartyID.Uninitialized;

            _mobs = new StubMobInfoProvider();
            _mobs.Mobs[Mob] = new MobInfo(new MobTypeID(7u), MOB_MAX_HP, Vector3.zero);
            _mobs.Mobs[SecondMob] = new MobInfo(new MobTypeID(7u), MOB_MAX_HP, Vector3.zero);
            _mobs.Mobs[ZeroHpMob] = new MobInfo(new MobTypeID(8u), 0, Vector3.zero);

            _tick = new FakeTick();
            _tracker = new PartyTagTracker(_parties, _mobs, _tick.Read);
        }

        // -----------------------------------------------------------------------
        // Fakes
        // -----------------------------------------------------------------------

        private sealed class StubPartyService : IPartyService
        {
            public readonly Dictionary<CharacterID, PartyID> Map = new Dictionary<CharacterID, PartyID>();

            public PartyID GetPartyID(CharacterID characterId)
            {
                return Map.TryGetValue(characterId, out PartyID party) ? party : PartyID.Uninitialized;
            }

            public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId)
            {
                var members = new List<CharacterID>();
                foreach (KeyValuePair<CharacterID, PartyID> pair in Map)
                {
                    if (pair.Value == partyId)
                    {
                        members.Add(pair.Key);
                    }
                }
                return members;
            }

            public CharacterID GetMemberAtIndex(PartyID partyId, int index) => CharacterID.Invalid;

            public int GetRrNextIndex(PartyID partyId) => 0;

            public void AdvanceRrNextIndex(PartyID partyId) { }

            public bool IsMemberConnected(CharacterID characterId) => true;
        }

        private sealed class StubMobInfoProvider : IMobInfoProvider
        {
            public readonly Dictionary<EntityID, MobInfo> Mobs = new Dictionary<EntityID, MobInfo>();
            public int TryGetMobCalls;

            public bool TryGetMob(EntityID mobEntityId, out MobInfo info)
            {
                TryGetMobCalls++;
                return Mobs.TryGetValue(mobEntityId, out info);
            }
        }

        private sealed class FakeTick
        {
            public uint Value;

            public uint Read() => Value;
        }

        private void Hit(CharacterID attacker, uint damage, uint tick)
        {
            _tick.Value = tick;
            _tracker.RecordDamage(Mob, attacker, damage);
        }

        private PartyID OwnerOrFail(EntityID mob)
        {
            Assert.IsTrue(_tracker.TryGetTagOwner(mob, out PartyID owner));
            return owner;
        }

        // -----------------------------------------------------------------------
        // AC-LT-3: threshold values
        // -----------------------------------------------------------------------

        [TestCase(120, 40)]
        [TestCase(1, 1)]
        [TestCase(300, 99)]
        [TestCase(9999, 3300)]
        public void ComputeTagThreshold_GddValues_MatchExpected(int maxHp, int expected)
        {
            Assert.AreEqual(expected, PartyTagTracker.ComputeTagThreshold(maxHp));
        }

        [Test]
        public void ComputeTagThreshold_EveryMaxHpInDesignRange_EqualsExactCeilingOf33Percent()
        {
            for (int maxHp = MIN_DESIGN_MAX_HP; maxHp <= MAX_DESIGN_MAX_HP; maxHp++)
            {
                // Exact integer ceiling of maxHp * 33 / 100.
                int expected = (maxHp * 33 + 99) / 100;
                Assert.AreEqual(expected, PartyTagTracker.ComputeTagThreshold(maxHp), "MaxHP " + maxHp);
            }
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void ComputeTagThreshold_MaxHpBelowOne_ClampsToOne(int maxHp)
        {
            Assert.AreEqual(1, PartyTagTracker.ComputeTagThreshold(maxHp));
        }

        // -----------------------------------------------------------------------
        // AC-LT-4: threshold lock
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_FirstCrossingParty_KeepsTagDespiteLargerLaterDamage()
        {
            Hit(CharA1, 60, 1);
            Hit(CharB, 50, 2);
            Hit(CharA1, 39, 3);
            Hit(CharB, 200, 4);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_OneBelowThreshold_DoesNotLock()
        {
            Hit(CharA1, 98, 1);
            Assert.AreEqual(PartyA, OwnerOrFail(Mob));

            Hit(CharB, 200, 2);

            Assert.AreEqual(PartyB, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_ExactlyAtThreshold_Locks()
        {
            Hit(CharA1, 99, 1);
            Hit(CharB, 200, 2);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // AC-LT-4: fallback
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_Fallback_HighestCumulativeDamageWins()
        {
            Hit(CharA1, 30, 10);
            Hit(CharB, 50, 12);

            Assert.AreEqual(PartyB, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_FallbackDamageTie_GoesToEarliestFirstDamageTick()
        {
            Hit(CharA1, 40, 10);
            Hit(CharB, 40, 12);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_SecondHitByParty_DoesNotOverwriteFirstDamageTick()
        {
            // A and B both end on 40. A's first hit is tick 10; if its second hit at tick 12
            // overwrote that, B (tick 11) would win the tie.
            Hit(CharA1, 20, 10);
            Hit(CharB, 40, 11);
            Hit(CharA1, 20, 12);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // CR-LT-3: per-party accumulation
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_TwoMembersOfOneParty_AccumulateTogether()
        {
            Hit(CharA1, 50, 1);
            Hit(CharA2, 49, 2);
            Hit(CharB, 200, 3);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_SoloCharacter_AccumulatesSeparatelyFromParty()
        {
            Hit(CharA1, 50, 1);
            Hit(CharSolo, 60, 2);
            Hit(CharA2, 40, 3);
            Assert.AreEqual(PartyA, OwnerOrFail(Mob));

            Hit(CharSolo, 39, 4);

            Assert.AreEqual(PartySolo, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // CR-LT-4 no attacker
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_MobWithoutRecordedDamage_HasNoOwner()
        {
            bool found = _tracker.TryGetTagOwner(Mob, out PartyID owner);

            Assert.IsFalse(found);
            Assert.AreEqual(PartyID.Uninitialized, owner);
        }

        [Test]
        public void PartyTag_UnknownMob_HasNoOwner()
        {
            bool found = _tracker.TryGetTagOwner(UnknownMob, out PartyID owner);

            Assert.IsFalse(found);
            Assert.AreEqual(PartyID.Uninitialized, owner);
        }

        // -----------------------------------------------------------------------
        // Same-tick crossing
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_SameTickCrossing_FirstProcessedPartyOwnsTag()
        {
            Hit(CharA1, 99, 7);
            Hit(CharB, 150, 7);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // Zero damage
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_ZeroDamage_RecordsNothingAndLogsNothing()
        {
            Hit(CharA1, 0, 1);

            Assert.IsFalse(_tracker.TryGetTagOwner(Mob, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void PartyTag_ZeroDamageHit_SetsNoFirstDamageTick()
        {
            Hit(CharA1, 0, 5);
            Hit(CharB, 10, 6);
            Hit(CharA1, 10, 7);

            Assert.AreEqual(PartyB, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // Fallback double tie
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_FallbackDoubleTie_GoesToPartyRecordedFirst()
        {
            Hit(CharA1, 40, 10);
            Hit(CharB, 40, 10);

            Assert.AreEqual(PartyA, OwnerOrFail(Mob));
        }

        [Test]
        public void PartyTag_FallbackDoubleTieReverseOrder_GoesToOtherParty()
        {
            Hit(CharB, 40, 10);
            Hit(CharA1, 40, 10);

            Assert.AreEqual(PartyB, OwnerOrFail(Mob));
        }

        // -----------------------------------------------------------------------
        // Unknown mob / uninitialized party / invalid MaxHP
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_UnknownMob_LogsErrorAndRecordsNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"EntityID\(999\)"));

            _tracker.RecordDamage(UnknownMob, CharA1, 50);

            Assert.IsFalse(_tracker.TryGetTagOwner(UnknownMob, out _));
        }

        [Test]
        public void PartyTag_AttackerWithUninitializedParty_LogsErrorAndRecordsNothing()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"CharacterID\(41\)"));

            _tracker.RecordDamage(Mob, CharNoParty, 50);

            Assert.IsFalse(_tracker.TryGetTagOwner(Mob, out _));
        }

        [Test]
        public void PartyTag_MobWithMaxHpZero_LogsErrorAndUsesThresholdOfOne()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"MaxHP=0"));

            // Threshold clamps to 1: the first real hit locks, and later larger damage cannot steal.
            _tracker.RecordDamage(ZeroHpMob, CharA1, 1);
            _tracker.RecordDamage(ZeroHpMob, CharB, 500);

            Assert.AreEqual(PartyA, OwnerOrFail(ZeroHpMob));
        }

        // -----------------------------------------------------------------------
        // Threshold caching (Performance note)
        // -----------------------------------------------------------------------

        [Test]
        public void PartyTag_RepeatedHitsOnOneMob_ConsultProviderOnce()
        {
            Hit(CharA1, 10, 1);
            Hit(CharB, 10, 2);
            Hit(CharA1, 10, 3);

            Assert.AreEqual(1, _mobs.TryGetMobCalls);
        }

        // -----------------------------------------------------------------------
        // Record lifetime
        // -----------------------------------------------------------------------

        [Test]
        public void ClearMob_AfterLock_RemovesOwnerAndNextHitStartsFreshRecord()
        {
            Hit(CharA1, 99, 1);
            Assert.AreEqual(PartyA, OwnerOrFail(Mob));

            _tracker.ClearMob(Mob);

            Assert.IsFalse(_tracker.TryGetTagOwner(Mob, out _));

            Hit(CharB, 10, 2);

            Assert.AreEqual(PartyB, OwnerOrFail(Mob));
            Assert.AreEqual(2, _mobs.TryGetMobCalls, "A cleared mob is looked up again on its next hit.");
        }

        [Test]
        public void ClearMob_UnknownMob_IsSilentNoOp()
        {
            Assert.DoesNotThrow(() => _tracker.ClearMob(UnknownMob));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Clear_WithRecordsForTwoMobs_RemovesBoth()
        {
            Hit(CharA1, 99, 1);
            _tracker.RecordDamage(SecondMob, CharB, 99);

            _tracker.Clear();

            Assert.IsFalse(_tracker.TryGetTagOwner(Mob, out _));
            Assert.IsFalse(_tracker.TryGetTagOwner(SecondMob, out _));
        }

        // -----------------------------------------------------------------------
        // Constructor guards
        // -----------------------------------------------------------------------

        [Test]
        public void Constructor_NullPartyService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyTagTracker(null, _mobs, _tick.Read));
        }

        [Test]
        public void Constructor_NullMobInfoProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyTagTracker(_parties, null, _tick.Read));
        }

        [Test]
        public void Constructor_NullTickSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PartyTagTracker(_parties, _mobs, null));
        }
    }
}

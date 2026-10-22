using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 002 — player/mob slot pools, EntityID minting and
    /// the player roster view (<see cref="ZoneSlotPool"/>; CR-ZI-3/4/5/15, EC-ZI-4, EC-ZI-12).
    /// Ticks are set programmatically; no wall-clock time is used.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_SlotPools_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint FOREIGN_ZONE_RAW = 8u;
        private static readonly Regex FOREIGN_ZONE_LOG = new Regex("foreign zone");
        private static readonly Regex EXHAUSTED_LOG = new Regex("EntityIDCounterExhausted");

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick;
            public uint ServerTickNumber => Tick;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public readonly List<(int Occupied, uint Tick)> Changes = new List<(int, uint)>();
            public readonly List<uint> ExhaustedTicks = new List<uint>();

            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) => Changes.Add((occupied, serverTick));

            public void OnEntityIDCounterExhausted(uint serverTick) => ExhaustedTicks.Add(serverTick);
        }

        private FakeTicks _ticks;
        private FakeListener _listener;
        private uint _nextCharacter;

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks { Tick = 100u };
            _listener = new FakeListener();
            _nextCharacter = 1000u;
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private ZoneSlotPool Create(int maxPlayers = ZoneSlotPool.MAX_PLAYERS_PER_ZONE,
            int maxMobs = ZoneSlotPool.MAX_MOBS_PER_ZONE, uint firstEntityId = 1u)
        {
            return new ZoneSlotPool(Zone, _ticks, _listener, maxPlayers, maxMobs, firstEntityId);
        }

        private bool AllocPlayer(ZoneSlotPool pool, out byte slot, out EntityID id)
        {
            uint n = _nextCharacter++;
            return pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u), n + 9000u, out slot, out id);
        }

        private EntityID MustAllocPlayer(ZoneSlotPool pool)
        {
            Assert.IsTrue(AllocPlayer(pool, out _, out EntityID id));
            return id;
        }

        private void Fill(ZoneSlotPool pool, int count, List<EntityID> ids = null)
        {
            for (int i = 0; i < count; i++)
            {
                EntityID id = MustAllocPlayer(pool);
                ids?.Add(id);
            }
        }

        // ----- Construction ---------------------------------------------------------------------

        [Test]
        public void Constructor_Defaults_MatchKnobsAndStartEmpty()
        {
            ZoneSlotPool pool = Create();

            Assert.AreEqual(50, ZoneSlotPool.MAX_PLAYERS_PER_ZONE);
            Assert.AreEqual(150, ZoneSlotPool.MAX_MOBS_PER_ZONE);
            Assert.AreEqual(50, pool.MaxPlayers);
            Assert.AreEqual(150, pool.MaxMobs);
            Assert.AreEqual(0, pool.OccupiedPlayerCount);
            Assert.AreEqual(0, pool.OccupiedMobCount);
            Assert.AreEqual(Zone, pool.Zone);
        }

        [Test]
        public void Constructor_NullTicks_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSlotPool(Zone, null, _listener));
        }

        [Test]
        public void Constructor_NullListener_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSlotPool(Zone, _ticks, null));
        }

        [Test]
        public void Constructor_InvalidZone_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ZoneSlotPool(ZoneID.Invalid, _ticks, _listener));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(256)]
        public void Constructor_MaxPlayersOutOfRange_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(maxPlayers: value));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(65536)]
        public void Constructor_MaxMobsOutOfRange_Throws(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(maxMobs: value));
        }

        [Test]
        public void Constructor_FirstEntityIdZero_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(firstEntityId: 0u));
        }

        [Test]
        public void Constructor_CeilingSizes_Accepted()
        {
            Assert.DoesNotThrow(() => Create(maxPlayers: 255, maxMobs: 65535));
            Assert.DoesNotThrow(() => Create(maxPlayers: 1, maxMobs: 1));
        }

        // ----- Player pool, capacity ------------------------------------------------------------

        [Test]
        public void TryAllocatePlayer_FreshPool_HandsOutIndicesInOrderInConnectedState()
        {
            ZoneSlotPool pool = Create();

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(AllocPlayer(pool, out byte slot, out EntityID id));
                Assert.AreEqual(i, slot);
                Assert.IsTrue(pool.TryGetPlayer(id, out ZonePlayerSlot player));
                Assert.AreEqual(ZoneSessionState.Connected, player.State);
            }

            Assert.AreEqual(3, pool.OccupiedPlayerCount);
        }

        [Test]
        public void TryAllocatePlayer_StoresSlotFields()
        {
            ZoneSlotPool pool = Create();

            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(11u), new AccountID(22u), 33u, out byte slot, out EntityID id));

            Assert.IsTrue(pool.TryGetPlayer(id, out ZonePlayerSlot player));
            Assert.AreEqual(id, player.Entity);
            Assert.AreEqual(new CharacterID(11u), player.Character);
            Assert.AreEqual(new AccountID(22u), player.Account);
            Assert.AreEqual(33u, player.ClientId);
            Assert.IsTrue(pool.TryGetPlayerSlot(id, out byte lookedUp));
            Assert.AreEqual(slot, lookedUp);
        }

        [Test]
        public void TryAllocatePlayer_AtCapacity_Fails()
        {
            ZoneSlotPool pool = Create();
            Fill(pool, 50);

            bool ok = AllocPlayer(pool, out byte slot, out EntityID id);

            Assert.IsFalse(ok);
            Assert.AreEqual(EntityID.Invalid, id);
            Assert.AreEqual(0, slot);
            Assert.AreEqual(50, pool.OccupiedPlayerCount);
        }

        [Test]
        public void TryAllocatePlayer_TwoCallsForLastSlot_OneSucceedsAndCountStays50_AC_ZI_4()
        {
            ZoneSlotPool pool = Create();
            Fill(pool, 49);

            bool first = AllocPlayer(pool, out _, out _);
            bool second = AllocPlayer(pool, out _, out _);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(50, pool.OccupiedPlayerCount);
        }

        [Test]
        public void TryAllocatePlayer_GhostSlotCountsTowardCapacity()
        {
            ZoneSlotPool pool = Create();
            var ids = new List<EntityID>();
            Fill(pool, 50, ids);

            Assert.IsTrue(pool.SetSessionState(ids[10], ZoneSessionState.Disconnected_SessionActive));

            Assert.IsFalse(AllocPlayer(pool, out _, out _));
            Assert.AreEqual(50, pool.OccupiedPlayerCount);
        }

        [TestCase(ZoneSessionState.Disconnected_SessionActive)]
        [TestCase(ZoneSessionState.Dead)]
        [TestCase(ZoneSessionState.Respawning)]
        public void TryAllocatePlayer_AnySlotHoldingState_CountsTowardCapacity(ZoneSessionState state)
        {
            ZoneSlotPool pool = Create(maxPlayers: 2);
            EntityID a = MustAllocPlayer(pool);
            MustAllocPlayer(pool);

            pool.SetSessionState(a, state);

            Assert.IsFalse(AllocPlayer(pool, out _, out _));
            Assert.AreEqual(2, pool.OccupiedPlayerCount);
        }

        [Test]
        public void ReleasePlayer_FreedIndexIsReused_OthersKeepIdentity()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            AllocPlayer(pool, out byte slotB, out EntityID b);
            AllocPlayer(pool, out _, out EntityID c);

            Assert.IsTrue(pool.ReleasePlayer(b));
            Assert.IsTrue(AllocPlayer(pool, out byte newSlot, out EntityID d));

            Assert.AreEqual(slotB, newSlot);
            Assert.AreNotEqual(b, d);
            Assert.IsTrue(pool.TryGetPlayerSlot(a, out byte slotA));
            Assert.IsTrue(pool.TryGetPlayerSlot(c, out byte slotC));
            Assert.AreEqual(0, slotA);
            Assert.AreEqual(2, slotC);
        }

        [Test]
        public void ReleasePlayer_ZeroesSlotAndDropsCount()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            AllocPlayer(pool, out _, out EntityID b);

            Assert.IsTrue(pool.ReleasePlayer(a));

            Assert.AreEqual(1, pool.OccupiedPlayerCount);
            Assert.IsFalse(pool.TryGetPlayer(a, out ZonePlayerSlot gone));
            Assert.AreEqual(EntityID.Invalid, gone.Entity);
            Assert.IsFalse(pool.TryGetPlayerSlot(a, out _));
            Assert.IsTrue(pool.TryGetPlayerSlot(b, out _));
        }

        [Test]
        public void ReleasePlayer_ReusedSlot_DoesNotLeakPreviousState()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            pool.SetSessionState(a, ZoneSessionState.Dead);
            pool.ReleasePlayer(a);

            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 3u, out _, out EntityID b));

            Assert.IsTrue(pool.TryGetPlayer(b, out ZonePlayerSlot player));
            Assert.AreEqual(ZoneSessionState.Connected, player.State);
        }

        [Test]
        public void ReleasePlayer_UnknownOrTwice_ReturnsFalseWithoutNotification()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            pool.ReleasePlayer(a);
            _listener.Changes.Clear();

            Assert.IsFalse(pool.ReleasePlayer(a));
            Assert.IsFalse(pool.ReleasePlayer(new EntityID(9999u)));
            Assert.IsFalse(pool.ReleasePlayer(EntityID.Invalid));

            Assert.AreEqual(0, pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _listener.Changes.Count);
        }

        [Test]
        public void UnknownEntityId_AllPlayerLookupsReturnFalse()
        {
            ZoneSlotPool pool = Create();
            var unknown = new EntityID(4242u);

            Assert.IsFalse(pool.ReleasePlayer(unknown));
            Assert.IsFalse(pool.SetSessionState(unknown, ZoneSessionState.Dead));
            Assert.IsFalse(pool.TryGetPlayerSlot(unknown, out _));
        }

        [Test]
        public void SetSessionState_UpdatesStateWithoutChangingOccupancy()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);

            Assert.IsTrue(pool.SetSessionState(a, ZoneSessionState.Respawning));

            Assert.IsTrue(pool.TryGetPlayer(a, out ZonePlayerSlot player));
            Assert.AreEqual(ZoneSessionState.Respawning, player.State);
            Assert.AreEqual(1, pool.OccupiedPlayerCount);
        }

        [Test]
        public void ZoneSessionState_Values_AreFixed()
        {
            Assert.AreEqual(0, (byte)ZoneSessionState.Connected);
            Assert.AreEqual(1, (byte)ZoneSessionState.Disconnected_SessionActive);
            Assert.AreEqual(2, (byte)ZoneSessionState.Dead);
            Assert.AreEqual(3, (byte)ZoneSessionState.Respawning);
        }

        // ----- Mob pool -------------------------------------------------------------------------

        [Test]
        public void TryAllocateMob_FreshPool_HandsOutIndicesInOrder()
        {
            ZoneSlotPool pool = Create();

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(pool.TryAllocateMob(Zone, out ushort slot, out EntityID id));
                Assert.AreEqual(i, slot);
                Assert.AreNotEqual(EntityID.Invalid, id);
            }

            Assert.AreEqual(3, pool.OccupiedMobCount);
            Assert.AreEqual(0, pool.OccupiedPlayerCount);
        }

        [Test]
        public void TryAllocateMob_At150_FailsAt151st()
        {
            ZoneSlotPool pool = Create();
            for (int i = 0; i < 150; i++)
            {
                Assert.IsTrue(pool.TryAllocateMob(Zone, out _, out _), "mob " + i);
            }

            bool ok = pool.TryAllocateMob(Zone, out ushort slot, out EntityID id);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, slot);
            Assert.AreEqual(EntityID.Invalid, id);
            Assert.AreEqual(150, pool.OccupiedMobCount);
        }

        [Test]
        public void ReleaseMob_FreesSlotForReuse_UnknownReturnsFalse()
        {
            ZoneSlotPool pool = Create();
            pool.TryAllocateMob(Zone, out _, out EntityID a);
            pool.TryAllocateMob(Zone, out ushort slotB, out EntityID b);

            Assert.IsTrue(pool.ReleaseMob(b));
            Assert.IsFalse(pool.ReleaseMob(b));
            Assert.IsFalse(pool.ReleaseMob(new EntityID(9999u)));
            Assert.IsTrue(pool.TryAllocateMob(Zone, out ushort reused, out _));

            Assert.AreEqual(slotB, reused);
            Assert.AreEqual(2, pool.OccupiedMobCount);
            Assert.IsTrue(pool.ReleaseMob(a));
        }

        [Test]
        public void ReleaseMob_OnPlayerId_ReturnsFalseAndKeepsPlayer()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID player);

            Assert.IsFalse(pool.ReleaseMob(player));
            Assert.AreEqual(1, pool.OccupiedPlayerCount);
        }

        [Test]
        public void GetOwlIndexForMobSlot_FirstAndLastMob_AreOffsetByMaxPlayers()
        {
            ZoneSlotPool pool = Create();

            Assert.AreEqual(50, pool.GetOwlIndexForMobSlot(0));
            Assert.AreEqual(199, pool.GetOwlIndexForMobSlot(149));
        }

        [Test]
        public void GetOwlIndexForMobSlot_BeyondPool_Throws()
        {
            ZoneSlotPool pool = Create();

            Assert.Throws<ArgumentOutOfRangeException>(() => pool.GetOwlIndexForMobSlot(150));
        }

        [Test]
        public void GetOwlIndexForMobSlot_UsesConfiguredMaxPlayers()
        {
            ZoneSlotPool pool = Create(maxPlayers: 10, maxMobs: 5);

            Assert.AreEqual(14, pool.GetOwlIndexForMobSlot(4));
        }

        // ----- EntityID minting -----------------------------------------------------------------

        [Test]
        public void EntityIds_MixedTenAllocations_AreOneToTenInOrder_AC_ZI_11()
        {
            ZoneSlotPool pool = Create();
            var raws = new List<uint>();

            for (int i = 0; i < 10; i++)
            {
                if (i % 2 == 0)
                {
                    AllocPlayer(pool, out _, out EntityID p);
                    raws.Add(p.RawValue);
                }
                else
                {
                    pool.TryAllocateMob(Zone, out _, out EntityID m);
                    raws.Add(m.RawValue);
                }
            }

            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, raws);
        }

        [Test]
        public void EntityIds_AreNotReusedAfterRelease()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            pool.ReleasePlayer(a);

            AllocPlayer(pool, out _, out EntityID b);

            Assert.AreEqual(a.RawValue + 1u, b.RawValue);
        }

        [Test]
        public void EntityIds_CustomFirstId_StartsThere()
        {
            ZoneSlotPool pool = Create(firstEntityId: 500u);

            AllocPlayer(pool, out _, out EntityID a);

            Assert.AreEqual(500u, a.RawValue);
        }

        [Test]
        public void CounterExhaustion_PlayerPath_AlertOnceCallbackOnce_EC_ZI_12()
        {
            ZoneSlotPool pool = Create(firstEntityId: uint.MaxValue);

            Assert.IsTrue(AllocPlayer(pool, out _, out EntityID last));
            Assert.AreEqual(uint.MaxValue, last.RawValue);
            Assert.AreEqual(0, _listener.ExhaustedTicks.Count);

            _ticks.Tick = 321u;
            LogAssert.Expect(LogType.Error, EXHAUSTED_LOG);
            Assert.IsFalse(AllocPlayer(pool, out byte slot, out EntityID none));
            Assert.AreEqual(EntityID.Invalid, none);
            Assert.AreEqual(0, slot);
            CollectionAssert.AreEqual(new uint[] { 321u }, _listener.ExhaustedTicks);

            // Two further failed attempts (player and mob): silent, no second callback.
            Assert.IsFalse(AllocPlayer(pool, out _, out _));
            Assert.IsFalse(pool.TryAllocateMob(Zone, out _, out EntityID mob));
            Assert.AreEqual(EntityID.Invalid, mob);
            Assert.AreEqual(1, _listener.ExhaustedTicks.Count);
        }

        [Test]
        public void CounterExhaustion_MobPath_FailedAllocationTakesNoSlot()
        {
            ZoneSlotPool pool = Create(firstEntityId: uint.MaxValue);

            Assert.IsTrue(pool.TryAllocateMob(Zone, out _, out EntityID last));
            Assert.AreEqual(uint.MaxValue, last.RawValue);

            LogAssert.Expect(LogType.Error, EXHAUSTED_LOG);
            Assert.IsFalse(pool.TryAllocateMob(Zone, out _, out _));
            Assert.IsFalse(AllocPlayer(pool, out _, out _));

            Assert.AreEqual(1, pool.OccupiedMobCount);
            Assert.AreEqual(0, pool.OccupiedPlayerCount);
            Assert.AreEqual(1, _listener.ExhaustedTicks.Count);
            Assert.AreEqual(0, _listener.Changes.Count);
            Assert.AreEqual(0, pool.GetPlayerEntityIDsInZone(Zone).Count);
        }

        [Test]
        public void CounterExhaustion_NeverIssuesZero()
        {
            ZoneSlotPool pool = Create(firstEntityId: uint.MaxValue - 1u);

            AllocPlayer(pool, out _, out EntityID a);
            AllocPlayer(pool, out _, out EntityID b);
            LogAssert.Expect(LogType.Error, EXHAUSTED_LOG);
            bool third = AllocPlayer(pool, out _, out EntityID c);

            Assert.AreEqual(uint.MaxValue - 1u, a.RawValue);
            Assert.AreEqual(uint.MaxValue, b.RawValue);
            Assert.IsFalse(third);
            Assert.AreEqual(EntityID.Invalid, c);
        }

        [Test]
        public void CounterExhaustion_FullPoolBeforeExhaustion_DoesNotRaiseAlert()
        {
            ZoneSlotPool pool = Create(maxPlayers: 1, firstEntityId: uint.MaxValue);
            Assert.IsTrue(AllocPlayer(pool, out _, out _));

            // Pool is full, so the failure is a capacity failure, not an exhaustion alert.
            Assert.IsFalse(AllocPlayer(pool, out _, out _));
            Assert.AreEqual(0, _listener.ExhaustedTicks.Count);
        }

        // ----- Roster ---------------------------------------------------------------------------

        [Test]
        public void GetPlayerEntityIDsInZone_CastToIList_MutatorThrows_AC_ZI_15()
        {
            ZoneSlotPool pool = Create();
            MustAllocPlayer(pool);
            IReadOnlyList<EntityID> roster = pool.GetPlayerEntityIDsInZone(Zone);

            TestDelegate mutate = () => ((IList<EntityID>)roster).Add(new EntityID(77u));

            Assert.That(mutate, Throws.TypeOf<NotSupportedException>().Or.TypeOf<InvalidCastException>());
        }

        [Test]
        public void GetPlayerEntityIDsInZone_ExcludesMobsAndEmptySlots()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);
            pool.TryAllocateMob(Zone, out _, out _);
            AllocPlayer(pool, out _, out EntityID b);
            AllocPlayer(pool, out _, out EntityID c);
            pool.ReleasePlayer(b);

            IReadOnlyList<EntityID> roster = pool.GetPlayerEntityIDsInZone(Zone);

            CollectionAssert.AreEqual(new[] { a, c }, roster);
        }

        [Test]
        public void GetPlayerEntityIDsInZone_EmptyPool_IsEmpty()
        {
            ZoneSlotPool pool = Create();

            Assert.AreEqual(0, pool.GetPlayerEntityIDsInZone(Zone).Count);
        }

        [Test]
        public void GetPlayerEntityIDsInZone_SameInstanceEveryCall_RefreshedOnAllocAndRelease()
        {
            ZoneSlotPool pool = Create();
            IReadOnlyList<EntityID> first = pool.GetPlayerEntityIDsInZone(Zone);
            AllocPlayer(pool, out _, out EntityID a);
            AllocPlayer(pool, out _, out EntityID b);
            IReadOnlyList<EntityID> second = pool.GetPlayerEntityIDsInZone(Zone);

            Assert.AreSame(first, second);
            CollectionAssert.AreEqual(new[] { a, b }, first);

            pool.ReleasePlayer(a);

            Assert.AreSame(first, pool.GetPlayerEntityIDsInZone(Zone));
            CollectionAssert.AreEqual(new[] { b }, first);
        }

        [Test]
        public void GetPlayerEntityIDsInZone_SessionStateChange_KeepsPlayerInRoster()
        {
            ZoneSlotPool pool = Create();
            AllocPlayer(pool, out _, out EntityID a);

            pool.SetSessionState(a, ZoneSessionState.Dead);

            CollectionAssert.AreEqual(new[] { a }, pool.GetPlayerEntityIDsInZone(Zone));
        }

        // ----- Listener notifications -----------------------------------------------------------

        [Test]
        public void Listener_AllocateThenRelease_SeesCountAndTick()
        {
            ZoneSlotPool pool = Create();
            _ticks.Tick = 100u;

            AllocPlayer(pool, out _, out EntityID a);
            pool.ReleasePlayer(a);

            CollectionAssert.AreEqual(
                new[] { (1, 100u), (0, 100u) },
                _listener.Changes);
        }

        [Test]
        public void Listener_ReadsTickAtEachCall()
        {
            ZoneSlotPool pool = Create();

            _ticks.Tick = 5u;
            AllocPlayer(pool, out _, out EntityID a);
            _ticks.Tick = 9u;
            pool.ReleasePlayer(a);

            CollectionAssert.AreEqual(new[] { (1, 5u), (0, 9u) }, _listener.Changes);
        }

        [Test]
        public void Listener_FailedAllocation_UnknownRelease_SetState_AndMobCalls_AddNothing()
        {
            ZoneSlotPool pool = Create(maxPlayers: 1);
            AllocPlayer(pool, out _, out EntityID a);
            _listener.Changes.Clear();

            AllocPlayer(pool, out _, out _);                       // full: fails
            pool.ReleasePlayer(new EntityID(9999u));               // unknown
            pool.SetSessionState(a, ZoneSessionState.Dead);        // state only
            pool.TryAllocateMob(Zone, out _, out EntityID mob);    // mob alloc
            pool.ReleaseMob(mob);                                  // mob release

            Assert.AreEqual(0, _listener.Changes.Count);
        }

        [Test]
        public void Listener_ReentrantRead_SeesFinalPoolState()
        {
            int seenCount = -1;
            int seenRoster = -1;
            ZoneSlotPool pool = null;
            var reentrant = new ReentrantListener(() =>
            {
                seenCount = pool.OccupiedPlayerCount;
                seenRoster = pool.GetPlayerEntityIDsInZone(Zone).Count;
            });
            pool = new ZoneSlotPool(Zone, _ticks, reentrant);

            AllocPlayer(pool, out _, out _);

            Assert.AreEqual(1, seenCount);
            Assert.AreEqual(1, seenRoster);
        }

        private sealed class ReentrantListener : IZoneSlotPoolListener
        {
            private readonly Action _onChanged;

            public ReentrantListener(Action onChanged) => _onChanged = onChanged;

            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) => _onChanged();

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        // ----- Foreign ZoneID -------------------------------------------------------------------

        [Test]
        public void TryAllocatePlayer_ForeignZone_FailsWithOneWarningAndNoSideEffects()
        {
            ZoneSlotPool pool = Create();
            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);

            bool ok = pool.TryAllocatePlayer(new ZoneID(FOREIGN_ZONE_RAW), new CharacterID(1u), new AccountID(1u), 1u,
                out byte slot, out EntityID id);

            Assert.IsFalse(ok);
            Assert.AreEqual(EntityID.Invalid, id);
            Assert.AreEqual(0, slot);
            Assert.AreEqual(0, pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _listener.Changes.Count);
        }

        [Test]
        public void TryAllocateMob_ForeignZone_FailsWithOneWarning()
        {
            ZoneSlotPool pool = Create();
            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);

            bool ok = pool.TryAllocateMob(new ZoneID(FOREIGN_ZONE_RAW), out _, out EntityID id);

            Assert.IsFalse(ok);
            Assert.AreEqual(EntityID.Invalid, id);
            Assert.AreEqual(0, pool.OccupiedMobCount);
        }

        [Test]
        public void GetPlayerEntityIDsInZone_ForeignZone_ReturnsEmptyReadOnlyListWithOneWarning()
        {
            ZoneSlotPool pool = Create();
            MustAllocPlayer(pool);
            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);

            IReadOnlyList<EntityID> roster = pool.GetPlayerEntityIDsInZone(new ZoneID(FOREIGN_ZONE_RAW));

            Assert.AreEqual(0, roster.Count);
            Assert.AreNotSame(roster, pool.GetPlayerEntityIDsInZone(Zone));
            TestDelegate mutate = () => ((IList<EntityID>)roster).Add(new EntityID(1u));
            Assert.That(mutate, Throws.TypeOf<NotSupportedException>().Or.TypeOf<InvalidCastException>());
        }

        [Test]
        public void ForeignZone_DoesNotConsumeEntityId()
        {
            ZoneSlotPool pool = Create();
            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            pool.TryAllocateMob(new ZoneID(FOREIGN_ZONE_RAW), out _, out _);

            AllocPlayer(pool, out _, out EntityID a);

            Assert.AreEqual(1u, a.RawValue);
        }
    }
}

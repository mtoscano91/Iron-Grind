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
    /// EditMode tests for the before-release hook of <see cref="ZoneSlotPool"/> (CR-ZI-4; Status Effects
    /// Story 008, OQ-SE-2): <see cref="IZoneSlotReleaseListener"/> and <see cref="ZoneSlotPool.SetReleaseListener"/>.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_SlotReleaseListener_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int MAX_PLAYERS = 4;
        private const int MAX_MOBS = 4;
        private const uint UNKNOWN_RAW_ID = 9999u;
        private static readonly Regex LISTENER_THREW_LOG = new Regex(@"\[ZoneSlotPool\] release listener threw");

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => 1u;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public readonly List<int> Changes = new List<int>();

            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) => Changes.Add(occupied);

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class SpyReleaseListener : IZoneSlotReleaseListener
        {
            public ZoneSlotPool Pool;
            public bool Throw;
            public readonly List<EntityID> Ids = new List<EntityID>();
            public bool PlayerHeldAtCall;
            public bool MobHeldAtCall;
            public int OccupiedPlayersAtCall;
            public int OccupiedMobsAtCall;
            public int PoolListenerChangesAtCall;
            public FakePoolListener PoolListener;

            public void BeforeRelease(EntityID id)
            {
                Ids.Add(id);
                PlayerHeldAtCall = Pool.TryGetPlayerSlot(id, out _);
                IReadOnlyList<EntityID> mobs = Pool.GetMobEntityIDsInZone(new ZoneID(ZONE_RAW));
                MobHeldAtCall = false;
                for (int i = 0; i < mobs.Count; i++)
                {
                    if (mobs[i].Equals(id))
                        MobHeldAtCall = true;
                }

                OccupiedPlayersAtCall = Pool.OccupiedPlayerCount;
                OccupiedMobsAtCall = Pool.OccupiedMobCount;
                PoolListenerChangesAtCall = PoolListener.Changes.Count;
                if (Throw)
                    throw new InvalidOperationException("SpyReleaseListener: injected failure");
            }
        }

        private FakePoolListener _poolListener;
        private ZoneSlotPool _pool;
        private SpyReleaseListener _spy;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _poolListener = new FakePoolListener();
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), _poolListener, MAX_PLAYERS, MAX_MOBS);
            _spy = new SpyReleaseListener { Pool = _pool, PoolListener = _poolListener };
        }

        private EntityID AllocPlayer()
        {
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 3u, out _, out EntityID id));
            return id;
        }

        private EntityID AllocMob()
        {
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID id));
            return id;
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_NoListener_ReleasesAsBefore()
        {
            EntityID player = AllocPlayer();
            EntityID mob = AllocMob();

            Assert.IsTrue(_pool.ReleasePlayer(player));
            Assert.IsTrue(_pool.ReleaseMob(mob));

            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_ReleasePlayer_CalledBeforeSlotChanges()
        {
            _pool.SetReleaseListener(_spy);
            EntityID player = AllocPlayer();
            int changesBefore = _poolListener.Changes.Count;

            Assert.IsTrue(_pool.ReleasePlayer(player));

            Assert.AreEqual(1, _spy.Ids.Count);
            Assert.AreEqual(player, _spy.Ids[0]);
            Assert.IsTrue(_spy.PlayerHeldAtCall);
            Assert.AreEqual(1, _spy.OccupiedPlayersAtCall);
            Assert.AreEqual(changesBefore, _spy.PoolListenerChangesAtCall, "pool listener not yet notified");
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.IsFalse(_pool.TryGetPlayerSlot(player, out _));
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_ReleaseMob_CalledBeforeSlotChanges()
        {
            _pool.SetReleaseListener(_spy);
            EntityID mob = AllocMob();

            Assert.IsTrue(_pool.ReleaseMob(mob));

            Assert.AreEqual(1, _spy.Ids.Count);
            Assert.AreEqual(mob, _spy.Ids[0]);
            Assert.IsTrue(_spy.MobHeldAtCall);
            Assert.AreEqual(1, _spy.OccupiedMobsAtCall);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_UnknownOrAlreadyReleased_NoCall()
        {
            _pool.SetReleaseListener(_spy);
            EntityID player = AllocPlayer();
            EntityID mob = AllocMob();
            _pool.ReleasePlayer(player);
            _pool.ReleaseMob(mob);
            _spy.Ids.Clear();

            Assert.IsFalse(_pool.ReleasePlayer(player));
            Assert.IsFalse(_pool.ReleaseMob(mob));
            Assert.IsFalse(_pool.ReleasePlayer(new EntityID(UNKNOWN_RAW_ID)));
            Assert.IsFalse(_pool.ReleaseMob(new EntityID(UNKNOWN_RAW_ID)));

            Assert.AreEqual(0, _spy.Ids.Count);
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_PlayerListenerThrows_LogsAndReleaseCompletes()
        {
            _pool.SetReleaseListener(_spy);
            _spy.Throw = true;
            EntityID player = AllocPlayer();
            int changesBefore = _poolListener.Changes.Count;
            LogAssert.Expect(LogType.Error, LISTENER_THREW_LOG);

            bool released = _pool.ReleasePlayer(player);

            Assert.IsTrue(released);
            Assert.AreEqual(0, _pool.OccupiedPlayerCount);
            Assert.IsFalse(_pool.TryGetPlayerSlot(player, out _));
            Assert.AreEqual(changesBefore + 1, _poolListener.Changes.Count, "pool listener still notified");
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_MobListenerThrows_LogsAndReleaseCompletes()
        {
            _pool.SetReleaseListener(_spy);
            _spy.Throw = true;
            EntityID mob = AllocMob();
            LogAssert.Expect(LogType.Error, LISTENER_THREW_LOG);

            bool released = _pool.ReleaseMob(mob);

            Assert.IsTrue(released);
            Assert.AreEqual(0, _pool.OccupiedMobCount);
            Assert.AreEqual(0, _pool.GetMobEntityIDsInZone(Zone).Count);
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_SetTwice_ThrowsInvalidOperation()
        {
            _pool.SetReleaseListener(_spy);

            Assert.Throws<InvalidOperationException>(() => _pool.SetReleaseListener(new SpyReleaseListener()));
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_SetNull_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => _pool.SetReleaseListener(null));
        }

        [Test]
        public void ZoneInstancing_SlotReleaseListener_SetNullThenValid_StillRegisters()
        {
            Assert.Throws<ArgumentNullException>(() => _pool.SetReleaseListener(null));
            _pool.SetReleaseListener(_spy);
            EntityID player = AllocPlayer();

            _pool.ReleasePlayer(player);

            Assert.AreEqual(1, _spy.Ids.Count);
        }
    }
}

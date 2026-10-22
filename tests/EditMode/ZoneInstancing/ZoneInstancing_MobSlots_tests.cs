using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 003 — the mob slot interface
    /// (<see cref="ZoneMobRoster"/>; CR-ZI-13, EC-ZI-10, AC-ZI-17). Ticks are set programmatically.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_MobSlots_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint FOREIGN_ZONE_RAW = 8u;
        private static readonly Regex FOREIGN_ZONE_LOG = new Regex("foreign zone");
        private static readonly Regex EXHAUSTED_LOG = new Regex("MobSlotExhausted");
        private static readonly Regex COUNTER_LOG = new Regex("EntityIDCounterExhausted");
        private static readonly Regex UNKNOWN_LOG = new Regex("unknown or already removed");

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick;
            public uint ServerTickNumber => Tick;
        }

        private sealed class FakeListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private FakeTicks _ticks;

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks { Tick = 0u };
        }

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static ZoneID Foreign => new ZoneID(FOREIGN_ZONE_RAW);

        private ZoneSlotPool CreatePool(int maxMobs = ZoneSlotPool.MAX_MOBS_PER_ZONE, uint firstEntityId = 1u)
        {
            return new ZoneSlotPool(Zone, _ticks, new FakeListener(), 50, maxMobs, firstEntityId);
        }

        private ZoneMobRoster CreateRoster(ZoneSlotPool pool, uint interval = ZoneMobRoster.MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS)
        {
            return new ZoneMobRoster(pool, _ticks, interval);
        }

        private static EntityID Add(ZoneMobRoster roster, uint type = 1u)
        {
            return roster.TryAddMob(Zone, new MobTypeID(type), Vector3.zero);
        }

        private static void FillAll(ZoneMobRoster roster, int count, List<EntityID> ids)
        {
            for (int i = 0; i < count; i++)
            {
                EntityID id = Add(roster);
                Assert.IsFalse(id.Equals(EntityID.Invalid), "add " + i);
                ids.Add(id);
            }
        }

        // ----- Construction ---------------------------------------------------------------------

        [Test]
        public void Constructor_NullPool_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneMobRoster(null, _ticks));
        }

        [Test]
        public void Constructor_NullTicks_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneMobRoster(CreatePool(), null));
        }

        [Test]
        public void Constructor_ZeroInterval_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneMobRoster(CreatePool(), _ticks, 0u));
        }

        [Test]
        public void Constructor_DefaultInterval_Is1200()
        {
            Assert.AreEqual(1200u, ZoneMobRoster.MOB_SLOT_EXHAUSTED_LOG_INTERVAL_TICKS);
        }

        // ----- AC-ZI-17 and cap -----------------------------------------------------------------

        [Test]
        public void AcZi17_AddRemoveAdd_FollowsCorrectedSequence()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            var seen = new HashSet<EntityID>();
            var ids = new List<EntityID>();

            FillAll(roster, 150, ids);
            foreach (EntityID id in ids)
            {
                Assert.IsTrue(seen.Add(id), "ids distinct");
            }

            Assert.AreEqual(150, roster.GetMobCount(Zone));

            for (int i = 0; i < 50; i++)
            {
                roster.RemoveMob(Zone, ids[i]);
            }

            Assert.AreEqual(100, roster.GetMobCount(Zone));

            for (int i = 0; i < 50; i++)
            {
                EntityID id = Add(roster);
                Assert.IsFalse(id.Equals(EntityID.Invalid));
                Assert.IsTrue(seen.Add(id), "new id distinct from all earlier ids");
            }

            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            Assert.AreEqual(EntityID.Invalid, Add(roster));
            Assert.AreEqual(150, roster.GetMobCount(Zone));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Cap_AddAtCap_ReturnsInvalidAndCountStays()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            FillAll(roster, 150, new List<EntityID>());

            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            Assert.AreEqual(EntityID.Invalid, Add(roster));
            Assert.AreEqual(150, roster.GetMobCount(Zone));
        }

        [Test]
        public void Cap_SmallPool_ReusesFreedSlotAfterRemove()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(maxMobs: 2));
            var ids = new List<EntityID>();
            FillAll(roster, 2, ids);

            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            Assert.AreEqual(EntityID.Invalid, Add(roster));
            roster.RemoveMob(Zone, ids[0]);
            Assert.IsFalse(Add(roster).Equals(EntityID.Invalid));
            Assert.AreEqual(2, roster.GetMobCount(Zone));
        }

        // ----- Log rate -------------------------------------------------------------------------

        [Test]
        public void LogRate_EveryTickFor2401Ticks_LogsThreeTimes()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            FillAll(roster, 150, new List<EntityID>());

            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);

            for (uint t = 0u; t <= 2400u; t++)
            {
                _ticks.Tick = t;
                Assert.AreEqual(EntityID.Invalid, Add(roster));
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogRate_WrapSafe_NearUintMax()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(maxMobs: 1));
            Add(roster);

            _ticks.Tick = uint.MaxValue - 5u;
            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            Add(roster);

            // 1199 ticks later (wrapped to 1193): silent.
            _ticks.Tick = unchecked((uint.MaxValue - 5u) + 1199u);
            Add(roster);

            // Exactly 1200 ticks later: logs again.
            _ticks.Tick = unchecked((uint.MaxValue - 5u) + 1200u);
            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            Add(roster);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogRate_CustomInterval_IsHonoured()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(maxMobs: 1), 10u);
            Add(roster);

            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            LogAssert.Expect(LogType.Warning, EXHAUSTED_LOG);
            _ticks.Tick = 100u;
            Add(roster);
            _ticks.Tick = 109u;
            Add(roster);
            _ticks.Tick = 110u;
            Add(roster);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Remove ---------------------------------------------------------------------------

        [Test]
        public void Remove_UnknownId_WarnsAndCountUnchanged()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            Add(roster);

            LogAssert.Expect(LogType.Warning, UNKNOWN_LOG);
            roster.RemoveMob(Zone, new EntityID(9999u));

            Assert.AreEqual(1, roster.GetMobCount(Zone));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Remove_Twice_SecondWarnsAndCountUnchanged()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            EntityID a = Add(roster);
            Add(roster);

            roster.RemoveMob(Zone, a);
            Assert.AreEqual(1, roster.GetMobCount(Zone));

            LogAssert.Expect(LogType.Warning, UNKNOWN_LOG);
            roster.RemoveMob(Zone, a);
            Assert.AreEqual(1, roster.GetMobCount(Zone));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Stored data ----------------------------------------------------------------------

        [Test]
        public void StoredData_TryGetMob_ReturnsTypeAndPosition_ThenFalseAfterRemove()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            var type = new MobTypeID(42u);
            var pos = new Vector3(1.5f, 2.5f, -3.5f);

            EntityID id = roster.TryAddMob(Zone, type, pos);

            Assert.IsTrue(roster.TryGetMob(id, out MobTypeID gotType, out Vector3 gotPos));
            Assert.AreEqual(type, gotType);
            Assert.AreEqual(pos, gotPos);

            roster.RemoveMob(Zone, id);

            Assert.IsFalse(roster.TryGetMob(id, out MobTypeID afterType, out Vector3 afterPos));
            Assert.AreEqual(default(MobTypeID), afterType);
            Assert.AreEqual(Vector3.zero, afterPos);
        }

        [Test]
        public void StoredData_ReusedSlot_HoldsNewMobData()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(maxMobs: 1));
            EntityID first = roster.TryAddMob(Zone, new MobTypeID(1u), new Vector3(1f, 1f, 1f));
            roster.RemoveMob(Zone, first);

            EntityID second = roster.TryAddMob(Zone, new MobTypeID(2u), new Vector3(2f, 2f, 2f));

            Assert.IsFalse(roster.TryGetMob(first, out _, out _));
            Assert.IsTrue(roster.TryGetMob(second, out MobTypeID type, out Vector3 pos));
            Assert.AreEqual(new MobTypeID(2u), type);
            Assert.AreEqual(new Vector3(2f, 2f, 2f), pos);
        }

        [Test]
        public void StoredData_UnknownId_ReturnsFalseAndDefaults()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());

            Assert.IsFalse(roster.TryGetMob(new EntityID(5u), out MobTypeID type, out Vector3 pos));
            Assert.AreEqual(default(MobTypeID), type);
            Assert.AreEqual(Vector3.zero, pos);
        }

        // ----- Spent counter --------------------------------------------------------------------

        [Test]
        public void SpentCounter_SecondAddInvalidWithCounterAlertAndNoMobSlotExhausted()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(firstEntityId: uint.MaxValue));

            EntityID first = Add(roster);
            Assert.IsFalse(first.Equals(EntityID.Invalid));

            LogAssert.Expect(LogType.Error, COUNTER_LOG);
            Assert.AreEqual(EntityID.Invalid, Add(roster));

            Assert.AreEqual(1, roster.GetMobCount(Zone));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- Foreign zone ---------------------------------------------------------------------

        [Test]
        public void ForeignZone_TryAddMob_InvalidWithOnePoolWarning()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());

            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            Assert.AreEqual(EntityID.Invalid, roster.TryAddMob(Foreign, new MobTypeID(1u), Vector3.zero));

            Assert.AreEqual(0, roster.GetMobCount(Zone));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ForeignZone_TryAddMobWhenFull_NoMobSlotExhausted()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool(maxMobs: 1));
            Add(roster);

            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            Assert.AreEqual(EntityID.Invalid, roster.TryAddMob(Foreign, new MobTypeID(1u), Vector3.zero));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ForeignZone_RemoveMob_OneWarningPerCallAndNothingChanges()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            EntityID id = Add(roster);

            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            roster.RemoveMob(Foreign, id);
            roster.RemoveMob(Foreign, id);

            Assert.AreEqual(1, roster.GetMobCount(Zone));
            Assert.IsTrue(roster.TryGetMob(id, out _, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ForeignZone_GetMobCountTwice_ZeroAndWarnsOnce()
        {
            ZoneMobRoster roster = CreateRoster(CreatePool());
            Add(roster);

            LogAssert.Expect(LogType.Warning, FOREIGN_ZONE_LOG);
            Assert.AreEqual(0, roster.GetMobCount(Foreign));
            Assert.AreEqual(0, roster.GetMobCount(Foreign));
            LogAssert.NoUnexpectedReceived();
        }
    }
}

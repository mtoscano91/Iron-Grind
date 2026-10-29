using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.MovementSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.MovementSystem
{
    /// <summary>
    /// EditMode integration tests for Movement Story 008 - zone entry registration, release cleanup (AC-MOV-27,
    /// EC-MOV-13, EC-MOV-14, EC-MOV-18), the entry listener hook, the composite release listener and the snapshot
    /// position source. Real <see cref="ZoneSlotPool"/>, <see cref="ZoneSlotPoolEntityIndex"/> and
    /// <see cref="ZoneEntryCompletion"/>; fake intents, speeds, sampler, respawn provider and outbox.
    /// </summary>
    [TestFixture]
    internal sealed class MovementSystem_ZoneEntryExit_Tests
    {
        private const float DT = ServerTickLoop.FIXED_DELTA_TIME;
        private const uint ZONE_RAW = 7u;
        private const uint TICK = 100u;
        private const short FULL = 32767;

        private static readonly Vector3 TownPoint = new Vector3(12f, 0f, -4f);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private sealed class FakeTicks : IServerTickSource
        {
            public uint ServerTickNumber => TICK;
        }

        private sealed class NullPoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeIntents : IMovementIntentSource
        {
            private readonly bool[] _has = new bool[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];
            private readonly MovementIntentMessage[] _msg = new MovementIntentMessage[ZoneSlotPool.MAX_PLAYERS_PER_ZONE];

            public void Set(int slot, short dirX, short dirZ, short facing)
            {
                _has[slot] = true;
                _msg[slot] = new MovementIntentMessage(dirX, dirZ, facing, 0u);
            }

            public bool Has(int slot) { return _has[slot]; }

            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                intent = _msg[playerSlot];
                bool has = _has[playerSlot];
                _has[playerSlot] = false;
                return has;
            }
        }

        private sealed class FakeSpeeds : IMovementSpeedSource
        {
            public bool Fail;

            public bool TryGetMovementSpeed(EntityID id, out float speed)
            {
                speed = 5.0f;
                return !Fail;
            }
        }

        private sealed class FakeSampler : INavMeshSampler
        {
            public bool HitsNear = true;
            public bool HitsFar = true;
            public int FarCalls;
            public bool HasNearHit;
            public Vector3 NearHit;

            public bool TrySample(Vector3 candidate, float radius, out Vector3 hit)
            {
                hit = candidate;
                if (Mathf.Approximately(radius, MovementService.NAVMESH_RECOVERY_SEARCH_RADIUS))
                {
                    FarCalls++;
                    return HitsFar;
                }

                if (HasNearHit)
                    hit = NearHit;

                return HitsNear;
            }
        }

        private sealed class FakeSink : IMovementGeometryErrorSink
        {
            public int Calls;

            public void ReportStuck(EntityID entity, Vector3 committedPosition) { Calls++; }
        }

        private sealed class FakeRespawn : ITownRespawnPointProvider
        {
            public Vector3 Point = TownPoint;
            public ZoneID LastZone;

            public Vector3 GetTownRespawnPoint(ZoneID zone)
            {
                LastZone = zone;
                return Point;
            }
        }

        private sealed class FakeInner : IZoneEntityStateSource
        {
            public readonly Dictionary<EntityID, ZonePlayerStateView> Players = new Dictionary<EntityID, ZonePlayerStateView>();
            public bool MobResult;
            public int Calls;

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                Calls++;
                return Players.TryGetValue(id, out view);
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return MobResult;
            }
        }

        // Records the first state read, which in the entry sequence is made by the snapshot builder.
        private sealed class OrderLog
        {
            public readonly List<string> Entries = new List<string>();
        }

        private sealed class RecordingEntryListener : IZonePlayerEntryListener
        {
            private readonly OrderLog _log;
            private readonly IZonePlayerEntryListener _inner;
            public bool Throw;

            public RecordingEntryListener(OrderLog log, IZonePlayerEntryListener inner)
            {
                _log = log;
                _inner = inner;
            }

            public void OnPlayerEntering(byte slot, EntityID id)
            {
                _log.Entries.Add("enter");
                if (Throw)
                    throw new InvalidOperationException("entry listener failed");
                _inner?.OnPlayerEntering(slot, id);
            }
        }

        private sealed class RecordingState : IZoneEntityStateSource
        {
            private readonly OrderLog _log;
            private readonly IZoneEntityStateSource _inner;
            public ZonePlayerStateView FirstView;
            public bool HasFirstView;

            public RecordingState(OrderLog log, IZoneEntityStateSource inner)
            {
                _log = log;
                _inner = inner;
            }

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view)
            {
                bool result = _inner.TryGetPlayerState(id, out view);
                _log.Entries.Add("state");
                if (!HasFirstView)
                {
                    HasFirstView = true;
                    FirstView = view;
                }

                return result;
            }

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                return _inner.TryGetMobState(id, out view);
            }
        }

        private sealed class FakeMobs : IZoneMobRoster
        {
            public EntityID TryAddMob(ZoneID zone, MobTypeID type, Vector3 spawnPosition) => EntityID.Invalid;

            public void RemoveMob(ZoneID zone, EntityID id) { }

            public int GetMobCount(ZoneID zone) => 0;

            public bool TryGetMob(EntityID id, out MobTypeID type, out Vector3 spawnPosition)
            {
                type = default;
                spawnPosition = Vector3.zero;
                return false;
            }
        }

        private sealed class CountingOutbox : IClientMessageOutbox
        {
            public int Fragments;

            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
            {
                if (messageTypeId == ZoneStateSnapshotFragment.MessageTypeId)
                    Fragments++;
            }
        }

        private sealed class CountingResponder : IZoneEntryResponder
        {
            public int Ready;

            public void SendSessionReady(uint clientId) { Ready++; }

            public void SendSkillCooldownSnapshot(uint clientId, EntityID entityId) { }
        }

        private sealed class NoParty : IPartyZonePresence
        {
            public bool TryGetFormalParty(CharacterID character, out PartyID party)
            {
                party = PartyID.Uninitialized;
                return false;
            }

            public void NotifyZoneEntry(PartyID party, EntityID entityId, ZoneID zone) { }

            public void NotifyZoneExit(PartyID party, EntityID entityId) { }
        }

        private sealed class OrderedReleaseListener : IZoneSlotReleaseListener
        {
            private readonly List<string> _calls;
            private readonly string _name;
            private readonly bool _throw;

            public OrderedReleaseListener(List<string> calls, string name, bool doThrow = false)
            {
                _calls = calls;
                _name = name;
                _throw = doThrow;
            }

            public void BeforeRelease(EntityID id)
            {
                _calls.Add(_name);
                if (_throw)
                    throw new InvalidOperationException("release listener failed");
            }
        }

        private ZoneSlotPool _pool;
        private ZoneSlotPoolEntityIndex _index;
        private FakeIntents _intents;
        private FakeSpeeds _speeds;
        private FakeSampler _sampler;
        private FakeSink _sink;
        private FakeRespawn _respawn;
        private MovementService _service;
        private uint _nextCharacter;

        [SetUp]
        public void SetUp()
        {
            _nextCharacter = 1000u;
            _pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            _index = new ZoneSlotPoolEntityIndex(_pool);
            _intents = new FakeIntents();
            _sampler = new FakeSampler();
            _sink = new FakeSink();
            _respawn = new FakeRespawn();
            _speeds = new FakeSpeeds();
            _service = new MovementService(_intents, _speeds, _sampler, _index, MovementTuningValues.Defaults,
                _sink, Zone, _respawn);
            _pool.SetReleaseListener(_service);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.NoUnexpectedReceived();
        }

        private static Regex Rx(string pattern) { return new Regex(pattern); }

        private EntityID AllocPlayer(out byte slot, uint clientId = 0u)
        {
            uint n = _nextCharacter++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u),
                clientId == 0u ? n + 9000u : clientId, out slot, out EntityID id));
            return id;
        }

        // Allocates a pool slot and registers it with movement through the entry path.
        private EntityID Enter(out byte slot)
        {
            EntityID id = AllocPlayer(out slot);
            _service.OnPlayerEntering(slot, id);
            return id;
        }

        private void Tick() { _service.Run(1u, DT); }

        private void MoveRight(int slot, short facing = 0) { _intents.Set(slot, FULL, 0, facing); }

        // ----- Entry ----------------------------------------------------------------------------

        [Test]
        public void MovementService_Entry_RegistersAtTownRespawnPointFacingZero()
        {
            EntityID id = Enter(out byte slot);

            Assert.IsTrue(_service.TryGetPosition(id, out Vector3 position));
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f);
            Assert.AreEqual(TownPoint.y, position.y, 1e-4f);
            Assert.AreEqual(TownPoint.z, position.z, 1e-4f);
            Assert.IsTrue(_service.TryGetFacing(id, out short facing));
            Assert.AreEqual((short)0, facing);
            Assert.AreEqual(Zone, _respawn.LastZone, "the respawn point is asked for the own zone");
            Assert.IsFalse(_intents.Has(slot));
        }

        [Test]
        public void MovementService_Entry_NotProhibited_FirstTickMoves()
        {
            EntityID id = Enter(out byte slot);
            MoveRight(slot);

            Tick();

            _service.TryGetPosition(id, out Vector3 position);
            Assert.Greater(position.x, TownPoint.x);
        }

        [Test]
        public void MovementService_Entry_DiscardsBufferedIntent()
        {
            EntityID id = AllocPlayer(out byte slot);
            MoveRight(slot);

            _service.OnPlayerEntering(slot, id);
            Tick();

            Assert.IsFalse(_intents.Has(slot));
            _service.TryGetPosition(id, out Vector3 position);
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f, "the stale intent was not applied");
        }

        [Test]
        public void MovementService_Entry_SlotOutOfRange_LogsErrorAndRegistersNothing()
        {
            EntityID id = AllocPlayer(out _);
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*slot 50"));

            _service.OnPlayerEntering((byte)ZoneSlotPool.MAX_PLAYERS_PER_ZONE, id);

            Assert.IsFalse(_service.TryGetPosition(id, out _));
        }

        [Test]
        public void MovementService_Entry_WithoutProvider_LogsOneErrorPerCallAndRegistersNothing()
        {
            var bare = new MovementService(_intents, new FakeSpeeds(), _sampler, _index);
            EntityID id = AllocPlayer(out byte slot);
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*no respawn point provider"));
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*no respawn point provider"));

            bare.OnPlayerEntering(slot, id);
            bare.OnPlayerEntering(slot, id);

            Assert.IsFalse(bare.TryGetPosition(id, out _));
        }

        [Test]
        public void MovementService_Entry_AlreadyOccupied_WarnsOnceAndOverwrites()
        {
            EntityID first = Enter(out byte slot);
            _respawn.Point = new Vector3(3f, 0f, 3f);
            LogAssert.Expect(LogType.Warning, Rx(@"\[Movement\].*already occupied"));

            _service.OnPlayerEntering(slot, first);

            _service.TryGetPosition(first, out Vector3 position);
            Assert.AreEqual(3f, position.x, 1e-4f);
            Assert.AreEqual(3f, position.z, 1e-4f);
        }

        [Test]
        public void MovementService_Entry_UnencodableRespawnPoint_LogsErrorButRegisters()
        {
            _respawn.Point = new Vector3(100000f, 0f, 0f);
            EntityID id = AllocPlayer(out byte slot);
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*encodable"));

            _service.OnPlayerEntering(slot, id);

            Assert.IsTrue(_service.TryGetPosition(id, out _));
        }

        [Test]
        public void MovementService_EightArgConstructor_InvalidZoneOrNullProvider_Throws()
        {
            Assert.Throws<ArgumentException>(() => new MovementService(_intents, new FakeSpeeds(), _sampler, _index,
                MovementTuningValues.Defaults, _sink, ZoneID.Invalid, _respawn));
            Assert.Throws<ArgumentNullException>(() => new MovementService(_intents, new FakeSpeeds(), _sampler, _index,
                MovementTuningValues.Defaults, _sink, Zone, null));
        }

        // ----- Release (AC-MOV-27, EC-MOV-13, EC-MOV-14) ------------------------------------------

        [Test]
        public void MovementService_Release_DiscardsBufferedIntent_AndUnregisters()
        {
            EntityID id = Enter(out byte slot);
            MoveRight(slot);

            Assert.IsTrue(_pool.ReleasePlayer(id));

            Assert.IsFalse(_intents.Has(slot), "the buffered intent was discarded");
            Tick();
            Assert.IsFalse(_service.TryGetPosition(id, out _));
        }

        [Test]
        public void MovementService_ExitBeforeTick_ThenReentry_AppliesNothing()
        {
            EntityID first = Enter(out byte slot);
            MoveRight(slot);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            Assert.AreEqual(slot, slot2, "the pool reuses the freed slot (LIFO)");
            _service.OnPlayerEntering(slot2, second);
            Tick();

            _service.TryGetPosition(second, out Vector3 position);
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f, "no delta from the message that arrived before the exit");
            Assert.AreEqual(TownPoint.z, position.z, 1e-4f);
        }

        [Test]
        public void MovementService_ReentryOfSameSlot_NextTickWithoutNewMessage_AppliesNothing()
        {
            EntityID first = Enter(out byte slot);
            MoveRight(slot);
            Tick();
            MoveRight(slot);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            Assert.AreEqual(slot, slot2, "the pool reuses the freed slot (LIFO)");
            _service.OnPlayerEntering(slot2, second);
            Tick();
            Tick();

            _service.TryGetPosition(second, out Vector3 position);
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f, "ticks N and N+1 without a new message apply nothing");
        }

        [Test]
        public void MovementService_ExitWithPendingOverride_ReentryFirstTickUsesClientIntent()
        {
            EntityID first = Enter(out byte slot);
            _service.InjectFacingOverride(first, Vector3.right);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            Assert.AreEqual(slot, slot2, "the pool reuses the freed slot (LIFO)");
            _service.OnPlayerEntering(slot2, second);
            MoveRight(slot2, 450);
            Tick();

            _service.TryGetFacing(second, out short facing);
            Assert.AreEqual((short)450, facing, "the override (900) did not carry over");
        }

        [Test]
        public void MovementService_ProhibitedThenExitAndReentry_NotProhibited()
        {
            EntityID first = Enter(out byte slot);
            _service.SetProhibited(first, true);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            Assert.AreEqual(slot, slot2, "the pool reuses the freed slot (LIFO)");
            _service.OnPlayerEntering(slot2, second);
            MoveRight(slot2);
            Tick();

            _service.TryGetPosition(second, out Vector3 position);
            Assert.Greater(position.x, TownPoint.x);
        }

        [Test]
        public void MovementService_StallStateClearedByExit_MissesCountFromZeroAgain()
        {
            int threshold = MovementTuningValues.Defaults.NavMeshStallRecoveryTicks;
            EntityID first = Enter(out byte slot);
            _sampler.HitsNear = false;
            _sampler.HitsFar = false;
            for (int i = 0; i < threshold; i++)
            {
                MoveRight(slot);
                Tick();
            }

            MoveRight(slot);
            Tick();
            Assert.AreEqual(1, _sink.Calls, "stuck reported once");
            Assert.AreEqual(1, _sampler.FarCalls);
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            Assert.AreEqual(slot, slot2, "the pool reuses the freed slot (LIFO)");
            _service.OnPlayerEntering(slot2, second);
            for (int i = 0; i < threshold; i++)
            {
                MoveRight(slot2);
                Tick();
            }

            Assert.AreEqual(1, _sampler.FarCalls, "no recovery before the counter reaches the threshold again");
            MoveRight(slot2);
            Tick();
            Assert.AreEqual(2, _sampler.FarCalls, "recovery runs after threshold fresh misses");
            Assert.AreEqual(2, _sink.Calls, "the reported flag was cleared, so the stall reports again");
        }

        [Test]
        public void MovementService_Release_UnknownId_NoChangeNoLog()
        {
            EntityID id = Enter(out _);

            _service.BeforeRelease(new EntityID(999999u));

            Assert.IsTrue(_service.TryGetPosition(id, out _));
        }

        [Test]
        public void MovementService_Release_MobSlot_LeavesPlayerStateUntouched()
        {
            EntityID player = Enter(out _);
            Assert.IsTrue(_pool.TryAllocateMob(Zone, out _, out EntityID mob));

            Assert.IsTrue(_pool.ReleaseMob(mob));

            Assert.IsTrue(_service.TryGetPosition(player, out Vector3 position));
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f);
        }

        [Test]
        public void MovementService_Release_SlotNeverRegistered_NoLog()
        {
            EntityID id = AllocPlayer(out _);

            Assert.IsTrue(_pool.ReleasePlayer(id));

            Assert.IsFalse(_service.TryGetPosition(id, out _));
        }

        // ----- Entry hook on ZoneEntryCompletion ----------------------------------------------------

        private sealed class Completion
        {
            public readonly OrderLog Log = new OrderLog();
            public readonly FakeInner Inner = new FakeInner();
            public readonly CountingOutbox Outbox = new CountingOutbox();
            public readonly CountingResponder Responder = new CountingResponder();
            public readonly MovementZoneEntityStateSource Decorator;
            public readonly RecordingState State;
            public readonly ZoneEntryCompletion Entry;

            public Completion(ZoneSlotPool pool)
            {
                Decorator = new MovementZoneEntityStateSource(Inner);
                State = new RecordingState(Log, Decorator);
                var ticks = new FakeTicks();
                var snapshot = new ZoneSnapshotBuilder(Zone, pool, new FakeMobs(), State, ticks, Outbox);
                Entry = new ZoneEntryCompletion(Zone, pool, new ZoneConnectionCharacterDirectory(), snapshot, Responder,
                    new NoParty(), State, Outbox, new ZoneTickRoster(pool, ticks));
            }
        }

        private static ZonePlayerStateView InnerView(string name = "Ayla")
        {
            return new ZonePlayerStateView(5, 6, 7, 1, 2, 3, 4, 8, 90, 100, 40, 50, 7, true, 1234, 3, name, 55u);
        }

        [Test]
        public void ZoneEntryCompletion_SetEntryListener_NullThrows_SecondCallThrows()
        {
            var c = new Completion(_pool);
            Assert.Throws<ArgumentNullException>(() => c.Entry.SetEntryListener(null));

            c.Entry.SetEntryListener(_service);

            Assert.Throws<InvalidOperationException>(() => c.Entry.SetEntryListener(_service));
        }

        [Test]
        public void ZoneEntryCompletion_NoListener_EntryStepsUnchanged()
        {
            var c = new Completion(_pool);
            EntityID id = AllocPlayer(out byte slot, 11u);
            c.Inner.Players[id] = InnerView();

            c.Entry.OnPlayerAdmitted(11u, slot, id);

            Assert.GreaterOrEqual(c.Outbox.Fragments, 1);
            Assert.AreEqual(1, c.Responder.Ready);
            Assert.IsFalse(_service.TryGetPosition(id, out _), "nothing registered with movement");
            Assert.AreEqual(0, c.Log.Entries.FindAll(e => e == "enter").Count);
        }

        [Test]
        public void ZoneEntryCompletion_EntryListener_RunsBeforeSnapshot_AndSnapshotCarriesRespawnPoint()
        {
            var c = new Completion(_pool);
            c.Decorator.AttachMovement(_service);
            c.Entry.SetEntryListener(new RecordingEntryListener(c.Log, _service));
            EntityID id = AllocPlayer(out byte slot, 11u);
            c.Inner.Players[id] = InnerView();

            c.Entry.OnPlayerAdmitted(11u, slot, id);

            Assert.AreEqual("enter", c.Log.Entries[0], "listener first, then the first state read of the snapshot");
            Assert.AreEqual("state", c.Log.Entries[1]);
            Assert.IsTrue(c.State.HasFirstView);
            Assert.AreEqual((short)1200, c.State.FirstView.PosX);
            Assert.AreEqual((short)0, c.State.FirstView.PosY);
            Assert.AreEqual((short)-400, c.State.FirstView.PosZ);
            Assert.AreEqual((short)0, c.State.FirstView.FacingAngle);
            Assert.AreEqual(100, c.State.FirstView.MaxHp, "other fields are the inner source's");
            Assert.GreaterOrEqual(c.Outbox.Fragments, 1);
        }

        [Test]
        public void ZoneEntryCompletion_EntryListenerThrows_LoggedAndRemainingStepsSkipped()
        {
            var c = new Completion(_pool);
            c.Entry.SetEntryListener(new RecordingEntryListener(c.Log, null) { Throw = true });
            EntityID id = AllocPlayer(out byte slot, 11u);
            c.Inner.Players[id] = InnerView();
            LogAssert.Expect(LogType.Error, Rx(@"\[ZoneEntryCompletion\] entry of client"));

            c.Entry.OnPlayerAdmitted(11u, slot, id);

            Assert.AreEqual(0, c.Outbox.Fragments);
            Assert.AreEqual(0, c.Responder.Ready);
        }

        // ----- Composite release listener ----------------------------------------------------------

        [Test]
        public void CompositeZoneSlotReleaseListener_NullArrayOrElement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CompositeZoneSlotReleaseListener((IZoneSlotReleaseListener[])null));
            Assert.Throws<ArgumentNullException>(() => new CompositeZoneSlotReleaseListener(_service, null));
        }

        [Test]
        public void CompositeZoneSlotReleaseListener_CallsInArrayOrder()
        {
            var calls = new List<string>();
            var composite = new CompositeZoneSlotReleaseListener(
                new OrderedReleaseListener(calls, "a"), new OrderedReleaseListener(calls, "b"), _service);

            composite.BeforeRelease(new EntityID(1u));

            CollectionAssert.AreEqual(new[] { "a", "b" }, calls);
        }

        [Test]
        public void CompositeZoneSlotReleaseListener_ArrayIsCopied()
        {
            var calls = new List<string>();
            var array = new IZoneSlotReleaseListener[] { new OrderedReleaseListener(calls, "a") };
            var composite = new CompositeZoneSlotReleaseListener(array);
            array[0] = new OrderedReleaseListener(calls, "changed");

            composite.BeforeRelease(new EntityID(1u));

            CollectionAssert.AreEqual(new[] { "a" }, calls);
        }

        [Test]
        public void CompositeZoneSlotReleaseListener_FirstThrows_MovementStillCleansUp_OneError()
        {
            var calls = new List<string>();
            var fresh = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            var service = new MovementService(_intents, new FakeSpeeds(), _sampler, new ZoneSlotPoolEntityIndex(fresh),
                MovementTuningValues.Defaults, _sink, Zone, _respawn);
            fresh.SetReleaseListener(new CompositeZoneSlotReleaseListener(
                new OrderedReleaseListener(calls, "statusEffects", true), service));
            Assert.IsTrue(fresh.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 3u, out byte slot, out EntityID id));
            service.OnPlayerEntering(slot, id);
            MoveRight(slot);
            LogAssert.Expect(LogType.Error, Rx(@"\[CompositeZoneSlotReleaseListener\]"));

            Assert.IsTrue(fresh.ReleasePlayer(id));

            CollectionAssert.AreEqual(new[] { "statusEffects" }, calls);
            Assert.IsFalse(_intents.Has(slot), "movement cleaned up after the first listener threw");
            Assert.IsFalse(service.TryGetPosition(id, out _));
        }

        // ----- Snapshot position source -------------------------------------------------------------

        [Test]
        public void MovementZoneEntityStateSource_MovedPlayer_OverwritesPositionFacingAndRotation()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            source.AttachMovement(_service);
            EntityID id = AllocPlayer(out byte slot);
            _service.SeedForTesting(slot, id, new Vector3(1.234f, 0.5f, -2.5f), 900);
            inner.Players[id] = InnerView();

            Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual((short)123, view.PosX);
            Assert.AreEqual((short)50, view.PosY);
            Assert.AreEqual((short)-250, view.PosZ);
            Assert.AreEqual((short)900, view.FacingAngle);
            Assert.AreEqual(0, view.RotX);
            Assert.AreEqual(23170, view.RotY, 1);
            Assert.AreEqual(0, view.RotZ);
            Assert.AreEqual(23170, view.RotW, 1);
            Assert.AreEqual(90, view.CurrentHp);
            Assert.AreEqual(7, view.Level);
            Assert.AreEqual((ushort)1234, view.CycleTimer);
            Assert.AreEqual("Ayla", view.CharacterName);
            Assert.AreEqual(55u, view.RespawnTick);
        }

        [Test]
        public void MovementZoneEntityStateSource_InnerFalse_ReturnsFalse()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            source.AttachMovement(_service);
            EntityID id = Enter(out _);

            Assert.IsFalse(source.TryGetPlayerState(id, out _));
        }

        [Test]
        public void MovementZoneEntityStateSource_NotAttachedOrNotHeld_ReturnsInnerViewUnchanged()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            EntityID id = Enter(out _);
            inner.Players[id] = InnerView();

            Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView notAttached));
            Assert.AreEqual((short)5, notAttached.PosX);
            Assert.AreEqual((short)8, notAttached.FacingAngle);

            source.AttachMovement(_service);
            EntityID unregistered = AllocPlayer(out _);
            inner.Players[unregistered] = InnerView();
            Assert.IsTrue(source.TryGetPlayerState(unregistered, out ZonePlayerStateView notHeld));
            Assert.AreEqual((short)5, notHeld.PosX);
            Assert.AreEqual((short)3, notHeld.RotZ);
        }

        [Test]
        public void MovementZoneEntityStateSource_MobState_PassesThrough()
        {
            var inner = new FakeInner { MobResult = true };
            var source = new MovementZoneEntityStateSource(inner);

            Assert.IsTrue(source.TryGetMobState(new EntityID(1u), out _));
            inner.MobResult = false;
            Assert.IsFalse(source.TryGetMobState(new EntityID(1u), out _));
        }

        [Test]
        public void MovementZoneEntityStateSource_NullInnerOrAttach_Throws_SecondAttachThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new MovementZoneEntityStateSource(null));
            var source = new MovementZoneEntityStateSource(new FakeInner());
            Assert.Throws<ArgumentNullException>(() => source.AttachMovement(null));

            source.AttachMovement(_service);

            Assert.Throws<InvalidOperationException>(() => source.AttachMovement(_service));
        }

        // ----- Review additions ------------------------------------------------------------------------

        private sealed class ThrowingIntents : IMovementIntentSource
        {
            public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
            {
                throw new InvalidOperationException("intent source failed");
            }
        }

        [Test]
        public void MovementZoneEntityStateSource_PositionBeyondWireRange_ClampsInsteadOfWrapping()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            source.AttachMovement(_service);
            EntityID id = AllocPlayer(out byte slot);
            _service.SeedForTesting(slot, id, new Vector3(-1000f, 400f, 0f), 0);
            inner.Players[id] = InnerView();

            Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView view));

            Assert.AreEqual((short)32767, view.PosY, "Y = 400 m clamps to the wire maximum, no wrap");
            Assert.AreEqual((short)-32767, view.PosX);
        }

        [Test]
        public void MovementZoneEntityStateSource_UnencodableRespawnPoint_SnapshotClamps()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            source.AttachMovement(_service);
            _respawn.Point = new Vector3(100000f, 0f, 0f);
            EntityID id = AllocPlayer(out byte slot);
            inner.Players[id] = InnerView();
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*encodable"));

            _service.OnPlayerEntering(slot, id);

            Assert.IsTrue(source.TryGetPlayerState(id, out ZonePlayerStateView view));
            Assert.AreEqual((short)32767, view.PosX);
        }

        [Test]
        public void MovementService_OverwriteWithDifferentId_ClearsAllSlotState_AndOldReleaseIsNoOp()
        {
            int threshold = MovementTuningValues.Defaults.NavMeshStallRecoveryTicks;
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            var intents = new FakeIntents();
            var sampler = new FakeSampler { HitsNear = false, HitsFar = false };
            var service = new MovementService(intents, new FakeSpeeds(), sampler, new ZoneSlotPoolEntityIndex(pool),
                MovementTuningValues.Defaults, _sink, Zone, _respawn);
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 3u, out byte slot, out EntityID first));
            service.OnPlayerEntering(slot, first);
            for (int i = 0; i < threshold; i++)
            {
                intents.Set(slot, FULL, 0, 0);
                service.Run(1u, DT);
            }

            service.SetProhibited(first, true);
            service.InjectFacingOverride(first, Vector3.right);
            Assert.IsTrue(pool.ReleasePlayer(first), "released without the movement listener, so movement still holds the slot");
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(3u), new AccountID(4u), 5u, out byte slot2, out EntityID second));
            Assert.AreEqual(slot, slot2);
            LogAssert.Expect(LogType.Warning, Rx(@"\[Movement\].*already occupied"));

            service.OnPlayerEntering(slot2, second);

            Assert.IsFalse(service.TryGetPosition(first, out _), "old id no longer registered");
            Assert.IsTrue(service.TryGetPosition(second, out _));
            intents.Set(slot2, FULL, 0, 450);
            service.Run(1u, DT);
            service.TryGetFacing(second, out short facing);
            Assert.AreEqual((short)450, facing, "the override was cleared and the client intent used");
            for (int i = 1; i < threshold; i++)
            {
                intents.Set(slot2, FULL, 0, 450);
                service.Run(1u, DT);
            }

            Assert.AreEqual(0, sampler.FarCalls, "misses count from 0 again");
            intents.Set(slot2, FULL, 0, 450);
            service.Run(1u, DT);
            Assert.AreEqual(1, sampler.FarCalls);

            sampler.HitsNear = true;
            intents.Set(slot2, FULL, 0, 450);
            service.Run(1u, DT);
            service.TryGetPosition(second, out Vector3 position);
            Assert.Greater(position.x, TownPoint.x, "Prohibited was cleared");

            service.BeforeRelease(first);
            Assert.IsTrue(service.TryGetPosition(second, out _), "the old id's release leaves the new occupant");
        }

        [Test]
        public void MovementService_SpeedFailingAndLastValidSpeed_ResetAcrossExitAndReentry()
        {
            EntityID first = Enter(out byte slot);
            MoveRight(slot);
            Tick();
            _speeds.Fail = true;
            LogAssert.Expect(LogType.Error, Rx(@"Invalid movement speed.*using last valid speed"));
            MoveRight(slot);
            Tick();
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            _service.OnPlayerEntering(slot2, second);
            LogAssert.Expect(LogType.Error, Rx(@"Invalid movement speed.*no valid speed yet, skipping movement"));
            MoveRight(slot2);
            Tick();

            _service.TryGetPosition(second, out Vector3 position);
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f, "no last valid speed carried over");
        }

        [Test]
        public void MovementService_OverflowWarned_ResetAcrossExitAndReentry()
        {
            _sampler.HasNearHit = true;
            _sampler.NearHit = new Vector3(1000f, 0f, 0f);
            EntityID first = Enter(out byte slot);
            LogAssert.Expect(LogType.Warning, Rx(@"MovementEncodeOverflow"));
            MoveRight(slot);
            Tick();
            MoveRight(slot);
            Tick();
            _pool.ReleasePlayer(first);

            EntityID second = AllocPlayer(out byte slot2);
            _service.OnPlayerEntering(slot2, second);
            LogAssert.Expect(LogType.Warning, Rx(@"MovementEncodeOverflow"));
            MoveRight(slot2);
            Tick();
        }

        [Test]
        public void MovementZoneEntityStateSource_Rotation_Facing2700_Facing450_AreUnitLength()
        {
            var inner = new FakeInner();
            var source = new MovementZoneEntityStateSource(inner);
            source.AttachMovement(_service);
            EntityID a = AllocPlayer(out byte slotA);
            EntityID b = AllocPlayer(out byte slotB);
            _service.SeedForTesting(slotA, a, Vector3.zero, 2700);
            _service.SeedForTesting(slotB, b, Vector3.zero, 450);
            inner.Players[a] = InnerView();
            inner.Players[b] = InnerView();

            source.TryGetPlayerState(a, out ZonePlayerStateView v2700);
            source.TryGetPlayerState(b, out ZonePlayerStateView v450);

            Assert.AreEqual(23170, Math.Abs((int)v2700.RotY), 1);
            Assert.AreEqual(23170, Math.Abs((int)v2700.RotW), 1);
            Assert.Less((int)v2700.RotY * v2700.RotW, 0, "yaw 270 degrees: y and w have opposite signs");
            Assert.AreEqual(12539, v450.RotY, 1);
            Assert.AreEqual(30274, v450.RotW, 1);
            AssertUnit(v2700);
            AssertUnit(v450);
        }

        private static void AssertUnit(ZonePlayerStateView v)
        {
            double sum = (double)v.RotY * v.RotY + (double)v.RotW * v.RotW;
            Assert.AreEqual(32767.0 * 32767.0, sum, 32767.0 * 32767.0 * 0.001);
        }

        [Test]
        public void MovementService_BeforeRelease_ThrowingIntentSource_LogsOneErrorAndDoesNotThrow()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            var service = new MovementService(new ThrowingIntents(), new FakeSpeeds(), _sampler, new ZoneSlotPoolEntityIndex(pool),
                MovementTuningValues.Defaults, _sink, Zone, _respawn);
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 3u, out byte slot, out EntityID id));
            service.SeedForTesting(slot, id, TownPoint, 0);
            LogAssert.Expect(LogType.Error, Rx(@"\[Movement\].*BeforeRelease failed"));

            Assert.DoesNotThrow(() => service.BeforeRelease(id));

            Assert.IsFalse(service.TryGetPosition(id, out _), "the slot is unregistered even though the intent source failed");
        }

        [Test]
        public void ZoneEntryExit_EndToEnd_CompositeListener_EntryThenReleaseThenReentry()
        {
            var pool = new ZoneSlotPool(Zone, new FakeTicks(), new NullPoolListener());
            var intents = new FakeIntents();
            var service = new MovementService(intents, new FakeSpeeds(), _sampler, new ZoneSlotPoolEntityIndex(pool),
                MovementTuningValues.Defaults, _sink, Zone, _respawn);
            var calls = new List<string>();
            pool.SetReleaseListener(new CompositeZoneSlotReleaseListener(new OrderedReleaseListener(calls, "stub"), service));
            var c = new Completion(pool);
            c.Decorator.AttachMovement(service);
            c.Entry.SetEntryListener(service);
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(1u), new AccountID(2u), 11u, out byte slot, out EntityID first));
            c.Inner.Players[first] = InnerView();

            c.Entry.OnPlayerAdmitted(11u, slot, first);

            service.TryGetPosition(first, out Vector3 entered);
            Assert.AreEqual(TownPoint.x, entered.x, 1e-4f);
            intents.Set(slot, FULL, 0, 0);
            service.Run(1u, DT);
            service.TryGetPosition(first, out Vector3 moved);
            Assert.Greater(moved.x, TownPoint.x);

            Assert.IsTrue(pool.ReleasePlayer(first));

            CollectionAssert.AreEqual(new[] { "stub" }, calls);
            Assert.IsFalse(service.TryGetPosition(first, out _));
            Assert.IsTrue(pool.TryAllocatePlayer(Zone, new CharacterID(3u), new AccountID(4u), 12u, out byte slot2, out EntityID second));
            c.Inner.Players[second] = InnerView();
            c.Entry.OnPlayerAdmitted(12u, slot2, second);
            service.TryGetPosition(second, out Vector3 reentered);
            Assert.AreEqual(TownPoint.x, reentered.x, 1e-4f, "fresh at the respawn point, not at the old position");
            Assert.AreEqual(TownPoint.z, reentered.z, 1e-4f);
        }

        // ----- EC-MOV-15 re-entry ----------------------------------------------------------------------

        [Test]
        public void MovementService_ReentryAfterRelease_RegistersFreshAtRespawnPoint_NotAtOldPosition()
        {
            EntityID first = Enter(out byte slot);
            MoveRight(slot);
            Tick();
            _service.TryGetPosition(first, out Vector3 moved);
            Assert.Greater(moved.x, TownPoint.x);
            _pool.ReleasePlayer(first);

            EntityID second = Enter(out _);

            _service.TryGetPosition(second, out Vector3 position);
            Assert.AreEqual(TownPoint.x, position.x, 1e-4f);
        }
    }
}

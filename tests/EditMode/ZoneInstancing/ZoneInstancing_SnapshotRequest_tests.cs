using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace IronGrind.Tests.EditMode.ZoneInstancing
{
    /// <summary>
    /// EditMode unit tests for Zone Instancing Story 008 — <see cref="ZoneSnapshotRequestService"/> (CR-ZI-9, EC-ZI-6,
    /// EC-ZI-7, AC-ZI-7). Real <see cref="ClientMessageOutbox"/> with a recording send seam, real pool, lifecycle and
    /// snapshot builder. Ticks are passed programmatically; no wall-clock time.
    /// </summary>
    [TestFixture]
    internal sealed class ZoneInstancing_SnapshotRequest_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const uint OTHER_ZONE_RAW = 99u;
        private const uint CLIENT_A = 42u;
        private const uint CLIENT_B = 43u;
        private const int ENVELOPE = ServerMessageEnvelope.WireSize;
        private static readonly Regex RATE_LIMITED = new Regex("SnapshotRequestRateLimited");
        private static readonly Regex IDENTITY = new Regex("SnapshotRequestIdentityMismatch");
        private static readonly Regex MALFORMED = new Regex("SnapshotRequestMalformed");
        private const uint CLIENT_C = 44u;

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Tick;
            public uint ServerTickNumber => Tick;
        }

        private sealed class FakePoolListener : IZoneSlotPoolListener
        {
            public void OnOccupiedPlayerSlotsChanged(int occupied, uint serverTick) { }

            public void OnEntityIDCounterExhausted(uint serverTick) { }
        }

        private sealed class FakeLifecycleListener : IZoneLifecycleListener
        {
            public void OnDraining(uint serverTick) { }

            public void OnClosed(ZoneCloseTransition transition, uint serverTick) { }
        }

        private sealed class FakeStateSource : IZoneEntityStateSource
        {
            public readonly Dictionary<EntityID, ZonePlayerStateView> Players = new Dictionary<EntityID, ZonePlayerStateView>();

            public bool TryGetPlayerState(EntityID id, out ZonePlayerStateView view) => Players.TryGetValue(id, out view);

            public bool TryGetMobState(EntityID id, out ZoneMobStateView view)
            {
                view = default;
                return false;
            }
        }

        private sealed class FakeDispatcher : IInboundRequestDispatcher
        {
            public int ConnectionLevelCalls;
            public ConnectionMessageDescriptor LastDescriptor;
            public IConnectionMessageSink LastSink;

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
            {
                ConnectionLevelCalls++;
                LastDescriptor = descriptor;
                LastSink = sink;
            }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private FakeTicks _ticks;
        private FakeStateSource _source;
        private ZoneSlotPool _pool;
        private ZoneMobRoster _roster;
        private OutboxRecordingSendSeam _sink;
        private ClientMessageOutbox _outbox;
        private NetworkTestObserver _observer;
        private ZoneSnapshotBuilder _builder;
        private ZoneLifecycle _lifecycle;
        private ZoneSnapshotRequestService _svc;
        private uint _nextCharacter;
        private byte _lastSlot;

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        [SetUp]
        public void SetUp()
        {
            _ticks = new FakeTicks { Tick = 100u };
            _source = new FakeStateSource();
            _pool = new ZoneSlotPool(Zone, _ticks, new FakePoolListener());
            _roster = new ZoneMobRoster(_pool, _ticks);
            _sink = new OutboxRecordingSendSeam();
            _outbox = new ClientMessageOutbox(_sink);
            _outbox.AddClient(CLIENT_A);
            _outbox.AddClient(CLIENT_B);
            _observer = (NetworkTestObserver)NetworkingTestHarness.CreateNetworkTestObserver();
            _outbox.SetTestObserver(_observer);
            _builder = new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, _ticks, _outbox);
            _lifecycle = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            _lifecycle.MarkActive(0u);
            _svc = new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, _builder, _outbox);
            _nextCharacter = 1000u;
        }

        // ----- helpers --------------------------------------------------------------------------

        private EntityID AddPlayer(uint clientId)
        {
            uint n = _nextCharacter++;
            Assert.IsTrue(_pool.TryAllocatePlayer(Zone, new CharacterID(n), new AccountID(n + 5000u), clientId,
                out _lastSlot, out EntityID id));
            _source.Players[id] = new ZonePlayerStateView(1, 2, 3, 4, 5, 6, 7, 8, 90, 100, 40, 50, 12, true, 2500, 3, "p" + n, 0u);
            return id;
        }

        private static byte[] Body(uint zoneRaw, uint version)
        {
            var body = new byte[ZoneSnapshotRequest.BodySize];
            ZoneSnapshotRequestCodec.WriteBody(body, new ZoneSnapshotRequest(new ZoneID(zoneRaw), version));
            return body;
        }

        private void Record(ZoneSnapshotRequestService svc, uint clientId, EntityID sender, byte[] body)
        {
            svc.OnConnectionMessage(clientId, ZoneSnapshotRequest.MessageTypeId, sender, body);
        }

        private void Record(uint clientId, EntityID sender, uint version = 0u, uint zoneRaw = ZONE_RAW)
        {
            Record(_svc, clientId, sender, Body(zoneRaw, version));
        }

        private void Serve(uint tick)
        {
            _ticks.Tick = tick;
            _svc.ServePending(tick);
            _outbox.Flush(tick);
        }

        private int Fragments(uint clientId) => _observer.GetOutboundMessageCount(clientId, ZoneStateSnapshotFragment.MessageTypeId);

        private int FullResponses(uint clientId) => _observer.GetOutboundMessageCount(clientId, ZoneFullResponse.MessageTypeId);

        private ZoneStateSnapshotFragment FragmentAt(int sendIndex)
        {
            byte[] bytes = _sink.Sends[sendIndex].Bytes;
            Assert.IsTrue(ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(bytes, ENVELOPE, bytes.Length - ENVELOPE),
                out ZoneStateSnapshotFragment header, out ReadOnlySpan<byte> _));
            return header;
        }

        private void AssertZoneDoesNotExistReply(uint clientId, uint expectedZoneRaw)
        {
            Assert.AreEqual(0, Fragments(clientId), "no snapshot");
            Assert.AreEqual(1, FullResponses(clientId));
            Assert.AreEqual(1, _sink.Sends.Count);
            byte[] bytes = _sink.Sends[0].Bytes;
            Assert.IsTrue(ZoneFullResponseCodec.TryReadBody(new ReadOnlySpan<byte>(bytes, ENVELOPE, bytes.Length - ENVELOPE),
                out ZoneFullResponse reply));
            Assert.AreEqual(new ZoneID(expectedZoneRaw), reply.ZoneId);
            Assert.AreEqual(JoinRejectedReason.ZoneDoesNotExist, reply.Reason);
            Assert.AreEqual(0, reply.RetryAfterSeconds);
        }

        private ZoneSnapshotRequestService NewServiceFor(ZoneLifecycle lifecycle, int foreignCapacity = ZoneSnapshotRequestService.FOREIGN_REQUEST_CAPACITY)
        {
            return new ZoneSnapshotRequestService(Zone, _pool, lifecycle, _builder, _outbox,
                ZoneSnapshotRequestService.SNAPSHOT_REQUEST_RATE_LIMIT_TICKS, foreignCapacity);
        }

        // ----- AC-ZI-7 / EC-ZI-7 ----------------------------------------------------------------

        [Test]
        public void ServePending_RequestWithOlderVersion_SendsSnapshotCarryingServeTick()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p, version: 100u);

            Serve(160u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(160u, FragmentAt(0).SnapshotVersion);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForgedFutureVersion_IsIgnoredAndSnapshotCarriesServeTick()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p, version: 999999u);

            Serve(160u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(160u, FragmentAt(0).SnapshotVersion);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- coalescing -----------------------------------------------------------------------

        [Test]
        public void ServePending_ThreeRequestsRecordedBeforeServe_SendsOneFragmentSetAndNoWarning()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p, 1u);
            Record(CLIENT_A, p, 2u);
            Record(CLIENT_A, p, 3u);

            Serve(500u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(1, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_SecondCallWithNothingRecorded_SendsNothing()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(500u);

            Serve(900u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- rate limit -----------------------------------------------------------------------

        [Test]
        public void ServePending_RequestsInsideWindow_AreDroppedWithOneWarningAndExactWindowEdgeIsServed()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(1000u);
            Assert.AreEqual(1, Fragments(CLIENT_A));

            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(1050u);
            Assert.AreEqual(1, Fragments(CLIENT_A), "1050 dropped");

            Record(CLIENT_A, p);
            Serve(1100u);
            Assert.AreEqual(1, Fragments(CLIENT_A), "1100 dropped, no new warning");

            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Log, new Regex("SnapshotRequestRateLimited.*2 drops"));
            Serve(1200u);
            Assert.AreEqual(2, Fragments(CLIENT_A), "1200 is exactly 200 after 1000: served; drops did not restart the window");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_DropInALaterWindow_LogsAgain()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(1000u);
            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(1050u);
            Record(CLIENT_A, p);
            Serve(1200u);

            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(1300u);

            Assert.AreEqual(2, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_FirstRequestOnASlot_IsAlwaysServedEvenAtLowTick()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);

            Serve(5u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_CustomWindow_IsHonoured()
        {
            _svc = new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, _builder, _outbox, 10u);
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(100u);
            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(109u);
            Record(CLIENT_A, p);
            Serve(110u);

            Assert.AreEqual(2, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- lifecycle state / zone mismatch --------------------------------------------------

        [Test]
        public void ServePending_DrainingZone_HonoursTheRequest()
        {
            EntityID p = AddPlayer(CLIENT_A);
            _lifecycle.OnOccupiedPlayerSlotsChanged(1, 10u);
            _lifecycle.RequestQuit(20u);
            Assert.AreEqual(ZoneState.Draining, _lifecycle.State);
            Record(CLIENT_A, p);

            Serve(30u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(0, FullResponses(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ClosedZone_RepliesZoneDoesNotExistAndSendsNoSnapshot()
        {
            EntityID p = AddPlayer(CLIENT_A);
            _lifecycle.RequestFatal(20u);
            Assert.AreEqual(ZoneState.Closed, _lifecycle.State);
            Record(CLIENT_A, p);

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_A, ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForeignZoneId_RepliesZoneDoesNotExistWithTheSentZoneAndSendsNoSnapshot()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p, zoneRaw: OTHER_ZONE_RAW);

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_A, OTHER_ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForeignZoneIdFromSenderWithoutSlot_StillRepliesBeforeTheIdentityCheck()
        {
            Record(CLIENT_A, new EntityID(9999u), zoneRaw: OTHER_ZONE_RAW);

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_A, OTHER_ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ClosedZoneWithRateLimitedSlot_RepliesInsteadOfRateLimiting()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(1000u);
            _sink.Sends.Clear();
            _lifecycle.RequestFatal(1010u);
            Record(CLIENT_A, p);

            Serve(1020u);

            Assert.AreEqual(1, FullResponses(CLIENT_A));
            Assert.AreEqual(1, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- identity -------------------------------------------------------------------------

        [Test]
        public void ServePending_SenderWithNoSlot_IsDroppedWithOneWarningAndNothingSent()
        {
            Record(CLIENT_A, new EntityID(9999u));
            LogAssert.Expect(LogType.Warning, IDENTITY);

            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_SlotWithDifferentClientId_IsDroppedWithOneWarningAndNothingSent()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_B, p);
            LogAssert.Expect(LogType.Warning, IDENTITY);

            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            Assert.AreEqual(0, Fragments(CLIENT_A));
            Assert.AreEqual(0, Fragments(CLIENT_B));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ZoneStillLoading_DropsTheRequestWithOneWarningAndNothingSent()
        {
            var loading = new ZoneLifecycle(Zone, new FakeLifecycleListener(), 0u);
            ZoneSnapshotRequestService svc = NewServiceFor(loading);
            Record(svc, CLIENT_A, new EntityID(1u), Body(ZONE_RAW, 0u));
            LogAssert.Expect(LogType.Warning, IDENTITY);

            svc.ServePending(30u);
            _outbox.Flush(30u);

            Assert.AreEqual(ZoneState.Loading, loading.State);
            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_SlotReleasedBetweenRecordAndServe_IsDroppedWithOneWarning()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Assert.IsTrue(_pool.ReleasePlayer(p));
            LogAssert.Expect(LogType.Warning, IDENTITY);

            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForeignListOverflow_LogsOneAggregateIdentityWarningForTheCall()
        {
            ZoneSnapshotRequestService svc = NewServiceFor(_lifecycle, foreignCapacity: 2);
            uint[] clients = { CLIENT_A, CLIENT_B, CLIENT_C };
            for (int i = 0; i < 3; i++)
            {
                Record(svc, clients[i], new EntityID(9000u + (uint)i), Body(ZONE_RAW, 0u));
            }

            LogAssert.Expect(LogType.Warning, new Regex("SnapshotRequestIdentityMismatch.*2 drops, 1 overflow"));
            svc.ServePending(30u);

            LogAssert.NoUnexpectedReceived();
            svc.ServePending(31u);
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(0, _sink.Sends.Count);
        }

        // ----- malformed ------------------------------------------------------------------------

        [Test]
        public void OnConnectionMessage_SevenByteBody_LogsNothingInCallbackAndOneWarningAtServe()
        {
            EntityID p = AddPlayer(CLIENT_A);

            Record(_svc, CLIENT_A, p, new byte[ZoneSnapshotRequest.BodySize - 1]);
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Warning, MALFORMED);
            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnConnectionMessage_ThreeMalformedBodies_LogNothingAndServeLogsOneWarning()
        {
            EntityID p = AddPlayer(CLIENT_A);

            Record(_svc, CLIENT_A, p, new byte[3]);
            Record(_svc, CLIENT_A, p, new byte[0]);
            Record(_svc, CLIENT_B, p, new byte[7]);
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Warning, new Regex("SnapshotRequestMalformed: 3 .*client " + CLIENT_B));
            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
            Serve(31u);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnConnectionMessage_EmptyBodyFromUnknownSender_LogsOnlyMalformed()
        {
            Record(_svc, CLIENT_A, new EntityID(9999u), new byte[0]);
            LogAssert.Expect(LogType.Warning, MALFORMED);

            Serve(30u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        // ----- review fixes: identity, foreign list, last-wins, wrap ---------------------------------

        [Test]
        public void ServePending_ReleasedSlotInsideRateWindow_ReportsIdentityNotRateLimit()
        {
            EntityID a = AddPlayer(CLIENT_A);
            Record(CLIENT_A, a);
            Serve(1000u);
            _sink.Sends.Clear();
            Record(CLIENT_A, a);
            Assert.IsTrue(_pool.ReleasePlayer(a));

            LogAssert.Expect(LogType.Warning, IDENTITY);
            Serve(1010u);

            Assert.AreEqual(0, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ClosedZoneWithForeignSender_RepliesWithoutIdentityWarning()
        {
            _lifecycle.RequestFatal(20u);
            Record(CLIENT_A, new EntityID(9999u));

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_A, ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ClosedZoneWithSlotRequestFromWrongClient_RepliesWithoutIdentityWarning()
        {
            EntityID a = AddPlayer(CLIENT_A);
            _lifecycle.RequestFatal(20u);
            Record(CLIENT_B, a);

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_B, ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForgedRequestOnOwnersEntityBeforeOwner_DoesNotPoisonTheOwner()
        {
            EntityID a = AddPlayer(CLIENT_A);
            Record(CLIENT_B, a);
            Record(CLIENT_A, a);

            LogAssert.Expect(LogType.Warning, IDENTITY);
            Serve(30u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(1, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ForgedRequestOnOwnersEntityAfterOwner_DoesNotPoisonTheOwner()
        {
            EntityID a = AddPlayer(CLIENT_A);
            Record(CLIENT_A, a);
            Record(CLIENT_B, a);

            LogAssert.Expect(LogType.Warning, IDENTITY);
            Serve(30u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(1, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_ManyRequestsFromOneForeignClient_UseOneEntryAndNoOverflow()
        {
            ZoneSnapshotRequestService svc = NewServiceFor(_lifecycle, foreignCapacity: 2);
            for (int i = 0; i < 20; i++)
            {
                Record(svc, CLIENT_A, new EntityID(9000u + (uint)i), Body(ZONE_RAW, 0u));
            }

            Record(svc, CLIENT_B, new EntityID(9100u), Body(ZONE_RAW, 0u));

            LogAssert.Expect(LogType.Warning, new Regex("SnapshotRequestIdentityMismatch.*2 drops, 0 overflow"));
            svc.ServePending(30u);

            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(0, _sink.Sends.Count);
        }

        [Test]
        public void ServePending_ForeignListExactlyAtCapacity_ReportsNoOverflow()
        {
            ZoneSnapshotRequestService svc = NewServiceFor(_lifecycle, foreignCapacity: 2);
            Record(svc, CLIENT_A, new EntityID(9000u), Body(ZONE_RAW, 0u));
            Record(svc, CLIENT_B, new EntityID(9001u), Body(ZONE_RAW, 0u));

            LogAssert.Expect(LogType.Warning, new Regex("SnapshotRequestIdentityMismatch.*0 overflow"));
            svc.ServePending(30u);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_StateChangedBetweenRecordAndServe_SnapshotReflectsServeTimeState()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            _source.Players[p] = new ZonePlayerStateView(111, 2, 3, 4, 5, 6, 7, 8, 77, 100, 40, 50, 12, true, 2500, 3, "changed", 0u);

            Serve(30u);

            Assert.AreEqual(1, _sink.Sends.Count);
            byte[] bytes = _sink.Sends[0].Bytes;
            Assert.IsTrue(ZoneStateSnapshotFragmentCodec.TryReadBody(new ReadOnlySpan<byte>(bytes, ENVELOPE, bytes.Length - ENVELOPE),
                out ZoneStateSnapshotFragment _, out ReadOnlySpan<byte> payload));
            Assert.IsTrue(EntityStateCodec.TryReadBody(payload, out EntityState state, out int _));
            Assert.AreEqual(111, state.PosX);
            Assert.AreEqual(77, state.CurrentHp);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_WrongZoneThenRightZoneOnSameSlot_ServesSnapshot()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p, zoneRaw: OTHER_ZONE_RAW);
            Record(CLIENT_A, p);

            Serve(30u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(0, FullResponses(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_RightZoneThenWrongZoneOnSameSlot_RepliesOnly()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Record(CLIENT_A, p, zoneRaw: OTHER_ZONE_RAW);

            Serve(30u);

            AssertZoneDoesNotExistReply(CLIENT_A, OTHER_ZONE_RAW);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_TickWrapsPastUintMax_RateLimitsByModularDifference()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(0xFFFFFFF0u);
            Assert.AreEqual(1, Fragments(CLIENT_A));

            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(0x10u);
            Assert.AreEqual(1, Fragments(CLIENT_A), "difference 32 is inside the window");

            Record(CLIENT_A, p);
            Serve(0xD8u);
            Assert.AreEqual(2, Fragments(CLIENT_A), "difference 232 is outside the window");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_TwoPlayersRecorded_EachGetsOwnFragmentSet()
        {
            EntityID a = AddPlayer(CLIENT_A);
            EntityID b = AddPlayer(CLIENT_B);
            Record(CLIENT_A, a);
            Record(CLIENT_B, b);

            Serve(30u);

            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.AreEqual(1, Fragments(CLIENT_B));
            Assert.AreEqual(2, _sink.Sends.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_SingleDropThenServe_LogsNoSummary()
        {
            EntityID p = AddPlayer(CLIENT_A);
            Record(CLIENT_A, p);
            Serve(1000u);
            Record(CLIENT_A, p);
            LogAssert.Expect(LogType.Warning, RATE_LIMITED);
            Serve(1050u);
            Record(CLIENT_A, p);

            Serve(1200u);

            Assert.AreEqual(2, Fragments(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- slot reuse -----------------------------------------------------------------------

        [Test]
        public void ServePending_NewOccupantOfAReusedSlot_StartsFreshAndIsServedInsideTheOldWindow()
        {
            EntityID a = AddPlayer(CLIENT_A);
            byte slotOfA = _lastSlot;
            Record(CLIENT_A, a);
            Serve(1000u);
            Assert.AreEqual(1, Fragments(CLIENT_A));
            Assert.IsTrue(_pool.ReleasePlayer(a));

            EntityID b = AddPlayer(CLIENT_B);
            Assert.AreEqual(slotOfA, _lastSlot, "B must reuse A's slot");
            Assert.AreNotEqual(a, b);
            Record(CLIENT_B, b);
            Serve(1010u);

            Assert.AreEqual(1, Fragments(CLIENT_B));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ServePending_PendingRequestOfThePreviousOccupant_DoesNotCarryOver()
        {
            EntityID a = AddPlayer(CLIENT_A);
            byte slotOfA = _lastSlot;
            Record(CLIENT_A, a);
            Assert.IsTrue(_pool.ReleasePlayer(a));
            EntityID b = AddPlayer(CLIENT_B);
            Assert.AreEqual(slotOfA, _lastSlot);

            // A's request is still pending against A, who no longer holds the slot: identity drop, not B's snapshot.
            LogAssert.Expect(LogType.Warning, IDENTITY);
            Serve(1010u);

            Assert.AreEqual(0, _sink.Sends.Count);
            Assert.AreEqual(0, Fragments(CLIENT_B));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- record only ----------------------------------------------------------------------

        [Test]
        public void OnConnectionMessage_Alone_EnqueuesNothing()
        {
            EntityID p = AddPlayer(CLIENT_A);

            Record(CLIENT_A, p);
            Record(CLIENT_A, new EntityID(9999u), zoneRaw: OTHER_ZONE_RAW);
            _outbox.Flush(500u);

            Assert.AreEqual(0, _sink.Sends.Count);
            Assert.AreEqual(0, Fragments(CLIENT_A));
            Assert.AreEqual(0, FullResponses(CLIENT_A));
            LogAssert.NoUnexpectedReceived();
        }

        // ----- registration ---------------------------------------------------------------------

        [Test]
        public void Descriptor_Fields_MatchTheZoneSnapshotRequestMessage()
        {
            Assert.AreEqual(ZoneSnapshotRequest.MessageTypeId, ZoneSnapshotRequestService.Descriptor.MessageTypeId);
            Assert.AreEqual(8, ZoneSnapshotRequestService.Descriptor.MaxBodyBytes);
            Assert.IsTrue(ZoneSnapshotRequestService.Descriptor.CarriesSenderEntityId);
        }

        [Test]
        public void Register_Dispatcher_RegistersTheDescriptorWithTheServiceAsSink()
        {
            var dispatcher = new FakeDispatcher();

            ZoneSnapshotRequestService.Register(dispatcher, _svc);

            Assert.AreEqual(1, dispatcher.ConnectionLevelCalls);
            Assert.AreEqual(ZoneSnapshotRequest.MessageTypeId, dispatcher.LastDescriptor.MessageTypeId);
            Assert.AreEqual(8, dispatcher.LastDescriptor.MaxBodyBytes);
            Assert.IsTrue(dispatcher.LastDescriptor.CarriesSenderEntityId);
            Assert.AreSame(_svc, dispatcher.LastSink);
        }

        [Test]
        public void Register_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ZoneSnapshotRequestService.Register(null, _svc));
            Assert.Throws<ArgumentNullException>(() => ZoneSnapshotRequestService.Register(new FakeDispatcher(), null));
        }

        // ----- construction ---------------------------------------------------------------------

        [Test]
        public void Constructor_NullArgumentsInvalidZoneWindowOrCapacity_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotRequestService(Zone, null, _lifecycle, _builder, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotRequestService(Zone, _pool, null, _builder, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, null, _outbox));
            Assert.Throws<ArgumentNullException>(() => new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, _builder, null));
            Assert.Throws<ArgumentException>(() => new ZoneSnapshotRequestService(ZoneID.Invalid, _pool, _lifecycle, _builder, _outbox));
            Assert.Throws<ArgumentException>(() => new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, _builder, _outbox, 0u));
            Assert.Throws<ArgumentException>(() => new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, _builder, _outbox, 200u, 0));
        }

        [Test]
        public void Constants_Defaults_MatchTheStory()
        {
            Assert.AreEqual(200u, ZoneSnapshotRequestService.SNAPSHOT_REQUEST_RATE_LIMIT_TICKS);
            Assert.AreEqual(16, ZoneSnapshotRequestService.FOREIGN_REQUEST_CAPACITY);
        }

        // ----- allocation -----------------------------------------------------------------------

        [Test]
        public void ServePending_SteadyRecordAndServe_DoesNotAllocate()
        {
            EntityID p = AddPlayer(CLIENT_A);
            var counting = new OutboxCountingSendSeam();
            var outbox = new ClientMessageOutbox(counting);
            outbox.AddClient(CLIENT_A);
            var builder = new ZoneSnapshotBuilder(Zone, _pool, _roster, _source, _ticks, outbox);
            var svc = new ZoneSnapshotRequestService(Zone, _pool, _lifecycle, builder, outbox);
            byte[] body = Body(ZONE_RAW, 5u);
            _ticks.Tick = 1000u;
            svc.OnConnectionMessage(CLIENT_A, ZoneSnapshotRequest.MessageTypeId, p, body);
            svc.ServePending(1000u);
            outbox.Flush(1000u);
            counting.Count = 0;
            _ticks.Tick = 1200u;

            Assert.That(() =>
            {
                svc.OnConnectionMessage(CLIENT_A, ZoneSnapshotRequest.MessageTypeId, p, body);
                svc.ServePending(1200u);
                outbox.Flush(1200u);
            }, Is.Not.AllocatingGCMemory(), "the steady record + serve path allocated");

            Assert.AreEqual(1, counting.Count);
            LogAssert.NoUnexpectedReceived();
        }
    }
}

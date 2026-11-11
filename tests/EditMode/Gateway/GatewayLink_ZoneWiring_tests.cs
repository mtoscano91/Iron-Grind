using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using IronGrind.Tests.EditMode.CharacterPersistence;
using IronGrind.Tests.EditMode.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;
using PendingPurchaseRecord = IronGrind.CharacterPersistence.PendingPurchaseRecord;

namespace IronGrind.Tests.EditMode.Gateway
{
    /// <summary>
    /// Gateway Story 011: the zone side of the link is wired into the zone (AC-ZI-32 and AC-ZI-35 send sides, S10
    /// registration, tick-start drain, PlayerCount / ZoneDraining, teardown step 2a). A fake link channel stands in for
    /// the socket thread; the zone is a real <see cref="ZoneInstancingWiring"/> with fake persistence. Ticks are
    /// advanced by hand; nothing here sleeps or opens a socket.
    /// </summary>
    [TestFixture]
    internal sealed class GatewayLink_ZoneWiring_Tests
    {
        private const uint ZONE_RAW = 7u;
        private const int HZ = 20;
        private const int CONNECTING_SECONDS = 5;
        private const int CONNECTING_TICKS = CONNECTING_SECONDS * HZ;
        private const int CAPACITY = 50;
        private const int FRAME_CAP = 65536;
        private const int LINK_PORT = 5555;
        private const ushort UDP_PORT = 7777;
        private const byte TEMPLATE = 3;
        private const uint ACCOUNT_BASE = 1000u;
        private const uint CHARACTER_BASE = 2000u;
        private const uint CLIENT_BASE = 3000u;
        private static readonly TimeSpan TEARDOWN_DRAIN_TIMEOUT = TimeSpan.FromMilliseconds(200);

        private static ZoneID Zone => new ZoneID(ZONE_RAW);

        private static byte[] Token(byte seed)
        {
            var token = new byte[SessionTokenStore.TOKEN_LENGTH_BYTES];
            for (int i = 0; i < token.Length; i++)
            {
                token[i] = (byte)(seed + i + 1);
            }

            return token;
        }

        // ----- fakes ----------------------------------------------------------------------------

        private sealed class FakeChannel : IGatewayLinkChannel
        {
            private readonly List<string> _log;

            public FakeChannel(List<string> log)
            {
                _log = log;
            }

            public int StartCalls;
            public int StopCalls;
            public bool ThrowOnStart;
            public bool ThrowOnNextEnqueue;
            public IPEndPoint Endpoint;
            public Func<RegisterZoneMessage> Registration;
            public readonly List<byte[]> Frames = new List<byte[]>();
            public readonly Queue<GatewayToZoneMessage> Inbox = new Queue<GatewayToZoneMessage>();

            public int LinkUpCount { get; set; }

            public bool IsConnected { get; set; }

            public void Start(IPEndPoint endpoint, Func<RegisterZoneMessage> registration)
            {
                StartCalls++;
                if (ThrowOnStart)
                {
                    throw new InvalidOperationException("link start failed");
                }

                Endpoint = endpoint;
                Registration = registration;
            }

            // Mirrors the real client: frames enqueued while the link is down are dropped.
            public void Enqueue(byte[] frame)
            {
                if (ThrowOnNextEnqueue)
                {
                    ThrowOnNextEnqueue = false;
                    throw new InvalidOperationException("link enqueue failed");
                }

                if (IsConnected)
                {
                    Frames.Add(frame);
                }
            }

            public bool TryDequeue(out GatewayToZoneMessage message)
            {
                if (Inbox.Count > 0)
                {
                    message = Inbox.Dequeue();
                    return true;
                }

                message = default;
                return false;
            }

            public void Stop()
            {
                StopCalls++;
                _log?.Add("LinkStop");
            }
        }

        private sealed class FakeTicks : IServerTickSource
        {
            public uint Now;

            public uint ServerTickNumber => Now;
        }

        private sealed class FakeClientOutbox : IClientMessageOutbox
        {
            public void Enqueue(uint clientId, ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt) { }
        }

        private sealed class FakeDispatcher : IInboundRequestDispatcher
        {
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }

            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }

            public void Seal() { }

            public bool AddConnection(uint clientId) => true;

            public void RemoveConnection(uint clientId) { }

            public void DispatchTick(uint currentTick) { }

            public int HeldCount(CharacterID charId) => 0;
        }

        private sealed class FakeDisconnector : IInboundTransportDisconnector
        {
            public void DisconnectClient(ulong transportClientId) { }
        }

        private sealed class FakeApplier : ILoadedRecordApplier
        {
            public CharacterLoadCode ApplyLoadedRecord(EntityID entityId, CharacterRecord record) => CharacterLoadCode.Success;
        }

        private sealed class FakePersistence : ICharacterPersistence, ICharacterSessionRelease
        {
            public Task<CharacterLoadResult> LoadCharacter(CharacterID id, AccountID ownerAccountID, CancellationToken ct) =>
                new TaskCompletionSource<CharacterLoadResult>().Task;

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

            public Task<CharacterSaveResult> SaveSession(CharacterID id, SessionEndReason reason, float hpOverride, CancellationToken ct) =>
                Task.FromResult(CharacterSaveResult.Success);

            public bool ReleaseSession(CharacterID id, out EntityID entityId)
            {
                entityId = EntityID.Invalid;
                return false;
            }

            public Task<CharacterSaveResult> SaveIrreversibleOutcome(CharacterID id, IrreversibleOutcomeTrigger trigger, CancellationToken ct) => throw new NotSupportedException();

            public Task CreateStub(AccountID accountId, CharacterID characterId, byte classType, CancellationToken ct) => throw new NotSupportedException();

            public Task<PendingPurchaseBeginResult> BeginPurchase(CharacterID charId, PendingPurchaseRecord record, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> CompletePurchase(long purchaseId, CharacterID charId, CancellationToken ct) => throw new NotSupportedException();

            public Task<CharacterSaveResult> RefundPurchase(long purchaseId, CharacterID charId, uint goldBalance, uint goldVersion, CancellationToken ct) => throw new NotSupportedException();

            public Task<IReadOnlyList<PendingPurchaseRecord>> LoadOutstandingPurchases(CharacterID charId, CancellationToken ct) => throw new NotSupportedException();
        }

        // ----- fixture state ----------------------------------------------------------------------

        private List<string> _log;
        private FakeChannel _channel;
        private GatewayLinkOutbox _linkOutbox;
        private GatewayLinkPump _pump;
        private FakeTicks _ticks;
        private FakePersistence _persistence;
        private TickCompletionQueue _queue;
        private ZoneInstancingWiring _wiring;

        [SetUp]
        public void SetUp()
        {
            _log = new List<string>();
            _channel = new FakeChannel(_log) { IsConnected = true, LinkUpCount = 1 };
            _linkOutbox = new GatewayLinkOutbox(_channel);
            _ticks = new FakeTicks();
            _persistence = new FakePersistence();
            _queue = new TickCompletionQueue();
            _wiring = null;
            _pump = null;
        }

        [TearDown]
        public void TearDown()
        {
            _queue.DrainOnShutdown(TEARDOWN_DRAIN_TIMEOUT);
        }

        private void Build()
        {
            var options = new ZoneInstancingWiringOptions
            {
                TickRateHz = HZ,
                ConnectingTimeoutSeconds = CONNECTING_SECONDS,
                MaxPlayers = CAPACITY,
                GatewayOutbox = _linkOutbox,
                Equipment = new FakeEquipmentService(),
            };
            _wiring = new ZoneInstancingWiring(Zone, _ticks, _queue, new FakeDispatcher(), new FakeClientOutbox(), _persistence,
                _persistence, new FakeApplier(), new CharacterMutationGate(), new RecordingInventory(), new FakeDisconnector(),
                _ => { }, options);
            _pump = new GatewayLinkPump(_channel, _linkOutbox, _wiring);
            _ticks.Now = 0u;
            _wiring.MarkActive(Zone);
        }

        // Builds the zone and consumes the PlayerCount the first LinkUp triggers, so a test starts with no frames.
        private void BuildSettled()
        {
            Build();
            _pump.Run(0u, 0f);
            _channel.Frames.Clear();
        }

        private void Occupy(uint account, uint character, uint client)
        {
            Assert.IsTrue(_wiring.Pool.TryAllocatePlayer(Zone, new CharacterID(character), new AccountID(account), client, out _, out _));
        }

        private void OccupyMany(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Occupy(ACCOUNT_BASE + (uint)i, CHARACTER_BASE + (uint)i, CLIENT_BASE + (uint)i);
            }
        }

        private void PushAdmit(uint requestId, uint account, uint character)
        {
            _channel.Inbox.Enqueue(Inbound(GatewayLinkCodec.Encode(
                new AdmitMessage(requestId, new AccountID(account), new CharacterID(character), Token((byte)requestId)))));
        }

        // One full tick as the pipeline runs it: step 1b drain, phase 3 (zone wiring then the pump).
        private void RunTick(uint tick)
        {
            _ticks.Now = tick;
            _pump.Drain(tick);
            _wiring.Run(tick, 0f);
            _pump.Run(tick, 0f);
        }

        private static GatewayToZoneMessage Inbound(byte[] frame)
        {
            Assert.AreEqual(GatewayLinkFrameStatus.Frame,
                GatewayLinkCodec.TryReadFrame(frame, FRAME_CAP, out byte type, out ReadOnlySpan<byte> body, out int consumed));
            Assert.AreEqual(frame.Length, consumed);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message));
            return message;
        }

        private static ZoneToGatewayMessage Decode(byte[] frame)
        {
            Assert.AreEqual(GatewayLinkFrameStatus.Frame,
                GatewayLinkCodec.TryReadFrame(frame, FRAME_CAP, out byte type, out ReadOnlySpan<byte> body, out int consumed));
            Assert.AreEqual(frame.Length, consumed);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message));
            return message;
        }

        private static string Describe(ZoneToGatewayMessage m)
        {
            switch (m.Type)
            {
                case GatewayLinkMessageType.PlayerCount:
                    return "PlayerCount:" + m.PlayerCount.Count;
                case GatewayLinkMessageType.ZoneDraining:
                    return "ZoneDraining";
                case GatewayLinkMessageType.SessionReleased:
                    return "SessionReleased:" + m.SessionReleased.AccountId.RawValue;
                case GatewayLinkMessageType.AdmitResult:
                    return "AdmitResult:" + m.AdmitResult.RequestId + ":" + m.AdmitResult.Reason;
                case GatewayLinkMessageType.EvictResult:
                    return "EvictResult:" + m.EvictResult.RequestId;
                case GatewayLinkMessageType.ReconnectResult:
                    return "ReconnectResult:" + m.ReconnectResult.RequestId + ":" + m.ReconnectResult.Accepted;
                default:
                    return m.Type.ToString();
            }
        }

        private string[] SentFrames()
        {
            var result = new string[_channel.Frames.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Describe(Decode(_channel.Frames[i]));
            }

            return result;
        }

        private static uint[] Raw(AccountID[] accounts)
        {
            var result = new uint[accounts.Length];
            for (int i = 0; i < accounts.Length; i++)
            {
                result[i] = accounts[i].RawValue;
            }

            return result;
        }

        // ----- AC-1: S10 registration -------------------------------------------------------------

        [Test]
        public void test_gateway_link_registration_starts_channel_once_with_a_callback_listing_occupied_and_admitted_accounts()
        {
            BuildSettled();
            Occupy(11u, 21u, 31u);
            _wiring.GatewayAdmit.Handle(1u, new AccountID(12u), new CharacterID(22u), Token(1), 0u);
            var registration = new GatewayLinkRegistration(_channel, _pump, LINK_PORT, () => TEMPLATE);

            registration.RegisterZone(Zone, UDP_PORT);

            Assert.AreEqual(1, _channel.StartCalls);
            Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, LINK_PORT), _channel.Endpoint);
            // The callback runs on the link thread in production: run it on another thread here too.
            RegisterZoneMessage message = Task.Run(_channel.Registration).Result;
            Assert.AreEqual(Zone, message.ZoneId);
            Assert.AreEqual(TEMPLATE, message.TemplateId);
            Assert.AreEqual(UDP_PORT, message.UdpPort);
            Assert.AreEqual((ushort)CAPACITY, message.Capacity);
            CollectionAssert.AreEquivalent(new uint[] { 11u, 12u }, Raw(message.LiveAccounts));
        }

        [Test]
        public void test_gateway_link_registration_callback_reflects_a_later_change_without_altering_the_earlier_snapshot()
        {
            BuildSettled();
            Occupy(11u, 21u, 31u);
            new GatewayLinkRegistration(_channel, _pump, LINK_PORT, () => TEMPLATE).RegisterZone(Zone, UDP_PORT);
            RegisterZoneMessage first = _channel.Registration();

            _wiring.GatewayAdmit.Handle(1u, new AccountID(13u), new CharacterID(23u), Token(2), 1u);
            RunTick(1u);
            RegisterZoneMessage second = _channel.Registration();

            CollectionAssert.AreEquivalent(new uint[] { 11u }, Raw(first.LiveAccounts));
            CollectionAssert.AreEquivalent(new uint[] { 11u, 13u }, Raw(second.LiveAccounts));
            Assert.AreSame(second.LiveAccounts, _channel.Registration().LiveAccounts, "No change: the same published array is reused.");
        }

        // ----- AC-2: outbox encodes onto the channel ----------------------------------------------

        [Test]
        public void test_gateway_link_outbox_encodes_admit_result_session_released_and_evict_result_frames()
        {
            _linkOutbox.EnqueueAdmitResult(7u, true, AdmitRejectReason.None);
            _linkOutbox.EnqueueAdmitResult(8u, false, AdmitRejectReason.Full);
            _linkOutbox.EnqueueSessionReleased(new AccountID(42u));
            _linkOutbox.EnqueueEvictResult(9u);

            CollectionAssert.AreEqual(
                new[] { "AdmitResult:7:None", "AdmitResult:8:Full", "SessionReleased:42", "EvictResult:9" }, SentFrames());
        }

        [Test]
        public void test_gateway_link_outbox_encodes_accepted_reconnect_result_with_its_token()
        {
            byte[] token = Token(5);

            _linkOutbox.EnqueueReconnectResult(4u, true, token);

            ZoneToGatewayMessage message = Decode(_channel.Frames[0]);
            Assert.IsTrue(message.ReconnectResult.Accepted);
            CollectionAssert.AreEqual(token, message.ReconnectResult.NewToken);
        }

        [Test]
        public void test_gateway_link_outbox_encodes_rejected_reconnect_result_with_sixteen_zero_bytes_and_player_count_and_zone_draining()
        {
            _linkOutbox.EnqueueReconnectResult(4u, false, null);
            _linkOutbox.EnqueuePlayerCount(49);
            _linkOutbox.EnqueueZoneDraining();

            ZoneToGatewayMessage rejected = Decode(_channel.Frames[0]);
            Assert.IsFalse(rejected.ReconnectResult.Accepted);
            CollectionAssert.AreEqual(new byte[GatewayLinkCodec.TOKEN_LENGTH], rejected.ReconnectResult.NewToken);
            Assert.AreEqual("PlayerCount:49", Describe(Decode(_channel.Frames[1])));
            Assert.AreEqual("ZoneDraining", Describe(Decode(_channel.Frames[2])));
        }

        // ----- AC-3: tick-start drain -------------------------------------------------------------

        [Test]
        public void test_gateway_link_drain_dispatches_admit_evict_and_reconnect_once_each_and_enqueues_replies()
        {
            BuildSettled();
            _wiring.GatewayAdmit.Handle(1u, new AccountID(102u), new CharacterID(202u), Token(1), 0u);
            RunTick(0u);
            _channel.Frames.Clear();
            PushAdmit(2u, 101u, 201u);
            _channel.Inbox.Enqueue(Inbound(GatewayLinkCodec.Encode(new EvictMessage(3u, new AccountID(102u)))));
            _channel.Inbox.Enqueue(Inbound(GatewayLinkCodec.Encode(new ReconnectMessage(4u, new AccountID(999u), Token(4)))));

            _ticks.Now = 7u;
            _pump.Drain(7u);

            CollectionAssert.AreEqual(new[] { "AdmitResult:2:None", "EvictResult:3", "ReconnectResult:4:False" }, SentFrames());
            Assert.IsTrue(_wiring.Admitted.IsReserved(new AccountID(101u)));
            Assert.IsFalse(_wiring.Admitted.IsReserved(new AccountID(102u)));
            Assert.AreEqual(0, _channel.Inbox.Count);
        }

        [Test]
        public void test_gateway_link_drain_passes_the_current_tick_to_the_handler_and_ignores_an_unexpected_message()
        {
            BuildSettled();
            // A zone-to-gateway type inside the inbox is not a valid request: ignored, and the Admit behind it still runs.
            _channel.Inbox.Enqueue(new GatewayToZoneMessage(GatewayLinkMessageType.PlayerCount, default, default, default));
            PushAdmit(1u, 101u, 201u);

            _pump.Drain(7u);

            CollectionAssert.AreEqual(new[] { "AdmitResult:1:None" }, SentFrames());
            // The admit tick is 7: the entry survives tick 106 and times out at 7 + CONNECTING_TICKS.
            _ticks.Now = 7u + CONNECTING_TICKS - 1u;
            _wiring.Run(_ticks.Now, 0f);
            Assert.IsTrue(_wiring.Admitted.IsReserved(new AccountID(101u)));
            _ticks.Now = 7u + CONNECTING_TICKS;
            _wiring.Run(_ticks.Now, 0f);
            Assert.IsFalse(_wiring.Admitted.IsReserved(new AccountID(101u)));
        }

        // ----- AC-4: PlayerCount and ZoneDraining -------------------------------------------------

        [Test]
        public void test_gateway_link_player_count_is_sent_on_change_at_most_once_per_tick()
        {
            BuildSettled();
            Occupy(11u, 21u, 31u);
            Occupy(12u, 22u, 32u);

            RunTick(1u);
            RunTick(2u);

            CollectionAssert.AreEqual(new[] { "PlayerCount:2" }, SentFrames());
        }

        [Test]
        public void test_gateway_link_player_count_is_not_recorded_as_sent_while_the_link_is_down_and_is_sent_after_re_registration()
        {
            BuildSettled();
            _channel.IsConnected = false;
            Occupy(11u, 21u, 31u);
            RunTick(1u);
            Assert.AreEqual(0, _channel.Frames.Count);

            _channel.IsConnected = true;
            _channel.LinkUpCount = 2;
            RunTick(2u);

            CollectionAssert.AreEqual(new[] { "PlayerCount:1" }, SentFrames());
        }

        [Test]
        public void test_gateway_link_player_count_is_resent_once_right_after_each_link_up_even_when_unchanged()
        {
            BuildSettled();

            _channel.LinkUpCount = 2;
            RunTick(1u);
            RunTick(2u);

            CollectionAssert.AreEqual(new[] { "PlayerCount:0" }, SentFrames());
        }

        [Test]
        public void test_gateway_link_re_registration_of_a_zone_that_is_not_active_sends_zone_draining_before_player_count()
        {
            BuildSettled();
            Occupy(11u, 21u, 31u); // keeps the zone Draining instead of closing at once
            _wiring.RequestQuit();
            _ticks.Now = 5u;
            _wiring.Run(5u, 0f);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);
            _channel.Frames.Clear();

            _channel.LinkUpCount = 2;
            RunTick(6u);

            CollectionAssert.AreEqual(new[] { "ZoneDraining", "PlayerCount:1" }, SentFrames());
        }

        // ----- AC-5 (AC-ZI-32): capacity reserved by admitted entries ------------------------------

        [Test]
        public void test_ac_zi_32_three_admits_with_two_places_free_accept_two_reject_the_third_full_and_send_player_count_fifty()
        {
            BuildSettled();
            OccupyMany(48);
            RunTick(0u);
            _channel.Frames.Clear();
            PushAdmit(1u, 101u, 201u);
            PushAdmit(2u, 102u, 202u);
            PushAdmit(3u, 103u, 203u);

            RunTick(1u);

            CollectionAssert.AreEqual(
                new[] { "AdmitResult:1:None", "AdmitResult:2:None", "AdmitResult:3:Full", "PlayerCount:50" }, SentFrames());
        }

        [Test]
        public void test_ac_zi_32_a_timed_out_admitted_entry_sends_player_count_forty_nine_and_a_new_admit_is_accepted()
        {
            BuildSettled();
            OccupyMany(48);
            PushAdmit(1u, 101u, 201u);
            RunTick(1u);
            PushAdmit(2u, 102u, 202u);
            RunTick(5u);
            PushAdmit(3u, 103u, 203u);
            RunTick(6u);
            _channel.Frames.Clear();

            RunTick(1u + CONNECTING_TICKS); // the first entry times out; the second (tick 5) does not

            CollectionAssert.AreEqual(new[] { "SessionReleased:101", "PlayerCount:49" }, SentFrames());
            _channel.Frames.Clear();
            PushAdmit(4u, 104u, 204u);

            RunTick(2u + CONNECTING_TICKS);

            CollectionAssert.AreEqual(new[] { "AdmitResult:4:None", "PlayerCount:50" }, SentFrames());
        }

        [Test]
        public void test_ac_zi_32_a_handshake_that_turns_an_admitted_entry_into_a_slot_sends_no_player_count()
        {
            // Conversion is driven through the two calls the handshake path makes (slot allocation, then
            // AdmittedEntries.OnSlotAllocated), not through the full SessionHandshake load, which Story 012 owns.
            BuildSettled();
            OccupyMany(48);
            PushAdmit(1u, 101u, 201u);
            RunTick(1u);
            _channel.Frames.Clear();

            Occupy(101u, 201u, CLIENT_BASE + 100u);
            _wiring.Admitted.OnSlotAllocated(new CharacterID(201u), 2u);
            RunTick(2u);

            Assert.AreEqual(0, _channel.Frames.Count);
            Assert.AreEqual(49, _wiring.SessionCount);
        }

        // ----- AC-6 (AC-ZI-35): leaving Active -----------------------------------------------------

        [Test]
        public void test_ac_zi_35_leaving_active_sends_zone_draining_then_session_released_then_player_count_without_the_entry()
        {
            BuildSettled();
            Occupy(50u, 250u, 350u);
            _wiring.GatewayAdmit.Handle(1u, new AccountID(101u), new CharacterID(201u), Token(1), 2u);
            RunTick(2u);
            _channel.Frames.Clear();

            _wiring.RequestQuit();
            RunTick(10u);

            CollectionAssert.AreEqual(new[] { "ZoneDraining", "SessionReleased:101", "PlayerCount:1" }, SentFrames());
        }

        [Test]
        public void test_ac_zi_35_a_further_admit_after_the_zone_left_active_is_rejected_not_active()
        {
            BuildSettled();
            Occupy(50u, 250u, 350u);
            _wiring.RequestQuit();
            RunTick(10u);
            _channel.Frames.Clear();
            PushAdmit(2u, 102u, 202u);

            RunTick(11u);

            CollectionAssert.AreEqual(new[] { "AdmitResult:2:NotActive" }, SentFrames());
        }

        [Test]
        public void test_ac_zi_35_an_active_to_closed_transition_that_skips_draining_sends_zone_draining_once()
        {
            BuildSettled();

            _wiring.RequestFatal(3u);

            CollectionAssert.AreEqual(new[] { "ZoneDraining" }, SentFrames());
        }

        // ----- Code-review additions: drain isolation and ZoneDraining once -----------------------------

        [Test]
        public void test_gateway_link_drain_a_handler_that_throws_logs_an_error_and_the_next_message_is_still_handled()
        {
            BuildSettled();
            // The first AdmitResult enqueue throws inside the Admit handler; the second Admit must still be answered.
            _channel.ThrowOnNextEnqueue = true;
            PushAdmit(1u, 101u, 201u);
            PushAdmit(2u, 102u, 202u);
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("GatewayLinkPump.*handling failed"));

            _pump.Drain(3u);

            CollectionAssert.AreEqual(new[] { "AdmitResult:2:None" }, SentFrames());
            Assert.AreEqual(0, _channel.Inbox.Count);
            Assert.IsTrue(_wiring.Admitted.IsReserved(new AccountID(102u)));
        }

        [Test]
        public void test_ac_zi_35_draining_then_closed_sends_zone_draining_exactly_once_in_total()
        {
            BuildSettled();
            Occupy(50u, 250u, 350u); // keeps the zone Draining instead of closing at once
            _wiring.RequestQuit();
            RunTick(5u);
            Assert.AreEqual(ZoneState.Draining, _wiring.Lifecycle.State);

            _wiring.RequestFatal(6u);
            RunTick(7u);

            Assert.AreEqual(ZoneState.Closed, _wiring.Lifecycle.State);
            int draining = 0;
            foreach (string sent in SentFrames())
            {
                if (sent == "ZoneDraining")
                {
                    draining++;
                }
            }

            Assert.AreEqual(1, draining);
        }

        // ----- Composition-level: S5 builds the link, S10 registers it, step 1b drains it ----------------

        private const string LINK_CONFIG = "{\"Host\":\"db.invalid\",\"Port\":5432,\"Database\":\"d\",\"Username\":\"u\","
            + "\"DbWarmupTimeoutMs\":2000,\"NgoShutdownWaitFrames\":2,\"GatewayZoneLinkPort\":6123}";

        private sealed class ThrowingRegistration : IGatewayRegistration
        {
            public int Calls;

            public void RegisterZone(ZoneID zone, ushort port)
            {
                Calls++;
                throw new InvalidOperationException("registration failed");
            }
        }

        // The real backend over fakes (like WiRig) with a fake link channel and, optionally, an injected registration.
        private sealed class LinkRig
        {
            public readonly List<string> Log = new List<string>();
            public readonly SuPlan Plan = new SuPlan();
            public readonly WiNetwork Net;
            public readonly FakeChannel Channel;
            public readonly ZoneServerComposition Comp;
            public int FactoryCalls;

            public LinkRig(IGatewayRegistration registration)
            {
                Net = new WiNetwork(Log);
                Channel = new FakeChannel(Log) { IsConnected = true, LinkUpCount = 1 };
                var env = new WiEnvironment();
                env.Variables["IRONGRIND_PG_PASSWORD"] = WiRig.PASSWORD;
                Comp = ZoneServerComposition.CreateWithRealBackend(
                    new SuApp(Log, Plan), Net, new SuNav(Log, Plan), new ZsClose(Log), new SuScene(Log, Plan),
                    new SuBounds(Log, Plan), new SuStatic(Log, Plan), new SuStateOwner(Log), registration,
                    new SuConfigSource(Log, Plan) { Json = LINK_CONFIG }, env, () => 0d);
                Comp.ItemDatabaseOverride = WiRig.EmptyItemDatabase();
                Comp.WarmUpOverride = ct => Task.CompletedTask;
                Comp.ListenerFactoryOverride = (connectionString, receiver) => new WiListener(Log);
                Comp.GatewayLinkChannelFactoryOverride = config =>
                {
                    FactoryCalls++;
                    return Channel;
                };
            }

            public void RunAll()
            {
                SuRig.Drive(Comp.Start(SuRig.ARGS));
                if (Comp.TryBeginTeardown())
                {
                    SuRig.Drive(Comp.RunTeardown());
                }
            }
        }

        [Test]
        public void test_gateway_link_composition_s10_without_an_injected_registration_starts_the_fake_channel_once_with_the_zone_values()
        {
            var rig = new LinkRig(null);

            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));

            Assert.AreEqual(1, rig.FactoryCalls);
            Assert.AreSame(rig.Channel, rig.Comp.LinkChannel);
            Assert.AreEqual(1, rig.Channel.StartCalls);
            Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, 6123), rig.Channel.Endpoint);
            RegisterZoneMessage message = Task.Run(rig.Channel.Registration).Result;
            Assert.AreEqual(Zone, message.ZoneId);
            Assert.AreEqual(UDP_PORT, message.UdpPort);
            Assert.AreEqual((ushort)rig.Comp.ZoneInstancing.Pool.MaxPlayers, message.Capacity);
            // The rig holds no ZoneDefinitionSlot (the host's), so the template falls back to 0.
            Assert.AreEqual((byte)0, message.TemplateId);
            Assert.AreEqual(0, message.LiveAccounts.Length);
            Assert.IsFalse(rig.Comp.TryBeginTeardown(), "A successful startup requests no teardown.");
        }

        [Test]
        public void test_gateway_link_composition_an_admit_in_the_fake_inbox_is_answered_by_one_real_pipeline_tick()
        {
            var rig = new LinkRig(null);
            SuRig.Drive(rig.Comp.Start(SuRig.ARGS));
            rig.Channel.Inbox.Enqueue(Inbound(GatewayLinkCodec.Encode(
                new AdmitMessage(5u, new AccountID(101u), new CharacterID(201u), Token(5)))));

            rig.Net.FireTick();

            Assert.GreaterOrEqual(rig.Channel.Frames.Count, 1);
            Assert.AreEqual("AdmitResult:5:None", Describe(Decode(rig.Channel.Frames[0])));
            Assert.AreEqual(0, rig.Channel.Inbox.Count);
            Assert.IsTrue(rig.Comp.ZoneInstancing.Admitted.IsReserved(new AccountID(101u)));
        }

        [Test]
        public void test_gateway_link_composition_an_injected_registration_wins_and_the_link_is_not_started()
        {
            var registration = new ThrowingRegistration();
            var rig = new LinkRig(registration);
            LogAssert.Expect(UnityEngine.LogType.Error, "[ZoneServer] startup failed at S10: InvalidOperationException");

            rig.RunAll();

            Assert.AreEqual(1, registration.Calls);
            Assert.AreEqual(0, rig.Channel.StartCalls);
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, rig.Channel.StopCalls, "Teardown step 2a still stops the link built at S5.");
        }

        [Test]
        public void test_gateway_link_composition_a_channel_start_that_throws_is_s_fail_at_s10_and_quits_1()
        {
            var rig = new LinkRig(null);
            rig.Channel.ThrowOnStart = true;
            LogAssert.Expect(UnityEngine.LogType.Error, "[ZoneServer] startup failed at S10: InvalidOperationException");

            rig.RunAll();

            Assert.AreEqual(1, rig.Channel.StartCalls);
            Assert.AreEqual("Quit:1", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, rig.Log.FindAll(s => s == "Quit:1").Count);
            Assert.AreEqual(1, rig.Channel.StopCalls);
        }

        // ----- AC-7: teardown step 2a --------------------------------------------------------------

        private static void Drive(IEnumerator routine)
        {
            int yields = 0;
            while (routine.MoveNext())
            {
                Assert.Less(++yields, 100000, "Runaway coroutine.");
            }
        }

        [Test]
        public void test_gateway_link_teardown_step_2a_stops_the_link_right_after_the_relay_listener_and_before_navigation()
        {
            var rig = new ZsRig();
            var channel = new FakeChannel(rig.Log);
            rig.Comp.AttachLinkChannel(channel);

            Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(1, channel.StopCalls);
            int listener = rig.Log.IndexOf("ListenerStop");
            Assert.GreaterOrEqual(listener, 0);
            Assert.AreEqual(listener + 1, rig.Log.IndexOf("LinkStop"));
            Assert.AreEqual(listener + 2, rig.Log.IndexOf("NavTeardown"));
        }

        [Test]
        public void test_gateway_link_on_destroy_path_teardown_now_stops_the_link_once()
        {
            var rig = new ZsRig();
            var channel = new FakeChannel(rig.Log);
            rig.Comp.AttachLinkChannel(channel);

            rig.Comp.TeardownNow();

            Assert.AreEqual(1, channel.StopCalls);
        }

        [Test]
        public void test_gateway_link_is_stopped_even_when_the_relay_listener_stop_throws()
        {
            var rig = new ZsRig();
            var channel = new FakeChannel(rig.Log);
            rig.Comp.AttachLinkChannel(channel);
            rig.Listener.Throw = true;
            LogAssert.Expect(UnityEngine.LogType.Error, "[ZoneServer] teardown step 2a failed: InvalidOperationException");

            Drive(rig.Comp.RunTeardown());

            Assert.AreEqual(1, channel.StopCalls);
        }
    }
}

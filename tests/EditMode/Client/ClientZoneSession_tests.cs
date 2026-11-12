using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Client.Session;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Client.Session
{
    /// <summary>
    /// EditMode tests for Authentication Story 018 (AC-1 to AC-8): the pure C# client zone entry, driven with a fake
    /// transport, fake login service and fake clock.
    /// </summary>
    [TestFixture]
    internal sealed class ClientZoneSession_Tests
    {
        private const string GATEWAY_URL = "https://127.0.0.1:8443";
        private const string OVERRIDE_HOST = "10.0.0.5";
        private const string USERNAME = "alice";
        private const string PASSWORD = "hunter22";
        private const uint CHARACTER_ID = 7u;
        private const uint ZONE_ID = 3u;
        private const uint NEW_ZONE_ID = 4u;
        private const ushort ZONE_PORT = 7777;
        private const ushort NEW_ZONE_PORT = 7778;
        private const uint SNAPSHOT_VERSION = 100u;
        private const int ENVELOPE = ServerMessageEnvelope.WireSize;
        private const int PAYLOAD_LEN = 16;
        private const int LONG_FRAGMENT_COUNT = 33;
        private const int HEARTBEAT_INTERVAL = 3;
        private const double REASSEMBLY_TIMEOUT = 10.0;
        private const double CONNECT_TIMEOUT = 10.0;
        private const ushort UNKNOWN_MESSAGE_ID = 0x1234;

        private static readonly byte[] TokenA = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        private static readonly byte[] TokenB = { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116 };

        private sealed class FakeTransport : IZoneClientTransport
        {
            public IZoneClientTransportListener Listener;
            public readonly List<string> Starts = new List<string>();
            public readonly List<byte[]> Sent = new List<byte[]>();
            public int StopCount;

            public void SetListener(IZoneClientTransportListener listener) { Listener = listener; }
            public void Start(string host, ushort port) { Starts.Add(host + ":" + port); }
            public void Stop() { StopCount++; }
            public void Send(ReadOnlySpan<byte> message) { Sent.Add(message.ToArray()); }
        }

        private sealed class LoginCall
        {
            public string User;
            public string Password;
            public byte[] Token;
        }

        private sealed class FakeLogin : IZoneLoginService
        {
            public readonly List<LoginCall> Calls = new List<LoginCall>();
            private readonly Queue<Action<LoginResponse>> _pending = new Queue<Action<LoginResponse>>();

            public void Login(string username, string password, byte[] token, Action<LoginResponse> onDone)
            {
                Calls.Add(new LoginCall { User = username, Password = password, Token = token == null ? null : (byte[])token.Clone() });
                _pending.Enqueue(onDone);
            }

            public void Complete(LoginResponse response) { _pending.Dequeue()(response); }
        }

        private sealed class FakeClock : IClientSessionClock
        {
            public double Now { get; set; }
        }

        private FakeTransport _transport;
        private FakeLogin _login;
        private FakeClock _clock;
        private List<string> _logs;
        private List<ZoneEntryFailureReason> _failures;
        private int _endedCount;
        private int _enteredCount;
        private ClientZoneSessionConfig _config;
        private ClientZoneSession _session;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _login = new FakeLogin();
            _clock = new FakeClock();
            _logs = new List<string>();
            _failures = new List<ZoneEntryFailureReason>();
            _endedCount = 0;
            _enteredCount = 0;
            _config = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL };
            _session = NewSession(_config);
        }

        private ClientZoneSession NewSession(ClientZoneSessionConfig config)
        {
            var session = new ClientZoneSession(config, _login, _transport, _clock, line => _logs.Add(line));
            session.EntryFailed += reason => _failures.Add(reason);
            session.SessionEnded += () => _endedCount++;
            session.Entered += () => _enteredCount++;
            return session;
        }

        // ---------- helpers ----------

        private static LoginResponse Ok(byte[] token, uint zone, ushort port)
        {
            return LoginResponse.ForSuccess((byte[])token.Clone(), new CharacterID(CHARACTER_ID), new ZoneID(zone), port);
        }

        private void LoginOk()
        {
            _login.Complete(Ok(TokenA, ZONE_ID, ZONE_PORT));
        }

        private void BeginAndConnect()
        {
            _session.Begin(USERNAME, PASSWORD);
            LoginOk();
            _transport.Listener.OnConnected();
        }

        private static byte[] Fragment(uint version, ushort total, ushort index, uint tick = 0u)
        {
            var message = new byte[ENVELOPE + ZoneStateSnapshotFragment.HeaderSize + PAYLOAD_LEN];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(ZoneStateSnapshotFragment.MessageTypeId, 1u, tick));
            var payload = new byte[PAYLOAD_LEN];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(index + 1);
            }

            ZoneStateSnapshotFragmentCodec.WriteBody(
                message.AsSpan(ENVELOPE), new ZoneStateSnapshotFragment(version, total, index), payload);
            return message;
        }

        private static byte[] ZoneFull()
        {
            var message = new byte[ENVELOPE + ZoneFullResponse.BodySize];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(ZoneFullResponse.MessageTypeId, 1u, 0u));
            ZoneFullResponseCodec.WriteBody(
                message.AsSpan(ENVELOPE), new ZoneFullResponse(new ZoneID(ZONE_ID), JoinRejectedReason.ZoneFull, 5));
            return message;
        }

        private static byte[] ZoneSessionEndedMessage()
        {
            var message = new byte[ENVELOPE + ZoneSessionEnded.BodySize];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(ZoneSessionEnded.MessageTypeId, 1u, 0u));
            BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(ENVELOPE), ZONE_ID);
            message[ENVELOPE + 4] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(ENVELOPE + 5), 30);
            return message;
        }

        private void Feed(byte[] message)
        {
            _transport.Listener.OnMessageReceived(message);
        }

        private void FeedFullSnapshot(uint version)
        {
            Feed(Fragment(version, 1, 0));
        }

        private void EnterZone()
        {
            BeginAndConnect();
            FeedFullSnapshot(SNAPSHOT_VERSION);
        }

        private static ushort IdOf(byte[] message)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(message);
        }

        private List<byte[]> SentOfType(ushort id)
        {
            return _transport.Sent.Where(m => IdOf(m) == id).ToList();
        }

        private static int CountTokenPathLogins(FakeLogin login)
        {
            return login.Calls.Count(c => c.Token != null && c.Password == "");
        }

        private static SnapshotFragmentResult Accept(SnapshotFragmentCollector collector, uint version, ushort total, ushort index)
        {
            var header = new ZoneStateSnapshotFragment(version, total, index);
            var payload = new byte[PAYLOAD_LEN];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(index + 1);
            }

            return collector.Accept(header, payload);
        }

        // ---------- snapshot assembled event (Story 017) ----------

        private static byte[] FragmentWithPayload(uint version, ushort total, ushort index, byte[] payload)
        {
            var message = new byte[ENVELOPE + ZoneStateSnapshotFragment.HeaderSize + payload.Length];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(ZoneStateSnapshotFragment.MessageTypeId, 1u, 0u));
            ZoneStateSnapshotFragmentCodec.WriteBody(
                message.AsSpan(ENVELOPE), new ZoneStateSnapshotFragment(version, total, index), payload);
            return message;
        }

        private static byte[] PatternBytes(int length, byte seed)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }

            return bytes;
        }

        [Test]
        public void test_snapshot_assembled_raised_once_per_completed_snapshot()
        {
            int raised = 0;
            _session.SnapshotAssembled += (buffer, length) => raised++;
            BeginAndConnect();

            FeedFullSnapshot(SNAPSHOT_VERSION);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(1, _enteredCount);
        }

        [Test]
        public void test_snapshot_assembled_not_raised_for_partial_snapshot()
        {
            int raised = 0;
            _session.SnapshotAssembled += (buffer, length) => raised++;
            BeginAndConnect();

            Feed(Fragment(SNAPSHOT_VERSION, 2, 0));

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void test_snapshot_assembled_raised_before_ack_is_transmitted()
        {
            int acksAtCallback = -1;
            int sentAtCallback = -1;
            _session.SnapshotAssembled += (buffer, length) =>
            {
                acksAtCallback = SentOfType(ZoneEntryAck.MessageTypeId).Count;
                sentAtCallback = _transport.Sent.Count;
            };
            BeginAndConnect();
            int sentBefore = _transport.Sent.Count;

            FeedFullSnapshot(SNAPSHOT_VERSION);

            Assert.AreEqual(0, acksAtCallback);
            Assert.AreEqual(sentBefore, sentAtCallback);
            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_snapshot_assembled_multi_fragment_bytes_and_length_match_payloads()
        {
            int tailLength = 10;
            byte[] first = PatternBytes(ZoneStateSnapshotFragment.MaxPayloadBytes, 1);
            byte[] second = PatternBytes(tailLength, 77);
            byte[] received = null;
            int receivedLength = -1;
            _session.SnapshotAssembled += (buffer, length) =>
            {
                receivedLength = length;
                received = new byte[length];
                Array.Copy(buffer, received, length);
            };
            BeginAndConnect();

            Feed(FragmentWithPayload(SNAPSHOT_VERSION, 2, 0, first));
            Feed(FragmentWithPayload(SNAPSHOT_VERSION, 2, 1, second));

            Assert.AreEqual(first.Length + tailLength, receivedLength);
            CollectionAssert.AreEqual(first.Concat(second).ToArray(), received);
        }

        [Test]
        public void test_snapshot_assembled_empty_single_fragment_has_length_zero()
        {
            int receivedLength = -1;
            _session.SnapshotAssembled += (buffer, length) => receivedLength = length;
            BeginAndConnect();

            Feed(FragmentWithPayload(SNAPSHOT_VERSION, 1, 0, new byte[0]));

            Assert.AreEqual(0, receivedLength);
            Assert.AreEqual(1, _enteredCount);
        }

        [Test]
        public void test_snapshot_assembled_throwing_subscriber_still_acks_and_enters()
        {
            _session.SnapshotAssembled += (buffer, length) => throw new InvalidOperationException("boom");
            BeginAndConnect();

            FeedFullSnapshot(SNAPSHOT_VERSION);

            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
            Assert.AreEqual(ClientZoneSessionState.Entered, _session.State);
            Assert.AreEqual(1, _enteredCount);
            Assert.IsTrue(_logs.Any(l => l.Contains("InvalidOperationException")));
        }

        [Test]
        public void test_collector_completed_length_is_full_fragments_plus_last_and_reset_clears_it()
        {
            var collector = new SnapshotFragmentCollector(4);
            collector.Accept(new ZoneStateSnapshotFragment(SNAPSHOT_VERSION, 2, 0), PatternBytes(ZoneStateSnapshotFragment.MaxPayloadBytes, 1));
            Assert.AreEqual(0, collector.CompletedLength);

            SnapshotFragmentResult result = collector.Accept(new ZoneStateSnapshotFragment(SNAPSHOT_VERSION, 2, 1), PatternBytes(10, 5));

            Assert.AreEqual(SnapshotFragmentResult.Completed, result);
            Assert.AreEqual(ZoneStateSnapshotFragment.MaxPayloadBytes + 10, collector.CompletedLength);
            collector.Reset();
            Assert.AreEqual(0, collector.CompletedLength);
        }

        // ---------- config ----------

        [Test]
        public void test_session_config_defaults_are_valid()
        {
            Assert.DoesNotThrow(() => new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL }.Validate());
            Assert.AreEqual(HeartbeatActivityTracker.HEARTBEAT_INTERVAL_SECONDS, _config.HeartbeatIntervalSeconds);
        }

        [Test]
        public void test_session_config_interval_five_is_rejected()
        {
            var config = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, HeartbeatIntervalSeconds = 5 };
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void test_session_config_interval_zero_is_rejected()
        {
            var config = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, HeartbeatIntervalSeconds = 0 };
            Assert.Throws<InvalidOperationException>(() => config.Validate());
        }

        [Test]
        public void test_session_config_interval_four_is_accepted()
        {
            var config = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, HeartbeatIntervalSeconds = 4 };
            Assert.DoesNotThrow(() => config.Validate());
        }

        [Test]
        public void test_session_config_bad_timeouts_and_fragment_cap_are_rejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, FragmentReassemblyTimeoutSeconds = 0 }.Validate());
            Assert.Throws<InvalidOperationException>(() =>
                new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, HeartbeatTimeoutSeconds = 0 }.Validate());
            Assert.Throws<InvalidOperationException>(() =>
                new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, MaxClientSnapshotFragments = 0 }.Validate());
        }

        [Test]
        public void test_session_constructor_rejects_invalid_config()
        {
            var bad = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, HeartbeatIntervalSeconds = 5 };
            Assert.Throws<InvalidOperationException>(() => NewSession(bad));
        }

        // ---------- collector (AC-2) ----------

        [Test]
        public void test_collector_in_order_fragments_complete_with_version()
        {
            var collector = new SnapshotFragmentCollector(32);
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 9u, 3, 0));
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 9u, 3, 1));
            Assert.AreEqual(SnapshotFragmentResult.Completed, Accept(collector, 9u, 3, 2));
            Assert.IsTrue(collector.IsComplete);
            Assert.AreEqual(9u, collector.CompletedVersion);
            Assert.AreEqual(3 * ZoneStateSnapshotFragment.MaxPayloadBytes, collector.Buffer.Length);
            Assert.AreEqual(2, collector.Buffer[ZoneStateSnapshotFragment.MaxPayloadBytes]);
            Assert.AreEqual(3, collector.Buffer[2 * ZoneStateSnapshotFragment.MaxPayloadBytes]);
        }

        [Test]
        public void test_collector_out_of_order_fragments_complete_once_index_zero_is_in()
        {
            var collector = new SnapshotFragmentCollector(32);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 3, 2));
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 9u, 3, 0));
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 9u, 3, 2));
            Assert.AreEqual(SnapshotFragmentResult.Completed, Accept(collector, 9u, 3, 1));
        }

        [Test]
        public void test_collector_duplicate_fragment_is_ignored()
        {
            var collector = new SnapshotFragmentCollector(32);
            Accept(collector, 9u, 3, 0);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 3, 0));
            Assert.AreEqual(1, collector.ReceivedCount);
        }

        [Test]
        public void test_collector_fragment_before_index_zero_is_dropped()
        {
            var collector = new SnapshotFragmentCollector(32);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 2, 1));
            Assert.IsNull(collector.Buffer);
        }

        [Test]
        public void test_collector_older_version_fragment_is_dropped()
        {
            var collector = new SnapshotFragmentCollector(32);
            Accept(collector, 10u, 2, 0);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 2, 1));
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 2, 0));
            Assert.AreEqual(1, collector.ReceivedCount);
        }

        [Test]
        public void test_collector_newer_version_index_zero_restarts()
        {
            var collector = new SnapshotFragmentCollector(32);
            Accept(collector, 10u, 3, 0);
            Accept(collector, 10u, 3, 1);
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 11u, 2, 0));
            Assert.AreEqual(1, collector.ReceivedCount);
            Assert.AreEqual(SnapshotFragmentResult.Completed, Accept(collector, 11u, 2, 1));
            Assert.AreEqual(11u, collector.CompletedVersion);
        }

        [Test]
        public void test_collector_newer_version_non_zero_index_is_dropped()
        {
            var collector = new SnapshotFragmentCollector(32);
            Accept(collector, 10u, 3, 0);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 11u, 3, 1));
            Assert.AreEqual(1, collector.ReceivedCount);
        }

        [Test]
        public void test_collector_mismatched_total_is_dropped()
        {
            var collector = new SnapshotFragmentCollector(32);
            Accept(collector, 10u, 3, 0);
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 10u, 4, 1));
        }

        [Test]
        public void test_collector_thirty_three_fragments_is_too_large_and_thirty_two_is_not()
        {
            var collector = new SnapshotFragmentCollector(32);
            Assert.AreEqual(SnapshotFragmentResult.TooLarge, Accept(collector, 9u, LONG_FRAGMENT_COUNT, 0));
            Assert.AreEqual(SnapshotFragmentResult.Accepted, Accept(collector, 9u, 32, 0));
        }

        [Test]
        public void test_collector_fragment_after_completion_is_ignored_until_reset()
        {
            var collector = new SnapshotFragmentCollector(32);
            Assert.AreEqual(SnapshotFragmentResult.Completed, Accept(collector, 9u, 1, 0));
            Assert.AreEqual(SnapshotFragmentResult.Ignored, Accept(collector, 9u, 1, 0));
            collector.Reset();
            Assert.AreEqual(SnapshotFragmentResult.Completed, Accept(collector, 10u, 1, 0));
        }

        // ---------- AC-1 entry sequence ----------

        [Test]
        public void test_session_login_ok_starts_transport_on_gateway_host_and_port()
        {
            _session.Begin(USERNAME, PASSWORD);
            Assert.AreEqual(ClientZoneSessionState.LoggingIn, _session.State);
            Assert.AreEqual(PASSWORD, _login.Calls[0].Password);
            Assert.IsNull(_login.Calls[0].Token);

            LoginOk();

            Assert.AreEqual(ClientZoneSessionState.Connecting, _session.State);
            CollectionAssert.AreEqual(new[] { "127.0.0.1:7777" }, _transport.Starts);
            Assert.IsTrue(_logs.Contains("login ok zone=3 port=7777"));
        }

        [Test]
        public void test_session_zone_host_override_wins_over_gateway_host()
        {
            _config.ZoneHostOverride = OVERRIDE_HOST;
            _session = NewSession(_config);

            _session.Begin(USERNAME, PASSWORD);
            LoginOk();

            CollectionAssert.AreEqual(new[] { "10.0.0.5:7777" }, _transport.Starts);
        }

        [Test]
        public void test_session_connected_sends_exactly_one_handshake_with_matching_body()
        {
            BeginAndConnect();

            Assert.AreEqual(ClientZoneSessionState.AwaitingEntry, _session.State);
            Assert.AreEqual(1, _transport.Sent.Count);
            byte[] sent = _transport.Sent[0];
            Assert.AreEqual(ENVELOPE + SessionHandshake.BodySize, sent.Length);
            Assert.IsTrue(MessageEnvelopeCodec.TryRead(sent, out ServerMessageEnvelope envelope));
            Assert.AreEqual(SessionHandshake.MessageTypeId, envelope.MessageTypeId);
            Assert.AreEqual(1u, envelope.SequenceNumber);
            Assert.AreEqual(0u, envelope.ServerTickNumber);
            var expected = new byte[SessionHandshake.BodySize];
            SessionHandshakeCodec.WriteBody(
                expected, new SessionHandshake(new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), TokenA));
            CollectionAssert.AreEqual(expected, sent.Skip(ENVELOPE).ToArray());
            Assert.IsTrue(_logs.Contains("handshake sent"));
        }

        [Test]
        public void test_session_failed_initial_login_reports_login_failed_once_and_ends()
        {
            _session.Begin(USERNAME, PASSWORD);
            _login.Complete(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials));

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.LoginFailed }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.AreEqual(0, _transport.Starts.Count);
        }

        // ---------- AC-2 snapshot and ack ----------

        [Test]
        public void test_session_all_fragments_send_one_ack_with_zone_and_version()
        {
            BeginAndConnect();

            Feed(Fragment(SNAPSHOT_VERSION, 2, 0));
            Assert.AreEqual(0, SentOfType(ZoneEntryAck.MessageTypeId).Count);
            Feed(Fragment(SNAPSHOT_VERSION, 2, 1));

            List<byte[]> acks = SentOfType(ZoneEntryAck.MessageTypeId);
            Assert.AreEqual(1, acks.Count);
            Assert.IsTrue(ZoneEntryAckCodec.TryReadBody(acks[0].AsSpan(ENVELOPE), out ZoneEntryAck ack));
            Assert.AreEqual(ZONE_ID, ack.ZoneId.RawValue);
            Assert.AreEqual(SNAPSHOT_VERSION, ack.SnapshotVersion);
            Assert.AreEqual(ClientZoneSessionState.Entered, _session.State);
            Assert.AreEqual(1, _enteredCount);
            Assert.IsTrue(_logs.Contains("snapshot v=100"));
            Assert.IsTrue(_logs.Contains("ack sent"));
        }

        [Test]
        public void test_session_repeated_completion_sends_no_second_ack()
        {
            BeginAndConnect();
            FeedFullSnapshot(SNAPSHOT_VERSION);
            FeedFullSnapshot(SNAPSHOT_VERSION);
            FeedFullSnapshot(SNAPSHOT_VERSION + 1);

            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_session_out_of_order_fragments_send_one_ack()
        {
            BeginAndConnect();
            Feed(Fragment(SNAPSHOT_VERSION, 3, 2));
            Feed(Fragment(SNAPSHOT_VERSION, 3, 0));
            Feed(Fragment(SNAPSHOT_VERSION, 3, 2));
            Feed(Fragment(SNAPSHOT_VERSION, 3, 1));

            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_session_newer_version_index_zero_restarts_and_ack_carries_new_version()
        {
            BeginAndConnect();
            Feed(Fragment(SNAPSHOT_VERSION, 3, 0));
            Feed(Fragment(SNAPSHOT_VERSION, 3, 1));
            Feed(Fragment(SNAPSHOT_VERSION + 1, 2, 0));
            Assert.AreEqual(0, SentOfType(ZoneEntryAck.MessageTypeId).Count);
            Feed(Fragment(SNAPSHOT_VERSION + 1, 2, 1));

            List<byte[]> acks = SentOfType(ZoneEntryAck.MessageTypeId);
            Assert.AreEqual(1, acks.Count);
            ZoneEntryAckCodec.TryReadBody(acks[0].AsSpan(ENVELOPE), out ZoneEntryAck ack);
            Assert.AreEqual(SNAPSHOT_VERSION + 1, ack.SnapshotVersion);
        }

        [Test]
        public void test_session_thirty_three_fragments_fails_entry_with_snapshot_too_large()
        {
            BeginAndConnect();
            Feed(Fragment(SNAPSHOT_VERSION, LONG_FRAGMENT_COUNT, 0));

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.SnapshotTooLarge }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(0, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_session_envelope_tick_of_last_received_message_is_used_on_later_sends()
        {
            BeginAndConnect();
            Feed(Fragment(SNAPSHOT_VERSION, 2, 0, 55u));
            _clock.Now = HEARTBEAT_INTERVAL;
            _session.Update();

            byte[] heartbeat = SentOfType(HeartbeatMessage.MessageTypeId).Single();
            MessageEnvelopeCodec.TryRead(heartbeat, out ServerMessageEnvelope envelope);
            Assert.AreEqual(55u, envelope.ServerTickNumber);
            Assert.AreEqual(2u, envelope.SequenceNumber);
        }

        // ---------- AC-3 snapshot timeout ----------

        [Test]
        public void test_session_snapshot_timeout_stops_transport_and_does_one_token_login_without_request()
        {
            BeginAndConnect();
            _clock.Now = REASSEMBLY_TIMEOUT - 0.5;
            _session.Update();
            Assert.AreEqual(0, _transport.StopCount);

            _clock.Now = REASSEMBLY_TIMEOUT;
            _session.Update();

            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(ClientZoneSessionState.Reconnecting, _session.State);
            Assert.AreEqual(2, _login.Calls.Count);
            Assert.AreEqual(USERNAME, _login.Calls[1].User);
            Assert.AreEqual("", _login.Calls[1].Password);
            CollectionAssert.AreEqual(TokenA, _login.Calls[1].Token);
            Assert.IsTrue(_transport.Sent.All(m => IdOf(m) == SessionHandshake.MessageTypeId || IdOf(m) == HeartbeatMessage.MessageTypeId));
            Assert.AreEqual(0, _failures.Count);
        }

        [Test]
        public void test_session_second_snapshot_timeout_fails_with_timeout_then_ends_without_more_logins()
        {
            BeginAndConnect();
            _clock.Now = REASSEMBLY_TIMEOUT;
            _session.Update();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();
            Assert.AreEqual(2, SentOfType(SessionHandshake.MessageTypeId).Count);

            _clock.Now = REASSEMBLY_TIMEOUT * 2 - 0.5;
            _session.Update();
            Assert.AreEqual(ClientZoneSessionState.AwaitingEntry, _session.State);
            _clock.Now = REASSEMBLY_TIMEOUT * 2;
            _session.Update();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.SnapshotTimeout }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.AreEqual(1, CountTokenPathLogins(_login));
            Assert.AreEqual(2, _login.Calls.Count);
            Assert.AreEqual(2, _transport.StopCount);
        }

        [Test]
        public void test_session_timeout_after_partial_snapshot_discards_buffer_and_reconnects()
        {
            BeginAndConnect();
            Feed(Fragment(SNAPSHOT_VERSION, 3, 0));
            _clock.Now = REASSEMBLY_TIMEOUT;
            _session.Update();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();

            Feed(Fragment(SNAPSHOT_VERSION + 1, 1, 0));

            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
            CollectionAssert.AreEqual(new[] { "127.0.0.1:7777", "127.0.0.1:7778" }, _transport.Starts);
        }

        // ---------- AC-4 heartbeat ----------

        [Test]
        public void test_session_idle_heartbeats_go_out_at_3_6_and_9_seconds()
        {
            EnterZone();

            _clock.Now = 2.0;
            _session.Update();
            Assert.AreEqual(0, SentOfType(HeartbeatMessage.MessageTypeId).Count);
            _clock.Now = 3.0;
            _session.Update();
            _session.Update();
            Assert.AreEqual(1, SentOfType(HeartbeatMessage.MessageTypeId).Count);
            _clock.Now = 6.0;
            _session.Update();
            Assert.AreEqual(2, SentOfType(HeartbeatMessage.MessageTypeId).Count);
            _clock.Now = 9.0;
            _session.Update();
            Assert.AreEqual(3, SentOfType(HeartbeatMessage.MessageTypeId).Count);
            Assert.AreEqual(ENVELOPE, SentOfType(HeartbeatMessage.MessageTypeId)[0].Length);
        }

        [Test]
        public void test_session_ack_sent_at_one_second_moves_first_heartbeat_to_four_seconds()
        {
            BeginAndConnect();
            _clock.Now = 1.0;
            FeedFullSnapshot(SNAPSHOT_VERSION);

            _clock.Now = 3.0;
            _session.Update();
            Assert.AreEqual(0, SentOfType(HeartbeatMessage.MessageTypeId).Count);
            _clock.Now = 4.0;
            _session.Update();
            Assert.AreEqual(1, SentOfType(HeartbeatMessage.MessageTypeId).Count);
        }

        [Test]
        public void test_session_no_heartbeat_before_handshake_is_sent()
        {
            _session.Begin(USERNAME, PASSWORD);
            LoginOk();

            _clock.Now = 5.0;
            _session.Update();

            Assert.AreEqual(0, _transport.Sent.Count);
        }

        [Test]
        public void test_session_no_heartbeat_after_leave()
        {
            EnterZone();
            _session.Leave();

            _clock.Now = 9.0;
            _session.Update();

            Assert.AreEqual(0, SentOfType(HeartbeatMessage.MessageTypeId).Count);
        }

        // ---------- AC-5 entry rejected ----------

        [Test]
        public void test_session_zone_full_response_fails_entry_once_stops_transport_and_sends_no_ack()
        {
            BeginAndConnect();
            Feed(ZoneFull());

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ZoneFull }, _failures);
            Assert.AreEqual(JoinRejectedReason.ZoneFull, _session.ZoneFullReason);
            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(0, SentOfType(ZoneEntryAck.MessageTypeId).Count);
            Assert.AreEqual(1, _login.Calls.Count);
        }

        [Test]
        public void test_session_close_before_ack_fails_entry_without_reconnect()
        {
            BeginAndConnect();
            _transport.Listener.OnDisconnected();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ClosedBeforeAck }, _failures);
            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(1, _login.Calls.Count);
            Assert.AreEqual(0, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_session_connect_failure_before_handshake_fails_entry()
        {
            _session.Begin(USERNAME, PASSWORD);
            LoginOk();
            _transport.Listener.OnDisconnected();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ClosedBeforeAck }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
        }

        // ---------- AC-6 token reconnect ----------

        [Test]
        public void test_session_close_after_ack_logs_in_once_with_old_token_and_reenters_with_new_token()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();

            Assert.AreEqual(ClientZoneSessionState.Reconnecting, _session.State);
            Assert.AreEqual(2, _login.Calls.Count);
            Assert.AreEqual("", _login.Calls[1].Password);
            CollectionAssert.AreEqual(TokenA, _login.Calls[1].Token);
            Assert.IsTrue(_logs.Contains("disconnected"));

            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            Assert.IsTrue(_logs.Contains("reconnect ok zone=4 port=7778"));
            CollectionAssert.AreEqual(new[] { "127.0.0.1:7777", "127.0.0.1:7778" }, _transport.Starts);
            _transport.Listener.OnConnected();

            List<byte[]> handshakes = SentOfType(SessionHandshake.MessageTypeId);
            Assert.AreEqual(2, handshakes.Count);
            SessionHandshakeCodec.TryReadBody(handshakes[1].AsSpan(ENVELOPE), out SessionHandshake second);
            CollectionAssert.AreEqual(TokenB, second.SessionToken);
            Assert.AreEqual(NEW_ZONE_ID, second.TargetZoneId.RawValue);
            MessageEnvelopeCodec.TryRead(handshakes[1], out ServerMessageEnvelope envelope);
            Assert.AreEqual(1u, envelope.SequenceNumber);

            FeedFullSnapshot(SNAPSHOT_VERSION + 5);
            List<byte[]> acks = SentOfType(ZoneEntryAck.MessageTypeId);
            Assert.AreEqual(2, acks.Count);
            ZoneEntryAckCodec.TryReadBody(acks[1].AsSpan(ENVELOPE), out ZoneEntryAck ack);
            Assert.AreEqual(NEW_ZONE_ID, ack.ZoneId.RawValue);
            Assert.AreEqual(0, _failures.Count);
            Assert.AreEqual(0, _endedCount);
        }

        [Test]
        public void test_session_failed_reconnect_login_ends_session_with_no_retry()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();
            _login.Complete(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials));

            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.AreEqual(2, _login.Calls.Count);
            Assert.AreEqual(1, CountTokenPathLogins(_login));
            Assert.IsTrue(_logs.Contains("reconnect failed"));
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_zone_session_ended_message_after_ack_is_treated_as_close()
        {
            EnterZone();
            Feed(ZoneSessionEndedMessage());

            Assert.AreEqual(ClientZoneSessionState.Reconnecting, _session.State);
            Assert.AreEqual(1, CountTokenPathLogins(_login));
            Assert.AreEqual(1, _transport.StopCount);
        }

        [Test]
        public void test_session_second_drop_after_completed_reconnect_may_reconnect_again()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();
            FeedFullSnapshot(SNAPSHOT_VERSION + 1);

            _transport.Listener.OnDisconnected();

            Assert.AreEqual(ClientZoneSessionState.Reconnecting, _session.State);
            Assert.AreEqual(2, CountTokenPathLogins(_login));
        }

        // ---------- AC-7 / AC-8 ----------

        [Test]
        public void test_session_leave_stops_transport_makes_no_login_and_zeroes_token()
        {
            EnterZone();
            Assert.IsFalse(_session.IsTokenZeroed());
            int loginsBefore = _login.Calls.Count;

            _session.Leave();
            _transport.Listener.OnDisconnected();

            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(loginsBefore, _login.Calls.Count);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_leave_twice_raises_session_ended_once()
        {
            EnterZone();
            _session.Leave();
            _session.Leave();

            Assert.AreEqual(1, _endedCount);
        }

        [Test]
        public void test_session_login_response_token_array_is_copied_not_referenced()
        {
            _session.Begin(USERNAME, PASSWORD);
            byte[] responseToken = (byte[])TokenA.Clone();
            _login.Complete(LoginResponse.ForSuccess(responseToken, new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT));
            Array.Clear(responseToken, 0, responseToken.Length);

            _transport.Listener.OnConnected();

            SessionHandshakeCodec.TryReadBody(_transport.Sent[0].AsSpan(ENVELOPE), out SessionHandshake handshake);
            CollectionAssert.AreEqual(TokenA, handshake.SessionToken);
        }

        [Test]
        public void test_session_logs_never_contain_token_hex_or_base64()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();
            FeedFullSnapshot(SNAPSHOT_VERSION);
            _session.Leave();

            string all = string.Join("\n", _logs).ToLowerInvariant();
            foreach (byte[] token in new[] { TokenA, TokenB })
            {
                string dashed = BitConverter.ToString(token).ToLowerInvariant();
                Assert.IsFalse(all.Contains(dashed.Replace("-", "")), "hex");
                Assert.IsFalse(all.Contains(dashed), "dashed hex");
                Assert.IsFalse(all.Contains(Convert.ToBase64String(token).ToLowerInvariant()), "base64");
            }

            Assert.IsFalse(all.Contains(PASSWORD));
            Assert.IsTrue(_logs.Count > 0);
        }

        [Test]
        public void test_session_begin_twice_throws()
        {
            _session.Begin(USERNAME, PASSWORD);
            Assert.Throws<InvalidOperationException>(() => _session.Begin(USERNAME, PASSWORD));
        }

        // ---------- connect timeout (code review 2026-11-11) ----------

        [Test]
        public void test_session_config_connect_timeout_zero_is_rejected()
        {
            var config = new ClientZoneSessionConfig { GatewayBaseUrl = GATEWAY_URL, ConnectTimeoutSeconds = 0 };
            Assert.Throws<InvalidOperationException>(config.Validate);
        }

        [Test]
        public void test_session_connect_timeout_fails_entry_with_connect_timeout_and_stops_transport()
        {
            _session.Begin(USERNAME, PASSWORD);
            LoginOk();

            _clock.Now = CONNECT_TIMEOUT - 0.5;
            _session.Update();
            Assert.AreEqual(ClientZoneSessionState.Connecting, _session.State);
            Assert.AreEqual(0, _transport.StopCount);

            _clock.Now = CONNECT_TIMEOUT;
            _session.Update();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ConnectTimeout }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _transport.StopCount);
            Assert.AreEqual(1, _endedCount);
            Assert.AreEqual(1, _login.Calls.Count);
            Assert.AreEqual(0, _transport.Sent.Count);
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_connect_timeout_on_reconnect_counts_from_reconnect_start_and_makes_no_second_login()
        {
            EnterZone();
            _clock.Now = 1.0;
            _transport.Listener.OnDisconnected();
            _clock.Now = 2.0;
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));

            _clock.Now = 2.0 + CONNECT_TIMEOUT - 0.5;
            _session.Update();
            Assert.AreEqual(ClientZoneSessionState.Connecting, _session.State);

            _clock.Now = 2.0 + CONNECT_TIMEOUT;
            _session.Update();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ConnectTimeout }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, CountTokenPathLogins(_login));
            Assert.AreEqual(2, _login.Calls.Count);
        }

        [Test]
        public void test_session_connected_in_time_has_no_connect_timeout()
        {
            BeginAndConnect();
            FeedFullSnapshot(SNAPSHOT_VERSION);

            _clock.Now = CONNECT_TIMEOUT + 1.0;
            _session.Update();

            Assert.AreEqual(ClientZoneSessionState.Entered, _session.State);
            Assert.AreEqual(0, _failures.Count);
        }

        // ---------- edge cases (code review 2026-11-11) ----------

        [Test]
        public void test_session_leave_while_logging_in_then_late_login_callback_starts_nothing()
        {
            _session.Begin(USERNAME, PASSWORD);
            _session.Leave();
            LoginOk();

            Assert.AreEqual(0, _transport.Starts.Count);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.AreEqual(0, _failures.Count);
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_leave_while_reconnecting_then_late_login_callback_starts_nothing()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();
            _session.Leave();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));

            Assert.AreEqual(1, _transport.Starts.Count);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, _endedCount);
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_close_before_ack_on_reconnected_connection_fails_without_second_token_login()
        {
            EnterZone();
            _transport.Listener.OnDisconnected();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();

            _transport.Listener.OnDisconnected();

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ClosedBeforeAck }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(1, CountTokenPathLogins(_login));
            Assert.AreEqual(1, SentOfType(ZoneEntryAck.MessageTypeId).Count);
        }

        [Test]
        public void test_session_second_connected_callback_sends_no_second_handshake()
        {
            BeginAndConnect();
            _transport.Listener.OnConnected();

            FeedFullSnapshot(SNAPSHOT_VERSION);
            _transport.Listener.OnConnected();

            Assert.AreEqual(1, SentOfType(SessionHandshake.MessageTypeId).Count);
            Assert.AreEqual(ClientZoneSessionState.Entered, _session.State);
        }

        [Test]
        public void test_session_connected_callback_while_logging_in_sends_nothing()
        {
            _session.Begin(USERNAME, PASSWORD);
            _transport.Listener.OnConnected();

            Assert.AreEqual(0, _transport.Sent.Count);
            Assert.AreEqual(ClientZoneSessionState.LoggingIn, _session.State);
        }

        [Test]
        public void test_session_zone_session_ended_before_ack_fails_with_closed_before_ack()
        {
            BeginAndConnect();
            Feed(ZoneSessionEndedMessage());

            CollectionAssert.AreEqual(new[] { ZoneEntryFailureReason.ClosedBeforeAck }, _failures);
            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.AreEqual(0, CountTokenPathLogins(_login));
        }

        [Test]
        public void test_session_heartbeat_restarts_from_the_reconnect_handshake()
        {
            EnterZone();
            _clock.Now = 1.0;
            _transport.Listener.OnDisconnected();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _clock.Now = 2.0;
            _transport.Listener.OnConnected();

            _clock.Now = 2.0 + HEARTBEAT_INTERVAL - 0.5;
            _session.Update();
            Assert.AreEqual(0, SentOfType(HeartbeatMessage.MessageTypeId).Count);

            _clock.Now = 2.0 + HEARTBEAT_INTERVAL;
            _session.Update();
            List<byte[]> heartbeats = SentOfType(HeartbeatMessage.MessageTypeId);
            Assert.AreEqual(1, heartbeats.Count);
            MessageEnvelopeCodec.TryRead(heartbeats[0], out ServerMessageEnvelope envelope);
            Assert.AreEqual(2u, envelope.SequenceNumber);
        }

        [Test]
        public void test_session_token_is_zeroed_after_snapshot_timeout_end()
        {
            BeginAndConnect();
            _clock.Now = REASSEMBLY_TIMEOUT;
            _session.Update();
            _login.Complete(Ok(TokenB, NEW_ZONE_ID, NEW_ZONE_PORT));
            _transport.Listener.OnConnected();
            _clock.Now = REASSEMBLY_TIMEOUT * 2;
            _session.Update();

            Assert.AreEqual(ClientZoneSessionState.Ended, _session.State);
            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_token_is_zeroed_after_zone_full()
        {
            BeginAndConnect();
            Assert.IsFalse(_session.IsTokenZeroed());
            Feed(ZoneFull());

            Assert.IsTrue(_session.IsTokenZeroed());
        }

        [Test]
        public void test_session_token_is_zeroed_after_failed_initial_login()
        {
            _session.Begin(USERNAME, PASSWORD);
            _login.Complete(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials));

            Assert.IsTrue(_session.IsTokenZeroed());
            Assert.AreEqual(0, _transport.Starts.Count);
        }

        [Test]
        public void test_session_short_envelope_and_unknown_message_id_are_ignored()
        {
            BeginAndConnect();
            int sentBefore = _transport.Sent.Count;

            Feed(new byte[ENVELOPE - 1]);
            var unknown = new byte[ENVELOPE + 4];
            MessageEnvelopeCodec.Write(unknown, new ServerMessageEnvelope(UNKNOWN_MESSAGE_ID, 1u, 0u));
            Feed(unknown);
            var truncatedFragment = new byte[ENVELOPE + 2];
            MessageEnvelopeCodec.Write(truncatedFragment, new ServerMessageEnvelope(ZoneStateSnapshotFragment.MessageTypeId, 1u, 0u));
            Feed(truncatedFragment);

            Assert.AreEqual(ClientZoneSessionState.AwaitingEntry, _session.State);
            Assert.AreEqual(sentBefore, _transport.Sent.Count);
            Assert.AreEqual(0, _failures.Count);

            FeedFullSnapshot(SNAPSHOT_VERSION);
            Assert.AreEqual(ClientZoneSessionState.Entered, _session.State);
        }
    }
}

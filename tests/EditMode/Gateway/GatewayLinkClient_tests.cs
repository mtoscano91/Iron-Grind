using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.ServerLogic.Gateway;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Gateway
{
    /// <summary>
    /// Gateway Story 010: the zone link thread, against a real loopback <see cref="TcpListener"/> as the fake gateway.
    /// Every wait is bounded; the retry interval is short.
    /// </summary>
    [TestFixture]
    internal sealed class GatewayLinkClient_Tests
    {
        private const int RETRY_MS = 50;
        private const int MAX_FRAME_BYTES = 65536;
        private const int INBOX_CAPACITY = 1024;
        private const int STOP_TIMEOUT_MS = 2000;
        private const int WAIT_MS = 3000;
        private const int READ_TIMEOUT_MS = 3000;
        private const int SETTLE_MS = 200;
        private const int SMALL_INBOX_CAPACITY = 4;
        private const int OVERFLOW_MESSAGES = 5;
        private const uint ZONE_ID = 5;
        private const byte TEMPLATE_ID = 3;
        private const ushort UDP_PORT = 7777;
        private const ushort CAPACITY = 50;
        private const uint FIRST_ACCOUNT = 1001;
        private const uint SECOND_ACCOUNT = 2002;
        private const uint CHARACTER_ID = 77;
        private const int TOKEN_LENGTH = 16;
        private const int LENGTH_PREFIX = 4;
        private const int CHUNK_BYTES = 3;
        private const int CHUNK_PAUSE_MS = 10;
        private const int PARTIAL_TAIL_BYTES = 10;

        private readonly List<TcpClient> _accepted = new List<TcpClient>();
        private TcpListener _listener;
        private GatewayLinkClient _client;
        private volatile AccountID[] _accounts;

        [SetUp]
        public void SetUp()
        {
            _accounts = new[] { new AccountID(FIRST_ACCOUNT) };
        }

        [TearDown]
        public void TearDown()
        {
            if (_client != null)
            {
                _client.Stop();
            }

            for (int i = 0; i < _accepted.Count; i++)
            {
                try { _accepted[i].Close(); } catch (Exception) { }
            }

            _accepted.Clear();
            if (_listener != null)
            {
                try { _listener.Stop(); } catch (Exception) { }
            }
        }

        // --------------------------------------------------------------- Fixtures

        private int StartListener(int port = 0)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            return ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        private static int ReservePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private RegisterZoneMessage Register()
        {
            return new RegisterZoneMessage(new ZoneID(ZONE_ID), TEMPLATE_ID, UDP_PORT, CAPACITY, _accounts);
        }

        private GatewayLinkClient StartClient(int port, int retryMs = RETRY_MS, int inboxCapacity = INBOX_CAPACITY)
        {
            _client = new GatewayLinkClient(retryMs, MAX_FRAME_BYTES, inboxCapacity, STOP_TIMEOUT_MS);
            _client.Start(new IPEndPoint(IPAddress.Loopback, port), Register);
            return _client;
        }

        private TcpClient AcceptWithin(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                if (_listener.Pending())
                {
                    TcpClient accepted = _listener.AcceptTcpClient();
                    accepted.GetStream().ReadTimeout = READ_TIMEOUT_MS;
                    _accepted.Add(accepted);
                    return accepted;
                }

                Thread.Sleep(5);
            }

            Assert.Fail("The fake gateway got no connection within " + milliseconds + " ms.");
            return null;
        }

        private static bool WaitUntil(Func<bool> condition, int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            return condition();
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            var data = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = stream.Read(data, got, count - got);
                Assert.Greater(n, 0, "The link closed while the fake gateway was reading a frame.");
                got += n;
            }

            return data;
        }

        private static ZoneToGatewayMessage ReadMessage(TcpClient fake)
        {
            NetworkStream stream = fake.GetStream();
            byte[] prefix = ReadExact(stream, LENGTH_PREFIX);
            int length = (int)BitConverter.ToUInt32(prefix, 0);
            byte[] rest = ReadExact(stream, length);
            var frame = new byte[LENGTH_PREFIX + length];
            Buffer.BlockCopy(prefix, 0, frame, 0, LENGTH_PREFIX);
            Buffer.BlockCopy(rest, 0, frame, LENGTH_PREFIX, length);

            var status = GatewayLinkCodec.TryReadFrame(frame, MAX_FRAME_BYTES, out byte type, out ReadOnlySpan<byte> body, out _);
            Assert.AreEqual(GatewayLinkFrameStatus.Frame, status);
            Assert.IsTrue(GatewayLinkCodec.TryDecodeZoneToGateway(type, body, out ZoneToGatewayMessage message));
            return message;
        }

        private int Drain(List<GatewayToZoneMessage> into)
        {
            while (_client.TryDequeue(out GatewayToZoneMessage m))
            {
                into.Add(m);
            }

            return into.Count;
        }

        private static void Send(TcpClient fake, byte[] bytes)
        {
            fake.GetStream().Write(bytes, 0, bytes.Length);
            fake.GetStream().Flush();
        }

        private static byte[] MakeToken()
        {
            var token = new byte[TOKEN_LENGTH];
            for (int i = 0; i < TOKEN_LENGTH; i++)
            {
                token[i] = (byte)(i + 1);
            }

            return token;
        }

        private static byte[] AdmitFrame(uint requestId)
        {
            return GatewayLinkCodec.Encode(new AdmitMessage(requestId, new AccountID(FIRST_ACCOUNT), new CharacterID(CHARACTER_ID), MakeToken()));
        }

        private static byte[] EvictFrame(uint requestId)
        {
            return GatewayLinkCodec.Encode(new EvictMessage(requestId, new AccountID(SECOND_ACCOUNT)));
        }

        private static byte[] RawFrame(byte type, int bodyLength)
        {
            var frame = new byte[LENGTH_PREFIX + 1 + bodyLength];
            BitConverter.GetBytes((uint)(1 + bodyLength)).CopyTo(frame, 0);
            frame[LENGTH_PREFIX] = type;
            return frame;
        }

        // Asserts the fake's connection ends (EOF or reset) within the read timeout, not a read timeout.
        private static void AssertClosedByClient(TcpClient fake)
        {
            var buffer = new byte[64];
            try
            {
                while (fake.GetStream().Read(buffer, 0, buffer.Length) > 0)
                {
                }
            }
            catch (IOException ex)
            {
                var socketError = ex.InnerException as SocketException;
                if (socketError != null && socketError.SocketErrorCode == SocketError.TimedOut)
                {
                    Assert.Fail("The link was not closed by the client within the read timeout.");
                }
            }
        }

        private TcpClient ConnectAndRegister(int port, int retryMs = RETRY_MS, int inboxCapacity = INBOX_CAPACITY)
        {
            StartClient(port, retryMs, inboxCapacity);
            TcpClient fake = AcceptWithin(WAIT_MS);
            ReadMessage(fake);
            Assert.IsTrue(WaitUntil(() => _client.IsConnected, WAIT_MS));
            return fake;
        }

        // --------------------------------------------------------------- AC-1: register on connect

        [Test]
        public void test_link_connect_sends_register_zone_as_first_frame()
        {
            int port = StartListener();
            StartClient(port);

            TcpClient fake = AcceptWithin(WAIT_MS);
            ZoneToGatewayMessage first = ReadMessage(fake);

            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, first.Type);
            Assert.AreEqual(ZONE_ID, first.RegisterZone.ZoneId.RawValue);
            Assert.AreEqual(TEMPLATE_ID, first.RegisterZone.TemplateId);
            Assert.AreEqual(UDP_PORT, first.RegisterZone.UdpPort);
            Assert.AreEqual(CAPACITY, first.RegisterZone.Capacity);
            Assert.AreEqual(1, first.RegisterZone.LiveAccounts.Length);
            Assert.AreEqual(FIRST_ACCOUNT, first.RegisterZone.LiveAccounts[0].RawValue);
            Assert.IsTrue(WaitUntil(() => _client.IsConnected, WAIT_MS));
            Assert.AreEqual(1, _client.LinkUpCount);
        }

        [Test]
        public void test_link_gateway_not_listening_at_start_retries_until_it_is()
        {
            int port = ReservePort();
            StartClient(port);
            Thread.Sleep(SETTLE_MS);
            Assert.IsFalse(_client.IsConnected);

            StartListener(port);
            TcpClient fake = AcceptWithin(WAIT_MS);
            ZoneToGatewayMessage first = ReadMessage(fake);

            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, first.Type);
            Assert.IsTrue(WaitUntil(() => _client.IsConnected, WAIT_MS));
            Assert.AreEqual(1, _client.LinkUpCount);
            Assert.AreEqual(0, _client.LinkLostCount);
        }

        // --------------------------------------------------------------- Inbound

        [Test]
        public void test_link_inbound_admit_and_evict_reach_inbox_in_order()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);

            Send(fake, AdmitFrame(11));
            Send(fake, EvictFrame(12));

            var messages = new List<GatewayToZoneMessage>();
            Assert.IsTrue(WaitUntil(() =>
            {
                while (_client.TryDequeue(out GatewayToZoneMessage m))
                {
                    messages.Add(m);
                }

                return messages.Count >= 2;
            }, WAIT_MS));

            Assert.AreEqual(2, messages.Count);
            Assert.AreEqual(GatewayLinkMessageType.Admit, messages[0].Type);
            Assert.AreEqual(11u, messages[0].Admit.RequestId);
            Assert.AreEqual(FIRST_ACCOUNT, messages[0].Admit.AccountId.RawValue);
            Assert.AreEqual(CHARACTER_ID, messages[0].Admit.CharacterId.RawValue);
            CollectionAssert.AreEqual(MakeToken(), messages[0].Admit.Token);
            Assert.AreEqual(GatewayLinkMessageType.Evict, messages[1].Type);
            Assert.AreEqual(12u, messages[1].Evict.RequestId);
            Assert.AreEqual(SECOND_ACCOUNT, messages[1].Evict.AccountId.RawValue);
        }

        [Test]
        public void test_link_frame_split_across_reads_is_reassembled_into_one_message()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);
            byte[] frame = AdmitFrame(21);

            for (int sent = 0; sent < frame.Length; sent += CHUNK_BYTES)
            {
                int count = Math.Min(CHUNK_BYTES, frame.Length - sent);
                fake.GetStream().Write(frame, sent, count);
                fake.GetStream().Flush();
                Thread.Sleep(CHUNK_PAUSE_MS);
            }

            GatewayToZoneMessage message = default;
            Assert.IsTrue(WaitUntil(() => _client.TryDequeue(out message), WAIT_MS));
            Assert.AreEqual(GatewayLinkMessageType.Admit, message.Type);
            Assert.AreEqual(21u, message.Admit.RequestId);
            Assert.IsFalse(_client.TryDequeue(out _));
            Assert.AreEqual(0, _client.LinkLostCount);
        }

        [Test]
        public void test_link_several_frames_and_a_partial_tail_in_one_write_all_arrive_in_order()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);
            byte[] third = AdmitFrame(33);
            var burst = new List<byte>();
            burst.AddRange(AdmitFrame(31));
            burst.AddRange(EvictFrame(32));
            for (int i = 0; i < PARTIAL_TAIL_BYTES; i++)
            {
                burst.Add(third[i]);
            }

            Send(fake, burst.ToArray());
            var messages = new List<GatewayToZoneMessage>();
            Assert.IsTrue(WaitUntil(() => Drain(messages) >= 2, WAIT_MS));
            Assert.AreEqual(2, messages.Count);

            fake.GetStream().Write(third, PARTIAL_TAIL_BYTES, third.Length - PARTIAL_TAIL_BYTES);
            fake.GetStream().Flush();
            Assert.IsTrue(WaitUntil(() => Drain(messages) >= 3, WAIT_MS));

            Assert.AreEqual(31u, messages[0].Admit.RequestId);
            Assert.AreEqual(32u, messages[1].Evict.RequestId);
            Assert.AreEqual(33u, messages[2].Admit.RequestId);
            Assert.AreEqual(0, _client.LinkLostCount);
        }

        [Test]
        public void test_link_frame_exactly_at_max_frame_bytes_is_accepted()
        {
            // Boundary: an Admit frame's length field (1 type byte + 28 body bytes) equals the smallest legal cap.
            int port = StartListener();
            _client = new GatewayLinkClient(RETRY_MS, ZoneServerConfig.MIN_GATEWAY_LINK_MAX_FRAME_BYTES, INBOX_CAPACITY, STOP_TIMEOUT_MS);
            _client.Start(new IPEndPoint(IPAddress.Loopback, port), Register);
            TcpClient fake = AcceptWithin(WAIT_MS);
            ReadMessage(fake);
            Assert.IsTrue(WaitUntil(() => _client.IsConnected, WAIT_MS));

            Send(fake, AdmitFrame(41));

            GatewayToZoneMessage message = default;
            Assert.IsTrue(WaitUntil(() => _client.TryDequeue(out message), WAIT_MS));
            Assert.AreEqual(41u, message.Admit.RequestId);
            Assert.AreEqual(0, _client.LinkLostCount);
        }

        [Test]
        public void test_link_try_dequeue_on_empty_inbox_returns_false()
        {
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY);

            Assert.IsFalse(_client.TryDequeue(out _));
        }

        // --------------------------------------------------------------- Outbound

        [Test]
        public void test_link_outbound_frames_reach_gateway_in_order()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);

            _client.Enqueue(GatewayLinkCodec.Encode(new AdmitResultMessage(7, true, AdmitRejectReason.None)));
            _client.Enqueue(GatewayLinkCodec.Encode(new EvictResultMessage(8)));

            ZoneToGatewayMessage first = ReadMessage(fake);
            ZoneToGatewayMessage second = ReadMessage(fake);

            Assert.AreEqual(GatewayLinkMessageType.AdmitResult, first.Type);
            Assert.AreEqual(7u, first.AdmitResult.RequestId);
            Assert.IsTrue(first.AdmitResult.Accepted);
            Assert.AreEqual(GatewayLinkMessageType.EvictResult, second.Type);
            Assert.AreEqual(8u, second.EvictResult.RequestId);
        }

        [Test]
        public void test_link_enqueue_while_not_connected_is_dropped_and_counted()
        {
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY, STOP_TIMEOUT_MS);
            _client.Enqueue(GatewayLinkCodec.Encode(new EvictResultMessage(1)));
            Assert.AreEqual(1, _client.DroppedOutboundCount);

            int port = StartListener();
            _client.Start(new IPEndPoint(IPAddress.Loopback, port), Register);
            TcpClient fake = AcceptWithin(WAIT_MS);
            ReadMessage(fake);
            Assert.IsTrue(WaitUntil(() => _client.IsConnected, WAIT_MS));
            _client.Enqueue(GatewayLinkCodec.Encode(new EvictResultMessage(2)));

            ZoneToGatewayMessage next = ReadMessage(fake);

            Assert.AreEqual(GatewayLinkMessageType.EvictResult, next.Type);
            Assert.AreEqual(2u, next.EvictResult.RequestId);
            Assert.AreEqual(1, _client.DroppedOutboundCount);
        }

        [Test]
        public void test_link_enqueue_null_throws()
        {
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY);

            Assert.Throws<ArgumentNullException>(() => _client.Enqueue(null));
        }

        // --------------------------------------------------------------- AC-3: reconnect

        [Test]
        public void test_link_gateway_closes_reconnects_and_reregisters_with_updated_accounts()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);
            _accounts = new[] { new AccountID(FIRST_ACCOUNT), new AccountID(SECOND_ACCOUNT) };

            fake.Close();
            TcpClient second = AcceptWithin(WAIT_MS);
            ZoneToGatewayMessage registered = ReadMessage(second);

            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, registered.Type);
            Assert.AreEqual(2, registered.RegisterZone.LiveAccounts.Length);
            Assert.AreEqual(SECOND_ACCOUNT, registered.RegisterZone.LiveAccounts[1].RawValue);
            Assert.IsTrue(WaitUntil(() => _client.LinkUpCount == 2, WAIT_MS));
            Assert.AreEqual(1, _client.LinkLostCount);
        }

        // --------------------------------------------------------------- AC-4: violations close the link

        [Test]
        public void test_link_inbox_overflow_closes_link_and_reconnects_keeping_queued_messages()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port, RETRY_MS, SMALL_INBOX_CAPACITY);

            var burst = new List<byte>();
            for (uint i = 0; i < OVERFLOW_MESSAGES; i++)
            {
                burst.AddRange(AdmitFrame(100 + i));
            }

            Send(fake, burst.ToArray());
            AssertClosedByClient(fake);

            TcpClient second = AcceptWithin(WAIT_MS);
            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, ReadMessage(second).Type);
            Assert.IsTrue(WaitUntil(() => _client.LinkUpCount == 2, WAIT_MS));
            Assert.AreEqual(1, _client.LinkLostCount);
            for (uint i = 0; i < SMALL_INBOX_CAPACITY; i++)
            {
                Assert.IsTrue(_client.TryDequeue(out GatewayToZoneMessage message));
                Assert.AreEqual(100 + i, message.Admit.RequestId);
            }

            Assert.IsFalse(_client.TryDequeue(out _));
        }

        [Test]
        public void test_link_oversized_frame_closes_link_and_reconnects()
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);

            Send(fake, BitConverter.GetBytes((uint)(MAX_FRAME_BYTES + 1)));
            AssertClosedByClient(fake);

            TcpClient second = AcceptWithin(WAIT_MS);
            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, ReadMessage(second).Type);
            Assert.IsTrue(WaitUntil(() => _client.LinkUpCount == 2, WAIT_MS));
            Assert.AreEqual(1, _client.LinkLostCount);
        }

        [TestCase((byte)0x01, 11)]
        [TestCase((byte)0x81, 3)]
        [TestCase((byte)0x7F, 3)]
        public void test_link_malformed_frame_closes_link_and_reconnects(byte type, int bodyLength)
        {
            int port = StartListener();
            TcpClient fake = ConnectAndRegister(port);

            Send(fake, RawFrame(type, bodyLength));
            AssertClosedByClient(fake);

            TcpClient second = AcceptWithin(WAIT_MS);
            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, ReadMessage(second).Type);
            Assert.IsTrue(WaitUntil(() => _client.LinkUpCount == 2, WAIT_MS));
            Assert.IsFalse(_client.TryDequeue(out _));
        }

        // --------------------------------------------------------------- AC-5: stop

        [Test]
        public void test_link_stop_while_connected_and_idle_returns_within_timeout_and_is_idempotent()
        {
            int port = StartListener();
            ConnectAndRegister(port);
            Thread worker = _client.WorkerThread;

            var clock = Stopwatch.StartNew();
            _client.Stop();
            clock.Stop();

            Assert.Less(clock.ElapsedMilliseconds, STOP_TIMEOUT_MS);
            Assert.IsFalse(worker.IsAlive);
            Assert.IsFalse(_client.IsRunning);
            Assert.IsFalse(_client.IsConnected);
            Assert.DoesNotThrow(() => _client.Stop());
        }

        [Test]
        public void test_link_stop_while_retrying_without_a_listener_returns_promptly()
        {
            int port = ReservePort();
            StartClient(port, retryMs: 60000);
            Thread.Sleep(SETTLE_MS);
            Thread worker = _client.WorkerThread;

            var clock = Stopwatch.StartNew();
            _client.Stop();
            clock.Stop();

            Assert.Less(clock.ElapsedMilliseconds, STOP_TIMEOUT_MS);
            Assert.IsFalse(worker.IsAlive);
        }

        [Test]
        public void test_link_stop_before_start_is_safe_and_start_after_stop_throws()
        {
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY);

            Assert.DoesNotThrow(() => _client.Stop());
            Assert.DoesNotThrow(() => _client.Stop());
            Assert.Throws<InvalidOperationException>(
                () => _client.Start(new IPEndPoint(IPAddress.Loopback, 1), Register));
            Assert.IsNull(_client.WorkerThread);
        }

        [Test]
        public void test_link_start_twice_does_not_start_a_second_thread()
        {
            int port = StartListener();
            StartClient(port);
            Thread first = _client.WorkerThread;

            _client.Start(new IPEndPoint(IPAddress.Loopback, port), Register);

            Assert.AreSame(first, _client.WorkerThread);
            Assert.AreEqual("IronGrindGatewayLink", first.Name);
            Assert.IsTrue(first.IsBackground);
        }

        [Test]
        public void test_link_registration_callback_that_throws_is_a_link_failure_then_retried()
        {
            int port = StartListener();
            int calls = 0;
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY, STOP_TIMEOUT_MS);
            _client.Start(new IPEndPoint(IPAddress.Loopback, port), () =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("first call fails");
                }

                return Register();
            });

            TcpClient first = AcceptWithin(WAIT_MS);
            AssertClosedByClient(first);
            TcpClient second = AcceptWithin(WAIT_MS);

            Assert.AreEqual(GatewayLinkMessageType.RegisterZone, ReadMessage(second).Type);
            Assert.AreEqual(0, _client.LinkLostCount);
        }

        // --------------------------------------------------------------- Construction and config

        [TestCase(0, 1, 1, 1)]
        [TestCase(1, 0, 1, 1)]
        [TestCase(1, 1, 0, 1)]
        [TestCase(1, 1, 1, 0)]
        [TestCase(-1, 1, 1, 1)]
        public void test_link_constructor_rejects_values_below_one(int retryMs, int maxFrameBytes, int inboxCapacity, int stopTimeoutMs)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new GatewayLinkClient(retryMs, maxFrameBytes, inboxCapacity, stopTimeoutMs));
        }

        [Test]
        public void test_link_start_with_null_arguments_throws()
        {
            _client = new GatewayLinkClient(RETRY_MS, MAX_FRAME_BYTES, INBOX_CAPACITY);

            Assert.Throws<ArgumentNullException>(() => _client.Start(null, Register));
            Assert.Throws<ArgumentNullException>(() => _client.Start(new IPEndPoint(IPAddress.Loopback, 1), null));
            Assert.IsNull(_client.WorkerThread);
        }

        [Test]
        public void test_link_from_config_builds_a_client_from_default_config()
        {
            var config = new ZoneServerConfig();

            _client = GatewayLinkClient.FromConfig(config);

            Assert.IsNotNull(_client);
            Assert.IsFalse(_client.IsRunning);
            Assert.IsFalse(_client.IsConnected);
        }

        [Test]
        public void test_link_from_config_rejects_null_and_invalid_knobs()
        {
            Assert.Throws<ArgumentNullException>(() => GatewayLinkClient.FromConfig(null));
            var bad = new ZoneServerConfig { GatewayLinkRetrySeconds = 0 };
            Assert.Throws<ArgumentOutOfRangeException>(() => GatewayLinkClient.FromConfig(bad));
        }
    }
}

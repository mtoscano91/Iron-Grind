using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Story 039 verification run (VS-1 to VS-5): the production send path, ClientMessageOutbox over
    /// NgoClientMessageSender, in Story 038's harness shape: one server NetworkManager and one client
    /// NetworkManager in one process on UnityTransport over 127.0.0.1, no player prefab, approval off, scene
    /// management off. The test owns both managers and drives <c>outbox.Flush(tick)</c> itself, one flush per
    /// frame. Every VS test ends with one "[VS-n] ..." line for production/qa/evidence/networking-core-039-ngo-send-verification.md.
    /// </summary>
    public sealed class ClientMessageOutbox_ngo_run_tests
    {
        private const ushort TEST_PORT = 7802; // 038's fixture uses 7801
        private const string TEST_ADDRESS = "127.0.0.1";
        private const int TARGET_FRAME_RATE = 60;

        private const int CONNECT_TIMEOUT_FRAMES = 600;
        private const int DELIVERY_TIMEOUT_FRAMES = 1200;
        private const int SHUTDOWN_TIMEOUT_FRAMES = 600;
        private const int SETTLE_FRAMES = 30;
        private const uint FIRST_TICK = 100u;

        private const int VS2_MESSAGE_COUNT = 11;
        private const int VS2_CAP = 8;
        private const int VS3_QUIET_FRAMES = 60;
        private const int VS4_WARMUP_FRAMES = 20;
        private const int VS4_MESSAGES_PER_FLUSH = 8;
        private const int VS4_MEASURED_FLUSHES = 63; // 63 * 8 = 504 messages, at least 500
        private const int VS4_MEASURED_MINIMUM = 500;
        private const int VS4_CONTROL_FLUSHES = 3;
        private const int VS4_CONTROL_BYTES = 65536;
        private const int VS5_MESSAGE_BYTES = 1300;
        private const int VS5_OBSERVE_FRAMES = 90;
        private const int UNKNOWN_CLIENT_ID = 999;
        private const int SENDER_MESSAGE_BYTES = 40;

        // ------------------------------------------------------------------ test doubles

        /// <summary>Intake and dispatcher in one double that accepts every connection (copied from Story 038's run).</summary>
        private sealed class OutboxRecordingIntake : IInboundMessageIntake, IInboundRequestDispatcher
        {
            public readonly HashSet<uint> Known = new HashSet<uint>();
            public readonly List<uint> AddLog = new List<uint>();
            public readonly List<uint> RemoveLog = new List<uint>();

            public bool TryAccept(uint clientId, ReadOnlySpan<byte> message)
            {
                return Known.Contains(clientId);
            }

            public bool AddConnection(uint clientId)
            {
                bool added = Known.Add(clientId);
                if (added)
                {
                    AddLog.Add(clientId);
                }
                return added;
            }

            public void RemoveConnection(uint clientId)
            {
                Known.Remove(clientId);
                RemoveLog.Add(clientId);
            }

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }
            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }
            public void Seal() { }
            public void DispatchTick(uint currentTick) { }
            public int HeldCount(CharacterID charId) { return 0; }
        }

        private sealed class OutboxReceived
        {
            public byte[] Bytes;
            public int Frame;
            public int Order;
        }

        private sealed class OutboxClientHandle
        {
            public GameObject Go;
            public NetworkManager Manager;
            public uint ServerSideId;
            public string Error;
            // Pre-sized so the list itself does not grow during a run; each message is copied into its own array.
            public readonly List<OutboxReceived> Received = new List<OutboxReceived>(2048);
        }

        /// <summary>
        /// Wraps the real sender for VS-4. With <see cref="Forward"/> off the message stops here, so a window
        /// around <c>Flush</c> holds the outbox's own code only; with it on, the window also holds the sender
        /// and NGO's queueing. It never touches the recorder: toggling one inside a window read 0 for
        /// everything, the control included (first run, 2026-10-12).
        /// </summary>
        private sealed class OutboxMeasuringSeam : IClientMessageSendSeam
        {
            private readonly IClientMessageSendSeam _inner;
            public bool Forward = true;
            public bool AllocateControl;
            public byte[] ControlSink;
            public int Calls;
            public int Forwarded;

            public OutboxMeasuringSeam(IClientMessageSendSeam inner)
            {
                _inner = inner;
            }

            public void Send(uint clientId, ReadOnlySpan<byte> message, NetworkChannel channel)
            {
                Calls++;
                if (AllocateControl)
                {
                    ControlSink = new byte[VS4_CONTROL_BYTES]; // the control: an allocation the window must see
                }

                if (Forward)
                {
                    Forwarded++;
                    _inner.Send(clientId, message, channel);
                }
            }
        }

        // ------------------------------------------------------------------ fixture state

        private GameObject _serverGo;
        private NetworkManager _server;
        private UnityTransport _serverTransport;
        private OutboxRecordingIntake _intake;
        private ClientMessageOutbox _outbox;
        private NgoInboundTransportShell _shell;
        private readonly List<OutboxClientHandle> _clients = new List<OutboxClientHandle>();
        private int _targetFrameRateBefore;
        private int _vSyncBefore;
        private string _setUpError;
        private uint _tick;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _setUpError = null;
            _tick = FIRST_TICK;
            _targetFrameRateBefore = Application.targetFrameRate;
            _vSyncBefore = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TARGET_FRAME_RATE;

            _intake = new OutboxRecordingIntake();

            // Inactive first so NetworkManager.Awake reads a NetworkConfig that is already in place.
            _serverGo = new GameObject("TestOutboxServerNetworkManager");
            _serverGo.SetActive(false);
            _server = _serverGo.AddComponent<NetworkManager>();
            _serverTransport = _serverGo.AddComponent<UnityTransport>();
            _serverTransport.SetConnectionData(TEST_ADDRESS, TEST_PORT);
            _server.NetworkConfig = CreateConfig(_serverTransport);
            _serverGo.SetActive(true);

            if (!_server.StartServer())
            {
                _setUpError = "NetworkManager.StartServer() returned false on " + TEST_ADDRESS + ":" + TEST_PORT
                    + " (port busy from an earlier run, or NGO refused to start).";
            }
            else
            {
                // Wired as production will: sender over the server manager, outbox over the sender, the shell
                // tells the outbox of added and removed clients, the outbox asks the adapter to disconnect.
                _outbox = new ClientMessageOutbox(new NgoClientMessageSender(_server));
                _shell = new NgoInboundTransportShell(_server, _intake, _intake, _outbox);
                _outbox.SetDisconnectRequester(_shell.Adapter);
                _shell.Start();
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Runs after a failed test too; nothing here may throw.
            LogAssert.ignoreFailingMessages = false;
            if (_shell != null)
            {
                _shell.Stop();
            }
            for (int i = 0; i < _clients.Count; i++)
            {
                SafeShutdown(_clients[i].Manager);
            }
            SafeShutdown(_server);

            yield return WaitFrames(AllShutDown, SHUTDOWN_TIMEOUT_FRAMES);
            if (!AllShutDown())
            {
                Debug.LogWarning("[TEARDOWN] a NetworkManager was still shutting down after "
                    + SHUTDOWN_TIMEOUT_FRAMES + " frames; destroying anyway.");
            }

            for (int i = 0; i < _clients.Count; i++)
            {
                if (_clients[i].Go != null)
                {
                    Object.Destroy(_clients[i].Go);
                }
            }
            _clients.Clear();
            if (_serverGo != null)
            {
                Object.Destroy(_serverGo);
            }
            yield return null;

            _shell = null;
            _outbox = null;
            _server = null;
            _serverGo = null;
            Application.targetFrameRate = _targetFrameRateBefore;
            QualitySettings.vSyncCount = _vSyncBefore;
        }

        // ------------------------------------------------------------------ VS items

        [UnityTest]
        public IEnumerator test_vs1_round_trip_full_sync_then_slot_update_arrive_byte_for_byte_in_order()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            byte[] fullBody = BuildFullSyncBody();
            byte[] updateBody = BuildSlotUpdateBody(7, 3);
            uint flushTick = _tick;

            _outbox.Enqueue(client.ServerSideId, InventoryFullSync.MessageTypeId, fullBody, false);
            _outbox.Enqueue(client.ServerSideId, InventorySlotUpdate.MessageTypeId, updateBody, false);
            _outbox.Flush(flushTick);
            _tick++;

            yield return WaitFrames(() => client.Received.Count >= 2, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES); // a late third message would show here

            // Observation first, so a failing run still carries it.
            byte[] expectedFull = BuildExpected(InventoryFullSync.MessageTypeId, 1u, flushTick, fullBody);
            byte[] expectedUpdate = BuildExpected(InventorySlotUpdate.MessageTypeId, 2u, flushTick, updateBody);
            int receivedCount = client.Received.Count;
            bool firstEqual = receivedCount >= 1 && BytesEqual(expectedFull, client.Received[0].Bytes);
            bool secondEqual = receivedCount >= 2 && BytesEqual(expectedUpdate, client.Received[1].Bytes);
            var receivedLengths = new StringBuilder();
            for (int i = 0; i < receivedCount; i++)
            {
                receivedLengths.Append(i > 0 ? ", " : string.Empty).Append(client.Received[i].Bytes.Length);
            }
            string observation = "[VS-1] sent InventoryFullSync(" + expectedFull.Length + " bytes with envelope, seq 1) then InventorySlotUpdate("
                + expectedUpdate.Length + " bytes, seq 2) at tick " + flushTick + "; received " + receivedCount
                + " messages of lengths " + receivedLengths
                + "; byte for byte equal: first=" + firstEqual + " second=" + secondEqual
                + "; arrival order = send order: " + (firstEqual && secondEqual);
            Debug.Log(observation);

            Assert.AreEqual(2, receivedCount, "exactly two messages must arrive");
            Assert.AreEqual(211, expectedFull.Length, "InventoryFullSync with its envelope is 211 bytes");
            Assert.IsTrue(firstEqual, "InventoryFullSync differs from what was enqueued");
            Assert.IsTrue(secondEqual, "InventorySlotUpdate differs from what was enqueued");
        }

        [UnityTest]
        public IEnumerator test_vs2_eleven_messages_arrive_as_eight_at_tick_t_and_three_at_tick_t_plus_one()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            uint t = _tick;
            var bodies = new byte[VS2_MESSAGE_COUNT][];
            for (int i = 0; i < VS2_MESSAGE_COUNT; i++)
            {
                bodies[i] = BuildSlotUpdateBody((byte)i, i + 1);
                _outbox.Enqueue(client.ServerSideId, InventorySlotUpdate.MessageTypeId, bodies[i], false);
            }

            _outbox.Flush(t);
            yield return null;
            _outbox.Flush(t + 1);
            _tick = t + 2;

            yield return WaitFrames(() => client.Received.Count >= VS2_MESSAGE_COUNT, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            // Observation first (every arrival with its frame), so a failing run still carries it.
            int atT = 0;
            int atTPlusOne = 0;
            int other = 0;
            bool sequenceOk = true;
            bool bodiesOk = true;
            bool envelopesOk = true;
            var seqs = new StringBuilder();
            for (int i = 0; i < client.Received.Count; i++)
            {
                byte[] bytes = client.Received[i].Bytes;
                if (!MessageEnvelopeCodec.TryRead(bytes, out ServerMessageEnvelope envelope))
                {
                    envelopesOk = false;
                    seqs.Append("<shorter than an envelope>@frame").Append(client.Received[i].Frame).Append(' ');
                    continue;
                }
                if (envelope.ServerTickNumber == t)
                {
                    atT++;
                }
                else if (envelope.ServerTickNumber == t + 1)
                {
                    atTPlusOne++;
                }
                else
                {
                    other++;
                }
                if (envelope.SequenceNumber != (uint)(i + 1))
                {
                    sequenceOk = false;
                }
                if (!BytesEqual(BuildExpected(InventorySlotUpdate.MessageTypeId, (uint)(i + 1), envelope.ServerTickNumber, bodies[i]), bytes))
                {
                    bodiesOk = false;
                }
                seqs.Append(envelope.SequenceNumber).Append('@').Append(envelope.ServerTickNumber)
                    .Append("/frame").Append(client.Received[i].Frame).Append(' ');
            }
            Debug.Log("[VS-2] 11 non-exempt messages enqueued, flushes at tick " + t + " and " + (t + 1) + " one frame apart: arrived with tick "
                + t + "=" + atT + ", tick " + (t + 1) + "=" + atTPlusOne + ", other=" + other
                + "; received " + client.Received.Count + " messages"
                + "; sequence@tick/frame in arrival order: " + seqs.ToString().TrimEnd()
                + "; sequence 1..11 in arrival order=" + sequenceOk + "; bodies and envelopes byte for byte=" + bodiesOk);

            Assert.AreEqual(VS2_MESSAGE_COUNT, client.Received.Count, "all eleven must arrive, none dropped, no extra");
            Assert.IsTrue(envelopesOk, "a message is shorter than an envelope");
            Assert.AreEqual(VS2_CAP, atT, "8 messages must carry tick T");
            Assert.AreEqual(VS2_MESSAGE_COUNT - VS2_CAP, atTPlusOne, "3 messages must carry tick T+1");
            Assert.AreEqual(0, other);
            Assert.IsTrue(sequenceOk, "sequence numbers 1..11 in arrival order");
            Assert.IsTrue(bodiesOk, "bodies in enqueue order, envelope fields as flushed");
        }

        [UnityTest]
        public IEnumerator test_vs3_removed_client_nothing_on_the_wire_and_no_ngo_error()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);
            uint id = client.ServerSideId;
            byte[] body = BuildSlotUpdateBody(1, 1);

            // Baseline: the path works before the disconnect.
            _outbox.Enqueue(id, InventorySlotUpdate.MessageTypeId, body, false);
            _outbox.Flush(_tick++);
            yield return WaitFrames(() => client.Received.Count >= 1, DELIVERY_TIMEOUT_FRAMES);
            Assert.AreEqual(1, client.Received.Count, "the baseline message must arrive before the disconnect");

            var captured = new List<string>();
            int errors = 0;
            Application.LogCallback capture = (string text, string stack, LogType type) =>
            {
                captured.Add(type + ": " + text);
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    errors++;
                }
            };
            Application.logMessageReceived += capture;
            int receivedAfter;
            int outboxLines;
            bool stillConnected;
            try
            {
                _shell.Adapter.Disconnect(id);

                // Enqueue after Disconnect, on several ticks, with a flush each frame.
                for (int frame = 0; frame < VS3_QUIET_FRAMES; frame++)
                {
                    if (frame < 3)
                    {
                        _outbox.Enqueue(id, InventorySlotUpdate.MessageTypeId, body, false);
                    }
                    _outbox.Flush(_tick++);
                    yield return null;
                }
                receivedAfter = client.Received.Count - 1;
                stillConnected = client.Manager.IsConnectedClient;
                outboxLines = 0;
                for (int i = 0; i < captured.Count; i++)
                {
                    if (captured[i].Contains("[ClientMessageOutbox]"))
                    {
                        outboxLines++;
                    }
                }
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            Debug.Log("[VS-3] after shell.Adapter.Disconnect(" + id + "): messages enqueued after it on 3 ticks, " + VS3_QUIET_FRAMES
                + " flushes; client received " + receivedAfter + " further messages; error/exception/assert log entries=" + errors
                + "; log lines mentioning [ClientMessageOutbox]=" + outboxLines + "; client still connected after the wait=" + stillConnected
                + "; all captured entries: [" + string.Join(" | ", captured) + "]");

            Assert.AreEqual(0, receivedAfter, "a message reached the client after the adapter's Disconnect");
            Assert.AreEqual(0, errors, "an error was logged");
        }

        [UnityTest]
        public IEnumerator test_vs4_flush_of_500_messages_allocates_nothing_in_the_outbox_and_ngo_is_recorded()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            // A second outbox over a measuring seam, for this client alone; the fixture's outbox stays idle.
            var seam = new OutboxMeasuringSeam(new NgoClientMessageSender(_server));
            var measured = new ClientMessageOutbox(seam);
            measured.AddClient(client.ServerSideId);
            byte[] body = BuildSlotUpdateBody(2, 5);

            // Story 038's VR-2 method: ONE recorder, enabled around Flush only, read right after it is disabled,
            // never toggled inside the window.
            Recorder recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();

            int MeasuredFlush()
            {
                EnqueueMany(measured, client.ServerSideId, body, VS4_MESSAGES_PER_FLUSH);
                recorder.enabled = true;
                measured.Flush(_tick++);
                recorder.enabled = false;
                return recorder.sampleBlockCount;
            }

            try
            {
                // Warm-up, not counted: lists grown, NGO's send queue and JIT warm.
                for (int f = 0; f < VS4_WARMUP_FRAMES; f++)
                {
                    EnqueueMany(measured, client.ServerSideId, body, VS4_MESSAGES_PER_FLUSH);
                    measured.Flush(_tick++);
                    yield return null;
                }
                int warmUpCalls = seam.Calls;

                // Phase A, asserted: the seam does not forward, so the window holds the outbox's code alone.
                seam.Forward = false;
                int outboxSamples = 0;
                int callsBefore = seam.Calls;
                for (int f = 0; f < VS4_MEASURED_FLUSHES; f++)
                {
                    outboxSamples += MeasuredFlush();
                    yield return null;
                }
                int measuredMessages = seam.Calls - callsBefore;

                // Control: one 64 KB array per message inside the same window must be counted.
                seam.AllocateControl = true;
                int controlSamples = 0;
                int controlCallsBefore = seam.Calls;
                for (int f = 0; f < VS4_CONTROL_FLUSHES; f++)
                {
                    controlSamples += MeasuredFlush();
                    yield return null;
                }
                seam.AllocateControl = false;
                int controlMessages = seam.Calls - controlCallsBefore;

                // Phase B, recorded only: the seam forwards, so the window holds the outbox, the sender and
                // NGO's queueing together.
                seam.Forward = true;
                int withNgoSamples = 0;
                int ngoCallsBefore = seam.Calls;
                for (int f = 0; f < VS4_MEASURED_FLUSHES; f++)
                {
                    withNgoSamples += MeasuredFlush();
                    yield return null;
                }
                int ngoMessages = seam.Calls - ngoCallsBefore;

                int totalForwarded = seam.Forwarded;
                yield return WaitFrames(() => client.Received.Count >= totalForwarded, DELIVERY_TIMEOUT_FRAMES);

                Debug.Log("[VS-4] GC.Alloc samples (profiler marker, current thread, one recorder around Flush, no Profiler window looked at): warmUpMessages=" + warmUpCalls
                    + " | outbox alone (seam not forwarding): flushes=" + VS4_MEASURED_FLUSHES + " messages=" + measuredMessages
                    + " samples (asserted 0)=" + outboxSamples
                    + " | control (one " + VS4_CONTROL_BYTES + "-byte array per message in the same window): messages=" + controlMessages
                    + " samples=" + controlSamples
                    + " | outbox + sender + NGO queueing (seam forwarding; recorded, not asserted): messages=" + ngoMessages
                    + " samples=" + withNgoSamples
                    + " (" + (ngoMessages > 0 ? ((double)withNgoSamples / ngoMessages).ToString("F2") : "n/a") + " per message)"
                    + " | client received " + client.Received.Count + " of " + totalForwarded + " forwarded.");

                Assert.GreaterOrEqual(measuredMessages, VS4_MEASURED_MINIMUM, "fewer than 500 messages passed the window");
                Assert.NotNull(seam.ControlSink);
                Assert.GreaterOrEqual(controlSamples, controlMessages, "the GC.Alloc marker did not count the control allocations");
                Assert.AreEqual(0, outboxSamples, "the outbox's Flush allocated managed memory");
            }
            finally
            {
                // Also after a failure: the recorder is global profiler state.
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }
        }

        [UnityTest]
        public IEnumerator test_vs5_oversize_1300_byte_message_through_the_sender_is_recorded_not_asserted()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            var sender = new NgoClientMessageSender(_server);
            byte[] big = BuildPatternMessage(VS5_MESSAGE_BYTES, 0x55);
            var captured = new List<string>();
            Application.LogCallback capture = (string text, string stack, LogType type) => captured.Add(type + ": " + text);
            string thrown = "none";
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true; // observation only, this test
                Application.logMessageReceived += capture;
                try
                {
                    sender.Send(client.ServerSideId, big, NetworkChannel.ReliableOrdered);
                }
                catch (Exception e)
                {
                    thrown = e.GetType().Name + ": " + e.Message;
                }

                for (int f = 0; f < VS5_OBSERVE_FRAMES; f++)
                {
                    yield return null;
                }

                int largest = 0;
                for (int i = 0; i < client.Received.Count; i++)
                {
                    largest = Math.Max(largest, client.Received[i].Bytes.Length);
                }
                Debug.Log("[VS-5] Editor run, " + VS5_MESSAGE_BYTES + "-byte message on ReliableOrdered (ReliableSequenced) through NgoClientMessageSender.Send: exception="
                    + thrown + "; log entries during the call and " + VS5_OBSERVE_FRAMES + " frames after: " + captured.Count
                    + " [" + string.Join(" | ", captured) + "]; client received " + client.Received.Count + " messages (largest "
                    + largest + " bytes); client still connected=" + client.Manager.IsConnectedClient
                    + "; server still listening=" + _server.IsListening);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        // ------------------------------------------------------------------ [SENDER]

        [UnityTest]
        public IEnumerator test_sender_one_message_per_channel_arrives_intact()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            var sender = new NgoClientMessageSender(_server);
            NetworkChannel[] channels = { NetworkChannel.Unreliable, NetworkChannel.ReliableUnordered, NetworkChannel.ReliableOrdered };
            var sent = new byte[channels.Length][];
            for (int i = 0; i < channels.Length; i++)
            {
                sent[i] = BuildPatternMessage(SENDER_MESSAGE_BYTES, (byte)(0x10 * (i + 1)));
                sender.Send(client.ServerSideId, sent[i], channels[i]);
            }

            yield return WaitFrames(() => client.Received.Count >= channels.Length, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            // Unreliable may be reordered in principle; match by the first byte, which is the tag.
            var matched = new bool[channels.Length];
            for (int r = 0; r < client.Received.Count; r++)
            {
                for (int i = 0; i < channels.Length; i++)
                {
                    if (BytesEqual(sent[i], client.Received[r].Bytes))
                    {
                        matched[i] = true;
                    }
                }
            }
            Debug.Log("[SENDER] one " + SENDER_MESSAGE_BYTES + "-byte message per channel: Unreliable->"
                + NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.Unreliable) + " intact=" + matched[0]
                + ", ReliableUnordered->" + NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.ReliableUnordered) + " intact=" + matched[1]
                + ", ReliableOrdered->" + NgoClientMessageSender.ToNetworkDelivery(NetworkChannel.ReliableOrdered) + " intact=" + matched[2]
                + "; received " + client.Received.Count + " messages in all.");

            Assert.AreEqual(channels.Length, client.Received.Count, "one message per channel must arrive");
            Assert.IsTrue(matched[0], "Unreliable message differs or is missing");
            Assert.IsTrue(matched[1], "ReliableUnordered message differs or is missing");
            Assert.IsTrue(matched[2], "ReliableOrdered message differs or is missing");
        }

        [UnityTest]
        public IEnumerator test_sender_on_a_manager_that_was_never_started_throws_invalid_operation_and_sends_nothing()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var idle = new OutboxClientHandle();
            idle.Go = new GameObject("TestOutboxIdleNetworkManager");
            idle.Go.SetActive(false);
            idle.Manager = idle.Go.AddComponent<NetworkManager>();
            var transport = idle.Go.AddComponent<UnityTransport>();
            idle.Manager.NetworkConfig = CreateConfig(transport);
            idle.Go.SetActive(true);
            _clients.Add(idle); // destroyed in tear-down
            var sender = new NgoClientMessageSender(idle.Manager);

            InvalidOperationException caught = null;
            try
            {
                sender.Send(1u, new byte[SENDER_MESSAGE_BYTES], NetworkChannel.ReliableOrdered);
            }
            catch (InvalidOperationException e)
            {
                caught = e;
            }

            Debug.Log("[SENDER] NetworkManager never started: Send threw InvalidOperationException=" + (caught != null));
            Assert.IsNotNull(caught);
            yield return null;
        }

        [UnityTest]
        public IEnumerator test_sender_to_an_unknown_client_id_is_recorded_not_asserted()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new OutboxClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            var sender = new NgoClientMessageSender(_server);
            var captured = new List<string>();
            Application.LogCallback capture = (string text, string stack, LogType type) => captured.Add(type + ": " + text);
            string thrown = "none";
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                Application.logMessageReceived += capture;
                try
                {
                    sender.Send(UNKNOWN_CLIENT_ID, new byte[SENDER_MESSAGE_BYTES], NetworkChannel.ReliableOrdered);
                }
                catch (Exception e)
                {
                    thrown = e.GetType().Name + ": " + e.Message;
                }
                yield return RunFrames(SETTLE_FRAMES);
                Debug.Log("[SENDER] Send to unknown client id " + UNKNOWN_CLIENT_ID + ": exception=" + thrown + "; log entries=" + captured.Count
                    + " [" + string.Join(" | ", captured) + "]; the real client received " + client.Received.Count + " messages");
            }
            finally
            {
                Application.logMessageReceived -= capture;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static NetworkConfig CreateConfig(UnityTransport transport)
        {
            return new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = null,
                ConnectionApproval = false,
                EnableSceneManagement = false,
            };
        }

        private IEnumerator ConnectClient(OutboxClientHandle h)
        {
            h.Go = new GameObject("TestOutboxClientNetworkManager");
            h.Go.SetActive(false);
            h.Manager = h.Go.AddComponent<NetworkManager>();
            var transport = h.Go.AddComponent<UnityTransport>();
            transport.SetConnectionData(TEST_ADDRESS, TEST_PORT);
            h.Manager.NetworkConfig = CreateConfig(transport);
            h.Go.SetActive(true);
            _clients.Add(h);

            int addsBefore = _intake.AddLog.Count;
            if (!h.Manager.StartClient())
            {
                h.Error = "NetworkManager.StartClient() returned false for the second NetworkManager in the process.";
                yield break;
            }

            yield return WaitFrames(() => h.Manager.IsConnectedClient && _intake.AddLog.Count > addsBefore, CONNECT_TIMEOUT_FRAMES);
            if (!(h.Manager.IsConnectedClient && _intake.AddLog.Count > addsBefore))
            {
                h.Error = "Client did not connect within " + CONNECT_TIMEOUT_FRAMES + " frames (clientConnected="
                    + h.Manager.IsConnectedClient + ", dispatcher AddConnection calls=" + (_intake.AddLog.Count - addsBefore)
                    + "), port " + TEST_PORT + " may be busy.";
                yield break;
            }
            h.ServerSideId = _intake.AddLog[addsBefore];

            // The client's own receive: copy each unnamed message, with the frame it arrived in.
            OutboxClientHandle handle = h;
            h.Manager.CustomMessagingManager.OnUnnamedMessage += (ulong senderId, FastBufferReader reader) =>
            {
                int length = reader.Length - reader.Position;
                byte[] bytes = new byte[length];
                reader.ReadBytesSafe(ref bytes, length);
                handle.Received.Add(new OutboxReceived { Bytes = bytes, Frame = Time.frameCount, Order = handle.Received.Count });
            };
        }

        private static void EnqueueMany(ClientMessageOutbox outbox, uint clientId, byte[] body, int count)
        {
            for (int i = 0; i < count; i++)
            {
                outbox.Enqueue(clientId, InventorySlotUpdate.MessageTypeId, body, false);
            }
        }

        private static byte[] BuildFullSyncBody()
        {
            var entries = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new InventorySlotEntry((byte)i, new ItemID((uint)(1000 + i)), i + 1, (byte)(i % 6));
            }
            var body = new byte[InventoryFullSync.BodySize];
            int written = InventoryFullSyncCodec.WriteBody(body, entries);
            Assert.AreEqual(InventoryFullSync.BodySize, written);
            return body;
        }

        private static byte[] BuildSlotUpdateBody(byte slotIndex, int quantity)
        {
            var entries = new[] { new InventorySlotEntry(slotIndex, new ItemID(2000u + slotIndex), quantity, 1) };
            var body = new byte[InventorySlotUpdate.GetBodySize(entries.Length)];
            int written = InventorySlotUpdateCodec.WriteBody(body, entries);
            Assert.AreEqual(body.Length, written);
            return body;
        }

        private static byte[] BuildExpected(ushort typeId, uint sequence, uint tick, byte[] body)
        {
            var message = new byte[ServerMessageEnvelope.WireSize + body.Length];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(typeId, sequence, tick));
            Buffer.BlockCopy(body, 0, message, ServerMessageEnvelope.WireSize, body.Length);
            return message;
        }

        private static byte[] BuildPatternMessage(int size, byte tag)
        {
            var bytes = new byte[size];
            bytes[0] = tag;
            for (int i = 1; i < size; i++)
            {
                bytes[i] = (byte)(i * 7 + tag);
            }
            return bytes;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static IEnumerator WaitFrames(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
            {
                yield return null;
            }
        }

        private static IEnumerator RunFrames(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }
        }

        private static void SafeShutdown(NetworkManager manager)
        {
            try
            {
                if (manager != null)
                {
                    manager.Shutdown();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TEARDOWN] Shutdown threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        private bool AllShutDown()
        {
            if (_server != null && (_server.ShutdownInProgress || _server.IsListening))
            {
                return false;
            }
            for (int i = 0; i < _clients.Count; i++)
            {
                NetworkManager m = _clients[i].Manager;
                if (m != null && (m.ShutdownInProgress || m.IsListening))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

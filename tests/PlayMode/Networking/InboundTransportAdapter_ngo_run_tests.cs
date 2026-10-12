using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using IronGrind.Currency;
using IronGrind.Networking;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Story 038 verification run (ADR-014 Verification Required 1-6): one server NetworkManager and
    /// client NetworkManagers in one process on UnityTransport over 127.0.0.1. Every test logs its
    /// observations as "[VR-n] ..." so they can be copied into
    /// production/qa/evidence/networking-core-038-ngo-verification.md. No host, no NetcodeIntegrationTest,
    /// no reflection. Clients are addressed through their own references, never through
    /// NetworkManager.Singleton.
    /// </summary>
    public sealed class InboundTransportAdapter_ngo_run_tests
    {
        private const ushort TEST_PORT = 7801;
        private const string TEST_ADDRESS = "127.0.0.1";
        private const int TARGET_FRAME_RATE = 60;

        private const int CONNECT_TIMEOUT_FRAMES = 600;
        private const int DELIVERY_TIMEOUT_FRAMES = 1200;
        private const int SHUTDOWN_TIMEOUT_FRAMES = 600;
        private const int SETTLE_FRAMES = 30;

        private const int SEQUENCE_BYTES = 4;
        private const int DEFAULT_MESSAGE_BYTES = 32;
        private const int SHORTEST_ACCEPTED_BYTES = InboundTransportAdapter.MIN_INBOUND_MESSAGE_BYTES;
        private const int LONGEST_ACCEPTED_BYTES = InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES;
        private const uint LENGTH_SENTINEL_SEQUENCE = 99u;
        private const string VERSION_PATTERN = @"^\d+\.\d+\.\d+";

        private const int VR5_MESSAGE_COUNT = 200;
        private const int VR5_MESSAGES_PER_FRAME = 5;
        private const int VR1_TRAFFIC_FRAMES = 90;
        private const int VR1_DISCONNECT_AT_FRAME = 45;
        private const int VR1_MESSAGES_PER_FRAME = 2;
        private const int VR3_CONNECTIONS = 3;
        private const int VR6_BURST_MESSAGES = 40;
        private const int VR2_WARMUP_FRAMES = 20;
        private const int VR2_CONTROL_MESSAGES = 20;
        private const int VR2_SEND_FRAMES = 100;
        private const int VR2_MESSAGES_PER_FRAME = 5;
        private const int VR2_CONTROL_BYTES = 65536;

        // ------------------------------------------------------------------ test doubles

        private sealed class Received
        {
            public uint ClientId;
            public byte[] Bytes;
            public int Frame;
            public bool InsideTick;
            public bool Accepted;
        }

        /// <summary>Intake and dispatcher in one double; records what the adapter does to it.</summary>
        private sealed class RecordingIntake : IInboundMessageIntake, IInboundRequestDispatcher
        {
            public readonly HashSet<uint> Known = new HashSet<uint>();
            public readonly List<string> CallLog = new List<string>();
            public readonly List<uint> AddLog = new List<uint>();
            public readonly List<uint> RemoveLog = new List<uint>();
            public readonly List<int> RemoveFrames = new List<int>();
            public readonly List<Received> Messages = new List<Received>();
            public TestTickDriver Driver;
            public bool RecordingEnabled = true;
            public Action OnFirstAccepted;
            public int AcceptedCount;
            public int AddInsideTickCount;
            public int RemoveInsideTickCount;

            public bool TryAccept(uint clientId, ReadOnlySpan<byte> message)
            {
                bool known = Known.Contains(clientId);
                if (known)
                {
                    AcceptedCount++;
                    if (OnFirstAccepted != null)
                    {
                        // Runs inside NGO's handler, with the rest of the batch still queued (VR-6).
                        Action hook = OnFirstAccepted;
                        OnFirstAccepted = null;
                        hook();
                    }
                }

                if (RecordingEnabled)
                {
                    Messages.Add(new Received
                    {
                        ClientId = clientId,
                        Bytes = message.ToArray(),
                        Frame = Time.frameCount,
                        InsideTick = Driver != null && Driver.IsInsideAdvanceTick,
                        Accepted = known,
                    });
                }
                return known;
            }

            public bool AddConnection(uint clientId)
            {
                CallLog.Add("Add " + clientId);
                if (Driver != null && Driver.IsInsideAdvanceTick)
                {
                    AddInsideTickCount++;
                }
                bool added = Known.Add(clientId);
                if (added)
                {
                    AddLog.Add(clientId);
                }
                return added;
            }

            public void RemoveConnection(uint clientId)
            {
                CallLog.Add("Remove " + clientId);
                if (Driver != null && Driver.IsInsideAdvanceTick)
                {
                    RemoveInsideTickCount++;
                }
                Known.Remove(clientId);
                RemoveLog.Add(clientId);
                RemoveFrames.Add(Time.frameCount);
            }

            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }
            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }
            public void Seal() { }
            public void DispatchTick(uint currentTick) { }
            public int HeldCount(CharacterID charId) { return 0; }

            public int CountInsideTick()
            {
                int n = 0;
                for (int i = 0; i < Messages.Count; i++)
                {
                    if (Messages[i].InsideTick)
                    {
                        n++;
                    }
                }
                return n;
            }

            public List<Received> AcceptedFrom(uint clientId)
            {
                var list = new List<Received>();
                for (int i = 0; i < Messages.Count; i++)
                {
                    if (Messages[i].Accepted && Messages[i].ClientId == clientId)
                    {
                        list.Add(Messages[i]);
                    }
                }
                return list;
            }
        }

        private sealed class ClientHandle
        {
            public GameObject Go;
            public NetworkManager Manager;
            public uint ServerSideId;
            public string Error;
        }

        // ------------------------------------------------------------------ fixture state

        private GameObject _serverGo;
        private GameObject _driverGo;
        private NetworkManager _server;
        private UnityTransport _serverTransport;
        private TestTickDriver _driver;
        private ServerTickLoop _tickLoop;
        private RecordingIntake _intake;
        private NgoInboundTransportShell _shell;
        private readonly List<ClientHandle> _clients = new List<ClientHandle>();
        private int _targetFrameRateBefore;
        private int _vSyncBefore;
        private string _setUpError;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _setUpError = null;
            _targetFrameRateBefore = Application.targetFrameRate;
            _vSyncBefore = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TARGET_FRAME_RATE;

            _intake = new RecordingIntake();
            _tickLoop = new ServerTickLoop();

            _driverGo = new GameObject("TestTickDriver");
            _driver = _driverGo.AddComponent<TestTickDriver>();
            _driver.Initialize(_tickLoop);
            _intake.Driver = _driver;

            // GameObject starts inactive so NetworkManager.OnEnable/Awake run only after the
            // NetworkConfig and the transport are in place (Awake reads NetworkConfig).
            _serverGo = new GameObject("TestServerNetworkManager");
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
                _shell = new NgoInboundTransportShell(_server, _intake, _intake);
                _shell.Start();
            }
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Runs after a failed test too; nothing here may throw.
            if (_shell != null)
            {
                _shell.Stop();
            }
            // Handlers the tests subscribed themselves are lambdas on the server's messaging manager,
            // which NGO drops on Shutdown.

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
            if (_driverGo != null)
            {
                Object.Destroy(_driverGo);
            }
            yield return null;

            _shell = null;
            _server = null;
            _serverGo = null;
            _driverGo = null;
            Application.targetFrameRate = _targetFrameRateBefore;
            QualitySettings.vSyncCount = _vSyncBefore;
        }

        // ------------------------------------------------------------------ tests

        [UnityTest]
        public IEnumerator test_config_server_starts_and_exchanges_message_with_no_prefab_no_approval_no_scene_management()
        {
            Assert.IsNull(_setUpError, _setUpError);
            NetworkConfig cfg = _server.NetworkConfig;
            Debug.Log("[CONFIG] PlayerPrefab=" + (cfg.PlayerPrefab == null ? "null" : cfg.PlayerPrefab.name)
                + " ConnectionApproval=" + cfg.ConnectionApproval
                + " EnableSceneManagement=" + cfg.EnableSceneManagement
                + " ForceSamePrefabs=" + cfg.ForceSamePrefabs + " (left at its default)"
                + " TickRate=" + cfg.TickRate + " NetworkTransport=" + cfg.NetworkTransport.GetType().Name
                + " endpoint=" + TEST_ADDRESS + ":" + TEST_PORT);

            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, 7u));
            yield return WaitFrames(() => _intake.AcceptedCount >= 1, DELIVERY_TIMEOUT_FRAMES);

            Assert.AreEqual(1, _intake.AcceptedCount, "one message was sent and must be accepted exactly once");
            Assert.AreEqual(7u, ReadSequence(_intake.AcceptedFrom(client.ServerSideId)[0].Bytes));
            Debug.Log("[CONFIG] server started and exchanged an unnamed message with these three settings; "
                + "none had to change. Client NGO id=" + client.Manager.LocalClientId
                + ", server-side id seen by dispatcher=" + client.ServerSideId);
        }

        [UnityTest]
        public IEnumerator test_vr5_reliable_sequenced_200_messages_arrive_in_order_once()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            uint next = 0;
            while (next < VR5_MESSAGE_COUNT)
            {
                for (int i = 0; i < VR5_MESSAGES_PER_FRAME && next < VR5_MESSAGE_COUNT; i++)
                {
                    SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, next), NetworkDelivery.ReliableSequenced);
                    next++;
                }
                yield return null;
            }
            yield return WaitFrames(() => _intake.AcceptedCount >= VR5_MESSAGE_COUNT, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES); // late duplicates would show up here

            List<Received> got = _intake.AcceptedFrom(client.ServerSideId);
            int outOfOrder = 0;
            var seen = new HashSet<uint>();
            int duplicates = 0;
            uint previous = 0;
            for (int i = 0; i < got.Count; i++)
            {
                uint seq = ReadSequence(got[i].Bytes);
                if (!seen.Add(seq))
                {
                    duplicates++;
                }
                if (i > 0 && seq != previous + 1)
                {
                    outOfOrder++;
                }
                previous = seq;
            }
            Debug.Log("[VR-5] sent=" + VR5_MESSAGE_COUNT + " accepted=" + got.Count + " duplicates=" + duplicates
                + " outOfOrderSteps=" + outOfOrder + " (ReliableSequenced, " + VR5_MESSAGES_PER_FRAME + " per frame)");

            Assert.AreEqual(VR5_MESSAGE_COUNT, got.Count, "every message must arrive");
            Assert.AreEqual(0, duplicates, "each message exactly once");
            Assert.AreEqual(0, outOfOrder, "in order");
            Assert.AreEqual(0u, ReadSequence(got[0].Bytes));
        }

        // VR-1, both tests: a check of frame position only. The tick of this run is an empty ServerTickLoop
        // that does no transport work, so nothing in it could call the handler; what the tests show is that
        // NGO's dispatch and callbacks do not land inside an Update-driven AdvanceTick.
        [UnityTest]
        public IEnumerator test_vr1_client_disconnect_never_enters_intake_inside_tick()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            uint seq = 0;
            for (int frame = 0; frame < VR1_TRAFFIC_FRAMES; frame++)
            {
                if (frame == VR1_DISCONNECT_AT_FRAME)
                {
                    client.Manager.Shutdown();
                }
                if (CanSend(client.Manager))
                {
                    for (int i = 0; i < VR1_MESSAGES_PER_FRAME; i++)
                    {
                        SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, seq++));
                    }
                }
                yield return null;
            }
            uint id = client.ServerSideId;
            yield return WaitFrames(() => _intake.RemoveLog.Contains(id), SHUTDOWN_TIMEOUT_FRAMES);

            Debug.Log("[VR-1] client-side disconnect: ticks=" + _driver.TickCount + " messagesSeen=" + _intake.Messages.Count
                + " accepted=" + _intake.AcceptedCount + " tryAcceptInsideTick=" + _intake.CountInsideTick()
                + " addConnectionInsideTick=" + _intake.AddInsideTickCount
                + " removeConnectionInsideTick=" + _intake.RemoveInsideTickCount
                + " removeCalls=" + _intake.RemoveLog.Count);

            Assert.Greater(_driver.TickCount, 0, "the test driver must have advanced the tick");
            Assert.Greater(_intake.AcceptedCount, 0, "messages must have flowed");
            Assert.IsTrue(_intake.RemoveLog.Contains(id), "the client-side disconnect must reach RemoveConnection");
            Assert.AreEqual(0, _intake.CountInsideTick(), "TryAccept was entered while AdvanceTick was running");
        }

        [UnityTest]
        public IEnumerator test_vr1_server_initiated_disconnect_never_enters_intake_inside_tick()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);
            uint id = client.ServerSideId;

            uint seq = 0;
            for (int frame = 0; frame < VR1_TRAFFIC_FRAMES; frame++)
            {
                if (frame == VR1_DISCONNECT_AT_FRAME)
                {
                    _shell.Disconnect(id);
                }
                if (CanSend(client.Manager))
                {
                    for (int i = 0; i < VR1_MESSAGES_PER_FRAME; i++)
                    {
                        SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, seq++));
                    }
                }
                yield return null;
            }
            yield return RunFrames(SETTLE_FRAMES);

            int removeCallsForId = 0;
            for (int i = 0; i < _intake.RemoveLog.Count; i++)
            {
                if (_intake.RemoveLog[i] == id)
                {
                    removeCallsForId++;
                }
            }
            Debug.Log("[VR-1] server-initiated disconnect: ticks=" + _driver.TickCount + " messagesSeen=" + _intake.Messages.Count
                + " accepted=" + _intake.AcceptedCount + " tryAcceptInsideTick=" + _intake.CountInsideTick()
                + " addConnectionInsideTick=" + _intake.AddInsideTickCount
                + " removeConnectionInsideTick=" + _intake.RemoveInsideTickCount
                + " removeCallsForId=" + removeCallsForId + " clientStillConnected=" + client.Manager.IsConnectedClient);

            Assert.Greater(_driver.TickCount, 0, "the test driver must have advanced the tick");
            Assert.Greater(_intake.AcceptedCount, 0, "messages must have flowed before the disconnect");
            Assert.AreEqual(1, removeCallsForId, "RemoveConnection exactly once for the id (not repeated by the transport event)");
            Assert.AreEqual(0, _intake.CountInsideTick(), "TryAccept was entered while AdvanceTick was running");
        }

        [UnityTest]
        public IEnumerator test_vr3_ids_across_connections_and_after_server_restart_are_recorded()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var ids = new List<uint>();
            var ngoIds = new List<ulong>();

            for (int n = 0; n < VR3_CONNECTIONS; n++)
            {
                var client = new ClientHandle();
                yield return ConnectClient(client);
                Assert.IsNull(client.Error, client.Error);
                ids.Add(client.ServerSideId);
                ngoIds.Add(client.Manager.LocalClientId);

                client.Manager.Shutdown();
                uint id = client.ServerSideId;
                yield return WaitFrames(() => _intake.RemoveLog.Contains(id) && !_intake.Known.Contains(id), SHUTDOWN_TIMEOUT_FRAMES);
                yield return WaitFrames(() => !client.Manager.ShutdownInProgress, SHUTDOWN_TIMEOUT_FRAMES);
                Object.Destroy(client.Go);
                _clients.Remove(client);
                yield return null;
            }

            bool repeats = false;
            var distinct = new HashSet<uint>(ids);
            if (distinct.Count != ids.Count)
            {
                repeats = true;
            }
            Debug.Log("[VR-3] successive connection ids (dispatcher AddConnection): " + string.Join(", ", ids)
                + " | client-side LocalClientId: " + string.Join(", ", ngoIds)
                + " | any id repeated within one server run after a disconnect: " + repeats);

            // Shut the server NetworkManager down and start it again in the same process.
            _shell.Stop();
            _server.Shutdown();
            yield return WaitFrames(() => !_server.ShutdownInProgress && !_server.IsListening, SHUTDOWN_TIMEOUT_FRAMES);
            bool stopped = !_server.IsListening;
            bool restarted = stopped && _server.StartServer();
            Assert.IsTrue(stopped, "server did not stop within " + SHUTDOWN_TIMEOUT_FRAMES + " frames");
            Assert.IsTrue(restarted, "server did not restart in the same process");
            _shell = new NgoInboundTransportShell(_server, _intake, _intake); // CustomMessagingManager is recreated by NGO
            _shell.Start();

            var after = new ClientHandle();
            yield return ConnectClient(after);
            Assert.IsNull(after.Error, after.Error);
            bool reusedAfterRestart = distinct.Contains(after.ServerSideId);
            Debug.Log("[VR-3] after NetworkManager shutdown and restart in one process: first id=" + after.ServerSideId
                + " (client LocalClientId=" + after.Manager.LocalClientId + "); equals an earlier id: " + reusedAfterRestart);

            Assert.AreEqual(VR3_CONNECTIONS, ids.Count);
            for (int i = 0; i < ngoIds.Count; i++)
            {
                Assert.LessOrEqual(ngoIds[i], (ulong)uint.MaxValue, "client id must fit in uint");
            }
            Assert.LessOrEqual(after.Manager.LocalClientId, (ulong)uint.MaxValue);
        }

        [UnityTest]
        public IEnumerator test_vr6a_raw_handler_after_direct_disconnect_client_is_recorded_not_asserted()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);
            ulong ngoId = client.ServerSideId;

            // The raw handler calls DisconnectClient on the first message it sees, from inside NGO's
            // receive loop: the rest of the burst, sent in the same frame, is still queued behind it.
            var rawFrames = new List<int>();
            int rawAtCall = -1;
            int acceptedAtCall = -1;
            int frameAtCall = -1;
            _server.CustomMessagingManager.OnUnnamedMessage += (ulong senderId, FastBufferReader reader) =>
            {
                rawFrames.Add(Time.frameCount);
                if (rawAtCall < 0)
                {
                    rawAtCall = rawFrames.Count;
                    acceptedAtCall = _intake.AcceptedCount;
                    frameAtCall = Time.frameCount;
                    _server.DisconnectClient(ngoId); // the deferred overload, called directly, not through the adapter
                }
            };

            for (int i = 0; i < VR6_BURST_MESSAGES; i++)
            {
                SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, (uint)i));
            }
            yield return WaitFrames(() => rawFrames.Count >= 1, DELIVERY_TIMEOUT_FRAMES);
            Assert.GreaterOrEqual(rawFrames.Count, 1, "no message reached the raw handler");
            yield return RunFrames(SETTLE_FRAMES);

            int sameFrame = 0;
            int laterFrames = 0;
            for (int i = rawAtCall; i < rawFrames.Count; i++)
            {
                if (rawFrames[i] == frameAtCall)
                {
                    sameFrame++;
                }
                else
                {
                    laterFrames++;
                }
            }
            int removeFrame = _intake.RemoveFrames.Count > 0 ? _intake.RemoveFrames[_intake.RemoveFrames.Count - 1] : -1;
            Debug.Log("[VR-6a] burst=" + VR6_BURST_MESSAGES + " rawHandlerBeforeCall=" + rawAtCall
                + " rawHandlerAfterCallSameFrame=" + sameFrame + " rawHandlerAfterCallLaterFrames=" + laterFrames
                + " intakeAcceptedAfterCall(adapter not told)=" + (_intake.AcceptedCount - acceptedAtCall)
                + " frameOfCall=" + frameAtCall + " frameOfRemoveConnection=" + removeFrame
                + ". DisconnectClient was called inside the handler of the first message. Record only.");
        }

        [UnityTest]
        public IEnumerator test_vr6b_adapter_disconnect_accepts_nothing_after_the_call()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);
            uint id = client.ServerSideId;

            // The adapter's Disconnect is called from inside the intake, on the first accepted message:
            // the rest of the burst, sent in the same frame, is still queued in NGO behind it.
            int markMessages = -1;
            int acceptedAtCall = -1;
            _intake.OnFirstAccepted = () =>
            {
                markMessages = _intake.Messages.Count + 1; // the first message is recorded after this hook
                acceptedAtCall = _intake.AcceptedCount;
                _shell.Disconnect(id);
            };

            for (int i = 0; i < VR6_BURST_MESSAGES; i++)
            {
                SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, (uint)i));
            }
            yield return WaitFrames(() => _intake.AcceptedCount >= 1, DELIVERY_TIMEOUT_FRAMES);
            Assert.GreaterOrEqual(_intake.AcceptedCount, 1, "no message reached the intake");
            yield return RunFrames(SETTLE_FRAMES);

            int acceptedAfter = _intake.AcceptedCount - acceptedAtCall;
            int rejectedAfter = 0;
            for (int i = markMessages; i < _intake.Messages.Count; i++)
            {
                if (!_intake.Messages[i].Accepted)
                {
                    rejectedAfter++;
                }
            }
            Debug.Log("[VR-6b] burst=" + VR6_BURST_MESSAGES + " acceptedBeforeCall=" + acceptedAtCall
                + " acceptedAfterCall=" + acceptedAfter + " reachedIntakeButRejectedAfterCall=" + rejectedAfter
                + (rejectedAfter == 0
                    ? " (NGO delivered nothing of that client after the call: the assertion was not exercised this run)"
                    : " (NGO still delivered queued messages after the call; the intake dropped them)"));

            Assert.AreEqual(0, acceptedAfter, "a message of the disconnected id was accepted after shell.Disconnect");
            if (rejectedAfter == 0)
            {
                Assert.Inconclusive("NGO delivered nothing of that client after the call, so the run showed nothing about VR-6.");
            }
        }

        [UnityTest]
        public IEnumerator test_vr2_shell_handler_and_core_allocate_nothing_per_message()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            _intake.RecordingEnabled = false; // non-recording, non-allocating intake from here on
            byte[] payload = BuildMessage(DEFAULT_MESSAGE_BYTES, 1u);

            // NGO calls the unnamed-message handlers in subscription order, with nothing but a Seek
            // between two of them. Subscribed in the order begin, shell, control, end, the profiler's
            // GC.Alloc marker is switched on right before the shell's handler and off right after it:
            // the window holds the shell's handler, the core and the intake double, and nothing of NGO.
            // This is the measurement of Is.Not.AllocatingGCMemory(), per message.
            _shell.Stop();
            Recorder gcAlloc = Recorder.Get("GC.Alloc");
            gcAlloc.enabled = false;
            gcAlloc.FilterToCurrentThread();
            int samples = 0;
            int measured = 0;
            bool allocateInWindow = false;
            byte[] controlSink = null;
            CustomMessagingManager messaging = _server.CustomMessagingManager;
            CustomMessagingManager.UnnamedMessageDelegate begin = (ulong id, FastBufferReader reader) =>
            {
                gcAlloc.enabled = true;
            };
            CustomMessagingManager.UnnamedMessageDelegate control = (ulong id, FastBufferReader reader) =>
            {
                if (allocateInWindow)
                {
                    controlSink = new byte[VR2_CONTROL_BYTES];
                }
            };
            CustomMessagingManager.UnnamedMessageDelegate end = (ulong id, FastBufferReader reader) =>
            {
                gcAlloc.enabled = false;
                samples += gcAlloc.sampleBlockCount;
                measured++;
            };
            try
            {
                messaging.OnUnnamedMessage += begin;
                _shell.Start();
                messaging.OnUnnamedMessage += control;
                messaging.OnUnnamedMessage += end;

                // Warm-up (JIT), not counted.
                for (int f = 0; f < VR2_WARMUP_FRAMES; f++)
                {
                    for (int i = 0; i < VR2_MESSAGES_PER_FRAME; i++)
                    {
                        SendMessage(client.Manager, payload);
                    }
                    yield return null;
                }
                int warmUpSent = VR2_WARMUP_FRAMES * VR2_MESSAGES_PER_FRAME;
                yield return WaitFrames(() => measured >= warmUpSent, DELIVERY_TIMEOUT_FRAMES);
                int warmUpSamples = samples;

                // Measured window.
                samples = 0;
                measured = 0;
                int acceptedStart = _intake.AcceptedCount;
                int sent = 0;
                for (int f = 0; f < VR2_SEND_FRAMES; f++)
                {
                    for (int i = 0; i < VR2_MESSAGES_PER_FRAME; i++)
                    {
                        SendMessage(client.Manager, payload);
                        sent++;
                    }
                    yield return null;
                }
                yield return WaitFrames(() => measured >= sent, DELIVERY_TIMEOUT_FRAMES);
                int measuredMessages = measured;
                int measuredSamples = samples;
                int accepted = _intake.AcceptedCount - acceptedStart;

                // Control: one allocation per message inside the window must be seen.
                samples = 0;
                measured = 0;
                allocateInWindow = true;
                for (int i = 0; i < VR2_CONTROL_MESSAGES; i++)
                {
                    SendMessage(client.Manager, payload);
                }
                yield return WaitFrames(() => measured >= VR2_CONTROL_MESSAGES, DELIVERY_TIMEOUT_FRAMES);
                int controlMessages = measured;
                int controlSamples = samples;
                allocateInWindow = false;

                Debug.Log("[VR-2] GC.Alloc samples inside the window [shell handler + core + intake double], per message: "
                    + "warmUpMessages=" + warmUpSent + " warmUpSamples=" + warmUpSamples
                    + " | measuredMessages=" + measuredMessages + " accepted=" + accepted + " gcAllocSamples=" + measuredSamples
                    + " | control (one " + VR2_CONTROL_BYTES + "-byte array per message inside the window): messages="
                    + controlMessages + " gcAllocSamples=" + controlSamples
                    + ". NGO's own receive path is outside the window and not measured. No Profiler window was looked at.");

                Assert.AreEqual(sent, measuredMessages, "every message must pass through the window");
                Assert.AreEqual(sent, accepted, "every message must reach the intake's accept path");
                Assert.NotNull(controlSink);
                Assert.GreaterOrEqual(controlSamples, controlMessages, "the GC.Alloc marker did not see the control allocations");
                Assert.AreEqual(0, measuredSamples, "the shell's handler or the core allocated managed memory");
            }
            finally
            {
                // Also after a failure: the recorder is global and the handlers would outlive the test.
                messaging.OnUnnamedMessage -= begin;
                messaging.OnUnnamedMessage -= control;
                messaging.OnUnnamedMessage -= end;
                gcAlloc.enabled = false;
                gcAlloc.CollectFromAllThreads();
            }
        }

        [UnityTest]
        public IEnumerator test_shell_length_check_passes_10_and_400_bytes_and_drops_9_and_401()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            // ReliableSequenced keeps the order, so once the sentinel is in, the four before it were handled.
            int[] sizes = { SHORTEST_ACCEPTED_BYTES - 1, SHORTEST_ACCEPTED_BYTES, LONGEST_ACCEPTED_BYTES, LONGEST_ACCEPTED_BYTES + 1 };
            for (int i = 0; i < sizes.Length; i++)
            {
                SendMessage(client.Manager, BuildMessage(sizes[i], (uint)i));
            }
            SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, LENGTH_SENTINEL_SEQUENCE));
            yield return WaitFrames(() => SawSequence(LENGTH_SENTINEL_SEQUENCE), DELIVERY_TIMEOUT_FRAMES);

            var lengths = new List<int>();
            for (int i = 0; i < _intake.Messages.Count; i++)
            {
                lengths.Add(_intake.Messages[i].Bytes.Length);
            }
            Debug.Log("[SHELL] sent lengths " + string.Join(", ", sizes) + " then a " + DEFAULT_MESSAGE_BYTES
                + "-byte sentinel; lengths that reached TryAccept: " + string.Join(", ", lengths));

            CollectionAssert.AreEqual(
                new[] { SHORTEST_ACCEPTED_BYTES, LONGEST_ACCEPTED_BYTES, DEFAULT_MESSAGE_BYTES }, lengths,
                "only the 10-byte, the 400-byte and the sentinel message may reach the intake, in that order");
        }

        [UnityTest]
        public IEnumerator test_shell_stop_ends_delivery_and_start_twice_resumes_it_once()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var client = new ClientHandle();
            yield return ConnectClient(client);
            Assert.IsNull(client.Error, client.Error);

            // A bare handler counts what NGO delivers, whatever the shell does.
            int raw = 0;
            _server.CustomMessagingManager.OnUnnamedMessage += (ulong senderId, FastBufferReader reader) => raw++;

            SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, 0u));
            yield return WaitFrames(() => raw >= 1, DELIVERY_TIMEOUT_FRAMES);
            int acceptedWhileStarted = _intake.AcceptedCount;

            _shell.Stop();
            _shell.Stop();
            SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, 1u));
            yield return WaitFrames(() => raw >= 2, DELIVERY_TIMEOUT_FRAMES);
            int acceptedWhileStopped = _intake.AcceptedCount - acceptedWhileStarted;

            _shell.Start();
            _shell.Start();
            SendMessage(client.Manager, BuildMessage(DEFAULT_MESSAGE_BYTES, 2u));
            yield return WaitFrames(() => raw >= 3, DELIVERY_TIMEOUT_FRAMES);
            int tryAcceptCalls = _intake.Messages.Count;

            Debug.Log("[SHELL] NGO delivered " + raw + " messages; accepted while started=" + acceptedWhileStarted
                + ", while stopped=" + acceptedWhileStopped + ", TryAccept calls in all after a double Start=" + tryAcceptCalls);

            Assert.AreEqual(3, raw, "NGO must have delivered all three messages");
            Assert.AreEqual(1, acceptedWhileStarted);
            Assert.AreEqual(0, acceptedWhileStopped, "a stopped shell passed a message on");
            Assert.AreEqual(2, tryAcceptCalls, "the first and the third message, once each");
        }

        [UnityTest]
        public IEnumerator test_shell_start_on_a_manager_that_is_not_started_throws_and_subscribes_nothing()
        {
            Assert.IsNull(_setUpError, _setUpError);
            var idle = new ClientHandle();
            idle.Go = new GameObject("TestIdleNetworkManager");
            idle.Go.SetActive(false);
            idle.Manager = idle.Go.AddComponent<NetworkManager>();
            var transport = idle.Go.AddComponent<UnityTransport>();
            idle.Manager.NetworkConfig = CreateConfig(transport);
            idle.Go.SetActive(true);
            _clients.Add(idle); // destroyed in tear-down
            var shell = new NgoInboundTransportShell(idle.Manager, _intake, _intake);

            bool messagingIsNull = idle.Manager.CustomMessagingManager == null;
            Assert.Throws<InvalidOperationException>(() => shell.Start());
            Assert.DoesNotThrow(() => shell.Stop());

            Debug.Log("[SHELL] NetworkManager never started: CustomMessagingManager is null=" + messagingIsNull
                + "; Start() threw InvalidOperationException.");
            Assert.IsTrue(messagingIsNull);
            yield return null;
        }

        [UnityTest]
        public IEnumerator test_vr4_versions_are_logged()
        {
            string netcode = "unread";
            string transport = "unread";
            try
            {
                string lockPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "packages-lock.json"));
                string text = File.ReadAllText(lockPath);
                netcode = FindLockVersion(text, "com.unity.netcode.gameobjects");
                transport = FindLockVersion(text, "com.unity.transport");
            }
            catch (Exception e)
            {
                netcode = transport = "packages-lock.json not readable at runtime: " + e.GetType().Name;
            }
            Debug.Log("[VR-4] Unity=" + Application.unityVersion
                + " packages-lock.json: com.unity.netcode.gameobjects=" + netcode + ", com.unity.transport=" + transport
                + " | assembly versions (not package versions): Unity.Netcode.Runtime="
                + typeof(NetworkManager).Assembly.GetName().Version
                + ", UnityTransport type loaded from " + typeof(UnityTransport).Assembly.GetName().Name
                + ". Signatures: this assembly compiled, so every NGO call in this file exists as written.");

            StringAssert.IsMatch(VERSION_PATTERN, netcode, "the NGO version was not read from packages-lock.json");
            StringAssert.IsMatch(VERSION_PATTERN, transport, "the Unity Transport version was not read from packages-lock.json");
            yield return null;
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

        private IEnumerator ConnectClient(ClientHandle h)
        {
            h.Go = new GameObject("TestClientNetworkManager");
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
                h.Error = "NetworkManager.StartClient() returned false for the second NetworkManager in the process. "
                    + "Known risk: two NetworkManagers in one process (NetworkManager.Singleton is "
                    + DescribeSingleton() + ").";
                yield break;
            }

            yield return WaitFrames(() => h.Manager.IsConnectedClient && _intake.AddLog.Count > addsBefore, CONNECT_TIMEOUT_FRAMES);
            if (!(h.Manager.IsConnectedClient && _intake.AddLog.Count > addsBefore))
            {
                h.Error = "Client did not connect within " + CONNECT_TIMEOUT_FRAMES + " frames (clientConnected="
                    + h.Manager.IsConnectedClient + ", dispatcher AddConnection calls=" + (_intake.AddLog.Count - addsBefore)
                    + "). Candidate causes: two NetworkManagers in one process (NetworkManager.Singleton is "
                    + DescribeSingleton() + "), port " + TEST_PORT + " busy, config-hash mismatch.";
                yield break;
            }
            h.ServerSideId = _intake.AddLog[addsBefore];
        }

        private string DescribeSingleton()
        {
            NetworkManager s = NetworkManager.Singleton; // diagnostics only
            if (s == null)
            {
                return "null";
            }
            return s == _server ? "the server manager" : "another manager (" + s.name + ")";
        }

        private static bool CanSend(NetworkManager client)
        {
            return client != null && client.IsConnectedClient && client.CustomMessagingManager != null;
        }

        private static void SendMessage(NetworkManager client, byte[] bytes, NetworkDelivery delivery = NetworkDelivery.ReliableSequenced)
        {
            using var writer = new FastBufferWriter(bytes.Length, Allocator.Temp);
            writer.WriteBytesSafe(bytes, bytes.Length);
            client.CustomMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer, delivery);
        }

        private static byte[] BuildMessage(int size, uint sequence)
        {
            Assert.GreaterOrEqual(size, SEQUENCE_BYTES);
            var bytes = new byte[size];
            bytes[0] = (byte)(sequence & 0xFF);
            bytes[1] = (byte)((sequence >> 8) & 0xFF);
            bytes[2] = (byte)((sequence >> 16) & 0xFF);
            bytes[3] = (byte)((sequence >> 24) & 0xFF);
            for (int i = SEQUENCE_BYTES; i < size; i++)
            {
                bytes[i] = (byte)i;
            }
            return bytes;
        }

        private bool SawSequence(uint sequence)
        {
            for (int i = 0; i < _intake.Messages.Count; i++)
            {
                if (_intake.Messages[i].Accepted && ReadSequence(_intake.Messages[i].Bytes) == sequence)
                {
                    return true;
                }
            }
            return false;
        }

        private static uint ReadSequence(byte[] bytes)
        {
            return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
        }

        private static string FindLockVersion(string json, string packageId)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(packageId) + "\"\\s*:\\s*\\{[^}]*?\"version\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : "not found";
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

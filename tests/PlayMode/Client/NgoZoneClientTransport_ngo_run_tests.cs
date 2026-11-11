using System;
using System.Collections;
using System.Collections.Generic;
using IronGrind.Client.Session;
using IronGrind.Networking;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Authentication Story 018 adapter run: one server NetworkManager and one client NetworkManager (carrying the
    /// <see cref="NgoZoneClientTransport"/> under test) in one process on UnityTransport over 127.0.0.1, in the
    /// shape of Story 038/039's fixtures (no player prefab, approval off, scene management off). Frame waits are
    /// bounded; nothing asserts wall-clock time.
    /// </summary>
    public sealed class NgoZoneClientTransport_ngo_run_tests
    {
        private const ushort TEST_PORT = 7803; // 038 uses 7801, 039 uses 7802
        private const string TEST_ADDRESS = "127.0.0.1";
        private const int CONNECT_TIMEOUT_FRAMES = 600;
        private const int DELIVERY_TIMEOUT_FRAMES = 1200;
        private const int SHUTDOWN_TIMEOUT_FRAMES = 600;
        private const int SETTLE_FRAMES = 60;

        private sealed class RecordingListener : IZoneClientTransportListener
        {
            public int Connected;
            public int Disconnected;
            public readonly List<byte[]> Messages = new List<byte[]>();

            public void OnConnected() { Connected++; }
            public void OnMessageReceived(ReadOnlySpan<byte> message) { Messages.Add(message.ToArray()); }
            public void OnDisconnected() { Disconnected++; }
        }

        private GameObject _serverGo;
        private GameObject _clientGo;
        private NetworkManager _server;
        private NetworkManager _clientManager;
        private NgoZoneClientTransport _adapter;
        private RecordingListener _listener;
        private readonly List<byte[]> _serverReceived = new List<byte[]>();
        private readonly List<ulong> _serverSideClientIds = new List<ulong>();
        private int _serverSeenDisconnects;
        private string _setUpError;
        private int _targetFrameRateBefore;
        private int _vSyncBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _setUpError = null;
            _serverReceived.Clear();
            _serverSideClientIds.Clear();
            _serverSeenDisconnects = 0;
            _targetFrameRateBefore = Application.targetFrameRate;
            _vSyncBefore = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            _serverGo = new GameObject("TestZoneAdapterServerNetworkManager");
            _serverGo.SetActive(false);
            _server = _serverGo.AddComponent<NetworkManager>();
            var serverTransport = _serverGo.AddComponent<UnityTransport>();
            serverTransport.SetConnectionData(TEST_ADDRESS, TEST_PORT);
            _server.NetworkConfig = CreateConfig(serverTransport);
            _serverGo.SetActive(true);

            if (!_server.StartServer())
            {
                _setUpError = "NetworkManager.StartServer() returned false on " + TEST_ADDRESS + ":" + TEST_PORT + " (port busy?).";
            }
            else
            {
                _server.OnClientConnectedCallback += id =>
                {
                    if (id != NetworkManager.ServerClientId)
                    {
                        _serverSideClientIds.Add(id);
                    }
                };
                _server.OnClientDisconnectCallback += id => _serverSeenDisconnects++;
                _server.CustomMessagingManager.OnUnnamedMessage += (ulong senderId, FastBufferReader reader) =>
                {
                    int length = reader.Length - reader.Position;
                    byte[] bytes = new byte[length];
                    reader.ReadBytesSafe(ref bytes, length);
                    _serverReceived.Add(bytes);
                };
            }

            _clientGo = new GameObject("TestZoneAdapterClientNetworkManager");
            _clientGo.SetActive(false);
            _clientManager = _clientGo.AddComponent<NetworkManager>();
            var clientTransport = _clientGo.AddComponent<UnityTransport>();
            _clientManager.NetworkConfig = CreateConfig(clientTransport);
            _adapter = _clientGo.AddComponent<NgoZoneClientTransport>();
            _clientGo.SetActive(true);
            _adapter.Init(_clientManager, clientTransport);
            _listener = new RecordingListener();
            _adapter.SetListener(_listener);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_adapter != null)
            {
                _adapter.Stop();
            }
            SafeShutdown(_clientManager);
            SafeShutdown(_server);
            yield return WaitFrames(AllShutDown, SHUTDOWN_TIMEOUT_FRAMES);
            if (_clientGo != null)
            {
                Object.Destroy(_clientGo);
            }
            if (_serverGo != null)
            {
                Object.Destroy(_serverGo);
            }
            yield return null;
            _adapter = null;
            _clientManager = null;
            _server = null;
            Application.targetFrameRate = _targetFrameRateBefore;
            QualitySettings.vSyncCount = _vSyncBefore;
        }

        [UnityTest]
        public IEnumerator test_adapter_start_raises_on_connected_once()
        {
            Assert.IsNull(_setUpError, _setUpError);
            ((IZoneClientTransport)_adapter).Start(TEST_ADDRESS, TEST_PORT);
            yield return WaitFrames(() => _listener.Connected >= 1, CONNECT_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            Assert.AreEqual(1, _listener.Connected, "OnConnected must be raised exactly once");
            Assert.AreEqual(0, _listener.Disconnected);
        }

        [UnityTest]
        public IEnumerator test_adapter_send_handshake_sized_message_arrives_byte_identical()
        {
            Assert.IsNull(_setUpError, _setUpError);
            yield return Connect();

            byte[] message = BuildMessage(SessionHandshake.MessageTypeId, SessionHandshake.BodySize);
            _adapter.Send(message);
            yield return WaitFrames(() => _serverReceived.Count >= 1, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            Assert.AreEqual(1, _serverReceived.Count, "exactly one message must arrive");
            CollectionAssert.AreEqual(message, _serverReceived[0]);
        }

        [UnityTest]
        public IEnumerator test_adapter_send_heartbeat_arrives_byte_identical()
        {
            Assert.IsNull(_setUpError, _setUpError);
            yield return Connect();

            byte[] message = BuildMessage(HeartbeatMessage.MessageTypeId, 0);
            _adapter.Send(message);
            yield return WaitFrames(() => _serverReceived.Count >= 1, DELIVERY_TIMEOUT_FRAMES);

            Assert.GreaterOrEqual(_serverReceived.Count, 1, "an unreliable heartbeat must arrive on loopback");
            CollectionAssert.AreEqual(message, _serverReceived[0]);
        }

        [UnityTest]
        public IEnumerator test_adapter_server_message_reaches_listener_byte_identical()
        {
            Assert.IsNull(_setUpError, _setUpError);
            yield return Connect();

            byte[] message = BuildMessage(ZoneStateSnapshotFragment.MessageTypeId, 200);
            using (var writer = new FastBufferWriter(message.Length, Allocator.Temp))
            {
                writer.WriteBytesSafe(message, message.Length);
                _server.CustomMessagingManager.SendUnnamedMessage(_serverSideClientIds[0], writer, NetworkDelivery.ReliableSequenced);
            }

            yield return WaitFrames(() => _listener.Messages.Count >= 1, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            Assert.AreEqual(1, _listener.Messages.Count, "exactly one message must reach the listener");
            CollectionAssert.AreEqual(message, _listener.Messages[0]);
        }

        [UnityTest]
        public IEnumerator test_adapter_stop_raises_no_on_disconnected()
        {
            Assert.IsNull(_setUpError, _setUpError);
            yield return Connect();

            _adapter.Stop();
            yield return WaitFrames(() => _serverSeenDisconnects >= 1, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            Assert.GreaterOrEqual(_serverSeenDisconnects, 1, "the server must have seen the client leave (the stop took effect)");
            Assert.AreEqual(0, _listener.Disconnected, "a deliberate Stop must not raise OnDisconnected");
        }

        [UnityTest]
        public IEnumerator test_adapter_server_disconnect_raises_on_disconnected_once()
        {
            Assert.IsNull(_setUpError, _setUpError);
            yield return Connect();

            _server.DisconnectClient(_serverSideClientIds[0]);
            yield return WaitFrames(() => _listener.Disconnected >= 1, DELIVERY_TIMEOUT_FRAMES);
            yield return RunFrames(SETTLE_FRAMES);

            Assert.AreEqual(1, _listener.Disconnected, "OnDisconnected must be raised exactly once");
        }

        // ------------------------------------------------------------------ helpers

        private IEnumerator Connect()
        {
            ((IZoneClientTransport)_adapter).Start(TEST_ADDRESS, TEST_PORT);
            yield return WaitFrames(() => _listener.Connected >= 1 && _serverSideClientIds.Count >= 1, CONNECT_TIMEOUT_FRAMES);
            Assert.AreEqual(1, _listener.Connected, "the adapter did not connect within " + CONNECT_TIMEOUT_FRAMES + " frames");
            Assert.GreaterOrEqual(_serverSideClientIds.Count, 1, "the server did not see the client");
        }

        private static byte[] BuildMessage(ushort messageTypeId, int bodyBytes)
        {
            var message = new byte[ServerMessageEnvelope.WireSize + bodyBytes];
            MessageEnvelopeCodec.Write(message, new ServerMessageEnvelope(messageTypeId, 1u, 7u));
            for (int i = 0; i < bodyBytes; i++)
            {
                message[ServerMessageEnvelope.WireSize + i] = (byte)(i * 5 + 3);
            }
            return message;
        }

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
            return (_server == null || (!_server.ShutdownInProgress && !_server.IsListening))
                && (_clientManager == null || (!_clientManager.ShutdownInProgress && !_clientManager.IsListening));
        }
    }
}

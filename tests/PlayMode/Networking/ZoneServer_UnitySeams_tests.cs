using System;
using System.Collections;
using System.Text.RegularExpressions;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneServer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace IronGrind.PlayModeTests
{
    /// <summary>
    /// Networking Core Story 045 (AC-2, AC-3, AC-4): the Unity seams of the zone server over a loopback
    /// <c>NetworkManager</c> in the Editor. The server listens on <see cref="TEST_PORT"/> on all interfaces.
    /// </summary>
    public sealed class ZoneServer_UnitySeams_tests
    {
        private const ushort TEST_PORT = 7803; // 038 uses 7801, 039 uses 7802
        private const int TICK_TIMEOUT_FRAMES = 600;
        private const int SHUTDOWN_CAP_FRAMES = 100;
        private const int TARGET_FRAME_RATE = 60;

        private NgoZoneNetwork _network;
        private int _targetFrameRateBefore;
        private int _vSyncBefore;
        private int _handlerCalls;

        private sealed class NullIntake : IInboundMessageIntake, IInboundRequestDispatcher
        {
            public bool TryAccept(uint clientId, ReadOnlySpan<byte> message) { return true; }
            public bool AddConnection(uint clientId) { return true; }
            public void RemoveConnection(uint clientId) { }
            public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler) { }
            public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink) { }
            public void Seal() { }
            public void DispatchTick(uint currentTick) { }
            public int HeldCount(CharacterID charId) { return 0; }
        }

        [SetUp]
        public void SetUp()
        {
            _targetFrameRateBefore = Application.targetFrameRate;
            _vSyncBefore = QualitySettings.vSyncCount;
            _handlerCalls = 0;
            _network = new NgoZoneNetwork();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_network != null && _network.IsAlive)
            {
                GameObject go = _network.Manager.gameObject;
                _network.Shutdown();
                for (int i = 0; i < SHUTDOWN_CAP_FRAMES && _network.ShutdownInProgress; i++)
                {
                    yield return null;
                }

                Object.Destroy(go);
                yield return null;
            }

            Application.targetFrameRate = _targetFrameRateBefore;
            QualitySettings.vSyncCount = _vSyncBefore;
        }

        private void OnTick()
        {
            _handlerCalls++;
        }

        private void StartNetwork()
        {
            _network.PrepareObject();
            _network.Configure(20u, TARGET_FRAME_RATE, 0, TEST_PORT);
            Assert.IsTrue(_network.StartServer(), "StartServer returned false (port " + TEST_PORT + " busy?).");
        }

        [UnityTest]
        public IEnumerator test_ngo_zone_network_start_server_true_and_tick_events_call_the_handler_at_least_three_times()
        {
            StartNetwork();
            Assert.AreEqual(20u, _network.Manager.NetworkConfig.TickRate);
            Assert.IsFalse(_network.Manager.NetworkConfig.EnableSceneManagement);
            Assert.AreEqual(TARGET_FRAME_RATE, Application.targetFrameRate);
            Assert.AreEqual(0, QualitySettings.vSyncCount);

            _network.SubscribeTick(OnTick);
            for (int i = 0; i < TICK_TIMEOUT_FRAMES && _handlerCalls < 3; i++)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(_handlerCalls, 3, "Three tick events should call the handler three times.");
            int callsAtUnsubscribe = _handlerCalls;
            _network.UnsubscribeTick(OnTick);
            yield return null;
            yield return null;
            Assert.AreEqual(callsAtUnsubscribe, _handlerCalls, "No call after unsubscribe.");
        }

        [UnityTest]
        public IEnumerator test_ngo_zone_network_shutdown_ends_with_shutdown_in_progress_false_within_the_cap()
        {
            StartNetwork();
            yield return null;

            _network.Shutdown();
            int frames = 0;
            while (_network.ShutdownInProgress && frames < SHUTDOWN_CAP_FRAMES)
            {
                frames++;
                yield return null;
            }

            Assert.IsFalse(_network.ShutdownInProgress, "Shutdown did not finish within " + SHUTDOWN_CAP_FRAMES + " frames.");
            Assert.IsTrue(_network.IsAlive, "The manager object still exists after Shutdown.");
        }

        [UnityTest]
        public IEnumerator test_ngo_zone_network_is_alive_false_after_the_manager_object_is_destroyed()
        {
            StartNetwork();
            Assert.IsTrue(_network.IsAlive);
            GameObject go = _network.Manager.gameObject;

            Object.Destroy(go);
            yield return null;

            Assert.IsFalse(_network.IsAlive);
            Assert.IsFalse(_network.ShutdownInProgress);
            Assert.DoesNotThrow(() => _network.Shutdown(), "Shutdown must tolerate a destroyed manager.");
            Assert.DoesNotThrow(() => _network.UnsubscribeTick(OnTick));
        }

        [UnityTest]
        public IEnumerator test_ngo_zone_network_unsubscribe_tick_is_safe_before_start_and_after_shutdown()
        {
            Assert.DoesNotThrow(() => _network.UnsubscribeTick(OnTick), "Before PrepareObject.");
            _network.PrepareObject();
            Assert.DoesNotThrow(() => _network.UnsubscribeTick(OnTick), "Before StartServer.");
            _network.Configure(20u, TARGET_FRAME_RATE, 0, TEST_PORT);
            Assert.IsTrue(_network.StartServer());
            _network.SubscribeTick(OnTick);
            yield return null;

            _network.Shutdown();
            for (int i = 0; i < SHUTDOWN_CAP_FRAMES && _network.ShutdownInProgress; i++)
            {
                yield return null;
            }

            Assert.DoesNotThrow(() => _network.UnsubscribeTick(OnTick), "After Shutdown.");
            Assert.DoesNotThrow(() => _network.UnsubscribeTick(OnTick), "Twice.");
        }

        [UnityTest]
        public IEnumerator test_unity_scene_loader_unknown_scene_is_done_and_not_succeeded()
        {
            var loader = new UnitySceneLoader(null, null, new IronGrind.ServerLogic.ZoneInstancing.ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Error, new Regex("NoSuchZoneScene"));

            loader.BeginLoad("NoSuchZoneScene");
            yield return null;

            Assert.IsTrue(loader.IsDone);
            Assert.IsFalse(loader.Succeeded);
        }

        [Test]
        public void test_unity_scene_loader_add_nav_mesh_data_returns_default_and_logs_the_stand_in_line_once()
        {
            var loader = new UnitySceneLoader(null, null, new IronGrind.ServerLogic.ZoneInstancing.ZoneDefinitionSlot());
            LogAssert.Expect(LogType.Log, "[StandIn] no zone NavMesh data");

            UnityEngine.AI.NavMeshDataInstance first = loader.AddNavMeshData();
            UnityEngine.AI.NavMeshDataInstance second = loader.AddNavMeshData();

            Assert.IsFalse(first.valid);
            Assert.IsFalse(second.valid);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator test_ngo_zone_inbound_transport_forwards_start_and_stop_to_the_shell()
        {
            // Not started manager: the shell's Start throws, which proves the call was forwarded.
            _network.PrepareObject();
            var intake = new NullIntake();
            IZoneInboundTransport early = _network.CreateInboundTransport(intake, intake, null);
            Assert.Throws<InvalidOperationException>(() => early.Start());
            Assert.DoesNotThrow(() => early.Stop(), "Stop before a successful Start is a no-op.");

            _network.Configure(20u, TARGET_FRAME_RATE, 0, TEST_PORT);
            Assert.IsTrue(_network.StartServer());
            yield return null;

            IZoneInboundTransport transport = _network.CreateInboundTransport(intake, intake, null);
            Assert.DoesNotThrow(() => transport.Start(), "Start on a started server.");
            Assert.DoesNotThrow(() => transport.Start(), "Start is idempotent.");
            Assert.DoesNotThrow(() => transport.Stop());
            Assert.DoesNotThrow(() => transport.Stop(), "Stop is idempotent.");
        }
    }
}

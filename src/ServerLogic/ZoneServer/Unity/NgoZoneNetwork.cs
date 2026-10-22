using System;
using IronGrind.Networking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneNetwork"/> over the NGO <c>NetworkManager</c> (ADR-009 Amendment 1 S1, S8, teardown step 6).
    /// It creates the root <c>NetworkManager</c> object itself in <see cref="PrepareObject"/> with
    /// <c>UnityTransport</c>, NGO's <c>DontDestroy</c> default (true) and scene management off, and null-guards a
    /// manager that was destroyed (in the Editor <c>NetworkManager.OnDestroy</c> can run before the host's).
    /// Main thread only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Listen address and port.</b> <see cref="Configure"/> sets the <c>-port</c> value on the transport and listens
    /// on all interfaces (0.0.0.0); clients reach the zone through the address the gateway hands out (user decision
    /// 2026-10-22, Story 045 <c>/dev-story</c>).
    /// </para>
    /// <para>
    /// <b>Tick probe.</b> In the Editor and development builds the handler given to <see cref="SubscribeTick"/> is
    /// wrapped by a named method that first feeds a <see cref="ZoneTickProbe"/> (AC-9 measurement); in a release build
    /// the handler is subscribed directly.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var network = new NgoZoneNetwork();
    /// network.PrepareObject();                    // S1
    /// network.Configure(20u, 20, 0, 7777);        // S8
    /// if (network.StartServer()) { network.SubscribeTick(OnNetworkTick); }
    /// // teardown: network.UnsubscribeTick(OnNetworkTick); network.Shutdown();
    /// </code>
    /// </example>
    public sealed class NgoZoneNetwork : IZoneNetwork
    {
        private const string OBJECT_NAME = "ZoneNetworkManager";
        private const string LISTEN_ADDRESS = "0.0.0.0";

        private GameObject _object;
        private NetworkManager _manager;
        private Action _handler;
        private Action _subscribed;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private Action _wrappedHandler;
        private ZoneTickProbe _probe;
#endif

        /// <inheritdoc/>
        public bool IsAlive => _manager != null;

        /// <inheritdoc/>
        public bool ShutdownInProgress => _manager != null && _manager.ShutdownInProgress;

        /// <summary>The managed <c>NetworkManager</c>, or null before <see cref="PrepareObject"/> / after it was destroyed.</summary>
        public NetworkManager Manager => _manager != null ? _manager : null;

        /// <inheritdoc/>
        public void PrepareObject()
        {
            if (_manager != null)
            {
                return;
            }

            // Created inactive so NetworkManager.Awake reads a NetworkConfig that is already in place. A new
            // GameObject is a root object; NetworkManager.DontDestroy stays at NGO's default (true).
            _object = new GameObject(OBJECT_NAME);
            _object.SetActive(false);
            try
            {
                _manager = _object.AddComponent<NetworkManager>();
                var transport = _object.AddComponent<UnityTransport>();
                _manager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    PlayerPrefab = null,
                    ConnectionApproval = false,
                    EnableSceneManagement = false,
                };
            }
            catch
            {
                // A half-built object is not left behind (S1 then fails and teardown finds no manager).
                _manager = null;
                UnityEngine.Object.Destroy(_object);
                _object = null;
                throw;
            }

            _object.SetActive(true);
        }

        /// <inheritdoc/>
        public void Configure(uint tickRate, int targetFrameRate, int vSyncCount, ushort port)
        {
            RequireManager();
            // The ConnectionData fields, not SetConnectionData: one of its overloads takes a NetworkEndpoint, which
            // would make this assembly reference Unity.Networking.Transport (outside ADR-012 Decision 1's list).
            var transport = (UnityTransport)_manager.NetworkConfig.NetworkTransport;
            transport.ConnectionData.Address = LISTEN_ADDRESS;
            transport.ConnectionData.Port = port;
            transport.ConnectionData.ServerListenAddress = LISTEN_ADDRESS;
            _manager.NetworkConfig.TickRate = tickRate;
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount = vSyncCount;
        }

        /// <inheritdoc/>
        public bool StartServer()
        {
            RequireManager();
            return _manager.StartServer();
        }

        /// <inheritdoc/>
        public void SubscribeTick(Action handler)
        {
            RequireManager();
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            NetworkTickSystem tickSystem = _manager.NetworkTickSystem;
            if (tickSystem == null)
            {
                throw new InvalidOperationException("NetworkTickSystem exists only after StartServer.");
            }

            _handler = handler;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_probe == null)
            {
                _probe = new ZoneTickProbe();
            }

            if (_wrappedHandler == null)
            {
                _wrappedHandler = OnProbedTick;
            }

            _subscribed = _wrappedHandler;
#else
            _subscribed = handler;
#endif
            tickSystem.Tick += _subscribed;
        }

        /// <inheritdoc/>
        public void UnsubscribeTick(Action handler)
        {
            if (_subscribed == null || handler != _handler)
            {
                return;
            }

            // The tick system exists only after StartServer and may be gone after a shutdown.
            if (_manager != null)
            {
                NetworkTickSystem tickSystem = _manager.NetworkTickSystem;
                if (tickSystem != null)
                {
                    tickSystem.Tick -= _subscribed;
                }
            }

            _subscribed = null;
            _handler = null;
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _probe?.Report();
#endif
            if (_manager != null)
            {
                _manager.Shutdown();
            }
        }

        /// <inheritdoc/>
        public IClientMessageSendSeam CreateSendSeam()
        {
            RequireManager();
            return new NgoClientMessageSender(_manager);
        }

        /// <inheritdoc/>
        public IZoneInboundTransport CreateInboundTransport(
            IInboundMessageIntake intake,
            IInboundRequestDispatcher dispatcher,
            IClientConnectionListener clientListener)
        {
            RequireManager();
            return new NgoZoneInboundTransport(new NgoInboundTransportShell(_manager, intake, dispatcher, clientListener));
        }

        private void RequireManager()
        {
            if (_manager == null)
            {
                throw new InvalidOperationException("NetworkManager object missing: call PrepareObject first.");
            }
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Named method, created once: feeds the probe, then the composition's handler.
        private void OnProbedTick()
        {
            _probe.OnTick();
            _handler?.Invoke();
        }
#endif
    }
}

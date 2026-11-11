using System;
using IronGrind.Networking;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace IronGrind.Client.Session
{
    /// <summary>
    /// NGO adapter for <see cref="IZoneClientTransport"/> (Authentication Story 018). Sends and receives NGO
    /// unnamed messages (ADR-004 Amendment 1) to and from the server (client id 0). The message bytes are the
    /// envelope plus body exactly as <see cref="ClientZoneSession"/> builds them.
    /// </summary>
    /// <remarks>
    /// <para>Delivery: <c>Heartbeat</c> goes <see cref="NetworkDelivery.Unreliable"/>; every other message
    /// (<c>SessionHandshake</c> 0xE136, <c>ZoneEntryAck</c> 0xE134, both R-OD) goes <see cref="NetworkDelivery.Reliable"/>.</para>
    /// <para><c>OnDisconnected</c> is raised from <c>Update</c> (never inside NGO's callback) and never after a
    /// <see cref="Stop"/>. A <c>Start</c> issued while a previous client is still shutting down waits in
    /// <c>Update</c> until the shutdown is done.</para>
    /// <para>Attach it to the object that holds the <see cref="NetworkManager"/> and <see cref="UnityTransport"/>,
    /// assign them in the Inspector or call <see cref="Init"/>. The send path allocates nothing except NGO's own
    /// temp <see cref="FastBufferWriter"/>.</para>
    /// </remarks>
    public sealed class NgoZoneClientTransport : MonoBehaviour, IZoneClientTransport
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private UnityTransport _unityTransport;
        [SerializeField] private int _initialBufferBytes = 2048;

        private IZoneClientTransportListener _listener;
        private byte[] _receiveBuffer;
        private byte[] _sendBuffer;
        private CustomMessagingManager.UnnamedMessageDelegate _onUnnamedMessage;
        private Action<ulong> _onConnected;
        private Action<ulong> _onDisconnect;
        private bool _active;
        private bool _connectedRaised;
        private bool _pendingStart;
        private bool _pendingStartFailure;
        private bool _pendingDisconnect;
        private bool _callbacksSubscribed;
        private string _pendingHost;
        private ushort _pendingPort;

        /// <summary>Assigns the NGO objects when the component was added in code.</summary>
        /// <param name="networkManager">The client's network manager (config already assigned).</param>
        /// <param name="unityTransport">The transport the manager uses.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public void Init(NetworkManager networkManager, UnityTransport unityTransport)
        {
            _networkManager = networkManager != null ? networkManager : throw new ArgumentNullException(nameof(networkManager));
            _unityTransport = unityTransport != null ? unityTransport : throw new ArgumentNullException(nameof(unityTransport));
        }

        /// <inheritdoc/>
        public void SetListener(IZoneClientTransportListener listener)
        {
            _listener = listener;
        }

        /// <inheritdoc/>
        /// <remarks>Explicit implementation: a public <c>Start</c> with parameters on a MonoBehaviour is taken by Unity
        /// for its parameterless <c>Start()</c> message and logs "Start() can not take parameters".</remarks>
        void IZoneClientTransport.Start(string host, ushort port)
        {
            if (_networkManager == null || _unityTransport == null)
            {
                throw new InvalidOperationException("NgoZoneClientTransport needs a NetworkManager and a UnityTransport (Init or Inspector).");
            }

            EnsureBuffers();
            Unsubscribe();
            _active = true;
            _connectedRaised = false;
            _pendingDisconnect = false;
            _pendingStartFailure = false;
            _pendingHost = host;
            _pendingPort = port;
            _pendingStart = true;

            if (_networkManager.IsListening && !_networkManager.ShutdownInProgress)
            {
                _networkManager.Shutdown();
            }

            TryBeginStart();
        }

        /// <inheritdoc/>
        public void Stop()
        {
            _active = false;
            _pendingStart = false;
            _pendingStartFailure = false;
            _pendingDisconnect = false;
            Unsubscribe();

            if (_networkManager != null
                && !_networkManager.ShutdownInProgress
                && (_networkManager.IsListening || _networkManager.IsConnectedClient))
            {
                _networkManager.Shutdown();
            }
        }

        /// <inheritdoc/>
        public void Send(ReadOnlySpan<byte> message)
        {
            if (!_active || _networkManager == null || !_networkManager.IsConnectedClient
                || _networkManager.CustomMessagingManager == null)
            {
                return;
            }

            NetworkDelivery delivery = NetworkDelivery.Reliable;
            if (MessageEnvelopeCodec.TryRead(message, out ServerMessageEnvelope envelope)
                && envelope.MessageTypeId == HeartbeatMessage.MessageTypeId)
            {
                delivery = NetworkDelivery.Unreliable;
            }

            if (message.Length > _sendBuffer.Length)
            {
                _sendBuffer = new byte[message.Length];
            }

            message.CopyTo(_sendBuffer);
            using var writer = new FastBufferWriter(message.Length, Allocator.Temp);
            writer.WriteBytesSafe(_sendBuffer, message.Length);
            _networkManager.CustomMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, writer, delivery);
        }

        private void Update()
        {
            TryBeginStart();

            if (_pendingStartFailure)
            {
                _pendingStartFailure = false;
                RaiseDisconnected();
            }
            else if (_pendingDisconnect)
            {
                _pendingDisconnect = false;
                RaiseDisconnected();
            }
        }

        private void OnDestroy()
        {
            Stop();
        }

        private void EnsureBuffers()
        {
            int size = Math.Max(_initialBufferBytes, 1);
            if (_receiveBuffer == null)
            {
                _receiveBuffer = new byte[size];
                _sendBuffer = new byte[size];
                _onUnnamedMessage = OnUnnamedMessage;
                _onConnected = OnClientConnected;
                _onDisconnect = OnClientDisconnect;
            }
        }

        private void TryBeginStart()
        {
            if (!_pendingStart || _networkManager.ShutdownInProgress || _networkManager.IsListening)
            {
                return;
            }

            _pendingStart = false;
            _unityTransport.SetConnectionData(_pendingHost, _pendingPort);
            _networkManager.OnClientConnectedCallback += _onConnected;
            _networkManager.OnClientDisconnectCallback += _onDisconnect;
            _callbacksSubscribed = true;

            if (!_networkManager.StartClient())
            {
                Unsubscribe();
                _pendingStartFailure = true;
                return;
            }

            // CustomMessagingManager is a new instance after every start, so it is subscribed per start.
            _networkManager.CustomMessagingManager.OnUnnamedMessage += _onUnnamedMessage;
        }

        private void Unsubscribe()
        {
            if (_networkManager == null)
            {
                return;
            }

            if (_callbacksSubscribed)
            {
                _networkManager.OnClientConnectedCallback -= _onConnected;
                _networkManager.OnClientDisconnectCallback -= _onDisconnect;
                _callbacksSubscribed = false;
            }

            if (_networkManager.CustomMessagingManager != null && _onUnnamedMessage != null)
            {
                _networkManager.CustomMessagingManager.OnUnnamedMessage -= _onUnnamedMessage;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!_active || _connectedRaised || clientId != _networkManager.LocalClientId)
            {
                return;
            }

            _connectedRaised = true;
            _listener?.OnConnected();
        }

        private void OnClientDisconnect(ulong clientId)
        {
            if (!_active || (clientId != _networkManager.LocalClientId && clientId != NetworkManager.ServerClientId))
            {
                return;
            }

            _pendingDisconnect = true;
        }

        private void OnUnnamedMessage(ulong senderId, FastBufferReader reader)
        {
            if (!_active || senderId != NetworkManager.ServerClientId || _listener == null)
            {
                return;
            }

            int length = reader.Length - reader.Position;
            if (length <= 0)
            {
                return;
            }

            if (length > _receiveBuffer.Length)
            {
                _receiveBuffer = new byte[length];
            }

            reader.ReadBytesSafe(ref _receiveBuffer, length);
            _listener.OnMessageReceived(new ReadOnlySpan<byte>(_receiveBuffer, 0, length));
        }

        private void RaiseDisconnected()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            Unsubscribe();
            _listener?.OnDisconnected();
        }
    }
}

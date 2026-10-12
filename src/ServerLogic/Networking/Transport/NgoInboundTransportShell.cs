using System;
using Unity.Netcode;

namespace IronGrind.Networking
{
    /// <summary>
    /// The thin NGO shell of the inbound transport adapter (ADR-014 Decision 2, ADR-004 Amendment 1,
    /// Networking Core Story 038). It holds only what cannot be tested without NGO: the three
    /// subscriptions, the read from NGO's reader into one array allocated at construction, and the
    /// implementation of the disconnect seam. All behaviour is in <see cref="InboundTransportAdapter"/>.
    /// A plain class, not a MonoBehaviour; the server composition root creates it (ADR-012 Decision 4).
    /// </summary>
    /// <remarks>
    /// The caller owns the <see cref="NetworkManager"/>: it creates and starts it, and this shell starts
    /// and stops nothing of it. NGO runs the unnamed-message handler on the main thread, in EarlyUpdate or
    /// inside a transport disconnect event. The NGO signatures used here are from the engine reference
    /// ("NGO 2.13 — Custom Messaging, Transport and Frame Timing"), confirmed in the run of 2026-10-12.
    /// <b>Restart.</b> NGO drops its <c>CustomMessagingManager</c> on shutdown and creates a new one on the
    /// next start, so after a restart of the <see cref="NetworkManager"/> the caller calls
    /// <see cref="Stop"/> and then <see cref="Start"/>. <b>One buffer.</b> The handler passes a span of the
    /// one read buffer to the core, so it must not be re-entered while the intake still reads that span;
    /// <see cref="DisconnectClient"/> uses NGO's deferred overload, which raises no event inside the call.
    /// </remarks>
    /// <example>
    /// <code>
    /// var shell = new NgoInboundTransportShell(networkManager, dispatcher, dispatcher);
    /// shell.Start();
    /// // ... later
    /// shell.Stop();
    /// </code>
    /// </example>
    public sealed class NgoInboundTransportShell : IInboundTransportDisconnector
    {
        private readonly NetworkManager _networkManager;
        private readonly byte[] _buffer = new byte[InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES];
        private readonly InboundTransportAdapter _adapter;
        private CustomMessagingManager _subscribedMessaging;
        private bool _started;

        /// <summary>Creates the shell and its core, with this shell as the disconnect seam. Allocates the one read buffer.</summary>
        /// <param name="networkManager">The started server <see cref="NetworkManager"/>; not null.</param>
        /// <param name="intake">Receives each accepted message.</param>
        /// <param name="dispatcher">Receives connection changes.</param>
        /// <param name="clientListener">Told of added and removed clients (the outbox); null means none.</param>
        public NgoInboundTransportShell(NetworkManager networkManager, IInboundMessageIntake intake,
            IInboundRequestDispatcher dispatcher, IClientConnectionListener clientListener = null)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _adapter = new InboundTransportAdapter(intake, dispatcher, this, clientListener);
        }

        /// <summary>The core this shell feeds.</summary>
        public InboundTransportAdapter Adapter => _adapter;

        /// <summary>
        /// Subscribes to the unnamed-message event and to the connect and disconnect callbacks. Does
        /// nothing when already started. The <see cref="NetworkManager"/> is already started by the caller.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The <see cref="NetworkManager"/> is not started (it has no <c>CustomMessagingManager</c>);
        /// nothing is subscribed.
        /// </exception>
        public void Start()
        {
            if (_started)
            {
                return;
            }

            CustomMessagingManager messaging = _networkManager.CustomMessagingManager;
            if (messaging == null)
            {
                throw new InvalidOperationException(
                    "NgoInboundTransportShell.Start: the NetworkManager has no CustomMessagingManager. Start the NetworkManager first.");
            }

            _subscribedMessaging = messaging;
            _subscribedMessaging.OnUnnamedMessage += HandleUnnamedMessage;
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _started = true;
        }

        /// <summary>
        /// Unsubscribes everything <see cref="Start"/> subscribed. Idempotent; tolerates a messaging
        /// manager or a <see cref="NetworkManager"/> that NGO already shut down or destroyed.
        /// </summary>
        public void Stop()
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            if (_subscribedMessaging != null)
            {
                _subscribedMessaging.OnUnnamedMessage -= HandleUnnamedMessage;
            }
            _subscribedMessaging = null;

            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= HandleClientConnected;
                _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        /// <summary>Server-initiated disconnect of a connection; forwards to <see cref="InboundTransportAdapter.Disconnect"/>.</summary>
        /// <param name="clientId">The connection to disconnect.</param>
        public void Disconnect(uint clientId)
        {
            _adapter.Disconnect(clientId);
        }

        /// <summary>
        /// Disconnect seam: asks NGO to disconnect the client. This is the deferred overload; NGO
        /// disconnects in PostLateUpdate of the frame.
        /// </summary>
        /// <param name="transportClientId">The NGO client id.</param>
        public void DisconnectClient(ulong transportClientId)
        {
            _networkManager.DisconnectClient(transportClientId);
        }

        private void HandleUnnamedMessage(ulong clientId, FastBufferReader reader)
        {
            int length = reader.Length - reader.Position;
            if (length < InboundTransportAdapter.MIN_INBOUND_MESSAGE_BYTES
                || length > InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES)
            {
                // Dropped before reading; the core would drop it too.
                return;
            }

            // A local, because a readonly field cannot be passed by ref; NGO fills the array and does not replace it.
            byte[] buffer = _buffer;
            reader.ReadBytesSafe(ref buffer, length);
            _adapter.OnMessageReceived(clientId, new ReadOnlySpan<byte>(buffer, 0, length));
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                // The server's own id (raised on a host only) is not a client connection.
                return;
            }

            _adapter.OnClientConnected(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            _adapter.OnClientDisconnected(clientId);
        }
    }
}

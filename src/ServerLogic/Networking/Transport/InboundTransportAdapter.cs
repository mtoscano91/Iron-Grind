using System;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The plain C# core of the inbound transport adapter (ADR-014 Decision 2 and Decision 4, ADR-004
    /// Amendment 1, Networking Core Story 038). It checks the length of each received message
    /// (10 to <see cref="InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES"/> bytes), converts the
    /// transport's <c>ulong</c> client id to the <c>uint</c> the dispatcher uses, and makes exactly one
    /// call, <see cref="IInboundMessageIntake.TryAccept"/>. It reads no byte of a message and calls no
    /// game system. It also turns connect and disconnect events into
    /// <see cref="IInboundRequestDispatcher.AddConnection"/> / <see cref="IInboundRequestDispatcher.RemoveConnection"/>
    /// calls. No engine networking type appears in its signature; the NGO shell feeds it.
    /// </summary>
    /// <remarks>
    /// Main thread only, outside the tick. A message dropped for its length is not activity: the core
    /// never reports activity. An exception from the intake, the dispatcher or the transport seam is
    /// logged as one error and goes no further. <b>Pending set.</b> A fixed array, allocated at
    /// construction, holds, oldest first, the ids disconnected by <see cref="Disconnect"/>: their
    /// connection is already removed, so the next disconnect event of that id must not call
    /// <c>RemoveConnection</c> again. One entry is consumed by that event; a connect event for an id first
    /// discards any stale entry of it. An entry whose event never comes (the transport no longer knew the
    /// id) stays until it is the oldest of a full array, where the next <see cref="Disconnect"/> overwrites
    /// it; if a live entry is overwritten that way, its disconnect event calls <c>RemoveConnection</c> a
    /// second time, which the dispatcher ignores for a removed connection. <b>Refused connect.</b> Nothing
    /// is recorded for a refused connect: the disconnect event that follows calls <c>RemoveConnection</c>,
    /// which does nothing for an id the dispatcher never added and, if the refusal was "already known",
    /// removes the connection the transport has just closed. The dispatcher also logs its own error when
    /// <c>AddConnection</c> returns false; the adapter's error is a second, separate log line.
    /// <b>Client list (Story 039).</b> An optional <see cref="IClientConnectionListener"/> is told
    /// <c>AddClient</c> after <c>AddConnection</c> returns true and <c>RemoveClient</c> beside each
    /// <c>RemoveConnection</c> call (and nowhere the adapter skips it). An exception from the listener is logged
    /// as one error and changes nothing else. The class is also the <see cref="IClientDisconnectRequester"/>
    /// of the outbox.
    /// </remarks>
    /// <example>
    /// <code>
    /// var adapter = new InboundTransportAdapter(intake, dispatcher, transport);
    /// adapter.OnClientConnected(1UL);
    /// adapter.OnMessageReceived(1UL, receivedBytes);
    /// adapter.Disconnect(1u);
    /// </code>
    /// </example>
    public sealed class InboundTransportAdapter : IClientDisconnectRequester
    {
        /// <summary>Shortest message the core passes on: the 10-byte server-format envelope (CR-NET-7.1).</summary>
        public const int MIN_INBOUND_MESSAGE_BYTES = 10;

        /// <summary>Capacity of the pending-disconnect set; when it is full the oldest entry is overwritten.</summary>
        public const int PENDING_DISCONNECT_CAPACITY = 128;

        private const string LOG_PREFIX = "[InboundTransportAdapter]";

        private readonly IInboundMessageIntake _intake;
        private readonly IInboundRequestDispatcher _dispatcher;
        private readonly IInboundTransportDisconnector _transport;
        private readonly ulong[] _pendingDisconnects = new ulong[PENDING_DISCONNECT_CAPACITY];
        private readonly IClientConnectionListener _clientListener;
        private int _pendingCount;

        /// <summary>Creates the core. The first three arguments may not be null.</summary>
        /// <param name="intake">Receives each accepted message.</param>
        /// <param name="dispatcher">Receives connection changes.</param>
        /// <param name="transport">Asked to disconnect clients.</param>
        /// <param name="clientListener">Told of added and removed clients (the outbox); null means none.</param>
        public InboundTransportAdapter(IInboundMessageIntake intake, IInboundRequestDispatcher dispatcher,
            IInboundTransportDisconnector transport, IClientConnectionListener clientListener = null)
        {
            _intake = intake ?? throw new ArgumentNullException(nameof(intake));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clientListener = clientListener;
        }

        /// <summary>
        /// Handles one received message (ADR-014 Decision 2). A message shorter than 10 or longer than
        /// <see cref="InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES"/> bytes is dropped without a log; a
        /// transport id above <see cref="uint.MaxValue"/> is dropped with one error. Otherwise exactly one
        /// <see cref="IInboundMessageIntake.TryAccept"/> call is made with the same span. Never throws; allocates nothing on the accept path.
        /// </summary>
        /// <param name="transportClientId">The sender's transport id.</param>
        /// <param name="message">The received bytes; not kept.</param>
        public void OnMessageReceived(ulong transportClientId, ReadOnlySpan<byte> message)
        {
            int length = message.Length;
            if (length < MIN_INBOUND_MESSAGE_BYTES || length > InboundDispatchConstants.MAX_INBOUND_MESSAGE_BYTES)
            {
                return;
            }

            if (transportClientId > uint.MaxValue)
            {
                Debug.LogError($"{LOG_PREFIX} MessageDropped: transportClientId={transportClientId} - the id does not fit a uint.");
                return;
            }

            try
            {
                _intake.TryAccept((uint)transportClientId, message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} TryAcceptThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles a transport connect event (ADR-014 Decision 4): calls <c>AddConnection</c> once with the
        /// <c>uint</c> id. An id above <see cref="uint.MaxValue"/>, a refusal by the dispatcher, or an
        /// exception from it, logs one error and asks the transport to disconnect that id.
        /// </summary>
        /// <param name="transportClientId">The new client's transport id.</param>
        public void OnClientConnected(ulong transportClientId)
        {
            RemovePending(transportClientId);

            if (transportClientId > uint.MaxValue)
            {
                Debug.LogError($"{LOG_PREFIX} ConnectRefused: transportClientId={transportClientId} - the id does not fit a uint.");
                RequestTransportDisconnect(transportClientId);
                return;
            }

            bool added;
            try
            {
                added = _dispatcher.AddConnection((uint)transportClientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} AddConnectionThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
                RequestTransportDisconnect(transportClientId);
                return;
            }

            if (!added)
            {
                Debug.LogError($"{LOG_PREFIX} ConnectRefused: transportClientId={transportClientId} - the dispatcher did not add the connection.");
                RequestTransportDisconnect(transportClientId);
                return;
            }

            NotifyClientAdded(transportClientId);
        }

        /// <summary>
        /// Handles a transport disconnect event (ADR-014 Decision 4): calls <c>RemoveConnection</c> once,
        /// unless the id is pending (see the class remarks), in which case the pending entry is consumed
        /// and nothing is called. An id above <see cref="uint.MaxValue"/> was never added and calls nothing.
        /// </summary>
        /// <param name="transportClientId">The client's transport id.</param>
        public void OnClientDisconnected(ulong transportClientId)
        {
            if (RemovePending(transportClientId))
            {
                return;
            }

            if (transportClientId > uint.MaxValue)
            {
                // Never added, so the dispatcher does not know it.
                return;
            }

            try
            {
                _dispatcher.RemoveConnection((uint)transportClientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} RemoveConnectionThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
            }

            NotifyClientRemoved(transportClientId);
        }

        /// <summary>
        /// Server-initiated disconnect (ADR-014 Decision 2 step 1 and Decision 4): calls
        /// <c>RemoveConnection</c> first, so a message the transport still delivers for this client is
        /// dropped by the intake, then asks the transport to disconnect. The later transport disconnect
        /// event for the same id does not call <c>RemoveConnection</c> again. Never throws: an exception
        /// from the transport is logged as one error.
        /// </summary>
        /// <param name="clientId">The connection to disconnect.</param>
        public void Disconnect(uint clientId)
        {
            try
            {
                _dispatcher.RemoveConnection(clientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} RemoveConnectionThrew: transportClientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }

            NotifyClientRemoved(clientId);

            // Recorded before the transport call: the transport may raise its event inside the call.
            AddPending(clientId);
            if (!RequestTransportDisconnect(clientId))
            {
                // No disconnect was requested, so no event will consume the entry.
                RemovePending(clientId);
            }
        }

        // The listener calls never throw and change nothing else: one error per failure.
        private void NotifyClientAdded(ulong transportClientId)
        {
            if (_clientListener == null)
            {
                return;
            }

            try
            {
                _clientListener.AddClient((uint)transportClientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} ClientListenerThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void NotifyClientRemoved(ulong transportClientId)
        {
            if (_clientListener == null)
            {
                return;
            }

            try
            {
                _clientListener.RemoveClient((uint)transportClientId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} ClientListenerThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Returns false when the transport threw; the exception is logged and goes no further.
        private bool RequestTransportDisconnect(ulong transportClientId)
        {
            try
            {
                _transport.DisconnectClient(transportClientId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} DisconnectClientThrew: transportClientId={transportClientId} - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void AddPending(ulong transportClientId)
        {
            for (int i = 0; i < _pendingCount; i++)
            {
                if (_pendingDisconnects[i] == transportClientId)
                {
                    return;
                }
            }

            if (_pendingCount >= PENDING_DISCONNECT_CAPACITY)
            {
                // Full: the oldest entry is the likeliest to be one whose event never came.
                RemovePendingAt(0);
            }

            _pendingDisconnects[_pendingCount] = transportClientId;
            _pendingCount++;
        }

        private bool RemovePending(ulong transportClientId)
        {
            for (int i = 0; i < _pendingCount; i++)
            {
                if (_pendingDisconnects[i] == transportClientId)
                {
                    RemovePendingAt(i);
                    return true;
                }
            }

            return false;
        }

        // Keeps the entries oldest first.
        private void RemovePendingAt(int index)
        {
            _pendingCount--;
            Array.Copy(_pendingDisconnects, index + 1, _pendingDisconnects, index, _pendingCount - index);
        }
    }
}

using System;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.Client.Session
{
    /// <summary>States of <see cref="ClientZoneSession"/>.</summary>
    public enum ClientZoneSessionState
    {
        /// <summary>Created; <see cref="ClientZoneSession.Begin"/> not called yet.</summary>
        Idle,

        /// <summary>The initial credential login is in flight.</summary>
        LoggingIn,

        /// <summary>The transport was started; waiting for the connection.</summary>
        Connecting,

        /// <summary>The handshake was sent; waiting for the complete snapshot.</summary>
        AwaitingEntry,

        /// <summary>The snapshot is complete and the <c>ZoneEntryAck</c> was sent.</summary>
        Entered,

        /// <summary>The token-path reconnect login is in flight.</summary>
        Reconnecting,

        /// <summary>Terminal state.</summary>
        Ended
    }

    /// <summary>Callback for a completely reassembled zone snapshot.</summary>
    /// <param name="buffer">The reassembly buffer; only valid during the callback (copy what you need). May be longer than <paramref name="length"/>.</param>
    /// <param name="length">The number of valid snapshot bytes at the start of <paramref name="buffer"/>.</param>
    public delegate void SnapshotAssembledHandler(byte[] buffer, int length);

    /// <summary>
    /// Pure C# client zone entry (Authentication Story 018): login, connect, <c>SessionHandshake</c>, snapshot reassembly,
    /// <c>ZoneEntryAck</c>, heartbeat and one token reconnect. Drive it with <see cref="Update"/> every frame and route
    /// the transport callbacks to it (the constructor registers it as the transport listener).
    /// </summary>
    /// <remarks>
    /// <para>Decision A (TD-104): the entry gate is the complete snapshot only; <c>SessionReady</c> does not exist yet.
    /// Decision B: on a snapshot timeout no <c>ZoneSnapshotRequest</c> is sent; the session reconnects once.</para>
    /// <para>Reconnect allowance: one per entry chain, shared by the snapshot timeout and the unexpected close after
    /// the ack. It is restored when an entry completes (ack sent), so a later drop may reconnect once again.</para>
    /// <para><c>ZoneSessionEnded</c> from the server is treated exactly like a transport close.</para>
    /// <para>Connect timeout: if the transport has not connected <c>ConnectTimeoutSeconds</c> after it was started
    /// (initial connection or reconnect), the entry fails with <see cref="ZoneEntryFailureReason.ConnectTimeout"/>;
    /// it does not use the reconnect allowance.</para>
    /// <para>Heartbeat ticks: <see cref="HeartbeatActivityTracker"/> is tick based, so ticks are derived from the clock
    /// as <c>floor(Now * TICKS_PER_SECOND)</c> with a fixed internal 20 Hz rate.</para>
    /// <para>The envelope <c>ServerTickNumber</c> is the tick of the last envelope received on the current connection
    /// (0 before any); the sequence counter restarts at 1 on every connection. Neither the token nor the password is
    /// ever logged; the password is not retained after the initial login call.</para>
    /// </remarks>
    public sealed class ClientZoneSession : IZoneClientTransportListener
    {
        private const int TICKS_PER_SECOND = 20;
        private const int TOKEN_LENGTH = SessionHandshake.TOKEN_LENGTH;
        private const int MAX_OUTBOUND_MESSAGE_BYTES = ServerMessageEnvelope.WireSize + SessionHandshake.BodySize;

        private readonly ClientZoneSessionConfig _config;
        private readonly IZoneLoginService _login;
        private readonly IZoneClientTransport _transport;
        private readonly IClientSessionClock _clock;
        private readonly Action<string> _logger;
        private readonly SnapshotFragmentCollector _collector;
        private readonly string _zoneHost;
        private readonly uint _heartbeatIntervalTicks;
        private readonly byte[] _sendBuffer = new byte[MAX_OUTBOUND_MESSAGE_BYTES];
        private readonly byte[] _token = new byte[TOKEN_LENGTH];
        private readonly Action<LoginResponse> _onLoginDone;

        private HeartbeatActivityTracker _tracker = new HeartbeatActivityTracker();
        private string _username;
        private CharacterID _characterId;
        private ZoneID _zoneId;
        private ushort _port;
        private bool _loginIsReconnect;
        private bool _reconnectUsed;
        private bool _transportActive;
        private bool _handshakeSent;
        private double _snapshotDeadline;
        private double _connectDeadline;
        private uint _sequence;
        private uint _lastServerTick;

        /// <summary>Creates a session and registers it as the transport's listener.</summary>
        /// <param name="config">Configuration; validated here.</param>
        /// <param name="login">Login seam.</param>
        /// <param name="transport">Zone transport seam.</param>
        /// <param name="clock">Monotonic clock seam.</param>
        /// <param name="logger">Milestone log sink (may be null). Never receives the token or password.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="InvalidOperationException">The config is invalid.</exception>
        public ClientZoneSession(
            ClientZoneSessionConfig config,
            IZoneLoginService login,
            IZoneClientTransport transport,
            IClientSessionClock clock,
            Action<string> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _login = login ?? throw new ArgumentNullException(nameof(login));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger;
            _config.Validate();
            _zoneHost = _config.ResolveZoneHost();
            _heartbeatIntervalTicks = (uint)(_config.HeartbeatIntervalSeconds * TICKS_PER_SECOND);
            _collector = new SnapshotFragmentCollector(_config.MaxClientSnapshotFragments);
            _onLoginDone = OnLoginDone;
            _transport.SetListener(this);
        }

        /// <summary>The current state.</summary>
        public ClientZoneSessionState State { get; private set; }

        /// <summary>The <c>JoinRejectedReason</c> of the last <c>ZoneFullResponse</c> (meaningful after <see cref="ZoneEntryFailureReason.ZoneFull"/>).</summary>
        public JoinRejectedReason ZoneFullReason { get; private set; }

        /// <summary>Raised once when an entry attempt fails (before <see cref="SessionEnded"/>).</summary>
        public event Action<ZoneEntryFailureReason> EntryFailed;

        /// <summary>
        /// Raised once per completed snapshot, before the ack is sent. The buffer is only valid during the callback.
        /// A subscriber exception is logged (type name only) and does not affect the entry.
        /// </summary>
        public event SnapshotAssembledHandler SnapshotAssembled;

        /// <summary>Raised each time an entry completes (the ack was sent).</summary>
        public event Action Entered;

        /// <summary>Raised exactly once when the session reaches <see cref="ClientZoneSessionState.Ended"/> for any reason, including <see cref="Leave"/>.</summary>
        public event Action SessionEnded;

        /// <summary>Starts the session with a credential login. The password is passed on and not kept.</summary>
        /// <param name="username">Account username.</param>
        /// <param name="password">Account password.</param>
        /// <exception cref="InvalidOperationException">The session is not <see cref="ClientZoneSessionState.Idle"/>.</exception>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public void Begin(string username, string password)
        {
            if (State != ClientZoneSessionState.Idle)
            {
                throw new InvalidOperationException("Begin can only be called once, in the Idle state.");
            }

            _username = username ?? throw new ArgumentNullException(nameof(username));
            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            State = ClientZoneSessionState.LoggingIn;
            _loginIsReconnect = false;
            _login.Login(username, password, null, _onLoginDone);
        }

        /// <summary>Drives the connect and snapshot deadlines and the heartbeat. Call every frame. Allocation free.</summary>
        public void Update()
        {
            if (State == ClientZoneSessionState.Connecting && _clock.Now >= _connectDeadline)
            {
                Log("connect timeout");
                FailEntry(ZoneEntryFailureReason.ConnectTimeout);
                return;
            }

            if (State == ClientZoneSessionState.AwaitingEntry && _clock.Now >= _snapshotDeadline)
            {
                OnSnapshotTimeout();
                return;
            }

            bool live = State == ClientZoneSessionState.AwaitingEntry || State == ClientZoneSessionState.Entered;
            if (live && _handshakeSent && _tracker.IsHeartbeatDue(CurrentTick(), _heartbeatIntervalTicks))
            {
                SendHeartbeat();
            }
        }

        /// <summary>Deliberate stop: shuts the transport down, never reconnects, ends the session.</summary>
        public void Leave()
        {
            Log("leave");
            End();
        }

        /// <summary>Test hook: true when the private token buffer is all zero.</summary>
        internal bool IsTokenZeroed()
        {
            for (int i = 0; i < _token.Length; i++)
            {
                if (_token[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        void IZoneClientTransportListener.OnConnected()
        {
            if (!_transportActive || State != ClientZoneSessionState.Connecting)
            {
                return;
            }

            SendHandshake();
            _handshakeSent = true;
            State = ClientZoneSessionState.AwaitingEntry;
            _snapshotDeadline = _clock.Now + _config.FragmentReassemblyTimeoutSeconds;
            Log("handshake sent");
        }

        void IZoneClientTransportListener.OnDisconnected()
        {
            if (!_transportActive)
            {
                return;
            }

            Log("disconnected");
            StopTransport();
            if (State == ClientZoneSessionState.Entered)
            {
                BeginReconnect();
            }
            else if (State == ClientZoneSessionState.Connecting || State == ClientZoneSessionState.AwaitingEntry)
            {
                FailEntry(ZoneEntryFailureReason.ClosedBeforeAck);
            }
        }

        void IZoneClientTransportListener.OnMessageReceived(ReadOnlySpan<byte> message)
        {
            if (!_transportActive || !MessageEnvelopeCodec.TryRead(message, out ServerMessageEnvelope envelope))
            {
                return;
            }

            _lastServerTick = envelope.ServerTickNumber;
            ReadOnlySpan<byte> body = message.Slice(ServerMessageEnvelope.WireSize);
            switch (envelope.MessageTypeId)
            {
                case ZoneStateSnapshotFragment.MessageTypeId:
                    OnFragment(body);
                    break;
                case ZoneFullResponse.MessageTypeId:
                    OnZoneFull(body);
                    break;
                case ZoneSessionEnded.MessageTypeId:
                    Log("zone session ended by server");
                    ((IZoneClientTransportListener)this).OnDisconnected();
                    break;
            }
        }

        private void OnFragment(ReadOnlySpan<byte> body)
        {
            if (State != ClientZoneSessionState.AwaitingEntry
                || !ZoneStateSnapshotFragmentCodec.TryReadBody(body, out ZoneStateSnapshotFragment header, out ReadOnlySpan<byte> payload))
            {
                return;
            }

            SnapshotFragmentResult result = _collector.Accept(in header, payload);
            if (result == SnapshotFragmentResult.TooLarge)
            {
                StopTransport();
                FailEntry(ZoneEntryFailureReason.SnapshotTooLarge);
            }
            else if (result == SnapshotFragmentResult.Completed)
            {
                CompleteEntry();
            }
        }

        private void OnZoneFull(ReadOnlySpan<byte> body)
        {
            if (State != ClientZoneSessionState.AwaitingEntry || !ZoneFullResponseCodec.TryReadBody(body, out ZoneFullResponse response))
            {
                return;
            }

            ZoneFullReason = response.Reason;
            StopTransport();
            FailEntry(ZoneEntryFailureReason.ZoneFull);
        }

        private void CompleteEntry()
        {
            uint version = _collector.CompletedVersion;
            Log("snapshot v=" + version);
            SnapshotAssembledHandler assembled = SnapshotAssembled;
            if (assembled != null)
            {
                try
                {
                    assembled(_collector.Buffer, _collector.CompletedLength);
                }
                catch (Exception ex)
                {
                    Log("snapshot subscriber threw " + ex.GetType().Name);
                }
            }

            SendAck(version);
            Log("ack sent");
            _collector.Reset();
            _reconnectUsed = false;
            State = ClientZoneSessionState.Entered;
            Entered?.Invoke();
        }

        private void OnSnapshotTimeout()
        {
            Log("snapshot timeout");
            StopTransport();
            _collector.Reset();
            if (_reconnectUsed)
            {
                FailEntry(ZoneEntryFailureReason.SnapshotTimeout);
                return;
            }

            BeginReconnect();
        }

        private void BeginReconnect()
        {
            _reconnectUsed = true;
            State = ClientZoneSessionState.Reconnecting;
            _loginIsReconnect = true;
            _login.Login(_username, "", _token, _onLoginDone);
        }

        private void OnLoginDone(LoginResponse response)
        {
            if (State != ClientZoneSessionState.LoggingIn && State != ClientZoneSessionState.Reconnecting)
            {
                return;
            }

            bool reconnect = _loginIsReconnect;
            if (!response.Success || response.SessionToken == null || response.SessionToken.Length != TOKEN_LENGTH)
            {
                if (reconnect)
                {
                    Log("reconnect failed");
                    End();
                }
                else
                {
                    Log("login failed");
                    FailEntry(ZoneEntryFailureReason.LoginFailed);
                }

                return;
            }

            response.SessionToken.AsSpan().CopyTo(_token);
            _characterId = response.CharacterId;
            _zoneId = response.ZoneId;
            _port = response.ZoneServerPort;
            Log((reconnect ? "reconnect ok zone=" : "login ok zone=") + _zoneId.RawValue + " port=" + _port);
            StartConnection();
        }

        private void StartConnection()
        {
            State = ClientZoneSessionState.Connecting;
            _collector.Reset();
            _tracker = new HeartbeatActivityTracker();
            _sequence = 0;
            _lastServerTick = 0;
            _handshakeSent = false;
            _transportActive = true;
            _connectDeadline = _clock.Now + _config.ConnectTimeoutSeconds;
            _transport.Start(_zoneHost, _port);
        }

        private void FailEntry(ZoneEntryFailureReason reason)
        {
            if (State == ClientZoneSessionState.Ended)
            {
                return;
            }

            EntryFailed?.Invoke(reason);
            End();
        }

        private void End()
        {
            if (State == ClientZoneSessionState.Ended)
            {
                return;
            }

            State = ClientZoneSessionState.Ended;
            StopTransport();
            _collector.Reset();
            Array.Clear(_token, 0, _token.Length);
            SessionEnded?.Invoke();
        }

        private void StopTransport()
        {
            _handshakeSent = false;
            if (!_transportActive)
            {
                return;
            }

            _transportActive = false;
            _transport.Stop();
        }

        private void SendHandshake()
        {
            Span<byte> span = _sendBuffer.AsSpan(0, ServerMessageEnvelope.WireSize + SessionHandshake.BodySize);
            WriteEnvelope(span, SessionHandshake.MessageTypeId);
            SessionHandshakeCodec.WriteBody(span.Slice(ServerMessageEnvelope.WireSize), new SessionHandshake(_characterId, _zoneId, _token));
            Transmit(ServerMessageEnvelope.WireSize + SessionHandshake.BodySize);
        }

        private void SendAck(uint snapshotVersion)
        {
            Span<byte> span = _sendBuffer.AsSpan(0, ServerMessageEnvelope.WireSize + ZoneEntryAck.BodySize);
            WriteEnvelope(span, ZoneEntryAck.MessageTypeId);
            ZoneEntryAckCodec.WriteBody(span.Slice(ServerMessageEnvelope.WireSize), new ZoneEntryAck(_zoneId, snapshotVersion));
            Transmit(ServerMessageEnvelope.WireSize + ZoneEntryAck.BodySize);
        }

        private void SendHeartbeat()
        {
            Span<byte> span = _sendBuffer.AsSpan(0, HeartbeatMessage.WireSize);
            WriteEnvelope(span, HeartbeatMessage.MessageTypeId);
            Transmit(HeartbeatMessage.WireSize);
        }

        private void WriteEnvelope(Span<byte> destination, ushort messageTypeId)
        {
            _sequence++;
            MessageEnvelopeCodec.Write(destination, new ServerMessageEnvelope(messageTypeId, _sequence, _lastServerTick));
        }

        private void Transmit(int length)
        {
            _transport.Send(new ReadOnlySpan<byte>(_sendBuffer, 0, length));
            _tracker.RecordOutboundPacket(CurrentTick());
        }

        private uint CurrentTick()
        {
            return unchecked((uint)(long)Math.Floor(_clock.Now * TICKS_PER_SECOND));
        }

        private void Log(string line)
        {
            _logger?.Invoke(line);
        }
    }
}

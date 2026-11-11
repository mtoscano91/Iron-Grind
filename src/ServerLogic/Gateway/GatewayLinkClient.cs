using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The zone side of the gateway link (Gateway Story 010, ADR-016 Decision 3): one dedicated background thread that
    /// connects to the gateway's loopback zone-link port, sends <c>RegisterZone</c> as the first frame of every
    /// connection, then reads gateway-to-zone frames into a bounded inbox and writes queued zone-to-gateway frames.
    /// Any failure closes the link, waits the retry interval on the stop signal and reconnects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> <see cref="Start"/> creates one <see cref="Thread"/> (<c>IsBackground = true</c>, named
    /// <c>IronGrindGatewayLink</c>) that does all connect, read and write work with blocking socket calls only (no
    /// async APIs: IL2CPP/Mono). The tick thread uses only <see cref="TryDequeue"/> and <see cref="Enqueue"/>; both are
    /// lock-free, never block and never touch the socket. <see cref="Start"/> and <see cref="Stop"/> may be called from
    /// any thread; <see cref="Stop"/> is idempotent.
    /// </para>
    /// <para>
    /// <b>Registration callback.</b> The callback passed to <see cref="Start"/> runs ON THE LINK THREAD, once per
    /// connection, and must be thread-safe (Story 011 supplies a snapshot of the live accounts, not the live
    /// collection). A callback that throws counts as a link failure: it is logged and the connect is retried.
    /// </para>
    /// <para>
    /// <b>Socket ownership.</b> The link thread creates and normally closes the socket. <see cref="Stop"/> also closes
    /// the current socket from the calling thread to unblock a pending connect, poll or receive; closing twice is
    /// harmless and every exception from it is swallowed.
    /// </para>
    /// <para>
    /// <b>Drop policy.</b> Outbound frames enqueued while the link is down are DROPPED (counted in
    /// <see cref="DroppedOutboundCount"/>, summarised in one log line when the next link comes up); the gateway's
    /// request timeouts cover them. Frames still queued when a link drops are discarded too. Inbound messages already
    /// in the inbox when the link drops stay there: they are valid requests and the tick drains them.
    /// </para>
    /// <para>
    /// <b>Failures.</b> An oversized or unknown-type frame, a malformed body, or a full inbox closes the link, logged
    /// once per event, and the link is re-established like a link loss. Connect failures log a warning for the first
    /// failure of a down period and then every tenth, to avoid log spam.
    /// </para>
    /// <para>
    /// <b>IL2CPP.</b> When the Linux IL2CPP server build is first made, check whether <c>System.Net.Sockets</c> needs
    /// preserving in <c>link.xml</c> (ADR-016 Verification 1).
    /// </para>
    /// </remarks>
    public sealed class GatewayLinkClient
    {
        /// <summary>Default longest join in <see cref="Stop"/>, milliseconds (GATEWAY_LINK_STOP_TIMEOUT_MS).</summary>
        public const int GATEWAY_LINK_STOP_TIMEOUT_MS = 2000;

        // Short so queued writes are not starved by reads, long enough not to spin.
        internal const int POLL_MICROSECONDS = 5000;

        // Bounds a Send to a gateway that stopped reading; Close may not wake a blocked Send under Mono on Linux.
        internal const int SEND_TIMEOUT_MS = 5000;

        private const int CONNECT_FAILURE_LOG_EVERY = 10;
        private const string LOG_PREFIX = "[GatewayLinkClient]";
        private const string THREAD_NAME = "IronGrindGatewayLink";

        private readonly int _retryMs;
        private readonly int _maxFrameBytes;
        private readonly int _inboxCapacity;
        private readonly int _stopTimeoutMs;
        private readonly byte[] _receiveBuffer;
        private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private readonly object _lifecycleLock = new object();
        private readonly ConcurrentQueue<GatewayToZoneMessage> _inbox = new ConcurrentQueue<GatewayToZoneMessage>();
        private readonly ConcurrentQueue<byte[]> _outbox = new ConcurrentQueue<byte[]>();
        private volatile bool _stopRequested;
        private volatile bool _joinTimedOut;
        private volatile bool _connected;
        private volatile Thread _thread;
        private volatile Socket _socket;
        private int _inboxCount;
        private int _linkUpCount;
        private int _linkLostCount;
        private int _droppedOutboundCount;
        private int _droppedReported;

        /// <summary>Creates the client; no thread starts and nothing connects until <see cref="Start"/>.</summary>
        /// <param name="retryMs">Wait between connect attempts, milliseconds (GATEWAY_LINK_RETRY_SECONDS x 1000); at least 1.</param>
        /// <param name="maxFrameBytes">Largest accepted frame length field (GATEWAY_LINK_MAX_FRAME_BYTES); at least 1.</param>
        /// <param name="inboxCapacity">Inbox size before the link is closed (GATEWAY_LINK_INBOX_CAPACITY); at least 1.</param>
        /// <param name="stopTimeoutMs">Longest join in <see cref="Stop"/>, milliseconds; at least 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument is below 1.</exception>
        public GatewayLinkClient(int retryMs, int maxFrameBytes, int inboxCapacity, int stopTimeoutMs = GATEWAY_LINK_STOP_TIMEOUT_MS)
        {
            if (retryMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(retryMs));
            }

            if (maxFrameBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
            }

            if (inboxCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(inboxCapacity));
            }

            if (stopTimeoutMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stopTimeoutMs));
            }

            _retryMs = retryMs;
            _maxFrameBytes = maxFrameBytes;
            _inboxCapacity = inboxCapacity;
            _stopTimeoutMs = stopTimeoutMs;
            _receiveBuffer = new byte[GatewayLinkCodec.LENGTH_PREFIX_BYTES + maxFrameBytes];
        }

        /// <summary>Creates a client from the zone server config's gateway link knobs (retry seconds become milliseconds).</summary>
        /// <param name="config">The validated config; not null.</param>
        /// <returns>The client, not started.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A knob is below 1.</exception>
        public static GatewayLinkClient FromConfig(ZoneServerConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            long retryMs = (long)config.GatewayLinkRetrySeconds * 1000L;
            int retry = retryMs > int.MaxValue ? int.MaxValue : (int)retryMs;
            return new GatewayLinkClient(retry, config.GatewayLinkMaxFrameBytes, config.GatewayLinkInboxCapacity);
        }

        /// <summary>True while a link is established (the registration frame has been sent).</summary>
        public bool IsConnected
        {
            get { return _connected; }
        }

        /// <summary>Number of links established since start (the LinkUp counter).</summary>
        public int LinkUpCount
        {
            get { return Volatile.Read(ref _linkUpCount); }
        }

        /// <summary>Number of established links that were lost or closed (the LinkLost counter).</summary>
        public int LinkLostCount
        {
            get { return Volatile.Read(ref _linkLostCount); }
        }

        /// <summary>
        /// Number of outbound frames dropped: enqueued while no link was up, or still queued when a link closed or
        /// a new link registered.
        /// </summary>
        public int DroppedOutboundCount
        {
            get { return Volatile.Read(ref _droppedOutboundCount); }
        }

        /// <summary>True while the link thread is alive.</summary>
        public bool IsRunning
        {
            get
            {
                Thread thread = _thread;
                return thread != null && thread.IsAlive;
            }
        }

        /// <summary>The link thread, or null before <see cref="Start"/>. For tests.</summary>
        internal Thread WorkerThread
        {
            get { return _thread; }
        }

        /// <summary>
        /// Starts the link thread. A second call while started is a no-op. The client is single use: after
        /// <see cref="Stop"/> this throws.
        /// </summary>
        /// <param name="endpoint">The gateway's zone-link endpoint (loopback, GATEWAY_ZONE_LINK_PORT).</param>
        /// <param name="registration">
        /// Produces the <c>RegisterZone</c> for each connection. Called on the link thread, so it must be thread-safe;
        /// a throwing callback is a link failure.
        /// </param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The client was already stopped.</exception>
        public void Start(IPEndPoint endpoint, Func<RegisterZoneMessage> registration)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            lock (_lifecycleLock)
            {
                if (_stopRequested)
                {
                    throw new InvalidOperationException("The gateway link client was stopped; create a new one.");
                }

                if (_thread != null)
                {
                    return;
                }

                var thread = new Thread(() => Run(endpoint, registration)) { IsBackground = true, Name = THREAD_NAME };
                _thread = thread;
                thread.Start();
            }
        }

        /// <summary>
        /// Queues one encoded frame for the link thread to write, in order. Called from the tick: never blocks and never
        /// touches the socket. While no link is up the frame is dropped and counted (see the class remarks).
        /// </summary>
        /// <param name="frame">A complete encoded frame (length prefix included); not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
        public void Enqueue(byte[] frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (!_connected)
            {
                Interlocked.Increment(ref _droppedOutboundCount);
                return;
            }

            _outbox.Enqueue(frame);
        }

        /// <summary>Takes the oldest received message, for the tick. Never blocks; allocates nothing.</summary>
        /// <param name="message">The message, or default when the inbox is empty.</param>
        /// <returns>True if a message was returned.</returns>
        public bool TryDequeue(out GatewayToZoneMessage message)
        {
            if (_inbox.TryDequeue(out message))
            {
                Interlocked.Decrement(ref _inboxCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Requests the end of the loop, closes the current socket from the calling thread (unblocking a connect, poll
        /// or receive), and joins the link thread for at most the stop timeout. Idempotent, safe before
        /// <see cref="Start"/>, safe from any thread, never throws. A join timeout is logged once.
        /// </summary>
        public void Stop()
        {
            try
            {
                _stopRequested = true;
                _stopSignal.Set();

                // Pairs with the barrier in OpenSocket: volatile alone allows store->load reordering, so without both
                // barriers this thread could read a stale null socket while the link thread misses the stop flag.
                Interlocked.MemoryBarrier();
                CloseQuietly(_socket);

                Thread thread = _thread;
                if (thread == null || _joinTimedOut || ReferenceEquals(thread, Thread.CurrentThread) || !thread.IsAlive)
                {
                    return;
                }

                if (!thread.Join(_stopTimeoutMs))
                {
                    _joinTimedOut = true;
                    Debug.LogWarning($"{LOG_PREFIX} StopTimedOut: the link thread did not end within {_stopTimeoutMs} ms; it is a background thread and ends with the process.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} StopFailed: {ex.GetType().Name}");
            }
        }

        private void Run(IPEndPoint endpoint, Func<RegisterZoneMessage> registration)
        {
            try
            {
                RunLoop(endpoint, registration);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} LoopEnded: unexpected {ex.GetType().Name}: {LogSafe(ex.Message)}; the gateway link is no longer running.");
            }
        }

        private void RunLoop(IPEndPoint endpoint, Func<RegisterZoneMessage> registration)
        {
            int consecutiveConnectFailures = 0;

            while (!_stopRequested)
            {
                Socket socket = null;
                bool established = false;
                string failure = null;
                try
                {
                    socket = OpenSocket();
                    if (!_stopRequested)
                    {
                        Establish(socket, endpoint, registration);
                        established = true;
                        failure = RunConnected(socket);
                    }
                }
                catch (Exception ex)
                {
                    failure = $"{ex.GetType().Name}: {LogSafe(ex.Message)}";
                }

                consecutiveConnectFailures = ReportLinkEnd(established, failure, consecutiveConnectFailures);
                ClearOutbox(true);
                _socket = null;
                CloseQuietly(socket);

                if (_stopRequested || _stopSignal.Wait(_retryMs))
                {
                    break;
                }
            }
        }

        // Creates the socket and publishes it so Stop() can close it.
        private Socket OpenSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            socket.SendTimeout = SEND_TIMEOUT_MS;
            _socket = socket;

            // Pairs with the barrier in Stop(): the stop flag read that follows cannot move before the store above.
            Interlocked.MemoryBarrier();
            return socket;
        }

        // Connects, sends the registration as the first frame, then marks the link up.
        private void Establish(Socket socket, IPEndPoint endpoint, Func<RegisterZoneMessage> registration)
        {
            socket.Connect(endpoint);

            // A frame queued for the previous link must not follow this link's registration.
            ClearOutbox(false);
            SendAll(socket, GatewayLinkCodec.Encode(registration()));

            _connected = true;
            Interlocked.Increment(ref _linkUpCount);
            LogLinkUp();
        }

        // Logs the end of one connection attempt and returns the updated consecutive connect failure count.
        private int ReportLinkEnd(bool established, string failure, int consecutiveConnectFailures)
        {
            if (established)
            {
                _connected = false;
                Interlocked.Increment(ref _linkLostCount);
                if (_stopRequested)
                {
                    Debug.Log($"{LOG_PREFIX} LinkClosed: stopping.");
                }
                else
                {
                    Debug.LogWarning($"{LOG_PREFIX} LinkLost: {failure}.");
                }

                return 0;
            }

            if (_stopRequested)
            {
                return consecutiveConnectFailures;
            }

            consecutiveConnectFailures++;
            if (consecutiveConnectFailures == 1 || consecutiveConnectFailures % CONNECT_FAILURE_LOG_EVERY == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} ConnectFailed: attempt {consecutiveConnectFailures} - {failure}; retrying in {_retryMs} ms.");
            }

            return consecutiveConnectFailures;
        }

        // Runs while the link is up; returns the failure reason (or null when stopping).
        private string RunConnected(Socket socket)
        {
            int filled = 0;
            while (!_stopRequested)
            {
                DrainOutbox(socket);

                if (!socket.Poll(POLL_MICROSECONDS, SelectMode.SelectRead))
                {
                    continue;
                }

                int read = socket.Receive(_receiveBuffer, filled, _receiveBuffer.Length - filled, SocketFlags.None);
                if (read == 0)
                {
                    return "PeerClosed";
                }

                filled += read;
                string failure = ParseFrames(ref filled);
                if (failure != null)
                {
                    return failure;
                }
            }

            return null;
        }

        private string ParseFrames(ref int filled)
        {
            int offset = 0;
            while (offset < filled)
            {
                GatewayLinkFrameStatus status = GatewayLinkCodec.TryReadFrame(
                    new ReadOnlySpan<byte>(_receiveBuffer, offset, filled - offset),
                    _maxFrameBytes,
                    out byte type,
                    out ReadOnlySpan<byte> body,
                    out int consumed);

                if (status == GatewayLinkFrameStatus.NeedMoreBytes)
                {
                    break;
                }

                if (status == GatewayLinkFrameStatus.ProtocolViolation)
                {
                    return "ProtocolViolation";
                }

                string failure = AcceptFrame(type, body);
                if (failure != null)
                {
                    return failure;
                }

                offset += consumed;
            }

            Compact(offset, ref filled);
            return null;
        }

        // Decodes one complete frame into the inbox; returns the failure reason, or null when queued.
        private string AcceptFrame(byte type, ReadOnlySpan<byte> body)
        {
            if (!GatewayLinkCodec.TryDecodeGatewayToZone(type, body, out GatewayToZoneMessage message))
            {
                return "MalformedFrame";
            }

            if (Volatile.Read(ref _inboxCount) >= _inboxCapacity)
            {
                return "InboxFull";
            }

            Interlocked.Increment(ref _inboxCount);
            _inbox.Enqueue(message);
            return null;
        }

        // Moves the unread tail (a partial frame) to the front of the receive buffer.
        private void Compact(int offset, ref int filled)
        {
            if (offset == 0)
            {
                return;
            }

            int left = filled - offset;
            if (left > 0)
            {
                Buffer.BlockCopy(_receiveBuffer, offset, _receiveBuffer, 0, left);
            }

            filled = left;
        }

        private void DrainOutbox(Socket socket)
        {
            while (!_stopRequested && _outbox.TryDequeue(out byte[] frame))
            {
                SendAll(socket, frame);
            }
        }

        private static void SendAll(Socket socket, byte[] frame)
        {
            int sent = 0;
            while (sent < frame.Length)
            {
                int n = socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
                if (n <= 0)
                {
                    throw new SocketException((int)SocketError.ConnectionReset);
                }

                sent += n;
            }
        }

        private void ClearOutbox(bool logDropped)
        {
            int dropped = 0;
            while (_outbox.TryDequeue(out _))
            {
                dropped++;
            }

            if (dropped == 0)
            {
                return;
            }

            Interlocked.Add(ref _droppedOutboundCount, dropped);
            if (logDropped)
            {
                // Reported here, so the next LinkUp summary does not report these again (link thread only).
                _droppedReported += dropped;
                Debug.LogWarning($"{LOG_PREFIX} OutboundDropped: {dropped} queued frame(s) discarded because the link closed.");
            }
        }

        private void LogLinkUp()
        {
            int totalDropped = Volatile.Read(ref _droppedOutboundCount);
            int newlyDropped = totalDropped - _droppedReported;
            _droppedReported = totalDropped;
            string suffix = newlyDropped > 0 ? $" {newlyDropped} outbound frame(s) were dropped while the link was down." : string.Empty;
            Debug.Log($"{LOG_PREFIX} LinkUp: registered with the gateway.{suffix}");
        }

        private static void CloseQuietly(Socket socket)
        {
            if (socket == null)
            {
                return;
            }

            try
            {
                socket.Close();
            }
            catch (Exception)
            {
                // Closed by the other owner already, or being abandoned either way.
            }
        }

        // Unity's native log stops at a NUL character; Mono's Winsock messages end with one.
        private static string LogSafe(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            return builder.ToString().Trim();
        }
    }
}

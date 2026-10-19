using System;
using System.Threading;
using Npgsql;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The PostgreSQL listener of the cross-zone relay (ADR-015 Decision 3): one dedicated background thread per zone
    /// process that owns one unpooled connection, issues <c>LISTEN</c> on the relay channel, loops on <c>Wait</c> and
    /// hands every notification payload to <see cref="CrossZoneRelayReceiver.OnPayload"/>. Any failure disposes the
    /// connection, logs, backs off (500 ms doubling to the cap) and reconnects; <see cref="Stop"/> ends it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> The loop and the notification handler run on the listener thread, which is created by
    /// <see cref="Start"/> as a dedicated <see cref="Thread"/> (<c>IsBackground = true</c>, named
    /// <c>IronGrindRelayListener</c>), never a pool thread. The handler calls the receiver and nothing else; the receiver
    /// never throws and calls no game code. <see cref="Start"/> and <see cref="Stop"/> are for the thread that owns the
    /// zone (composition root, teardown, host destroy); they may be called from any one thread at a time.
    /// </para>
    /// <para>
    /// <b>Connection ownership.</b> Only the listener thread disposes the connection, on its way out of an iteration;
    /// <see cref="Stop"/> never touches it, because disposing a connection under a thread blocked in <c>Wait</c> is
    /// unsafe. <see cref="Stop"/> sets a volatile flag and a signal; the loop leaves within one <c>Wait</c> or at once
    /// when it is in a backoff delay.
    /// </para>
    /// <para>
    /// <b>Failures.</b> The loop catches every exception type. Consecutive failures are counted; the
    /// <c>reconnectAlertAfter</c>-th logs an error, the others a warning. A successful <c>LISTEN</c> resets the count
    /// and the backoff. Notifications sent while disconnected are lost (Decision 6).
    /// </para>
    /// <para>
    /// <b>Allocation.</b> Npgsql allocates the payload string and event arguments for each notification, off the tick
    /// thread, a few per hour. The receiver allocates nothing of its own except when it logs. A reconnect allocates a
    /// new connection object.
    /// </para>
    /// <para>
    /// <b>Editor.</b> Never create one in an EditMode or PlayMode test against a database (ADR-015); tests use the
    /// internal seams. The production call sites of <see cref="Stop"/> belong to the composition-root story.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var listener = new NpgsqlRelayListener(baseConnectionString, receiver);
    /// listener.Start();
    /// // ... zone teardown, after the tick loop has stopped:
    /// listener.Stop();
    /// </code>
    /// </example>
    public sealed class NpgsqlRelayListener
    {
        /// <summary>Default longest block in one <c>Wait</c>, milliseconds (tuning knob RELAY_LISTENER_WAIT_MS).</summary>
        public const int RELAY_LISTENER_WAIT_MS = 500;

        /// <summary>Default cap of the reconnect backoff, milliseconds (tuning knob RELAY_RECONNECT_MAX_BACKOFF_MS).</summary>
        public const int RELAY_RECONNECT_MAX_BACKOFF_MS = 10000;

        /// <summary>Default consecutive-failure count that logs an error (tuning knob RELAY_RECONNECT_ALERT_AFTER).</summary>
        public const int RELAY_RECONNECT_ALERT_AFTER = 5;

        /// <summary>Default longest join in <see cref="Stop"/>, milliseconds (tuning knob RELAY_LISTENER_STOP_TIMEOUT_MS).</summary>
        public const int RELAY_LISTENER_STOP_TIMEOUT_MS = 2000;

        private const int INITIAL_BACKOFF_MS = 500;
        private const string LOG_PREFIX = "[NpgsqlRelayListener]";
        private const string THREAD_NAME = "IronGrindRelayListener";

        private readonly Func<IRelayListenerConnection> _connectionFactory;
        private readonly CrossZoneRelayReceiver _receiver;
        private readonly string _channelName;
        private readonly int _waitMs;
        private readonly int _reconnectMaxBackoffMs;
        private readonly int _reconnectAlertAfter;
        private readonly int _stopTimeoutMs;
        private readonly Func<int, ManualResetEventSlim, bool> _backoffWait;
        private readonly Action<string> _onNotification;
        private readonly ManualResetEventSlim _stopSignal = new ManualResetEventSlim(false);
        private readonly object _lifecycleLock = new object();
        private volatile bool _stopRequested;
        private volatile bool _joinTimedOut;
        private volatile Thread _thread;

        /// <summary>Creates the listener; no thread starts and nothing connects until <see cref="Start"/>.</summary>
        /// <param name="baseConnectionString">The zone's base Npgsql connection string; the listener derives its own unpooled string from it.</param>
        /// <param name="receiver">Receives every payload; not null.</param>
        /// <param name="channelName">The channel; the RELAY_CHANNEL_NAME knob; not empty.</param>
        /// <param name="waitMs">The longest block in one wait; the RELAY_LISTENER_WAIT_MS knob; at least 1.</param>
        /// <param name="reconnectMaxBackoffMs">The backoff cap; the RELAY_RECONNECT_MAX_BACKOFF_MS knob; at least 1.</param>
        /// <param name="reconnectAlertAfter">The failure count that logs an error; the RELAY_RECONNECT_ALERT_AFTER knob; at least 1.</param>
        /// <param name="stopTimeoutMs">The longest join in <see cref="Stop"/>; the RELAY_LISTENER_STOP_TIMEOUT_MS knob; at least 1.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException">A string argument is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A number is below 1.</exception>
        public NpgsqlRelayListener(
            string baseConnectionString,
            CrossZoneRelayReceiver receiver,
            string channelName = NpgsqlRelayPublisher.RELAY_CHANNEL_NAME,
            int waitMs = RELAY_LISTENER_WAIT_MS,
            int reconnectMaxBackoffMs = RELAY_RECONNECT_MAX_BACKOFF_MS,
            int reconnectAlertAfter = RELAY_RECONNECT_ALERT_AFTER,
            int stopTimeoutMs = RELAY_LISTENER_STOP_TIMEOUT_MS)
            : this(
                new NpgsqlRelayListenerConnection.Factory(BuildListenerConnectionString(baseConnectionString)).Create,
                receiver,
                channelName,
                waitMs,
                reconnectMaxBackoffMs,
                reconnectAlertAfter,
                stopTimeoutMs,
                null)
        {
        }

        /// <summary>Creates the listener over seams. For tests; a null delay uses the stop signal's own timed wait.</summary>
        internal NpgsqlRelayListener(
            Func<IRelayListenerConnection> connectionFactory,
            CrossZoneRelayReceiver receiver,
            string channelName,
            int waitMs,
            int reconnectMaxBackoffMs,
            int reconnectAlertAfter,
            int stopTimeoutMs,
            Func<int, ManualResetEventSlim, bool> backoffWait)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            if (channelName == null)
            {
                throw new ArgumentNullException(nameof(channelName));
            }

            if (channelName.Length == 0)
            {
                throw new ArgumentException("The channel name must not be empty.", nameof(channelName));
            }

            if (waitMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(waitMs));
            }

            if (reconnectMaxBackoffMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(reconnectMaxBackoffMs));
            }

            if (reconnectAlertAfter < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(reconnectAlertAfter));
            }

            if (stopTimeoutMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stopTimeoutMs));
            }

            _channelName = channelName;
            _waitMs = waitMs;
            _reconnectMaxBackoffMs = reconnectMaxBackoffMs;
            _reconnectAlertAfter = reconnectAlertAfter;
            _stopTimeoutMs = stopTimeoutMs;
            _backoffWait = backoffWait ?? WaitForStop;
            _onNotification = OnNotification;
        }

        /// <summary>True while the listener thread is alive.</summary>
        public bool IsRunning
        {
            get
            {
                Thread thread = _thread;
                return thread != null && thread.IsAlive;
            }
        }

        /// <summary>The listener thread, or null before <see cref="Start"/>. For tests.</summary>
        internal Thread WorkerThread
        {
            get { return _thread; }
        }

        /// <summary>
        /// Creates and starts the dedicated listener thread. A second call while started is a no-op (it does not start a
        /// second thread). Calling it after <see cref="Stop"/> throws: a listener is single use.
        /// </summary>
        /// <exception cref="InvalidOperationException">The listener was already stopped.</exception>
        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_stopRequested)
                {
                    throw new InvalidOperationException("The relay listener was stopped; create a new one.");
                }

                if (_thread != null)
                {
                    return;
                }

                var thread = new Thread(Run) { IsBackground = true, Name = THREAD_NAME };
                _thread = thread;
                thread.Start();
            }
        }

        /// <summary>
        /// Requests the end of the loop and joins the thread for at most the stop timeout. Idempotent: a repeat call, a
        /// call before <see cref="Start"/> and a call after the thread has ended return at once and never throw. A thread
        /// that does not end in time is logged once; being a background thread it does not hold the process open, and it
        /// still disposes its own connection when it ends. Never touches the connection.
        /// </summary>
        public void Stop()
        {
            _stopRequested = true;
            _stopSignal.Set();

            Thread thread = _thread;
            if (thread == null || _joinTimedOut || ReferenceEquals(thread, Thread.CurrentThread) || !thread.IsAlive)
            {
                return;
            }

            if (!thread.Join(_stopTimeoutMs))
            {
                _joinTimedOut = true;
                Debug.LogWarning($"{LOG_PREFIX} StopTimedOut: the listener thread did not end within {_stopTimeoutMs} ms; it is a background thread and ends with the process, and it disposes its own connection when it returns.");
            }
        }

        /// <summary>
        /// Builds the listener's connection string from the zone's base string: <c>Pooling=false</c>,
        /// <c>Multiplexing=false</c>, <c>Tcp Keepalive=true</c>, and no protocol-level <c>Keepalive</c> (its probes would
        /// contend with the blocking read in <c>Wait</c>).
        /// </summary>
        /// <param name="baseConnectionString">The zone's base string.</param>
        /// <returns>The listener's string.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="baseConnectionString"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="baseConnectionString"/> is blank.</exception>
        internal static string BuildListenerConnectionString(string baseConnectionString)
        {
            if (baseConnectionString == null)
            {
                throw new ArgumentNullException(nameof(baseConnectionString));
            }

            if (string.IsNullOrWhiteSpace(baseConnectionString))
            {
                throw new ArgumentException("The base connection string must not be blank.", nameof(baseConnectionString));
            }

            var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            builder.Pooling = false;
            builder.Multiplexing = false;
            builder.TcpKeepAlive = true;
            builder.Remove("Keepalive");
            return builder.ConnectionString;
        }

        /// <summary>The default backoff delay: waits on the stop signal for at most the time given. Returns true if stop was signalled.</summary>
        internal static bool WaitForStop(int milliseconds, ManualResetEventSlim stopSignal)
        {
            return stopSignal.Wait(milliseconds);
        }

        /// <summary>
        /// The listener loop, run by the listener thread; internal so tests can run it on the test thread. Returns when
        /// stop is requested. Catches every exception.
        /// </summary>
        internal void RunLoop()
        {
            int consecutiveFailures = 0;
            int backoffMs = INITIAL_BACKOFF_MS;

            while (!_stopRequested)
            {
                IRelayListenerConnection connection = null;
                Exception failure = null;
                try
                {
                    connection = _connectionFactory();
                    connection.NotificationHandler = _onNotification;
                    connection.Open();
                    connection.Listen(_channelName);
                    // A successful LISTEN resets the count and the backoff (ADR-015 Decision 3). A connection that
                    // keeps passing LISTEN and failing in the first Wait therefore retries every 500 ms and never
                    // reaches the alert; each failure still logs a warning.
                    consecutiveFailures = 0;
                    backoffMs = INITIAL_BACKOFF_MS;
                    while (!_stopRequested)
                    {
                        connection.Wait(_waitMs);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                DisposeQuietly(connection);

                if (failure == null)
                {
                    break;
                }

                if (_stopRequested)
                {
                    Debug.Log($"{LOG_PREFIX} FailureAfterStop: {failure.GetType().Name}: {failure.Message}; the listener is stopping, no reconnect.");
                    break;
                }

                consecutiveFailures++;
                int delayMs = Math.Min(backoffMs, _reconnectMaxBackoffMs);
                if (consecutiveFailures == _reconnectAlertAfter)
                {
                    Debug.LogError($"{LOG_PREFIX} ReconnectAlert: {consecutiveFailures} consecutive failures - {failure.GetType().Name}: {failure.Message}; still retrying in {delayMs} ms. Cross-zone messages are being lost.");
                }
                else
                {
                    Debug.LogWarning($"{LOG_PREFIX} ConnectionLost: consecutiveFailures={consecutiveFailures} - {failure.GetType().Name}: {failure.Message}; reconnecting in {delayMs} ms.");
                }

                if (_backoffWait(delayMs, _stopSignal))
                {
                    break;
                }

                backoffMs = Math.Min(backoffMs * 2, _reconnectMaxBackoffMs);
            }
        }

        private void Run()
        {
            try
            {
                RunLoop();
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} LoopEnded: unexpected {ex.GetType().Name}: {ex.Message}; the relay listener is no longer running.");
            }
        }

        private void OnNotification(string payload)
        {
            _receiver.OnPayload(payload);
        }

        private static void DisposeQuietly(IRelayListenerConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            try
            {
                connection.Dispose();
            }
            catch (Exception)
            {
                // The connection is being abandoned either way; the failure that led here is already logged.
            }
        }
    }
}

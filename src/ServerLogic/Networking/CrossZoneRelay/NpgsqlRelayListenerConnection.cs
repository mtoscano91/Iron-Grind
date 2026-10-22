using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;

namespace IronGrind.Networking
{
    /// <summary>
    /// The seam under <see cref="NpgsqlRelayListener"/> (ADR-015 Decision 3): one dedicated connection that opens,
    /// issues <c>LISTEN</c>, blocks in <c>Wait</c> and raises notifications to a handler on the calling thread.
    /// Internal so EditMode tests can replace the database; the production implementation is
    /// <see cref="NpgsqlRelayListenerConnection"/>.
    /// </summary>
    /// <remarks>Used by the listener thread only; only that thread calls <see cref="IDisposable.Dispose"/>.</remarks>
    internal interface IRelayListenerConnection : IDisposable
    {
        /// <summary>Receives the payload text of every notification, on the thread that called <see cref="Wait"/>, before it returns.</summary>
        Action<string> NotificationHandler { set; }

        /// <summary>Opens the connection (blocking).</summary>
        void Open();

        /// <summary>Issues <c>LISTEN</c> on the channel (blocking). Its notifications go to the handler.</summary>
        /// <param name="channel">The channel name.</param>
        void Listen(string channel);

        /// <summary>
        /// Issues <c>LISTEN</c> on the listener's private wake channel (blocking). A notification there only ends the
        /// current <see cref="Wait"/>; it never reaches the handler.
        /// </summary>
        /// <param name="channel">The wake channel name.</param>
        void ListenWake(string channel);

        /// <summary>Blocks for at most <paramref name="timeoutMs"/> until a message arrives; notifications go to the handler.</summary>
        /// <param name="timeoutMs">The longest block, in milliseconds.</param>
        /// <returns>True if a message arrived, false on timeout.</returns>
        bool Wait(int timeoutMs);
    }

    /// <summary>
    /// Production <see cref="IRelayListenerConnection"/> over one unpooled <c>NpgsqlConnection</c>. Thin on purpose:
    /// it is what the two-process verification run exercises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wait goes through <c>WaitAsync(int)</c>, blocked on by the listener thread (ADR-015 note 2026-10-19).</b>
    /// Npgsql 8.0.9's synchronous <c>Wait(int)</c> implements its timeout with <c>Socket.ReceiveTimeout</c> and
    /// recognises the expiry by the socket error code, which it expects to be <c>WouldBlock</c> whenever the
    /// <c>Mono.Runtime</c> type exists. Under Unity's Mono on Windows (the Editor and the Mono server build) the expiry
    /// was instead reported as a broken connection (<c>NpgsqlException: Exception while reading from stream</c>) on
    /// every empty wait, so the listener reconnected about once a second and lost messages (Story 041, VR-1, first
    /// run). <c>WaitAsync(int, CancellationToken)</c> implements the timeout with a timer and returns false on expiry
    /// (8.0.9 documentation). The listener thread still blocks, now on the task's result, so ADR-015's dedicated-thread
    /// model and <c>Stop()</c> are unchanged.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Every call, including <c>Dispose</c>, comes from the one listener thread. On the asynchronous
    /// path Npgsql raises <c>Notification</c> on the thread that completes the read (a thread-pool or I/O thread), so
    /// the event handler only queues the payload; <see cref="Wait"/> hands the queued payloads to the handler on the
    /// listener thread after the wait returns. The receiver therefore still runs on the listener thread only, one
    /// payload at a time. Not thread-safe otherwise.
    /// </para>
    /// <para>
    /// <b>Allocation.</b> Npgsql allocates the payload string, the event arguments and, per wait, its task (off the
    /// tick thread: a few notifications per hour, two waits per second). The pending list is reused. <c>LISTEN</c>
    /// cannot take a parameter, so the channel is written as a quoted identifier (embedded quotes doubled), which keeps
    /// its exact case, the same text <c>pg_notify</c> receives.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var connection = new NpgsqlRelayListenerConnection(listenerConnectionString);
    /// connection.NotificationHandler = OnPayload;
    /// connection.Open();
    /// connection.Listen("irongrind_relay");
    /// connection.Wait(500);
    /// </code>
    /// </example>
    internal sealed class NpgsqlRelayListenerConnection : IRelayListenerConnection
    {
        private readonly NpgsqlConnection _connection;
        private readonly object _pendingLock = new object();
        private readonly List<string> _pending = new List<string>();
        private readonly List<string> _delivering = new List<string>();
        private Action<string> _handler;
        private volatile string _deliveryChannel;

        /// <summary>Creates the connection object; nothing connects until <see cref="Open"/>.</summary>
        /// <param name="listenerConnectionString">A string from <c>NpgsqlRelayListener.BuildListenerConnectionString</c>.</param>
        internal NpgsqlRelayListenerConnection(string listenerConnectionString)
        {
            _connection = new NpgsqlConnection(listenerConnectionString);
            _connection.Notification += OnNotification;
        }

        /// <inheritdoc />
        public Action<string> NotificationHandler
        {
            set { _handler = value; }
        }

        /// <inheritdoc />
        public void Open()
        {
            _connection.Open();
        }

        /// <inheritdoc />
        public void Listen(string channel)
        {
            _deliveryChannel = channel;
            IssueListen(channel);
        }

        /// <inheritdoc />
        public void ListenWake(string channel)
        {
            IssueListen(channel);
        }

        private void IssueListen(string channel)
        {
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = "LISTEN \"" + channel.Replace("\"", "\"\"") + "\"";
                command.ExecuteNonQuery();
            }
        }

        /// <inheritdoc />
        public bool Wait(int timeoutMs)
        {
            // Blocking on the task is intended: this is the dedicated listener thread, never the tick thread.
            bool received = _connection.WaitAsync(timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
            DeliverPending();
            return received;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _connection.Notification -= OnNotification;
            _connection.Dispose();
        }

        private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            // Only the relay channel is delivered; a wake notification has already done its job by ending the wait.
            if (!string.Equals(e.Channel, _deliveryChannel, StringComparison.Ordinal))
            {
                return;
            }

            lock (_pendingLock)
            {
                _pending.Add(e.Payload);
            }
        }

        private void DeliverPending()
        {
            // Take the queued payloads first, so a throwing handler can never cause a payload to be delivered twice.
            lock (_pendingLock)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                _delivering.AddRange(_pending);
                _pending.Clear();
            }

            Action<string> handler = _handler;
            try
            {
                for (int i = 0; i < _delivering.Count; i++)
                {
                    if (handler != null)
                    {
                        handler(_delivering[i]);
                    }
                }
            }
            finally
            {
                _delivering.Clear();
            }
        }

        /// <summary>Holds a connection string so a factory delegate can be made once from a method group.</summary>
        internal sealed class Factory
        {
            private const int WAKE_COMMAND_TIMEOUT_SECONDS = 2;
            private readonly string _listenerConnectionString;

            internal Factory(string listenerConnectionString)
            {
                _listenerConnectionString = listenerConnectionString;
            }

            /// <summary>Creates a new, unopened connection.</summary>
            internal IRelayListenerConnection Create()
            {
                return new NpgsqlRelayListenerConnection(_listenerConnectionString);
            }

            /// <summary>
            /// Sends one empty notification on the wake channel over a short-lived connection of its own, so a listener
            /// blocked in <c>WaitAsync</c> returns (ADR-015 note 2026-10-22: under Unity's Mono the wait's timeout does not
            /// fire). Called by <c>NpgsqlRelayListener.Stop</c> on the stopping thread; never touches the listener's
            /// connection. Blocks for one connect and one statement, bounded by the string's <c>Timeout</c>.
            /// </summary>
            /// <param name="wakeChannel">The listener's wake channel.</param>
            internal void SendWake(string wakeChannel)
            {
                using (var connection = new NpgsqlConnection(_listenerConnectionString))
                {
                    connection.Open();
                    using (NpgsqlCommand command = connection.CreateCommand())
                    {
                        // Short, so a hung server cannot hold the stopping thread for Npgsql's default 30 s.
                        command.CommandTimeout = WAKE_COMMAND_TIMEOUT_SECONDS;
                        command.CommandText = "SELECT pg_notify(@channel, '')";
                        command.Parameters.AddWithValue("channel", wakeChannel);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}

using System;
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
        /// <summary>Receives the payload text of every notification, on the thread that is inside <see cref="Wait"/>.</summary>
        Action<string> NotificationHandler { set; }

        /// <summary>Opens the connection (blocking).</summary>
        void Open();

        /// <summary>Issues <c>LISTEN</c> on the channel (blocking).</summary>
        /// <param name="channel">The channel name.</param>
        void Listen(string channel);

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
    /// <b>Threading.</b> Every call, including <c>Dispose</c>, comes from the one listener thread; Npgsql raises
    /// <c>Notification</c> on the thread that is inside <c>Wait</c>. Not thread-safe.
    /// </para>
    /// <para>
    /// <b>Allocation.</b> Npgsql allocates the payload string and the event arguments for each notification (off the
    /// tick thread, a few per hour). <c>LISTEN</c> cannot take a parameter, so the channel is written as a quoted
    /// identifier (embedded quotes doubled), which keeps its exact case, the same text <c>pg_notify</c> receives.
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
        private Action<string> _handler;

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
            using (NpgsqlCommand command = _connection.CreateCommand())
            {
                command.CommandText = "LISTEN \"" + channel.Replace("\"", "\"\"") + "\"";
                command.ExecuteNonQuery();
            }
        }

        /// <inheritdoc />
        public bool Wait(int timeoutMs)
        {
            return _connection.Wait(timeoutMs);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _connection.Notification -= OnNotification;
            _connection.Dispose();
        }

        private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
        {
            Action<string> handler = _handler;
            if (handler != null)
            {
                handler(e.Payload);
            }
        }

        /// <summary>Holds a connection string so a factory delegate can be made once from a method group.</summary>
        internal sealed class Factory
        {
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
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace IronGrind.Networking
{
    /// <summary>
    /// The seam under <see cref="NpgsqlRelayPublisher"/> (ADR-015 Decision 2): starts one <c>pg_notify</c> command and
    /// returns its task. Internal so EditMode tests can replace the database call; the production implementation is
    /// <see cref="NpgsqlRelayNotifyCommandStarter"/>.
    /// </summary>
    /// <remarks>
    /// Called on the tick thread. An implementation returns as soon as the command is started and never blocks on the
    /// database. It may throw synchronously; the publisher catches that.
    /// </remarks>
    internal interface IRelayNotifyCommandStarter
    {
        /// <summary>Starts <c>SELECT pg_notify(@channel, @payload)</c> and returns the running task.</summary>
        /// <param name="channel">The notification channel.</param>
        /// <param name="payload">The envelope text.</param>
        /// <param name="commandTimeoutMs">The command's own timeout, in milliseconds.</param>
        /// <param name="cancellationToken">Cancels the command.</param>
        /// <returns>The task of the command; it ends when the statement ends (success, failure or cancellation).</returns>
        Task<int> Start(string channel, string payload, int commandTimeoutMs, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Production <see cref="IRelayNotifyCommandStarter"/>: <c>dataSource.CreateCommand(sql)</c>, two parameters,
    /// <c>CommandTimeout</c> from the publish timeout, then <c>ExecuteNonQueryAsync(token)</c>. It never creates or
    /// disposes the data source (the composition root owns it).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Command disposal (Npgsql 8.0.9).</b> A command made by <c>NpgsqlDataSource.CreateCommand</c> owns its
    /// connection: it takes one from the data source's pool when it executes and gives it back when the execution ends
    /// (documented behaviour of the data-source command, to be confirmed by ADR-015 Verification 5). This class does not
    /// rely on that alone: it also disposes the command in a continuation that runs when the task ends (success,
    /// failure or cancellation). The continuation runs synchronously on whichever thread completes the task (in
    /// practice an I/O or thread-pool thread; the tick thread only if the task has already ended when it is attached),
    /// so the command is never disposed while it still runs. The returned task is the command's own task, not the continuation, so the tick queue sees
    /// the real outcome.
    /// </para>
    /// <para>
    /// <b>Threading and allocation.</b> Tick thread only. Allocates the command, its parameters and one continuation
    /// per call (a few relayed messages per hour). The call sets <c>CommandTimeout</c> in whole seconds, at least 1
    /// (0 would mean no timeout).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// IRelayNotifyCommandStarter starter = new NpgsqlRelayNotifyCommandStarter(dataSource);
    /// Task&lt;int&gt; task = starter.Start("irongrind_relay", payload, 2000, cts.Token);
    /// </code>
    /// </example>
    internal sealed class NpgsqlRelayNotifyCommandStarter : IRelayNotifyCommandStarter
    {
        private const string SQL = "SELECT pg_notify(@channel, @payload)";
        private const int MILLISECONDS_PER_SECOND = 1000;

        private static readonly Action<Task<int>, object> DisposeCommandAction = DisposeCommand;

        private readonly NpgsqlDataSource _dataSource;

        /// <summary>Creates the starter over the zone's data source, which it never creates or disposes.</summary>
        /// <param name="dataSource">The zone's pooled data source; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        internal NpgsqlRelayNotifyCommandStarter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <inheritdoc />
        public Task<int> Start(string channel, string payload, int commandTimeoutMs, CancellationToken cancellationToken)
        {
            NpgsqlCommand command = _dataSource.CreateCommand(SQL);
            try
            {
                command.Parameters.AddWithValue("channel", channel);
                command.Parameters.AddWithValue("payload", payload);
                command.CommandTimeout = Math.Max(1, (commandTimeoutMs + MILLISECONDS_PER_SECOND - 1) / MILLISECONDS_PER_SECOND);
                Task<int> task = command.ExecuteNonQueryAsync(cancellationToken);
                task.ContinueWith(
                    DisposeCommandAction, command, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return task;
            }
            catch
            {
                command.Dispose();
                throw;
            }
        }

        private static void DisposeCommand(Task<int> finished, object state)
        {
            try
            {
                ((NpgsqlCommand)state).Dispose();
            }
            catch (Exception)
            {
                // The outcome of the publish is reported through the tracked task; a failed dispose adds nothing.
            }
        }
    }
}

using System;
using Npgsql;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The connection string fields as plain values, so tests read what the composition built without naming an Npgsql
    /// type (the test assembly does not reference Npgsql.dll; ADR-012 Decision 1).
    /// </summary>
    internal struct ZoneConnectionFields
    {
        /// <summary>Host.</summary>
        public string Host;

        /// <summary>Port.</summary>
        public int Port;

        /// <summary>Database name.</summary>
        public string Database;

        /// <summary>User name.</summary>
        public string Username;

        /// <summary>Password (round-tripped through the builder's escaping).</summary>
        public string Password;

        /// <summary>Connection timeout in seconds.</summary>
        public int Timeout;

        /// <summary>Command timeout in seconds.</summary>
        public int CommandTimeout;

        /// <summary>Whether pooling is on.</summary>
        public bool Pooling;

        /// <summary>Minimum pool size.</summary>
        public int MinPoolSize;

        /// <summary>Maximum pool size.</summary>
        public int MaxPoolSize;

        /// <summary>Connection idle lifetime in seconds.</summary>
        public int ConnectionIdleLifetime;

        /// <summary>Application name.</summary>
        public string ApplicationName;
    }

    /// <summary>
    /// Test access for Networking Core Story 044 (AC-2, AC-9, AC-11). Used ONLY by tests, through
    /// <c>InternalsVisibleTo</c>; no signature names an Npgsql type. Production code never calls it.
    /// </summary>
    /// <example>
    /// <code>
    /// ZoneConnectionFields f = ZoneCompositionDatabaseTestAccess.BuildFields(config, "pw", 7u);
    /// int rows = ZoneCompositionDatabaseTestAccess.CountConnections(adminConnectionString, f.ApplicationName);
    /// </code>
    /// </example>
    internal static class ZoneCompositionDatabaseTestAccess
    {
        /// <summary>Builds the zone connection string exactly as S4 does and reads its fields back.</summary>
        /// <param name="config">The server config.</param>
        /// <param name="password">The password.</param>
        /// <param name="zoneId">The zone id.</param>
        /// <returns>The fields of the built string.</returns>
        internal static ZoneConnectionFields BuildFields(ZoneServerConfig config, string password, uint zoneId)
        {
            return Parse(ZoneServerComposition.BuildConnectionString(config, password, zoneId));
        }

        /// <summary>Reads the fields of an Npgsql connection string (for example <c>IRONGRIND_DEV_PG</c>).</summary>
        /// <param name="connectionString">An Npgsql connection string.</param>
        /// <returns>Its fields.</returns>
        internal static ZoneConnectionFields Parse(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return new ZoneConnectionFields
            {
                Host = builder.Host,
                Port = builder.Port,
                Database = builder.Database,
                Username = builder.Username,
                Password = builder.Password,
                Timeout = builder.Timeout,
                CommandTimeout = builder.CommandTimeout,
                Pooling = builder.Pooling,
                MinPoolSize = builder.MinPoolSize,
                MaxPoolSize = builder.MaxPoolSize,
                ConnectionIdleLifetime = builder.ConnectionIdleLifetime,
                ApplicationName = builder.ApplicationName
            };
        }

        /// <summary>The application name S4 sets for a zone id.</summary>
        /// <param name="zoneId">The zone id.</param>
        /// <returns>The application name.</returns>
        internal static string ApplicationNameOf(uint zoneId)
        {
            return ZoneServerComposition.ApplicationNameFor(zoneId);
        }

        /// <summary>True once S4 created the data source.</summary>
        /// <param name="composition">The composition.</param>
        /// <returns>True when a data source exists.</returns>
        internal static bool DataSourceCreated(ZoneServerComposition composition)
        {
            return composition.DataSourceCreated;
        }

        /// <summary>True while the composition's relay listener is the real one and its thread is alive.</summary>
        /// <param name="composition">The composition.</param>
        /// <returns>True when the real listener is running.</returns>
        internal static bool ListenerIsRunning(ZoneServerComposition composition)
        {
            var listener = composition.Listener as IronGrind.Networking.NpgsqlRelayListener;
            return listener != null && listener.IsRunning;
        }

        /// <summary>How many times the Dispose pass disposed the data source.</summary>
        /// <param name="composition">The composition.</param>
        /// <returns>0 or 1.</returns>
        internal static int DataSourceDisposeCount(ZoneServerComposition composition)
        {
            return composition.DataSourceDisposeCount;
        }

        /// <summary>
        /// Counts the sessions of <c>pg_stat_activity</c> whose <c>application_name</c> equals the given name. Opens its own
        /// unpooled connection with the connection string the test passes in.
        /// </summary>
        /// <param name="adminConnectionString">A connection string for any role that may read <c>pg_stat_activity</c>.</param>
        /// <param name="applicationName">The application name to count.</param>
        /// <returns>The number of sessions.</returns>
        internal static int CountConnections(string adminConnectionString, string applicationName)
        {
            return CountSessions(adminConnectionString, applicationName,
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name");
        }

        /// <summary>
        /// Counts the sessions of the application name whose last statement was a <c>LISTEN</c>: the relay listener's own
        /// connection once it is in its wait loop (the pooled connections of the same name never run <c>LISTEN</c>).
        /// </summary>
        /// <param name="adminConnectionString">A connection string for any role that may read <c>pg_stat_activity</c>.</param>
        /// <param name="applicationName">The application name to count.</param>
        /// <returns>The number of listening sessions.</returns>
        internal static int CountListeningConnections(string adminConnectionString, string applicationName)
        {
            return CountSessions(adminConnectionString, applicationName,
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name AND query ILIKE 'LISTEN%'");
        }

        /// <summary>Sends one <c>pg_notify(channel, payload)</c> over its own unpooled connection.</summary>
        /// <param name="adminConnectionString">A connection string for the test database.</param>
        /// <param name="channel">The channel.</param>
        /// <param name="payload">The payload.</param>
        internal static void Notify(string adminConnectionString, string channel, string payload)
        {
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Pooling = false };
            using (var connection = new NpgsqlConnection(builder.ConnectionString))
            {
                connection.Open();
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT pg_notify(@channel, @payload)";
                    command.Parameters.AddWithValue("channel", channel);
                    command.Parameters.AddWithValue("payload", payload);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static int CountSessions(string adminConnectionString, string applicationName, string sql)
        {
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Pooling = false };
            using (var connection = new NpgsqlConnection(builder.ConnectionString))
            {
                connection.Open();
                using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("name", applicationName);
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }
    }
}

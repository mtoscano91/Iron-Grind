using Dapper;
using Npgsql;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Development check that Npgsql and Dapper work in the project (Character Persistence Story 012,
    /// ADR-006 Decision 2). It names <c>NpgsqlDataSource</c> and a Dapper extension method so that the
    /// precompiled references of <c>IronGrind.ServerLogic</c> are exercised by the compiler.
    /// <para>
    /// Synchronous on purpose: this is a dev-time round trip run from a test or the Editor, never from the
    /// server tick. Real persistence code (Stories 013 and 014) is asynchronous and off the tick.
    /// Not thread-safe by contract; each call builds its own data source.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// int one = NpgsqlRoundTripProbe.SelectOne("Host=127.0.0.1;Port=5432;Username=irongrind;Password=...;Database=irongrind");
    /// </code>
    /// </example>
    internal static class NpgsqlRoundTripProbe
    {
        /// <summary>Opens a connection, runs <c>SELECT 1</c> through Dapper and returns the value (1).</summary>
        /// <param name="connectionString">An Npgsql connection string.</param>
        /// <returns>The scalar the server returned.</returns>
        internal static int SelectOne(string connectionString)
        {
            using (NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString))
            using (NpgsqlConnection connection = dataSource.OpenConnection())
            {
                return connection.ExecuteScalar<int>("SELECT 1");
            }
        }
    }
}

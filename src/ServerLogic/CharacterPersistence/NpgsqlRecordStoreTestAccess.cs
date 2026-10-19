using System;
using System.Data.Common;
using Npgsql;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Factory for the database tests of <see cref="NpgsqlCharacterRecordStore"/> (Character Persistence Story 014). Used ONLY
    /// by tests, through <c>InternalsVisibleTo</c>. No signature here names an Npgsql type, so the test assembly,
    /// which does not reference Npgsql.dll, needs none (ADR-012 Decision 1). Production code never calls it; the
    /// composition root builds the data source and the store itself.
    /// </summary>
    internal static class NpgsqlRecordStoreTestAccess
    {
        /// <summary>Builds a data source from a connection string and a store over it.</summary>
        /// <param name="connectionString">An Npgsql connection string.</param>
        /// <returns>The store, and the object that owns the data source; the caller disposes it when the test ends.</returns>
        internal static (ICharacterRecordStore Store, IDisposable DataSource) CreateStoreWithOwnedDataSource(string connectionString)
        {
            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            return (new NpgsqlCharacterRecordStore(dataSource), dataSource);
        }

        /// <summary>Builds one data source and two stores over it (the "never owns the data source" check).</summary>
        /// <param name="connectionString">An Npgsql connection string.</param>
        /// <returns>Both stores, and the object that owns the data source; the caller disposes it when the test ends.</returns>
        internal static (ICharacterRecordStore First, ICharacterRecordStore Second, IDisposable DataSource) CreateTwoStoresOverOneDataSource(string connectionString)
        {
            NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            return (new NpgsqlCharacterRecordStore(dataSource), new NpgsqlCharacterRecordStore(dataSource), dataSource);
        }

        /// <summary>Opens a raw connection (a second connection for the tests: seeding rows, changing a column, taking a row lock, cleaning up).</summary>
        /// <param name="connectionString">An Npgsql connection string.</param>
        /// <returns>An open connection; the caller disposes it.</returns>
        internal static DbConnection OpenConnection(string connectionString)
        {
            var connection = new NpgsqlConnection(connectionString);
            try
            {
                connection.Open();
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            return connection;
        }
    }
}

using System;
using IronGrind.CharacterPersistence;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Round trip against the development PostgreSQL (Docker container or portable binaries; Character Persistence Story 012, ADR-006).
    /// Explicit: it needs a server from <c>tools/postgres/README.md</c> running and <c>IRONGRIND_DEV_PG</c> set, so the default
    /// suite never runs it. Run it from the Test Runner by selecting the "Database" category.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_NpgsqlRoundTrip_Tests
    {
        private const string CONNECTION_VARIABLE = "IRONGRIND_DEV_PG";

        /// <summary><c>SELECT 1</c> through Dapper over an Npgsql data source returns 1.</summary>
        [Test]
        [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
        [Category("Database")]
        public void test_select_one_through_dapper_returns_one()
        {
            string connectionString = Environment.GetEnvironmentVariable(CONNECTION_VARIABLE);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore(CONNECTION_VARIABLE + " is not set (see tools/postgres/README.md).");
            }

            int value = NpgsqlRoundTripProbe.SelectOne(connectionString);

            Assert.AreEqual(1, value);
        }
    }
}

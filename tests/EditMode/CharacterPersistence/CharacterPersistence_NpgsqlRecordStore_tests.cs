using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.LevelingSystem;
using IronGrind.Tests.EditMode.CharacterStats;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>
    /// Database access shared by the Story 014 database fixtures. Names no Npgsql type: the connections are plain
    /// <see cref="DbConnection"/>s from <c>NpgsqlRecordStoreTestAccess</c> (ADR-012 Decision 1).
    /// </summary>
    internal static class PgTestDatabase
    {
        private const string CONNECTION_VARIABLE = "IRONGRIND_DEV_PG";

        /// <summary>Returns the connection string of the dev database, or ignores the test when <c>IRONGRIND_DEV_PG</c> is not set.</summary>
        internal static string ConnectionStringOrIgnore()
        {
            string connectionString = Environment.GetEnvironmentVariable(CONNECTION_VARIABLE);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore(CONNECTION_VARIABLE + " is not set (see tools/postgres/README.md).");
            }

            return connectionString;
        }

        /// <summary>Runs one statement on an open connection.</summary>
        internal static void Execute(DbConnection connection, string sql)
        {
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Runs one query on an open connection and returns its first value.</summary>
        internal static object Scalar(DbConnection connection, string sql)
        {
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                return command.ExecuteScalar();
            }
        }

        /// <summary>Opens a connection of its own, runs one statement and closes it.</summary>
        internal static void Execute(string connectionString, string sql)
        {
            using (DbConnection connection = NpgsqlRecordStoreTestAccess.OpenConnection(connectionString))
            {
                Execute(connection, sql);
            }
        }

        /// <summary>Opens a connection of its own, runs one query, returns the first value as a <c>long</c> and closes it.</summary>
        internal static long ScalarLong(string connectionString, string sql)
        {
            using (DbConnection connection = NpgsqlRecordStoreTestAccess.OpenConnection(connectionString))
            {
                return Convert.ToInt64(Scalar(connection, sql));
            }
        }

        /// <summary>Deletes the rows of the given characters: their <c>pending_purchases</c> first (foreign key), then the character rows.</summary>
        internal static void DeleteCharacters(string connectionString, params uint[] ids)
        {
            string list = string.Join(",", ids.Select(id => id.ToString()).ToArray());
            using (DbConnection connection = NpgsqlRecordStoreTestAccess.OpenConnection(connectionString))
            {
                Execute(connection, "DELETE FROM pending_purchases WHERE char_id IN (" + list + ")");
                Execute(connection, "DELETE FROM character_records WHERE character_id IN (" + list + ")");
            }
        }
    }

    /// <summary>
    /// Story 014: the shared store contract (Story 002's suite) against the real PostgreSQL store, plus the timestamp
    /// variants for a store whose database stamps <c>NOW()</c>. Explicit: needs a local PostgreSQL with the Story 013 schema
    /// and <c>IRONGRIND_DEV_PG</c>; run it with <c>-testFilter</c> (tools/postgres/README.md). Each test starts and ends with
    /// the rows of the ids it uses deleted.
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
    [Category("Database")]
    internal sealed class CharacterPersistence_NpgsqlRecordStoreContract_Tests : CharacterRecordStoreContractTests
    {
        private static readonly uint[] USED_IDS = { CHAR_A_RAW, CHAR_B_RAW };

        private string _connectionString;
        private IDisposable _dataSource;

        protected override ICharacterRecordStore CreateStore()
        {
            _connectionString = PgTestDatabase.ConnectionStringOrIgnore();
            PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(_connectionString);
            _dataSource = created.DataSource;
            return created.Store;
        }

        [TearDown]
        public void TearDownDatabase()
        {
            // Runs also when the SetUp ignored the test; then there is nothing to clean.
            if (_dataSource != null)
            {
                _dataSource.Dispose();
                _dataSource = null;
            }

            if (!string.IsNullOrEmpty(_connectionString))
            {
                PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            }
        }

        [Test]
        public void NpgsqlStore_Insert_BothTimestampsAreSetByTheDatabase()
        {
            Assert.IsTrue(Insert(MakeRecord(CHAR_A, VERSION_FOUR)));

            CharacterRecord fetched = Fetch(CHAR_A);

            Assert.AreNotEqual(default(DateTime), fetched.CreatedAtUtc);
            Assert.AreNotEqual(default(DateTime), fetched.LastSavedAtUtc);
            Assert.AreEqual(DateTimeKind.Utc, fetched.CreatedAtUtc.Kind);
            Assert.AreEqual(DateTimeKind.Utc, fetched.LastSavedAtUtc.Kind);
            Assert.AreEqual(fetched.CreatedAtUtc, fetched.LastSavedAtUtc, "one statement, one NOW()");
            Assert.AreEqual(VERSION_FOUR, fetched.SaveVersion);
        }

        [Test]
        public void NpgsqlStore_Update_LastSavedIsSetToNowAndCreatedIsUnchanged()
        {
            Insert(MakeRecord(CHAR_A, VERSION_FOUR));
            // Backdate both timestamps by an hour with raw SQL, so "later" cannot be an artefact of two NOW() calls in one millisecond.
            PgTestDatabase.Execute(_connectionString, "UPDATE character_records SET last_saved_at_utc = last_saved_at_utc - interval '1 hour', "
                + "created_at_utc = created_at_utc - interval '1 hour' WHERE character_id = " + CHAR_A_RAW);
            CharacterRecord before = Fetch(CHAR_A);
            CharacterRecord incoming = MakeRecord(CHAR_A, 99u);
            incoming.Level = 13;
            incoming.CreatedAtUtc = T2;

            Assert.AreEqual(1, Update(incoming, VERSION_FOUR));

            CharacterRecord after = Fetch(CHAR_A);
            Assert.Greater(after.LastSavedAtUtc, before.LastSavedAtUtc, "strictly later than the backdated value");
            long secondsFromNow = PgTestDatabase.ScalarLong(_connectionString,
                "SELECT ABS(EXTRACT(EPOCH FROM (NOW() - last_saved_at_utc)))::bigint FROM character_records WHERE character_id = " + CHAR_A_RAW);
            Assert.Less(secondsFromNow, 60L, "last_saved_at_utc is within 60 s of the database's NOW()");
            Assert.AreEqual(before.CreatedAtUtc, after.CreatedAtUtc, "created_at_utc is unchanged by the update");
            Assert.AreEqual(VERSION_FOUR + 1u, after.SaveVersion);
            Assert.AreEqual(13, after.Level);
        }
    }

    /// <summary>
    /// Story 014's other database criteria: the round trip, the update statement, AC-CP-19 against a second connection,
    /// cancellation and the write timeout through <see cref="CharacterPersistenceService"/>, and corrupt rows. Explicit, like
    /// the contract fixture. Every id is test-only (9_000_000 and up) so the dev database's real characters are never touched;
    /// the one exception is <c>uint.MaxValue</c> (<c>ID_EXTREME</c>), kept because the test is about the largest id.
    /// </summary>
    [TestFixture]
    [Explicit("Needs a local PostgreSQL and IRONGRIND_DEV_PG — tools/postgres/README.md")]
    [Category("Database")]
    internal sealed class CharacterPersistence_NpgsqlRecordStore_Tests
    {
        private const uint ID_EXTREME = uint.MaxValue;
        private const uint ID_EMPTY = 9_000_002u;
        private const uint ID_UPDATE = 9_000_003u;
        private const uint ID_CONFLICT = 9_000_004u;
        private const uint ID_CANCEL = 9_000_005u;
        private const uint ID_TIMEOUT = 9_000_006u;
        private const uint ID_CORRUPT = 9_000_007u;
        private const uint ID_POOL = 9_000_008u;
        private const uint ID_NEGATIVE = 9_000_009u;
        private const uint ID_WRAP = 9_000_010u;
        private const int WAIT_MS = 5000;
        private const int GEAR_COUNT = 7;
        private const int INVENTORY_COUNT = 20;
        private const byte WARRIOR_CLASS_TYPE = 1;
        private const int CAP_THRESHOLD = 300;

        private static readonly uint[] USED_IDS = { ID_EXTREME, ID_EMPTY, ID_UPDATE, ID_CONFLICT, ID_CANCEL, ID_TIMEOUT, ID_CORRUPT, ID_POOL, ID_NEGATIVE, ID_WRAP };
        private static readonly AccountID ACCOUNT = new AccountID(7u);
        private static readonly DateTime SERVICE_TIME = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);

        // 26 UTF-8 bytes in 13 characters: 3 x 3-byte CJK, 3 x 2-byte Latin, 5 x 2-byte A-ring, 1 ASCII.
        private const string MULTIBYTE_NAME = "日本語ÇñéÅÅÅÅÅx";

        private string _connectionString;
        private IDisposable _dataSource;
        private ICharacterRecordStore _store;
        private DbConnection _lockConnection;

        [SetUp]
        public void SetUp()
        {
            _connectionString = PgTestDatabase.ConnectionStringOrIgnore();
            PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(_connectionString);
            _store = created.Store;
            _dataSource = created.DataSource;
        }

        [TearDown]
        public void TearDown()
        {
            ReleaseLock();
            if (_dataSource != null)
            {
                _dataSource.Dispose();
                _dataSource = null;
            }

            if (!string.IsNullOrEmpty(_connectionString))
            {
                PgTestDatabase.DeleteCharacters(_connectionString, USED_IDS);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static T Complete<T>(Task<T> task)
        {
            Assert.IsTrue(task.Wait(WAIT_MS), "The task did not complete within " + WAIT_MS + " ms.");
            return task.Result;
        }

        private static TException Faults<TException>(Task task) where TException : Exception
        {
            try
            {
                task.Wait(WAIT_MS);
            }
            catch (AggregateException)
            {
                // Inspected below.
            }

            Assert.IsTrue(task.IsFaulted, "The task did not fault, status " + task.Status);
            Exception inner = task.Exception.InnerException;
            Assert.IsInstanceOf<TException>(inner);
            return (TException)inner;
        }

        private CharacterRecord Fetch(uint id)
        {
            return Complete(_store.FetchAsync(new CharacterID(id), CancellationToken.None));
        }

        private bool Insert(CharacterRecord record)
        {
            return Complete(_store.InsertAsync(record, CancellationToken.None));
        }

        private int Update(CharacterRecord record, uint expected)
        {
            return Complete(_store.UpdateAsync(record, expected, CancellationToken.None));
        }

        private long Raw(string sql)
        {
            return PgTestDatabase.ScalarLong(_connectionString, sql);
        }

        private void RawExecute(string sql)
        {
            PgTestDatabase.Execute(_connectionString, sql);
        }

        private string RawText(string sql)
        {
            using (DbConnection connection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString))
            {
                return Convert.ToString(PgTestDatabase.Scalar(connection, sql));
            }
        }

        // Takes an ACCESS EXCLUSIVE lock on the whole table in a transaction of its own; every statement on the table then waits.
        private void LockTable()
        {
            _lockConnection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString);
            PgTestDatabase.Execute(_lockConnection, "BEGIN");
            PgTestDatabase.Execute(_lockConnection, "LOCK TABLE character_records IN ACCESS EXCLUSIVE MODE");
        }

        // Waits (bounded) for the task to end and asserts it ended cancelled, or faulted with an OperationCanceledException.
        private static void AssertEndsCancelledWithin(Task task, int milliseconds)
        {
            try
            {
                task.Wait(milliseconds);
            }
            catch (AggregateException)
            {
                // Inspected below.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within " + milliseconds + " ms, status " + task.Status);
            if (task.IsFaulted)
            {
                Assert.IsInstanceOf<OperationCanceledException>(task.Exception.GetBaseException());
            }
            else
            {
                Assert.IsTrue(task.IsCanceled, "The task ended " + task.Status);
            }
        }

        private static CharacterRecord MakeExtremeRecord()
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(new AccountID(uint.MaxValue), new CharacterID(ID_EXTREME), 255);
            r.CharacterName = MULTIBYTE_NAME;
            r.Level = 59;
            r.Experience = 123456;
            r.HeldFreePoints = 37;
            r.STR = 901;
            r.DEX = 902;
            r.VIT = 903;
            r.INT = 904;
            r.MaxHP = 98765;
            r.MaxMP = 54321;
            r.AttackPower = 1111;
            r.Defense = 2222;
            r.MagicDefense = 3333;
            r.CritChance = 0.065f;
            r.CritMultiplier = 1.75f;
            r.AttackRange = 3.25f;
            r.AttackSpeedMultiplier = 1.03f;
            r.MovementSpeed = 5.5f;
            r.CurrentHP = 12345.678f;
            r.CurrentMP = 987.654f;
            r.GoldBalance = uint.MaxValue;
            r.GoldVersion = uint.MaxValue;
            r.LastZoneID = new ZoneID(uint.MaxValue);
            r.SaveVersion = uint.MaxValue;
            for (int i = 0; i < GEAR_COUNT; i++)
            {
                r.GearSlots[i] = new GearSlotRecord(new ItemID(4000000000u + (uint)i), (byte)(i + 1));
            }

            for (int j = 0; j < INVENTORY_COUNT; j++)
            {
                r.InventorySlots[j] = new ItemID(5000u + (uint)j);
                r.InventoryItemCounts[j] = j + 1;
                r.InventoryEnhancementLevels[j] = (byte)(j * 3);
            }

            return r;
        }

        private static CharacterRecord MakeSimpleRecord(uint id, uint saveVersion)
        {
            CharacterRecord r = CharacterStubDefaults.CreateRecord(ACCOUNT, new CharacterID(id), WARRIOR_CLASS_TYPE);
            r.CharacterName = "Brann";
            r.Level = 10;
            r.SaveVersion = saveVersion;
            r.GearSlots[2] = new GearSlotRecord(new ItemID(77u), 3);
            r.InventorySlots[0] = new ItemID(5u);
            r.InventoryItemCounts[0] = 4;
            r.InventoryEnhancementLevels[0] = 2;
            return r;
        }

        private void LockRow(uint id)
        {
            _lockConnection = NpgsqlRecordStoreTestAccess.OpenConnection(_connectionString);
            PgTestDatabase.Execute(_lockConnection, "BEGIN");
            PgTestDatabase.Scalar(_lockConnection, "SELECT character_id FROM character_records WHERE character_id = " + id + " FOR UPDATE");
        }

        private void ReleaseLock()
        {
            if (_lockConnection == null)
            {
                return;
            }

            try
            {
                PgTestDatabase.Execute(_lockConnection, "ROLLBACK");
            }
            finally
            {
                _lockConnection.Dispose();
                _lockConnection = null;
            }
        }

        // ---------------------------------------------------------------- round trip

        [Test]
        public void NpgsqlStore_RoundTripOfTheExtremeRecord_EveryFieldIsEqual()
        {
            CharacterRecord record = MakeExtremeRecord();
            Assert.AreEqual(26, Encoding.UTF8.GetByteCount(record.CharacterName), "the name is 26 UTF-8 bytes");

            Assert.IsTrue(Insert(record));

            CharacterRecord fetched = Fetch(ID_EXTREME);
            record.CreatedAtUtc = fetched.CreatedAtUtc;
            record.LastSavedAtUtc = fetched.LastSavedAtUtc;
            CharacterRecordStoreContractTests.AssertSame(record, fetched);
            Assert.AreEqual(uint.MaxValue, fetched.SaveVersion);
            Assert.AreEqual(0.065f, fetched.CritChance);
        }

        [Test]
        public void NpgsqlStore_RoundTripWithAllSlotsEmpty_EveryFieldIsEqual()
        {
            CharacterRecord record = CharacterStubDefaults.CreateRecord(ACCOUNT, new CharacterID(ID_EMPTY), WARRIOR_CLASS_TYPE);
            record.CharacterName = "Empty";

            Assert.IsTrue(Insert(record));

            CharacterRecord fetched = Fetch(ID_EMPTY);
            record.CreatedAtUtc = fetched.CreatedAtUtc;
            record.LastSavedAtUtc = fetched.LastSavedAtUtc;
            CharacterRecordStoreContractTests.AssertSame(record, fetched);
        }

        [Test]
        public void NpgsqlStore_SlotWithoutEnhancementLevelInTheRow_ReadsAsLevelZero()
        {
            Insert(MakeSimpleRecord(ID_CORRUPT, 0u));
            RawExecute("UPDATE character_records SET gear_slots = '[{\"item_id\":700},null,null,null,null,null,null]'::jsonb WHERE character_id = " + ID_CORRUPT);

            CharacterRecord fetched = Fetch(ID_CORRUPT);

            Assert.AreEqual(700u, fetched.GearSlots[0].ItemId.RawValue);
            Assert.AreEqual(0, fetched.GearSlots[0].EnhancementLevel);
        }

        // ---------------------------------------------------------------- update statement and AC-CP-19

        [Test]
        public void NpgsqlStore_Update_RaisesTheVersionByOneOnlyWhenTheExpectedVersionMatches()
        {
            Insert(MakeSimpleRecord(ID_UPDATE, 5u));
            CharacterRecord changed = MakeSimpleRecord(ID_UPDATE, 0u);
            changed.Level = 22;

            Assert.AreEqual(1, Update(changed, 5u));

            Assert.AreEqual(6L, Raw("SELECT save_version FROM character_records WHERE character_id = " + ID_UPDATE));
            Assert.AreEqual(22L, Raw("SELECT level FROM character_records WHERE character_id = " + ID_UPDATE));
            changed.Level = 23;
            Assert.AreEqual(0, Update(changed, 5u), "the old version no longer matches");
            Assert.AreEqual(6L, Raw("SELECT save_version FROM character_records WHERE character_id = " + ID_UPDATE));
            Assert.AreEqual(22L, Raw("SELECT level FROM character_records WHERE character_id = " + ID_UPDATE));
        }

        [Test]
        public void NpgsqlStore_SaveVersionChangedByASecondConnection_UpdateAffectsZeroRowsAndTheRowIsUnchanged()
        {
            Insert(MakeSimpleRecord(ID_CONFLICT, 0u));
            RawExecute("UPDATE character_records SET save_version = 9 WHERE character_id = " + ID_CONFLICT);
            CharacterRecord before = Fetch(ID_CONFLICT);
            CharacterRecord changed = MakeSimpleRecord(ID_CONFLICT, 0u);
            changed.Level = 50;
            changed.GoldBalance = 12345u;

            Assert.AreEqual(0, Update(changed, 0u));

            CharacterRecord after = Fetch(ID_CONFLICT);
            Assert.AreEqual(9u, after.SaveVersion);
            CharacterRecordStoreContractTests.AssertSame(before, after);
        }

        // ---------------------------------------------------------------- cancellation

        [Test]
        public void NpgsqlStore_InsertWithACancelledToken_EndsCancelledAndWritesNothing()
        {
            Task<bool> t = _store.InsertAsync(MakeSimpleRecord(ID_CANCEL, 0u), new CancellationToken(true));
            SettleTask(t);

            Assert.IsTrue(t.IsCanceled);
            Assert.AreEqual(0L, Raw("SELECT COUNT(*) FROM character_records WHERE character_id = " + ID_CANCEL));
        }

        [Test]
        public void NpgsqlStore_UpdateWithACancelledToken_EndsCancelledAndWritesNothing()
        {
            Insert(MakeSimpleRecord(ID_CANCEL, 3u));
            CharacterRecord changed = MakeSimpleRecord(ID_CANCEL, 0u);
            changed.Level = 40;

            Task<int> t = _store.UpdateAsync(changed, 3u, new CancellationToken(true));
            SettleTask(t);

            Assert.IsTrue(t.IsCanceled);
            Assert.AreEqual(3L, Raw("SELECT save_version FROM character_records WHERE character_id = " + ID_CANCEL));
            Assert.AreEqual(10L, Raw("SELECT level FROM character_records WHERE character_id = " + ID_CANCEL));
        }

        // Waits (bounded) for the task to end, whatever its outcome, and asserts that it ended; the outcome is the test's to inspect.
        private static void SettleTask(Task task)
        {
            try
            {
                task.Wait(WAIT_MS);
            }
            catch (AggregateException)
            {
                // The status is the test's to inspect.
            }

            Assert.IsTrue(task.IsCompleted, "The task did not end within " + WAIT_MS + " ms.");
        }

        [Test]
        public void NpgsqlStore_FetchWhileTheTableIsLocked_CancelledMidFlightEndsCancelledWithinThreeSeconds()
        {
            Insert(MakeSimpleRecord(ID_CANCEL, 0u));
            LockTable();
            var watch = Stopwatch.StartNew();

            try
            {
                using (var cts = new CancellationTokenSource(300))
                {
                    Task<CharacterRecord> t = _store.FetchAsync(new CharacterID(ID_CANCEL), cts.Token);
                    AssertEndsCancelledWithin(t, 3000);
                }

                Assert.Less(watch.ElapsedMilliseconds, 3000L);
            }
            finally
            {
                ReleaseLock();
            }
        }

        [Test]
        public void NpgsqlStore_InsertWhileTheTableIsLocked_CancelledMidFlightEndsCancelledWithinThreeSeconds()
        {
            LockTable();
            var watch = Stopwatch.StartNew();

            try
            {
                using (var cts = new CancellationTokenSource(300))
                {
                    Task<bool> t = _store.InsertAsync(MakeSimpleRecord(ID_CANCEL, 0u), cts.Token);
                    AssertEndsCancelledWithin(t, 3000);
                }

                Assert.Less(watch.ElapsedMilliseconds, 3000L);
            }
            finally
            {
                ReleaseLock();
            }

            Assert.AreEqual(0L, Raw("SELECT COUNT(*) FROM character_records WHERE character_id = " + ID_CANCEL));
        }

        // ---------------------------------------------------------------- connection pool

        [Test]
        public void NpgsqlStore_EveryOutcomeOverAPoolOfOneConnection_NeverLeaksTheConnection()
        {
            // Maximum Pool Size=1 and a 3 s connection timeout: a connection that one outcome leaked would make a later call time out.
            string pooled = _connectionString.TrimEnd(';', ' ') + ";Maximum Pool Size=1;Timeout=3";
            var created = NpgsqlRecordStoreTestAccess.CreateStoreWithOwnedDataSource(pooled);
            try
            {
                ICharacterRecordStore store = created.Store;
                var id = new CharacterID(ID_POOL);

                Assert.IsTrue(Complete(store.InsertAsync(MakeSimpleRecord(ID_POOL, 0u), CancellationToken.None)), "normal insert");
                Assert.IsNotNull(Complete(store.FetchAsync(id, CancellationToken.None)), "normal fetch");
                Assert.AreEqual(1, Complete(store.UpdateAsync(MakeSimpleRecord(ID_POOL, 0u), 0u, CancellationToken.None)), "normal update");
                Assert.AreEqual(0, Complete(store.UpdateAsync(MakeSimpleRecord(ID_POOL, 0u), 0u, CancellationToken.None)), "conflicting update");
                Assert.IsFalse(Complete(store.InsertAsync(MakeSimpleRecord(ID_POOL, 0u), CancellationToken.None)), "duplicate insert");

                RawExecute("UPDATE character_records SET gear_slots = '[null,null,null,null,null,null]'::jsonb WHERE character_id = " + ID_POOL);
                Faults<CharacterRecordCorruptException>(store.FetchAsync(id, CancellationToken.None));

                SettleTask(store.FetchAsync(id, new CancellationToken(true)));

                RawExecute("UPDATE character_records SET gear_slots = '[null,null,null,null,null,null,null]'::jsonb WHERE character_id = " + ID_POOL);
                Assert.IsNotNull(Complete(store.FetchAsync(id, CancellationToken.None)), "the last fetch got a connection");
            }
            finally
            {
                created.DataSource.Dispose();
            }
        }

        [Test]
        public void NpgsqlStore_TwoStoresOverOneDataSourceOfOneConnection_BothWorkInTurn()
        {
            // The store never owns or disposes the data source, and returns its connection before the call ends: a second store
            // over the same pool of one connection works after the first, and the first again after the second.
            Insert(MakeSimpleRecord(ID_POOL, 0u));
            string pooled = _connectionString.TrimEnd(';', ' ') + ";Maximum Pool Size=1;Timeout=3";
            var created = NpgsqlRecordStoreTestAccess.CreateTwoStoresOverOneDataSource(pooled);
            try
            {
                var id = new CharacterID(ID_POOL);
                Assert.IsNotNull(Complete(created.First.FetchAsync(id, CancellationToken.None)), "first store");
                Assert.IsNotNull(Complete(created.Second.FetchAsync(id, CancellationToken.None)), "second store");
                Assert.AreEqual(1, Complete(created.Second.UpdateAsync(MakeSimpleRecord(ID_POOL, 0u), 0u, CancellationToken.None)), "second store update");
                Assert.IsNotNull(Complete(created.First.FetchAsync(id, CancellationToken.None)), "first store again");
            }
            finally
            {
                created.DataSource.Dispose();
            }
        }

        // ---------------------------------------------------------------- values the database or the codec cannot hold

        [Test]
        public void NpgsqlStore_InsertWithNegativeInventoryCount_FaultsWithArgumentExceptionAndWritesNothing()
        {
            // The in-memory store accepts a negative count (InMemoryStore_InsertWithNegativeInventoryCount_...); this one rejects it.
            CharacterRecord record = MakeSimpleRecord(ID_NEGATIVE, 0u);
            record.InventorySlots[0] = new ItemID(5u);
            record.InventoryItemCounts[0] = -1;

            Task<bool> t = null;
            Assert.DoesNotThrow(() => t = _store.InsertAsync(record, CancellationToken.None), "the failure is on the task, not a synchronous throw");

            Faults<ArgumentException>(t);
            Assert.AreEqual(0L, Raw("SELECT COUNT(*) FROM character_records WHERE character_id = " + ID_NEGATIVE));
        }

        [Test]
        public void NpgsqlStore_UpdateWithNegativeInventoryCount_FaultsWithArgumentExceptionAndTheRowIsUnchanged()
        {
            Insert(MakeSimpleRecord(ID_NEGATIVE, 3u));
            string where = " WHERE character_id = " + ID_NEGATIVE;
            string slotsBefore = RawText("SELECT inventory_slots::text FROM character_records" + where);
            CharacterRecord changed = MakeSimpleRecord(ID_NEGATIVE, 0u);
            changed.Level = 40;
            changed.InventoryItemCounts[0] = -1;

            Task<int> t = null;
            Assert.DoesNotThrow(() => t = _store.UpdateAsync(changed, 3u, CancellationToken.None), "the failure is on the task, not a synchronous throw");

            Faults<ArgumentException>(t);
            Assert.AreEqual(3L, Raw("SELECT save_version FROM character_records" + where));
            Assert.AreEqual(10L, Raw("SELECT level FROM character_records" + where));
            Assert.AreEqual(slotsBefore, RawText("SELECT inventory_slots::text FROM character_records" + where));
        }

        [Test]
        public void NpgsqlStore_SaveVersionAtUintMaxValue_UpdateWrapsInTheColumnAndTheNextFetchIsCorrupt()
        {
            // Pins the open divergence from InMemoryCharacterRecordStore (wraps to 0) — TD-075.
            Insert(MakeSimpleRecord(ID_WRAP, uint.MaxValue));

            Assert.AreEqual(1, Update(MakeSimpleRecord(ID_WRAP, 0u), uint.MaxValue));

            Assert.AreEqual(4294967296L, Raw("SELECT save_version FROM character_records WHERE character_id = " + ID_WRAP));
            Faults<CharacterRecordCorruptException>(_store.FetchAsync(new CharacterID(ID_WRAP), CancellationToken.None));
        }

        // ---------------------------------------------------------------- write timeout through the service

        [Test]
        public void NpgsqlStore_RowLockedBySecondConnection_SaveThroughServiceEndsWithDatabaseErrorAndTheRowIsUnchanged()
        {
            // The service cancels the store's token at the write timeout (ADR-006 Decision 4), so Npgsql cancels the
            // UPDATE waiting on the lock and nothing is written after the lock is released.
            LogAssert.Expect(LogType.Error, new Regex(@"SaveIrreversibleOutcome \(LevelUp\) for .* failed.*TimeoutException"));
            var leveling = new LevelingService(BuildXpThresholds(), BuildClassRegistry());
            IronGrind.CharacterStats.CharacterStats stats = CharacterStatsFixture.CreateWithLeveling(leveling);
            leveling.AttachCharacterStats(stats);
            var currency = new CurrencySystem();
            var inventory = new RecordingInventory { ExportResult = InventorySnapshot.Empty };
            var zones = new FakeZoneLocator();
            var service = new CharacterPersistenceService(
                _store,
                stats,
                leveling,
                inventory,
                currency, new FakeEquipmentService(),
                zones,
                new CharacterWriteQueue(),
                () => SERVICE_TIME,
                TimeSpan.FromSeconds(1));
            var id = new CharacterID(ID_TIMEOUT);
            Insert(MakeSimpleRecord(ID_TIMEOUT, 0u));
            CharacterLoadResult loaded = Complete(service.LoadCharacter(id, ACCOUNT, CancellationToken.None));
            Assert.AreEqual(CharacterLoadCode.Success, loaded.Code);
            Assert.AreEqual(CharacterLoadCode.Success, service.ApplyLoadedRecord(CharacterStatsFixture.PlayerEntityId, loaded.Record));
            LockRow(ID_TIMEOUT);

            Task<CharacterSaveResult> save = service.SaveIrreversibleOutcome(id, IrreversibleOutcomeTrigger.LevelUp, CancellationToken.None);
            bool ended = save.Wait(3000);

            Assert.IsTrue(ended, "the save did not end within 3 s");
            Assert.AreEqual(CharacterSaveResult.DatabaseError, save.Result);

            // Proof that the server cancelled the UPDATE (not merely that the service stopped waiting): no UPDATE is still waiting on a lock.
            long waiting = -1;
            for (int attempt = 0; attempt < 40 && waiting != 0; attempt++)
            {
                waiting = Raw("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' "
                    + "AND query LIKE 'UPDATE character_records%'");
                if (waiting != 0)
                {
                    Thread.Sleep(50);
                }
            }

            Assert.AreEqual(0L, waiting, "the UPDATE waiting on the row lock was cancelled on the server");
            ReleaseLock();
            Assert.AreEqual(0L, Raw("SELECT save_version FROM character_records WHERE character_id = " + ID_TIMEOUT), "the cancelled command wrote nothing");

            LogAssert.NoUnexpectedReceived();
        }

        private static int[] BuildXpThresholds()
        {
            var t = new int[62];
            t[58] = 100;
            t[59] = 200;
            t[60] = CAP_THRESHOLD;
            t[61] = int.MaxValue;
            return t;
        }

        private static IClassRegistry BuildClassRegistry()
        {
            var registry = new ClassRegistry();
            registry.RegisterClass(WARRIOR_CLASS_TYPE, new ClassDefinition(
                strengthAutoAlloc: 2,
                dexterityAutoAlloc: 1,
                vitalityAutoAlloc: 1,
                intelligenceAutoAlloc: 0,
                freePointsPerLevel: 1));
            return registry;
        }

        // ---------------------------------------------------------------- corrupt rows

        [TestCase("account_id", -1L)]
        [TestCase("account_id", 4294967296L)]
        [TestCase("gold_version", -1L)]
        [TestCase("last_zone_id", -1L)]
        [TestCase("gold_balance", -1L)]
        [TestCase("gold_balance", 4294967296L)]
        [TestCase("gold_version", 4294967296L)]
        [TestCase("last_zone_id", 4294967296L)]
        [TestCase("save_version", 4294967296L)]
        [TestCase("save_version", -1L)]
        [TestCase("class_type", 256L)]
        [TestCase("class_type", -1L)]
        public void NpgsqlStore_ColumnOutsideTheRecordRange_FetchThrowsCorruptRecord(string column, long value)
        {
            Insert(MakeSimpleRecord(ID_CORRUPT, 0u));
            RawExecute("UPDATE character_records SET " + column + " = " + value + " WHERE character_id = " + ID_CORRUPT);

            Faults<CharacterRecordCorruptException>(_store.FetchAsync(new CharacterID(ID_CORRUPT), CancellationToken.None));
        }

        [TestCase("gear6")]
        [TestCase("gear8")]
        [TestCase("gearStringItemId")]
        [TestCase("gearUnknownKey")]
        [TestCase("inventory19")]
        [TestCase("inventoryObject")]
        [TestCase("inventoryStringCount")]
        public void NpgsqlStore_MalformedSlotJsonInTheRow_FetchThrowsCorruptRecord(string kind)
        {
            Insert(MakeSimpleRecord(ID_CORRUPT, 0u));
            string nulls6 = "null,null,null,null,null,null";
            string set;
            switch (kind)
            {
                case "gear6":
                    set = "gear_slots = '[" + nulls6 + "]'::jsonb";
                    break;
                case "gear8":
                    set = "gear_slots = '[" + nulls6 + ",null,null]'::jsonb";
                    break;
                case "gearStringItemId":
                    set = "gear_slots = '[{\"item_id\":\"x\",\"enhancement_level\":0}," + nulls6 + "]'::jsonb";
                    break;
                case "gearUnknownKey":
                    set = "gear_slots = '[{\"item_id\":1,\"bogus\":2}," + nulls6 + "]'::jsonb";
                    break;
                case "inventory19":
                    set = "inventory_slots = '[null" + string.Concat(Enumerable.Repeat(",null", 18).ToArray()) + "]'::jsonb";
                    break;
                case "inventoryObject":
                    set = "inventory_slots = '{}'::jsonb";
                    break;
                default:
                    set = "inventory_slots = '[{\"item_id\":1,\"count\":\"x\",\"enhancement_level\":0}" + string.Concat(Enumerable.Repeat(",null", 19).ToArray()) + "]'::jsonb";
                    break;
            }

            RawExecute("UPDATE character_records SET " + set + " WHERE character_id = " + ID_CORRUPT);

            Faults<CharacterRecordCorruptException>(_store.FetchAsync(new CharacterID(ID_CORRUPT), CancellationToken.None));
        }

        [Test]
        public void NpgsqlStore_ColumnDefaultEmptyArraysInTheRow_FetchReadsEverySlotEmpty()
        {
            // '[]' is the column default (Story 013) for a row inserted by hand; the codec reads it as all empty.
            Insert(MakeSimpleRecord(ID_CORRUPT, 0u));
            RawExecute("UPDATE character_records SET gear_slots = '[]'::jsonb, inventory_slots = '[]'::jsonb WHERE character_id = " + ID_CORRUPT);

            CharacterRecord r = Complete(_store.FetchAsync(new CharacterID(ID_CORRUPT), CancellationToken.None));

            Assert.IsTrue(r.GearSlots.All(s => s.ItemId == ItemID.Invalid), "every gear slot empty");
            Assert.IsTrue(r.InventorySlots.All(i => i == ItemID.Invalid), "every inventory slot empty");
            Assert.IsTrue(r.InventoryItemCounts.All(c => c == 0) && r.InventoryEnhancementLevels.All(l => l == 0));
        }
    }

    /// <summary>
    /// Story 014's source searches. Not explicit and not database tests: they read the store and codec source files.
    /// </summary>
    [TestFixture]
    internal sealed class CharacterPersistence_NpgsqlRecordStoreSource_Tests
    {
        private static string RepoRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        // The file's text with every // comment (also /// doc comments) and every /* */ comment removed. A "//" inside a
        // string or char literal (regular or verbatim) is not a comment.
        private static string CodeOf(string path)
        {
            Assert.IsTrue(File.Exists(path), "missing source file " + path);
            return StripComments(File.ReadAllText(path));
        }

        private static string StripComments(string text)
        {
            var code = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    code.Append(' ');
                }
                else if (c == '@' && next == '"')
                {
                    // Verbatim string: ends at a quote that is not doubled.
                    code.Append("@\"");
                    i += 2;
                    while (i < text.Length)
                    {
                        code.Append(text[i]);
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                code.Append('"');
                                i++;
                            }
                            else
                            {
                                i++;
                                break;
                            }
                        }

                        i++;
                    }
                }
                else if (c == '"' || c == '\'')
                {
                    // Regular string or char literal: backslash escapes the next character; a line break ends an unterminated one.
                    code.Append(c);
                    i++;
                    while (i < text.Length)
                    {
                        char ch = text[i];
                        code.Append(ch);
                        i++;
                        if (ch == '\\' && i < text.Length)
                        {
                            code.Append(text[i]);
                            i++;
                        }
                        else if (ch == c || ch == '\n')
                        {
                            break;
                        }
                    }
                }
                else
                {
                    code.Append(c);
                    i++;
                }
            }

            return code.ToString();
        }

        private static string PersistenceSource(string fileName)
        {
            return CodeOf(Path.Combine(RepoRoot(), "src", "ServerLogic", "CharacterPersistence", fileName));
        }

        [TestCase("NpgsqlCharacterRecordStore.cs")]
        [TestCase("CharacterSlotJsonCodec.cs")]
        public void Source_EveryAwaitIsFollowedByConfigureAwaitFalseOnTheSameExpression(string fileName)
        {
            string code = PersistenceSource(fileName);

            foreach (Match match in Regex.Matches(code, @"\bawait\b"))
            {
                // The expression runs to the end of the statement (";") or, for await using / await foreach, to the block ("{").
                int end = code.IndexOfAny(new[] { ';', '{' }, match.Index);
                Assert.GreaterOrEqual(end, 0, fileName + ": an await without a statement end");
                string expression = code.Substring(match.Index, end - match.Index);
                StringAssert.Contains(".ConfigureAwait(false)", expression, fileName + ": await without ConfigureAwait(false): " + expression.Trim());
            }
        }

        [Test]
        public void Source_TheStoreAwaitsSoTheConfigureAwaitSearchIsNotEmpty()
        {
            string code = PersistenceSource("NpgsqlCharacterRecordStore.cs");

            Assert.GreaterOrEqual(Regex.Matches(code, @"\bawait\b").Count, 6);
        }

        [Test]
        public void Source_NoCodeFileUnderSrcHoldsAConnectionStringLiteral()
        {
            string src = Path.Combine(RepoRoot(), "src");
            string[] files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(files, "no source found under " + src);

            foreach (string file in files)
            {
                string code = CodeOf(file);
                foreach (string key in new[] { "Password=", "Host=", "Username=", "Server=" })
                {
                    Assert.IsFalse(code.Contains(key), file + " holds a " + key + " literal");
                }
            }
        }
    }
}
